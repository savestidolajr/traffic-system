using System.Globalization;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// Partition key format: PK = "Schedule" (all schedules share one partition — low volume is acceptable).
public sealed class TableStorageScheduleRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    IScheduleOccurrenceResolver occurrenceResolver,
    TimeProvider timeProvider,
    ILogger<TableStorageScheduleRepository> logger,
    IOptions<ScheduleDueIndexOptions>? dueIndexOptions = null)
    : TableRepositoryBase("radioSchedules", options, credential, logger), IScheduleRepository
{
    private const string PK = "Schedule";
    private const string DueIndexPartitionPrefix = "ScheduleDue_";
    private readonly ScheduleDueIndexOptions _dueIndexOptions = dueIndexOptions?.Value ?? new();

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    public async Task<Result<IEnumerable<MarketSchedule>>> ListSchedulesAsync(CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<ScheduleEntity>($"PartitionKey eq '{PK}'", ct);
            return entities.Select(MapToDomain).OfType<MarketSchedule>().OrderBy(s => s.Name).AsEnumerable();
        });
    }
    public async Task<Result<IEnumerable<ScheduleReferenceSummary>>> ListScheduleReferenceSummariesAsync(
        CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            // Keep this projection deliberately narrower than ScheduleEntity. Reference checks
            // must not download coverage/calendar/content and other large persisted JSON values.
            var select = new[]
            {
                "PartitionKey", "RowKey", "Station", "VoiceProfileId", "DeliveryStyleId", "PlaybookId",
                "PronunciationDictionaryId", "BookendId", "VoiceRotationJson", "BookendRotationJson"
            };
            var query = TableClient.QueryAsync<ScheduleReferenceEntity>(
                $"PartitionKey eq '{PK}'",
                select: select,
                cancellationToken: ct);
            var summaries = new List<ScheduleReferenceSummary>();
            await foreach (var entity in query)
                summaries.Add(MapToReferenceSummary(entity));
            return summaries.AsEnumerable();
        });
    }


    public async Task<Result<IEnumerable<MarketSchedule>>> GetDueSchedulesAsync(DateTime thresholdUtc,
        CancellationToken ct = default)
    {
        if (!DueIndexReadsEnabled)
            return await GetDueSchedulesLegacyAsync(thresholdUtc, ct);

        var indexed = await GetDueSchedulesFromIndexAsync(thresholdUtc, ct);
        if (indexed.IsSuccess && !indexed.Value.RequiresLegacyFallback)
            return Result<IEnumerable<MarketSchedule>>.Success(indexed.Value.Schedules);

        if (indexed.IsFailure)
            logger.LogWarning(
                "[ScheduleDueIndex] Read failed; falling back to the bounded legacy schedule query: {Error}",
                indexed.Error);
        else
            logger.LogWarning(
                "[ScheduleDueIndex] Stale or incomplete pointers detected; falling back to the bounded legacy schedule query.");

        return await GetDueSchedulesLegacyAsync(thresholdUtc, ct);
    }

    private bool DueIndexReadsEnabled =>
        _dueIndexOptions.ReadEnabled && !_dueIndexOptions.KillSwitch;

    private bool DueIndexWritesEnabled =>
        _dueIndexOptions.WriteEnabled && !_dueIndexOptions.KillSwitch;

    private async Task<Result<IEnumerable<MarketSchedule>>> GetDueSchedulesLegacyAsync(
        DateTime thresholdUtc, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var schedules = new List<MarketSchedule>();
            int pageSize = Math.Clamp(_dueIndexOptions.PageSize, 1, 1_000);
            var query = TableClient.QueryAsync<ScheduleEntity>(
                $"PartitionKey eq '{PK}' and Enabled eq true",
                maxPerPage: pageSize,
                cancellationToken: ct);
            await foreach (var page in query.AsPages())
            {
                foreach (var entity in page.Values)
                {
                    if (!entity.NextRunUtc.HasValue
                        || entity.NextRunUtc.Value.UtcDateTime > thresholdUtc
                        || !string.IsNullOrEmpty(entity.CurrentJobId))
                        continue;

                    var schedule = MapToDomain(entity);
                    if (schedule is not null)
                        schedules.Add(schedule);
                }
            }

            return schedules.AsEnumerable();
        });
    }

    private async Task<Result<DueIndexReadResult>> GetDueSchedulesFromIndexAsync(
        DateTime thresholdUtc, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            await EnsureTableExistsAsync(ct);
            var schedules = new List<MarketSchedule>();
            bool requiresLegacyFallback = false;
            int pageSize = Math.Clamp(_dueIndexOptions.PageSize, 1, 1_000);
            DateTime bucket = FloorToMinute(thresholdUtc)
                .AddMinutes(-Math.Max(0, _dueIndexOptions.LookbackMinutes));
            DateTime lastBucket = FloorToMinute(thresholdUtc);

            for (; bucket <= lastBucket; bucket = bucket.AddMinutes(1))
            {
                string partition = DueIndexPartition(bucket);
                var query = TableClient.QueryAsync<ScheduleDueIndexEntity>(
                    $"PartitionKey eq '{partition}'",
                    maxPerPage: pageSize,
                    cancellationToken: ct);
                await foreach (var page in query.AsPages())
                {
                    foreach (var pointer in page.Values)
                    {
                        if (string.IsNullOrWhiteSpace(pointer.MarketId)
                            || pointer.NextRunUtc == default)
                        {
                            requiresLegacyFallback = true;
                            continue;
                        }

                        var rawResult = await GetEntityOrDefaultAsync<ScheduleEntity>(
                            PK, pointer.MarketId, ct);
                        if (rawResult.IsFailure || rawResult.Value.IsNone)
                        {
                            requiresLegacyFallback = true;
                            continue;
                        }

                        var raw = rawResult.Value.Value;
                        var schedule = MapToDomain(raw);
                        if (schedule is null)
                        {
                            requiresLegacyFallback = true;
                            continue;
                        }

                        bool pointerMatches = raw.Enabled == pointer.Enabled
                            && raw.NextRunUtc.HasValue
                            && raw.NextRunUtc.Value.UtcDateTime == pointer.NextRunUtc.UtcDateTime
                            && string.Equals(raw.CurrentJobId, pointer.CurrentJobId, StringComparison.Ordinal);
                        if (!pointerMatches)
                            requiresLegacyFallback = true;

                        if (schedule.Enabled
                            && schedule.NextRunUtc.HasValue
                            && schedule.NextRunUtc.Value <= thresholdUtc
                            && !schedule.CurrentJobId.HasValue
                            && schedules.All(s => !string.Equals(
                                s.MarketId, schedule.MarketId, StringComparison.OrdinalIgnoreCase)))
                            schedules.Add(schedule);
                    }
                }
            }

            return new DueIndexReadResult(schedules, requiresLegacyFallback);
        });
    }

    private static DateTime FloorToMinute(DateTime utc)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    private static string DueIndexPartition(DateTime bucketUtc) =>
        DueIndexPartitionPrefix + bucketUtc.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);

    public async Task<Result<MarketSchedule>> GetScheduleAsync(string marketId, CancellationToken ct = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<ScheduleEntity>(PK, marketId, ct);
        if (entityResult.IsFailure)
            return Result<MarketSchedule>.Failure(entityResult.Error);
        return entityResult.Value.Match(
            e =>
            {
                var schedule = MapToDomain(e);
                return schedule is null
                    ? Result<MarketSchedule>.Failure(ErrorMessages.Schedule.NotFound)
                    : Result<MarketSchedule>.Success(schedule);
            },
            () => Result<MarketSchedule>.Failure(ErrorMessages.Schedule.NotFound));
    }

    public async Task<Result> SaveScheduleAsync(MarketSchedule schedule, CancellationToken ct = default)
    {
        if (schedule.Enabled && !schedule.NextRunUtc.HasValue)
            schedule.ScheduleNextRun(occurrenceResolver.NextRunUtc(schedule,
                timeProvider.GetUtcNow().UtcDateTime));

        schedule.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        const int maxRetries = 3;

        for (int attempt = 0; attempt < maxRetries; attempt++)
            try
            {
                var response =
                    await TableClient.GetEntityAsync<ScheduleEntity>(
                        PK, schedule.MarketId, cancellationToken: ct);
                var existing = response.Value;
                DateTime? previousNextRunUtc = existing.NextRunUtc?.UtcDateTime;

                var entity = MapToEntity(schedule);
                entity.ETag = existing.ETag;

                await TableClient.UpdateEntityAsync(entity, existing.ETag, TableUpdateMode.Replace, ct);
                await MaintainDueIndexAsync(previousNextRunUtc, schedule, schedule.MarketId, ct);
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity doesn't exist yet — create it
                var entity = MapToEntity(schedule);
                await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
                await MaintainDueIndexAsync(null, schedule, schedule.MarketId, ct);
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                logger.LogWarning("ETag conflict on SaveScheduleAsync for market {MarketId}, attempt {Attempt}. Retrying.",
                    schedule.MarketId, attempt + 1);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                logger.LogWarning(
                    "ETag conflict (retries exhausted) on SaveScheduleAsync for market {MarketId}.", schedule.MarketId);
                return Result.Failure(
                    "Conflict: the schedule was modified by another request. Reload and try again.");
            }
            catch (RequestFailedException ex)
            {
                logger.LogError(ex, "Table Storage operation failed in SaveScheduleAsync for market {MarketId}",
                    schedule.MarketId);
                return Result.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in SaveScheduleAsync for market {MarketId}", schedule.MarketId);
                return Result.Failure(ex.Message);
            }

        return Result.Failure(
            $"Failed to save schedule '{schedule.MarketId}' after {maxRetries} attempts due to concurrent modifications");
    }

    public async Task<Result> DeleteScheduleAsync(string marketId, CancellationToken ct = default)
    {
        // The legacy delete path has no need to read the row first. Only the additive due-index
        // maintenance path needs the previous next-run value to remove its pointer. Avoiding that
        // extra point read keeps deletion bounded to the one authoritative delete by default and
        // preserves compatibility with rows for which the optional index is disabled.
        DateTime? previousNextRunUtc = null;
        if (DueIndexWritesEnabled)
        {
            var existingResult = await GetEntityOrDefaultAsync<ScheduleEntity>(PK, marketId, ct);
            if (existingResult.IsFailure)
                return Result.Failure(existingResult.Error);

            previousNextRunUtc = existingResult.Value.Match(
                entity => entity.NextRunUtc?.UtcDateTime,
                () => null);
        }

        var result = await ExecuteAsync(async () =>
            await TableClient.DeleteEntityAsync(PK, marketId, cancellationToken: ct));
        if (result.IsSuccess && DueIndexWritesEnabled)
            await MaintainDueIndexAsync(previousNextRunUtc, null, marketId, ct);
        return result;
    }

    public async Task<Result> TryStartJobAsync(string marketId, string jobId, CancellationToken ct = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<ScheduleEntity>(PK, marketId, ct);
        if (entityResult.IsFailure)
            return Result.Failure(entityResult.Error);
        if (entityResult.Value.IsNone)
            return Result.Failure(ErrorMessages.Schedule.NotFound);

        var raw = entityResult.Value.Value;
        var etag = raw.ETag;
        var schedule = MapToDomain(raw);
        if (schedule is null)
            return Result.Failure(ErrorMessages.Schedule.NotFound);

        if (schedule.CurrentJobId.HasValue)
            return Result.Failure($"Schedule already running job {schedule.CurrentJobId.Value}");

        schedule.StartRun(jobId, timeProvider.GetUtcNow().UtcDateTime);

        try
        {
            await TableClient.UpdateEntityAsync(MapToEntity(schedule), etag, TableUpdateMode.Replace, ct);
            await MaintainDueIndexAsync(raw.NextRunUtc?.UtcDateTime, schedule, marketId, ct);
            return Result.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            logger.LogWarning("Concurrent modification detected for schedule {MarketId} in TryStartJob", marketId);
            return Result.Failure("Concurrent modification detected");
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Failed to start job on schedule {MarketId}", marketId);
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> CompleteJobAsync(string marketId, Option<DateTime> nextRunUtc,
        CancellationToken ct = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<ScheduleEntity>(PK, marketId, ct);
        if (entityResult.IsFailure)
            return Result.Failure(entityResult.Error);
        if (entityResult.Value.IsNone)
            return Result.Failure(ErrorMessages.Schedule.NotFound);

        var raw = entityResult.Value.Value;
        var etag = raw.ETag;
        var schedule = MapToDomain(raw);
        if (schedule is null)
            return Result.Failure(ErrorMessages.Schedule.NotFound);

        var effectiveNextRun = nextRunUtc.HasValue
            ? nextRunUtc
            : occurrenceResolver.NextRunUtc(schedule, timeProvider.GetUtcNow().UtcDateTime);
        schedule.CompleteRun(timeProvider.GetUtcNow().UtcDateTime, effectiveNextRun);

        try
        {
            await TableClient.UpdateEntityAsync(MapToEntity(schedule), etag, TableUpdateMode.Replace, ct);
            await MaintainDueIndexAsync(raw.NextRunUtc?.UtcDateTime, schedule, marketId, ct);
            return Result.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            logger.LogWarning("Concurrent modification detected for schedule {MarketId} in CompleteJob", marketId);
            return Result.Failure("Concurrent modification detected");
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "Failed to complete job on schedule {MarketId}", marketId);
            return Result.Failure(ex.Message);
        }
    }

    public Task<Result<IEnumerable<MarketSchedule>>> GetDueSchedulesAsync(CancellationToken ct = default)
    {
        return GetDueSchedulesAsync(timeProvider.GetUtcNow().UtcDateTime, ct);
    }
    private async Task MaintainDueIndexAsync(
        DateTime? previousNextRunUtc,
        MarketSchedule? schedule,
        string marketId,
        CancellationToken ct)
    {
        if (!DueIndexWritesEnabled)
            return;

        try
        {
            if (previousNextRunUtc.HasValue)
                await DeleteDueIndexRowAsync(previousNextRunUtc.Value, marketId, ct);

            if (schedule is { Enabled: true } && schedule.NextRunUtc.HasValue)
            {
                var nextRunUtc = DateTime.SpecifyKind(schedule.NextRunUtc.Value, DateTimeKind.Utc);
                await TableClient.UpsertEntityAsync(
                    new ScheduleDueIndexEntity
                    {
                        PartitionKey = DueIndexPartition(nextRunUtc),
                        RowKey = marketId,
                        MarketId = marketId,
                        Enabled = schedule.Enabled,
                        NextRunUtc = new DateTimeOffset(nextRunUtc),
                        CurrentJobId = schedule.CurrentJobId.GetValueOr(string.Empty)
                    },
                    TableUpdateMode.Replace,
                    ct);
            }
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // A missing old pointer is harmless during migration/backfill.
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning(
                "[ScheduleDueIndex] Maintenance cancelled for {MarketId}; the schedule write succeeded.",
                marketId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[ScheduleDueIndex] Maintenance failed for {MarketId}; the schedule write succeeded.",
                marketId);
        }
    }

    private async Task DeleteDueIndexRowAsync(
        DateTime nextRunUtc, string marketId, CancellationToken ct)
    {
        try
        {
            await TableClient.DeleteEntityAsync(
                DueIndexPartition(DateTime.SpecifyKind(nextRunUtc, DateTimeKind.Utc)),
                marketId,
                cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The pointer may already have been removed by a resumable backfill.
        }
    }


    // ─── Mapping ─────────────────────────────────────────────────────

    private MarketSchedule? MapToDomain(ScheduleEntity e)
    {
        var coverage = SafeJson.Deserialize<CoverageArea>(e.CoverageJson, logger, $"schedule {e.RowKey} coverage")
                       ?? CoverageArea.Create(0, 0, 1).Value;
        var contentSelection = SafeJson.DeserializeOption<BulletinContentSelection>(e.ContentSelectionJson,
            logger, $"schedule {e.RowKey} contentSelection", JsonOptions);

        // Rehydrate, NOT Create: an existing row must always load, even if it predates an
        // admission rule. A Create failure here is treated as "corrupt, skip" below, which would
        // silently remove the station from the console and the poller.
        var result = MarketSchedule.Rehydrate(
            e.RowKey ?? string.Empty,
            e.Name ?? string.Empty,
            e.Station ?? string.Empty,
            e.Timezone ?? string.Empty,
            e.CronExpression ?? string.Empty,
            e.Enabled,
            coverage,
            e.VoiceProfileId ?? string.Empty,
            e.OutputPath ?? e.S3Path ?? string.Empty,
            e.TargetDurationSeconds,
            e.NotifyBeforePlayoutMinutes,
            e.EscalationBeforePlayoutMinutes,
            e.CreatedAt.UtcDateTime,
            e.UpdatedAt.UtcDateTime,
            e.DeliveryStyleId ?? "",
            e.PronunciationDictionaryId ?? "",
            curationEnabled: e.CurationEnabled,
            // Nullable on the entity precisely so an absent column is distinguishable from a
            // stored false. Every station written before this flag existed was delivering, so
            // absent MUST rehydrate as true — a false default would take live stations off air
            // the moment this build deployed.
            playoutEnabled: e.PlayoutEnabled ?? true,
            playbookId: e.PlaybookId ?? "",
            contentSelection: contentSelection);

        if (result.IsFailure)
        {
            // A persisted schedule missing a required field is unusable. Skip it (and log) rather
            // than throwing, so one corrupt record can't break loading every other schedule.
            logger.LogError("Skipping corrupt schedule {RowKey}: {Error}", e.RowKey, result.Error);
            return null;
        }

        var schedule = result.Value;
        schedule.Calendar = SafeJson.DeserializeOption<ScheduleCalendar>(e.ScheduleCalendarJson,
            logger, $"schedule {e.RowKey} calendar", JsonOptions);

        schedule.LastRunUtc = e.LastRunUtc.ToUtcOption();
        schedule.NextRunUtc = e.NextRunUtc.ToUtcOption();
        schedule.CurrentJobId = e.CurrentJobId.ToOption();
        schedule.VoiceRotation = SafeJson.DeserializeOption<VoiceRotationPolicy>(e.VoiceRotationJson,
            logger, $"schedule {e.RowKey} voiceRotation", JsonOptions);
        schedule.VoiceRotationCursor = e.VoiceRotationCursor;
        schedule.BookendId = e.BookendId ?? "";
        // Missing/blank column = no rotation configured. Rows persisted before the pair-library
        // collapse carry the old Intro/Outro columns, which the SDK ignores, so they degrade to
        // an unassigned schedule here rather than throwing.
        schedule.BookendRotation = SafeJson.DeserializeOption<BookendRotationPolicy>(e.BookendRotationJson,
            logger, $"schedule {e.RowKey} bookendRotation", JsonOptions);
        schedule.BookendRotationCursor = e.BookendRotationCursor;
        // Missing/blank column = no tokens configured (old rows predate the column).
        schedule.BookendTokens = SafeJson.Deserialize<Dictionary<string, string>>(e.BookendTokensJson,
            logger, $"schedule {e.RowKey} bookendTokens", JsonOptions)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // FillOrder persisted as lowercase wire strings ("weather"/"fuel"/"filler") for
        // human-readable Table rows; null/absent column = model default (weather, fuel, filler).
        var fillOrder = SafeJson.DeserializeOption<List<string>>(e.FillOrderJson,
            logger, $"schedule {e.RowKey} fillOrder", JsonOptions);
        if (fillOrder.HasValue)
        {
            var parsed = new List<FillSource>(fillOrder.Value.Count);
            foreach (string raw in fillOrder.Value)
            {
                FillSource? source = raw?.Trim().ToLowerInvariant() switch
                {
                    "weather" => FillSource.Weather,
                    "fuel" => FillSource.Fuel,
                    "minortraffic" => FillSource.MinorTraffic,
                    "filler" => FillSource.Filler,
                    _ => null
                };
                if (source is not null && !parsed.Contains(source.Value))
                    parsed.Add(source.Value);
            }
            schedule.FillOrder = parsed;
        }

        return schedule;
    }
    private static ScheduleReferenceSummary MapToReferenceSummary(ScheduleReferenceEntity entity)
    {
        var voiceIds = new List<string>();
        AddReference(voiceIds, entity.VoiceProfileId);
        AddReference(voiceIds, ParseVoiceRotation(entity.VoiceRotationJson));

        var bookendIds = new List<string>();
        AddReference(bookendIds, entity.BookendId);
        AddReference(bookendIds, ParseBookendRotation(entity.BookendRotationJson));

        return new ScheduleReferenceSummary(
            entity.RowKey ?? string.Empty,
            entity.Station ?? string.Empty,
            entity.VoiceProfileId ?? string.Empty,
            entity.DeliveryStyleId ?? string.Empty,
            entity.PlaybookId ?? string.Empty,
            entity.PronunciationDictionaryId ?? string.Empty,
            entity.BookendId ?? string.Empty,
            voiceIds,
            bookendIds);
    }

    private static IEnumerable<string> ParseVoiceRotation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var policy = JsonSerializer.Deserialize<VoiceRotationPolicy>(json, JsonOptions)
            ?? throw new InvalidDataException("Schedule voice rotation was empty.");
        return policy.AllReferencedVoiceIds();
    }

    private static IEnumerable<string> ParseBookendRotation(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        var policy = JsonSerializer.Deserialize<BookendRotationPolicy>(json, JsonOptions)
            ?? throw new InvalidDataException("Schedule bookend rotation was empty.");
        return policy.AllReferencedBookendIds();
    }

    private static void AddReference(List<string> target, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id)
            && !target.Contains(id.Trim(), StringComparer.OrdinalIgnoreCase))
            target.Add(id.Trim());
    }

    private static void AddReference(List<string> target, IEnumerable<string> ids)
    {
        foreach (var id in ids)
            AddReference(target, id);
    }


    private static ScheduleEntity MapToEntity(MarketSchedule s)
    {
        return new ScheduleEntity

        {
            PartitionKey = PK,
            RowKey = s.MarketId,
            Name = s.Name,
            Station = s.Station,
            Timezone = s.Timezone,
            CronExpression = s.CronExpression,
            Enabled = s.Enabled,
            CurationEnabled = s.CurationEnabled,
            PlayoutEnabled = s.PlayoutEnabled,
            VoiceProfileId = s.VoiceProfileId,
            DeliveryStyleId = s.DeliveryStyleId,
            PlaybookId = s.PlaybookId,
            BookendId = s.BookendId,
            PronunciationDictionaryId = s.PronunciationDictionaryId,
            ContentSelectionJson =
                s.ContentSelection.Match(c => JsonSerializer.Serialize(c, JsonOptions), () => null!),
            ScheduleCalendarJson = s.Calendar.Match(c => JsonSerializer.Serialize(c, JsonOptions), () => null!),
            VoiceRotationJson = s.VoiceRotation.Match(p => JsonSerializer.Serialize(p, JsonOptions), () => null!),
            VoiceRotationCursor = s.VoiceRotationCursor,
            BookendRotationJson = s.BookendRotation.Match(p => JsonSerializer.Serialize(p, JsonOptions), () => null!),
            BookendRotationCursor = s.BookendRotationCursor,
            BookendTokensJson = s.BookendTokens.Count > 0
                ? JsonSerializer.Serialize(s.BookendTokens, JsonOptions)
                : null,
            FillOrderJson = JsonSerializer.Serialize(
                s.FillOrder.Select(f => f.ToString().ToLowerInvariant()).ToList(), JsonOptions),
            OutputPath = s.OutputPath,
            TargetDurationSeconds = s.TargetDurationSeconds,
            NotifyBeforePlayoutMinutes = s.NotifyBeforePlayoutMinutes,
            EscalationBeforePlayoutMinutes = s.EscalationBeforePlayoutMinutes,
            LastRunUtc = s.LastRunUtc.ToNullableOffset(),
            NextRunUtc = s.NextRunUtc.ToNullableOffset(),
            CurrentJobId = s.CurrentJobId.ToNullable(),
            CoverageJson = JsonSerializer.Serialize(s.Coverage),
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }

    /// <summary>Storage shape for the narrow schedule assignment projection.</summary>
    private sealed class ScheduleReferenceEntity : ITableEntity
    {
        public string? Station { get; set; }
        public string? VoiceProfileId { get; set; }
        public string? DeliveryStyleId { get; set; }
        public string? PlaybookId { get; set; }
        public string? PronunciationDictionaryId { get; set; }
        public string? BookendId { get; set; }
        public string? VoiceRotationJson { get; set; }
        public string? BookendRotationJson { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    private sealed record DueIndexReadResult(
        IReadOnlyList<MarketSchedule> Schedules,
        bool RequiresLegacyFallback);

    internal sealed class ScheduleDueIndexEntity : ITableEntity
    {
        public string MarketId { get; set; } = default!;
        public bool Enabled { get; set; }
        public DateTimeOffset NextRunUtc { get; set; }
        public string? CurrentJobId { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class ScheduleEntity : ITableEntity
    {
        public string Name { get; set; } = default!;
        public string Station { get; set; } = default!;
        public string Timezone { get; set; } = default!;
        public string CronExpression { get; set; } = default!;
        public bool Enabled { get; set; }
        public bool CurationEnabled { get; set; }
        public bool? PlayoutEnabled { get; set; }
        public string? VoiceProfileId { get; set; }
        public string? DeliveryStyleId { get; set; }
        public string? PlaybookId { get; set; }
        public string? PronunciationDictionaryId { get; set; }
        public string? BookendId { get; set; }
        public string? ContentSelectionJson { get; set; }
        public string? ScheduleCalendarJson { get; set; }
        public string? VoiceRotationJson { get; set; }
        public int VoiceRotationCursor { get; set; }
        public string? BookendRotationJson { get; set; }
        public int BookendRotationCursor { get; set; }
        public string? BookendTokensJson { get; set; }
        public string? FillOrderJson { get; set; }
        public string? OutputPath { get; set; }
        public string? S3Path { get; set; }
        public double TargetDurationSeconds { get; set; }
        public int NotifyBeforePlayoutMinutes { get; set; } = 15;
        public int EscalationBeforePlayoutMinutes { get; set; } = 5;
        public DateTimeOffset? LastRunUtc { get; set; }
        public DateTimeOffset? NextRunUtc { get; set; }
        public string? CurrentJobId { get; set; }
        public string? CoverageJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

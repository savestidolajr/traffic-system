using Azure.Data.Tables;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.ValueObjects;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Maps between the domain <see cref="Job"/> aggregate and its <see cref="JobEntity"/> Table
///     Storage row, and enforces Table Storage's per-column size limit on the JSON columns that
///     scale with fetched traffic data.
///     <para>
///         Compression contract: ten columns are wrapped through <see cref="FitTableProperty"/>
///         / <see cref="FitJobHistoryJson{T}"/> on write and <see cref="ExpandTableProperty"/> on
///         read — <c>ScriptSentencesJson</c>, <c>IncidentsJson</c>, <c>AllIncidentsJson</c>,
///         <c>CorridorsJson</c>, <c>FlowRoadsJson</c>, <c>GeneratorInputJson</c>,
///         <c>SupersededTakeJson</c>, <c>RevoiceObservationJson</c>, plus the two append-only
///         history columns <c>LogsJson</c> and <c>RejectionHistoryJson</c>. A value
///         over <see cref="MaxTablePropertyChars"/> (30,000 chars) is gzip-compressed and stored
///         base64-encoded with a <see cref="GzipSentinel"/> ("gz:") prefix; JSON never starts
///         with "gz:" so detection on read is unambiguous. If the compressed form still exceeds
///         the cap, the column is dropped (logged as a warning) as a last resort rather than
///         aborting the whole job write — see the cap-constants comment below for the incident
///         that motivated this. The two history columns add one rung to that ladder: because
///         they grow with every pipeline step / rejection round, the OLDEST entries are trimmed
///         (keeping the most recent and terminal-state entries) before the column is dropped.
///     </para>
/// </summary>
internal sealed class JobEntityMapper(ILogger<TableStorageJobRepository> logger)
{
    internal static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;
    private static readonly JsonSerializerOptions SourceDiagnosticsJsonOptions = new(JsonOptions)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Normalises a market value into a Job partition key.</summary>
    internal static string JobPartition(string? market)
    {
        var normalised = market is null ? null : MarketKey.Normalize(market);
        return string.IsNullOrEmpty(normalised) ? "Job_unknown" : "Job_" + normalised;
    }

    internal Job MapToDomain(JobEntity entity)
    {
        var status = ParseEnum(entity.Status, JobStatus.Requested, entity.RowKey, "Status");
        var failedAtStatus = ParseOptionalEnum<JobStatus>(entity.FailedAtStatus, entity.RowKey, "FailedAtStatus");

        var job = Job.Reconstitute(entity.RowKey, entity.Station, entity.Market, entity.TargetDurationSeconds,
            SafeJson.Deserialize<List<StatusHistoryEntry>>(entity.StatusHistoryJson, logger,
                $"job {entity.RowKey} statusHistory", JsonOptions) ?? [],
            entity.Latitude.ToOption(),
            entity.Longitude.ToOption(),
            entity.RadiusKm.ToOption(),
            entity.TimezoneId.ToOption(),
            coverage: Option.OfNullable(SafeJson.Deserialize<CoverageArea>(entity.CoverageJson, logger,
                $"job {entity.RowKey} coverage", JsonOptions)),
            createdAt: entity.CreatedAt.UtcDateTime,
            status: status,
            attemptCount: entity.AttemptCount,
            fallbackStreak: entity.FallbackStreak);

        job.LastUpdatedAt = entity.LastUpdatedAt.UtcDateTime;
        job.MeasuredDurationSec = entity.MeasuredDurationSec.ToOption();
        job.ScriptText = entity.ScriptText.ToOption();
        job.TaggedScriptText = entity.TaggedScriptText.ToOption();
        job.DeliveryStyleId = entity.DeliveryStyleId.ToOption();
        job.DeliveryStyleVersion = entity.DeliveryStyleVersion.ToOption();
        job.VoiceProfileId = entity.VoiceProfileId.ToOption();
        job.VoiceProfileVersion = entity.VoiceProfileVersion.ToOption();
        job.BookendId = entity.BookendId.ToOption();
        job.BookendVersion = entity.BookendVersion.ToOption();
        job.PronunciationDictionaryId = entity.PronunciationDictionaryId.ToOption();
        job.PronunciationVersionId = entity.PronunciationVersionId.ToOption();
        job.GlobalPronunciationDictionaryId = entity.GlobalPronunciationDictionaryId.ToOption();
        job.GlobalPronunciationVersionId = entity.GlobalPronunciationVersionId.ToOption();
        job.AudioUrl = entity.AudioUrl.ToOption();
        job.AudioSha256 = entity.AudioSha256;
        job.ErrorMessage = entity.ErrorMessage.ToOption();
        job.ScheduledForUtc = entity.ScheduledForUtc.ToUtcOption();
        job.CurationDeadlineUtc = entity.CurationDeadlineUtc.ToUtcOption();
        job.FallbackDeadlineUtc = entity.FallbackDeadlineUtc.ToUtcOption();
        job.MissedDeadlineUtc = entity.MissedDeadlineUtc.ToUtcOption();
        job.FallbackSourceJobId = entity.FallbackSourceJobId.ToOption();
        job.FallbackAppliedAt = entity.FallbackAppliedAt.ToUtcOption();
        job.FallbackReason = entity.FallbackReason.ToOption();
        job.ApprovedBy = entity.ApprovedBy;
        job.ApprovedByObjectId = entity.ApprovedByObjectId;
        job.ApprovedAt = entity.ApprovedAt;
        job.PublishedBy = entity.PublishedBy;
        job.PublishedByObjectId = entity.PublishedByObjectId;
        job.PublishedAt = entity.PublishedAt;
        job.DeliveryIdempotencyKey = entity.DeliveryIdempotencyKey;
        job.DeliveryReviewedEtag = entity.DeliveryReviewedEtag;
        job.DeliveryKind = ParseOptionalEnum<DeliveryKind>(entity.DeliveryKind, entity.RowKey, "DeliveryKind");
        job.DeliveryRequestedBy = entity.DeliveryRequestedBy;
        job.DeliveryRequestedByObjectId = entity.DeliveryRequestedByObjectId;
        job.DeliveryFallbackSourceJobId = entity.DeliveryFallbackSourceJobId;
        job.DeliveryFallbackAudioUrl = entity.DeliveryFallbackAudioUrl;
        job.DeliveryFallbackReason = entity.DeliveryFallbackReason;
        job.DeliveryFallbackNotes = entity.DeliveryFallbackNotes;
        job.DeliveryFallbackDurationSeconds = entity.DeliveryFallbackDurationSeconds;
        job.DeliveryAttemptCount = entity.DeliveryAttemptCount;
        job.DeliveryLastAttemptAt = entity.DeliveryLastAttemptAt;
        job.DeliveryLastError = entity.DeliveryLastError;
        job.PreviousJobId = entity.PreviousJobId.ToOption();
        job.PreviousScriptContext = entity.PreviousScriptContext.ToOption();
        job.IncidentChangeSummary = Option.OfNullable(
            SafeJson.Deserialize<IncidentChangeSummary>(entity.IncidentChangeSummaryJson, logger,
                $"job {entity.RowKey} incident-change summary", JsonOptions));
        job.NarrativeIncidentChangeSummary = Option.OfNullable(
            SafeJson.Deserialize<IncidentChangeSummary>(entity.NarrativeIncidentChangeSummaryJson, logger,
                $"job {entity.RowKey} narrative incident-change summary", JsonOptions));
        job.FailedAtStatus = failedAtStatus;
        job.LastEstimatedDurationSec = entity.LastEstimatedDurationSec.ToOption();
        job.IncidentsFetchedAt = entity.IncidentsFetchedAt.ToUtcOption();
        job.SourceDiagnostics = SafeJson.Deserialize<List<SourceFetchDiagnostic>>(
            ExpandTableProperty(entity.SourceDiagnosticsJson, "SourceDiagnosticsJson", entity.RowKey), logger,
            $"job {entity.RowKey} sourceDiagnostics", SourceDiagnosticsJsonOptions) ?? [];
        job.SetLogs(
            SafeJson.Deserialize<List<JobLog>>(ExpandTableProperty(entity.LogsJson, "LogsJson", entity.RowKey),
                logger, $"job {entity.RowKey} logs", JsonOptions) ?? []);
        job.SetIncidents(SafeJson.Deserialize<List<JobIncident>>(
            ExpandTableProperty(entity.IncidentsJson, "IncidentsJson", entity.RowKey), logger,
            $"job {entity.RowKey} incidents", JsonOptions) ?? []);
        job.SetAllIncidents(SafeJson.Deserialize<List<JobIncident>>(
            ExpandTableProperty(entity.AllIncidentsJson, "AllIncidentsJson", entity.RowKey), logger,
            $"job {entity.RowKey} allIncidents", JsonOptions) ?? []);
        var corridors = SafeJson.Deserialize<List<JobCorridor>>(
            ExpandTableProperty(entity.CorridorsJson, "CorridorsJson", entity.RowKey), logger,
            $"job {entity.RowKey} corridors", JsonOptions) ?? [];
        job.SetCorridors(corridors.Select(c =>
            c.LegacyOfficialEvidence is { } legacy && c.OfficialEvidences is null
                ? c with { OfficialEvidences = [legacy], LegacyOfficialEvidence = null }
                : c).ToList());
        job.SetFlowRoads(SafeJson.Deserialize<List<JobFlowRoad>>(
            ExpandTableProperty(entity.FlowRoadsJson, "FlowRoadsJson", entity.RowKey), logger,
            $"job {entity.RowKey} flowRoads", JsonOptions) ?? []);
        job.SetScriptSentences(SafeJson.Deserialize<List<JobScriptSentence>>(
            ExpandTableProperty(entity.ScriptSentencesJson, "ScriptSentencesJson", entity.RowKey), logger,
            $"job {entity.RowKey} scriptSentences", JsonOptions) ?? []);

        job.ETag = entity.ETag.ToString();
        job.ConcurrencyFingerprint = ComputeFingerprint(entity);
        job.VoiceConfig = SafeJson.DeserializeOption<JobVoiceConfig>(entity.VoiceConfigJson, logger,
            $"job {entity.RowKey} voiceConfig", JsonOptions);
        job.WordTimings = SafeJson.DeserializeOption<IReadOnlyList<WordTiming>>(entity.WordTimingsJson, logger,
            $"job {entity.RowKey} wordTimings", JsonOptions);
        job.PreReview = SafeJson.DeserializeOption<AudioPreReviewOutcome>(entity.PreReviewJson, logger,
            $"job {entity.RowKey} preReview", JsonOptions);
        job.ContentSelection = SafeJson.DeserializeOption<BulletinContentSelection>(entity.ContentSelectionJson,
            logger, $"job {entity.RowKey} contentSelection", JsonOptions);
        job.Weather = SafeJson.DeserializeOption<WeatherReport>(entity.WeatherJson, logger,
            $"job {entity.RowKey} weather", JsonOptions);
        job.Fuel = SafeJson.DeserializeOption<FuelPriceReport>(entity.FuelJson, logger,
            $"job {entity.RowKey} fuel", JsonOptions);
        job.GoodRunRoads = SafeJson.Deserialize<List<string>>(entity.GoodRunRoadsJson, logger,
            $"job {entity.RowKey} goodRunRoads", JsonOptions) ?? [];
        job.MonitoredRoutes = SafeJson.Deserialize<List<MonitoredRouteStatus>>(entity.MonitoredRoutesJson, logger,
            $"job {entity.RowKey} monitoredRoutes", JsonOptions) ?? [];
        job.SetAttemptHistory(SafeJson.Deserialize<List<AttemptResult>>(entity.AttemptHistoryJson, logger,
            $"job {entity.RowKey} attemptHistory", JsonOptions) ?? []);
        // MUST expand before deserializing: the write side runs through FitTableProperty, so a
        // large take arrives "gz:"-prefixed and a raw Deserialize would silently drop it.
        job.SetSupersededTake(SafeJson.DeserializeOption<SupersededTake>(
            ExpandTableProperty(entity.SupersededTakeJson, "SupersededTakeJson", entity.RowKey), logger,
            $"job {entity.RowKey} supersededTake", JsonOptions));
        job.PendingRevoiceObservation = SafeJson.DeserializeOption<RevoiceObservation>(
            ExpandTableProperty(entity.RevoiceObservationJson, "RevoiceObservationJson", entity.RowKey), logger,
            $"job {entity.RowKey} revoiceObservation", JsonOptions);
        job.SetRejectionHistory(SafeJson.Deserialize<List<RejectionEntry>>(
            ExpandTableProperty(entity.RejectionHistoryJson, "RejectionHistoryJson", entity.RowKey), logger,
            $"job {entity.RowKey} rejectionHistory", JsonOptions) ?? []);
        job.GeneratorInput = SafeJson.Deserialize<GeneratorInputSnapshot>(
            ExpandTableProperty(entity.GeneratorInputJson, "GeneratorInputJson", entity.RowKey), logger,
            $"job {entity.RowKey} generatorInput", JsonOptions);

        return job;
    }

    /// <summary>
    ///     Maps only the scalar fields required by a list row. Full job JSON is deliberately not
    ///     expanded here; list endpoints must not pay to rebuild the aggregate they do not return.
    /// </summary>
    internal JobListItem MapToListItem(JobEntity entity) => new(
        entity.RowKey,
        entity.Station,
        entity.Market,
        entity.TargetDurationSeconds,
        ParseEnum(entity.Status, JobStatus.Requested, entity.RowKey, "Status"),
        entity.CreatedAt.UtcDateTime,
        entity.LastUpdatedAt.UtcDateTime,
        entity.ETag.ToString(),
        entity.MeasuredDurationSec);

    internal JobListItem MapToListItem(TableEntity entity) => new(
        entity.RowKey,
        StringValue(entity, "Station"),
        StringValue(entity, "Market"),
        DoubleValue(entity, "TargetDurationSeconds"),
        ParseEnum(StringValue(entity, "Status"), JobStatus.Requested, entity.RowKey, "Status"),
        DateValue(entity, "CreatedAt"),
        DateValue(entity, "LastUpdatedAt"),
        entity.ETag.ToString(),
        NullableDoubleValue(entity, "MeasuredDurationSec"));

    internal JobLedgerProjection MapToLedger(JobEntity entity) =>
        BuildLedgerProjection(
            entity.RowKey,
            entity.Station,
            entity.Market,
            entity.TargetDurationSeconds,
            entity.Status,
            entity.CreatedAt.UtcDateTime,
            entity.LastUpdatedAt.UtcDateTime,
            entity.AllIncidentsJson,
            entity.CorridorsJson,
            entity.ScriptSentencesJson,
            entity.PreviousJobId);

    internal JobLedgerProjection MapToLedger(TableEntity entity) =>
        BuildLedgerProjection(
            entity.RowKey,
            StringValue(entity, "Station"),
            StringValue(entity, "Market"),
            DoubleValue(entity, "TargetDurationSeconds"),
            StringValue(entity, "Status"),
            DateValue(entity, "CreatedAt"),
            DateValue(entity, "LastUpdatedAt"),
            StringValue(entity, "AllIncidentsJson"),
            StringValue(entity, "CorridorsJson"),
            StringValue(entity, "ScriptSentencesJson"),
            StringValue(entity, "PreviousJobId"));

    private JobLedgerProjection BuildLedgerProjection(
        string jobId,
        string station,
        string market,
        double targetDurationSeconds,
        string? statusValue,
        DateTime createdAt,
        DateTime lastUpdatedAt,
        string? allIncidentsJson,
        string? corridorsJson,
        string? scriptSentencesJson,
        string? previousJobId)
    {
        var allIncidents = SafeJson.Deserialize<List<JobIncident>>(
            ExpandTableProperty(allIncidentsJson, "AllIncidentsJson", jobId), logger,
            $"job {jobId} allIncidents", JsonOptions) ?? [];
        var corridors = SafeJson.Deserialize<List<JobCorridor>>(
            ExpandTableProperty(corridorsJson, "CorridorsJson", jobId), logger,
            $"job {jobId} corridors", JsonOptions) ?? [];
        var sentences = SafeJson.Deserialize<List<JobScriptSentence>>(
            ExpandTableProperty(scriptSentencesJson, "ScriptSentencesJson", jobId), logger,
            $"job {jobId} scriptSentences", JsonOptions) ?? [];

        return new JobLedgerProjection(
            jobId,
            station,
            market,
            targetDurationSeconds,
            ParseEnum(statusValue, JobStatus.Requested, jobId, "Status"),
            createdAt,
            lastUpdatedAt,
            allIncidents,
            corridors,
            sentences,
            previousJobId);
    }

    private static string StringValue(TableEntity entity, string name) =>
        entity.TryGetValue(name, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static double DoubleValue(TableEntity entity, string name) =>
        NullableDoubleValue(entity, name) ?? 0;

    private static double? NullableDoubleValue(TableEntity entity, string name) =>
        entity.TryGetValue(name, out var value) && value is not null
            ? Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static DateTime DateValue(TableEntity entity, string name) =>
        entity.TryGetValue(name, out var value)
            ? value switch
            {
                DateTimeOffset dto => dto.UtcDateTime,
                DateTime dt => dt.ToUniversalTime(),
                _ => DateTime.TryParse(value?.ToString(), out var parsed)
                    ? parsed.ToUniversalTime()
                    : default
            }
            : default;

    private TEnum ParseEnum<TEnum>(string? value, TEnum fallback, string jobId, string fieldName)
        where TEnum : struct, Enum
    {
        if (Enum.TryParse<TEnum>(value, out var parsed)) return parsed;
        logger.LogWarning("Unknown {FieldName} '{Value}' for job {JobId}, defaulting to {Fallback}", fieldName, value,
            jobId, fallback);
        return fallback;
    }

    private Option<TEnum> ParseOptionalEnum<TEnum>(string? value, string jobId, string fieldName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrEmpty(value)) return Option<TEnum>.None;
        if (Enum.TryParse<TEnum>(value, out var parsed)) return Option<TEnum>.Some(parsed);
        logger.LogWarning("Unknown {FieldName} '{Value}' for job {JobId}, defaulting to None", fieldName, value,
            jobId);
        return Option<TEnum>.None;
    }

    // Azure Table Storage hard-caps a single string property at 64 KB (32 K UTF-16 chars). The
    // per-job JSON columns that scale with fetched traffic (the generator-input replay snapshot and
    // the full incident / corridor / flow lists) can exceed this for an incident-heavy market and
    // abort the ENTIRE entity write — failing an otherwise-good bulletin into the fallback.
    // Oversized JSON is gzip-compressed and stored base64 with a "gz:" sentinel (~5-8x reduction;
    // JSON never starts with "gz:", so detection is unambiguous). Observed 2026-07-16: adelaide's
    // snapshot was 31-33 K chars on EVERY job, so the previous drop-on-oversize behaviour silently
    // blanked GeneratorInputJson for the whole market (perth/melbourne intermittently). Only if the
    // compressed form STILL exceeds the cap is the column dropped (with a warning) rather than lose
    // the whole job; core fields (script, audio, word timings, status) always persist.
    private const int MaxTablePropertyChars = 30_000;
    private const string GzipSentinel = "gz:";

    private string? FitTableProperty(string? json, string column, string jobId)
    {
        if (json is null || json.Length <= MaxTablePropertyChars)
            return json;
        var compressed = Compress(json);
        if (compressed.Length <= MaxTablePropertyChars)
        {
            logger.LogInformation(
                "[JobPersist] Job {JobId} column {Column} is {Length} chars, over the {Max}-char Table Storage limit — stored gzip-compressed ({Compressed} chars).",
                jobId, column, json.Length, MaxTablePropertyChars, compressed.Length);
            return compressed;
        }
        logger.LogWarning(
            "[JobPersist] Job {JobId} column {Column} is {Length} chars ({Compressed} compressed), over the {Max}-char Table Storage limit even gzipped — dropping it so the job still persists.",
            jobId, column, json.Length, compressed.Length, MaxTablePropertyChars);
        return null;
    }

    /// <summary>Gzip-compresses a JSON payload, base64-encoded with the <see cref="GzipSentinel"/> prefix.</summary>
    private static string Compress(string json)
    {
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes(json));
        }
        return GzipSentinel + Convert.ToBase64String(buffer.ToArray());
    }

    /// <summary>
    ///     Serialises an append-only per-job history list (pipeline logs / rejection rounds) for
    ///     storage under the same gzip-then-degrade contract as the traffic columns, with one
    ///     extra rung before the last-resort drop: when compression alone cannot fit the column
    ///     (an incompressible payload — GUID-heavy logs, rejection rounds carrying full scripts),
    ///     the OLDEST entries are trimmed first so the most recent and terminal-state entries
    ///     survive, then the remainder is stored plain. A column that still exceeds the cap after
    ///     trimming to a single entry is dropped with a warning rather than aborting the whole
    ///     job write — the invariant is that no append/state write may ever 500 on the property
    ///     ceiling and wedge the row (the exact deadlock the trimmer exists to prevent: shrinking
    ///     the history is itself the write the ceiling rejects).
    /// </summary>
    internal string? FitJobHistoryJson<T>(
        IReadOnlyList<T> entries, Func<T, DateTime> at, string column, string jobId)
    {
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        if (json.Length <= MaxTablePropertyChars)
            return json;

        var compressed = Compress(json);
        if (compressed.Length <= MaxTablePropertyChars)
        {
            logger.LogInformation(
                "[JobPersist] Job {JobId} column {Column} is {Length} chars, over the {Max}-char Table Storage limit — stored gzip-compressed ({Compressed} chars).",
                jobId, column, json.Length, MaxTablePropertyChars, compressed.Length);
            return compressed;
        }

        var ordered = entries.OrderBy(at).ToList();
        var dropped = 0;
        while (ordered.Count > 1 && json.Length > MaxTablePropertyChars)
        {
            ordered.RemoveAt(0);
            dropped++;
            json = JsonSerializer.Serialize(ordered, JsonOptions);
        }

        if (json.Length <= MaxTablePropertyChars)
        {
            logger.LogWarning(
                "[JobPersist] Job {JobId} column {Column} is {Length} chars ({Compressed} compressed), over the {Max}-char Table Storage limit even gzipped — dropped the {Dropped} oldest entr{Plural} and stored the remainder ({Remaining} entr{PluralRemaining}).",
                jobId, column, json.Length, compressed.Length, MaxTablePropertyChars, dropped,
                dropped == 1 ? "y" : "ies", ordered.Count, ordered.Count == 1 ? "y" : "ies");
            return json;
        }

        logger.LogWarning(
            "[JobPersist] Job {JobId} column {Column} is {Length} chars ({Compressed} compressed), over the {Max}-char Table Storage limit even after trimming to a single entry — dropping the column so the job still persists.",
            jobId, column, json.Length, compressed.Length, MaxTablePropertyChars);
        return null;
    }

    /// <summary>Inverse of <see cref="FitTableProperty" /> / <see cref="FitJobHistoryJson{T}" />: expands a gzip-compressed column value.</summary>
    internal string? ExpandTableProperty(string? stored, string column, string jobId)
    {
        if (stored is null || !stored.StartsWith(GzipSentinel, StringComparison.Ordinal))
            return stored;
        try
        {
            using var gzip = new GZipStream(
                new MemoryStream(Convert.FromBase64String(stored[GzipSentinel.Length..])),
                CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            logger.LogWarning(ex,
                "[JobPersist] Job {JobId} column {Column} has a corrupt compressed value — treating as missing.",
                jobId, column);
            return null;
        }
    }

    /// <summary>
    ///     Deterministic fingerprint over an entity's material (state) columns, excluding logs,
    ///     ETag, and Timestamp. Comparing this before and after a 412 reload in
    ///     <see cref="TableStorageJobRepository.SaveJobAsync"/> tells a benign concurrent log
    ///     append (fingerprint unchanged) apart from a real concurrent state change (fingerprint
    ///     changed), so the retry loop knows whether it is safe to reconcile-and-retry or must
    ///     surface a conflict.
    /// </summary>
    internal static string ComputeFingerprint(JobEntity e)
    {
        const char sep = '\u001f';
        var material = string.Join(sep, new[]
        {
            e.Status,
            e.StatusHistoryJson,
            e.ScriptText,
            e.AudioUrl,
            e.AudioSha256,
            e.ApprovedBy,
            e.ApprovedByObjectId,
            e.ApprovedAt?.ToString("O"),
            e.PublishedBy,
            e.PublishedByObjectId,
            e.PublishedAt?.ToString("O"),
            e.DeliveryIdempotencyKey,
            e.DeliveryReviewedEtag,
            e.DeliveryKind,
            e.DeliveryRequestedBy,
            e.DeliveryRequestedByObjectId,
            e.DeliveryFallbackSourceJobId,
            e.DeliveryFallbackAudioUrl,
            e.DeliveryFallbackReason,
            e.DeliveryFallbackNotes,
            e.DeliveryFallbackDurationSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            e.DeliveryAttemptCount.ToString(),
            e.DeliveryLastAttemptAt?.ToString("O"),
            e.DeliveryLastError,
            e.ErrorMessage,
            e.AttemptCount.ToString(),
            e.FallbackStreak.ToString(),
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    internal JobEntity MapToEntity(Job job)
    {
        return new JobEntity
        {
            PartitionKey = JobPartition(job.Market),
            RowKey = job.JobId,
            Station = job.Station,
            Market = job.Market,
            TargetDurationSeconds = job.TargetDurationSeconds,
            CreatedAt = job.CreatedAt,
            LastUpdatedAt = job.LastUpdatedAt,
            Status = job.Status.ToString(),
            MeasuredDurationSec = job.MeasuredDurationSec.ToNullable(),
            ScriptText = job.ScriptText.ToNullable(),
            TaggedScriptText = job.TaggedScriptText.ToNullable(),
            DeliveryStyleId = job.DeliveryStyleId.ToNullable(),
            DeliveryStyleVersion = job.DeliveryStyleVersion.ToNullable(),
            VoiceProfileId = job.VoiceProfileId.ToNullable(),
            VoiceProfileVersion = job.VoiceProfileVersion.ToNullable(),
            BookendId = job.BookendId.ToNullable(),
            BookendVersion = job.BookendVersion.ToNullable(),
            PronunciationDictionaryId = job.PronunciationDictionaryId.ToNullable(),
            PronunciationVersionId = job.PronunciationVersionId.ToNullable(),
            GlobalPronunciationDictionaryId = job.GlobalPronunciationDictionaryId.ToNullable(),
            GlobalPronunciationVersionId = job.GlobalPronunciationVersionId.ToNullable(),
            AudioUrl = job.AudioUrl.ToNullable(),
            AudioSha256 = job.AudioSha256,
            ErrorMessage = job.ErrorMessage.ToNullable(),
            ScheduledForUtc = job.ScheduledForUtc.ToNullableOffset(),
            CurationDeadlineUtc = job.CurationDeadlineUtc.ToNullableOffset(),
            FallbackDeadlineUtc = job.FallbackDeadlineUtc.ToNullableOffset(),
            MissedDeadlineUtc = job.MissedDeadlineUtc.ToNullableOffset(),
            FallbackSourceJobId = job.FallbackSourceJobId.ToNullable(),
            FallbackAppliedAt = job.FallbackAppliedAt.ToNullableOffset(),
            FallbackReason = job.FallbackReason.ToNullable(),
            FallbackStreak = job.FallbackStreak,
            ApprovedBy = job.ApprovedBy,
            ApprovedByObjectId = job.ApprovedByObjectId,
            ApprovedAt = job.ApprovedAt,
            PublishedBy = job.PublishedBy,
            PublishedByObjectId = job.PublishedByObjectId,
            PublishedAt = job.PublishedAt,
            DeliveryIdempotencyKey = job.DeliveryIdempotencyKey,
            DeliveryReviewedEtag = job.DeliveryReviewedEtag,
            DeliveryKind = job.DeliveryKind.Map(k => k.ToString()).ToNullable(),
            DeliveryRequestedBy = job.DeliveryRequestedBy,
            DeliveryRequestedByObjectId = job.DeliveryRequestedByObjectId,
            DeliveryFallbackSourceJobId = job.DeliveryFallbackSourceJobId,
            DeliveryFallbackAudioUrl = job.DeliveryFallbackAudioUrl,
            DeliveryFallbackReason = job.DeliveryFallbackReason,
            DeliveryFallbackNotes = job.DeliveryFallbackNotes,
            DeliveryFallbackDurationSeconds = job.DeliveryFallbackDurationSeconds,
            DeliveryAttemptCount = job.DeliveryAttemptCount,
            DeliveryLastAttemptAt = job.DeliveryLastAttemptAt,
            DeliveryLastError = job.DeliveryLastError,
            PreviousJobId = job.PreviousJobId.ToNullable(),
            PreviousScriptContext = job.PreviousScriptContext.ToNullable(),
            IncidentChangeSummaryJson = job.IncidentChangeSummary
                .Map(s => JsonSerializer.Serialize(s, JsonOptions)).ToNullable(),
            NarrativeIncidentChangeSummaryJson = job.NarrativeIncidentChangeSummary
                .Map(s => JsonSerializer.Serialize(s, JsonOptions)).ToNullable(),
            FailedAtStatus = job.FailedAtStatus.Map(s => s.ToString()).ToNullable(),
            AttemptCount = job.AttemptCount,
            LastEstimatedDurationSec = job.LastEstimatedDurationSec.ToNullable(),
            LogsJson = FitJobHistoryJson(job.Logs, l => l.At, "LogsJson", job.JobId),
            ScriptSentencesJson = FitTableProperty(
                job.ScriptSentences.Count > 0 ? JsonSerializer.Serialize(job.ScriptSentences, JsonOptions) : null,
                "ScriptSentencesJson", job.JobId),
            IncidentsJson = FitTableProperty(JsonSerializer.Serialize(job.Incidents, JsonOptions),
                "IncidentsJson", job.JobId),
            AllIncidentsJson = FitTableProperty(
                job.AllIncidents.Count > 0 ? JsonSerializer.Serialize(job.AllIncidents, JsonOptions) : null,
                "AllIncidentsJson", job.JobId),
            CorridorsJson = FitTableProperty(JsonSerializer.Serialize(job.Corridors, JsonOptions),
                "CorridorsJson", job.JobId),
            FlowRoadsJson = FitTableProperty(
                job.FlowRoads.Count > 0 ? JsonSerializer.Serialize(job.FlowRoads, JsonOptions) : null,
                "FlowRoadsJson", job.JobId),
            IncidentsFetchedAt = job.IncidentsFetchedAt.ToNullableOffset(),
            VoiceConfigJson = job.VoiceConfig.Map(v => JsonSerializer.Serialize(v, JsonOptions)).ToNullable(),
            WordTimingsJson = job.WordTimings.Map(t => JsonSerializer.Serialize(t, JsonOptions)).ToNullable(),
            PreReviewJson = job.PreReview.Map(p => JsonSerializer.Serialize(p, JsonOptions)).ToNullable(),
            SupersededTakeJson = FitTableProperty(
                job.SupersededTake.Map(t => JsonSerializer.Serialize(t, JsonOptions)).ToNullable(),
                "SupersededTakeJson", job.JobId),
            RevoiceObservationJson = FitTableProperty(
                job.PendingRevoiceObservation.Map(o => JsonSerializer.Serialize(o, JsonOptions)).ToNullable(),
                "RevoiceObservationJson", job.JobId),
            ContentSelectionJson = job.ContentSelection.Map(c => JsonSerializer.Serialize(c, JsonOptions)).ToNullable(),
            WeatherJson = job.Weather.Map(w => JsonSerializer.Serialize(w, JsonOptions)).ToNullable(),
            FuelJson = job.Fuel.Map(f => JsonSerializer.Serialize(f, JsonOptions)).ToNullable(),
            GoodRunRoadsJson = job.GoodRunRoads.Count > 0
                ? JsonSerializer.Serialize(job.GoodRunRoads, JsonOptions)
                : null,
            MonitoredRoutesJson = job.MonitoredRoutes.Count > 0
                ? JsonSerializer.Serialize(job.MonitoredRoutes, JsonOptions)
                : null,
            CoverageJson = job.Coverage.Map(c => JsonSerializer.Serialize(c, JsonOptions)).ToNullable(),
            SourceDiagnosticsJson = FitTableProperty(
                job.SourceDiagnostics.Count > 0
                    ? JsonSerializer.Serialize(job.SourceDiagnostics, SourceDiagnosticsJsonOptions)
                    : null,
                "SourceDiagnosticsJson", job.JobId),
            AttemptHistoryJson = JsonSerializer.Serialize(job.AttemptHistory, JsonOptions),
            RejectionHistoryJson = FitJobHistoryJson(job.RejectionHistory, r => r.At, "RejectionHistoryJson",
                job.JobId),
            StatusHistoryJson = JsonSerializer.Serialize(job.StatusHistory, JsonOptions),
            GeneratorInputJson = FitTableProperty(
                job.GeneratorInput is null ? null : JsonSerializer.Serialize(job.GeneratorInput, JsonDefaults.Storage),
                "GeneratorInputJson", job.JobId),
            Latitude = job.Latitude.ToNullable(),
            Longitude = job.Longitude.ToNullable(),
            RadiusKm = job.RadiusKm.ToNullable(),
            TimezoneId = job.TimezoneId.ToNullable()
        };
    }
}

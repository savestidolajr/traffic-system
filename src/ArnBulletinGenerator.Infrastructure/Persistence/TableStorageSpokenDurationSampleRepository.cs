using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Table Storage store for measured spoken-duration samples.
///     Partition key: the calibration fingerprint (<c>voiceId|modelId|spN|pbN</c>) so every read for
///     one fitted scale lives in one partition and the fit is a single partition scan.
///     Row key: <c>{CapturedAt:yyyyMMddTHHmmssfff}_{jobId}</c> — chronological within the partition,
///     and idempotent per (instant, job) so a retried write cannot double-count a measurement into
///     the scale.
/// </summary>
public sealed class TableStorageSpokenDurationSampleRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageSpokenDurationSampleRepository> logger)
    : TableRepositoryBase(TableName, options, credential, logger), ISpokenDurationSampleRepository
{
    private const string TableName = "spokenDurationSamples";

    /// <summary>Table Storage rejects a property over 32 KB; a bulletin is ~400 chars, so this only
    ///     ever bites on a pathological input and truncating is better than losing the sample.</summary>
    private const int MaxTextLength = 4000;

    private readonly ILogger<TableStorageSpokenDurationSampleRepository> _logger = logger;

    public async Task RecordAsync(SpokenDurationSample sample, CancellationToken cancellationToken = default)
    {
        // Fail-soft by contract: this is telemetry for a duration model and must never be able to
        // fail a bulletin. A dropped sample costs a little accuracy on the next fit; a thrown
        // exception here would cost a broadcast.
        try
        {
            var entity = new TableEntity(sample.Fingerprint, RowKeyFor(sample))
            {
                ["VoiceId"] = sample.VoiceId,
                ["ModelId"] = sample.ModelId,
                ["Speed"] = sample.Speed,
                ["PaceBoostWpm"] = sample.PaceBoostWpm,
                ["ShapeTotal"] = sample.ShapeTotal,
                ["NaturalSeconds"] = sample.NaturalSeconds,
                ["TargetSeconds"] = sample.TargetSeconds,
                ["WordCount"] = sample.WordCount,
                ["SyllableCount"] = sample.SyllableCount,
                ["ImpliedScale"] = sample.ImpliedScale,
                ["SpokenText"] = Truncate(sample.SpokenText),
                ["JobId"] = sample.JobId,
                ["CapturedAt"] = sample.CapturedAt
            };

            await ExecuteAsync(() => TableClient.UpsertEntityAsync(
                entity, TableUpdateMode.Replace, cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DurationSample] Could not record sample for {Fingerprint}; the fit will be one observation short.",
                sample.Fingerprint);
        }
    }

    public async Task<IReadOnlyList<SpokenDurationSample>> GetSamplesAsync(
        string fingerprint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await QueryEntitiesAsync<TableEntity>(
                $"PartitionKey eq '{fingerprint.Replace("'", "''")}'", cancellationToken);
            return entities
                .Select(MapToDomain)
                .OrderByDescending(s => s.CapturedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DurationSample] Could not read samples for {Fingerprint}.", fingerprint);
            return [];
        }
    }

    private static string RowKeyFor(SpokenDurationSample s) =>
        $"{s.CapturedAt.UtcDateTime:yyyyMMddTHHmmssfff}_{(string.IsNullOrWhiteSpace(s.JobId) ? "adhoc" : s.JobId)}";

    private static string Truncate(string? text) =>
        string.IsNullOrEmpty(text) ? "" : text[..Math.Min(MaxTextLength, text.Length)];

    private static SpokenDurationSample MapToDomain(TableEntity e) => new()
    {
        VoiceId = e.GetString("VoiceId") ?? "",
        ModelId = e.GetString("ModelId") ?? "",
        Speed = e.GetDouble("Speed") ?? 1.0,
        PaceBoostWpm = e.GetDouble("PaceBoostWpm") ?? 0,
        ShapeTotal = e.GetDouble("ShapeTotal") ?? 0,
        NaturalSeconds = e.GetDouble("NaturalSeconds") ?? 0,
        TargetSeconds = e.GetDouble("TargetSeconds") ?? 0,
        WordCount = e.GetInt32("WordCount") ?? 0,
        SyllableCount = e.GetInt32("SyllableCount") ?? 0,
        SpokenText = e.GetString("SpokenText") ?? "",
        JobId = e.GetString("JobId") ?? "",
        CapturedAt = e.GetDateTimeOffset("CapturedAt") ?? e.Timestamp ?? DateTimeOffset.UtcNow
    };
}

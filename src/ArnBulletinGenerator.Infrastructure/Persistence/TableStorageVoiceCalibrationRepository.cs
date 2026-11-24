using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// Partition key format: "Calibration" prefix — all calibrations share one partition (low volume, acceptable).
// Row key format: {voiceId}_{settings configuration key hash} — enables fast lookup by voice+settings.

public sealed class TableStorageVoiceCalibrationRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageVoiceCalibrationRepository> logger)
    : TableRepositoryBase(TableName, options, credential, logger), IVoiceCalibrationRepository
{
    private const string TableName = "radioVoiceCalibrations";
    private const string PartitionKeyPrefix = "Calibration";

    public async Task<Result<IReadOnlyDictionary<string, Option<VoiceCalibration>>>> GetManyAsync(
        IReadOnlyCollection<VoiceCalibrationLookup> lookups,
        CancellationToken cancellationToken = default)
    {
        var unique = lookups.Distinct().ToArray();
        using var gate = new SemaphoreSlim(8, 8);
        var reads = await Task.WhenAll(unique.Select(async lookup =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string modelId = string.IsNullOrWhiteSpace(lookup.ModelId) ? "unknown" : lookup.ModelId.Trim();
                var result = modelId.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                    ? await GetCalibrationAsync(lookup.VoiceId, lookup.Settings, cancellationToken)
                    : await GetCalibrationAsync(lookup.VoiceId, lookup.Settings, cancellationToken, modelId);
                return (lookup, result);
            }
            finally
            {
                gate.Release();
            }
        }));

        var values = new Dictionary<string, Option<VoiceCalibration>>(StringComparer.Ordinal);
        foreach (var read in reads)
        {
            if (read.result.IsFailure)
                return Result<IReadOnlyDictionary<string, Option<VoiceCalibration>>>.Failure(read.result.Error);
            values[read.lookup.Key] = read.result.Value;
        }

        return Result<IReadOnlyDictionary<string, Option<VoiceCalibration>>>.Success(values);
    }

    public async Task<Result<Option<VoiceCalibration>>> GetCalibrationAsync(
        string voiceId,
        VoiceSettings settings,
        CancellationToken cancellationToken)
    {
        // Legacy callers intentionally read the pre-model key so existing calibration rows remain
        // visible during the model-aware rollout.
        var result = await GetEntityOrDefaultAsync<CalibrationEntity>(
            PartitionKeyPrefix, settings.GetConfigurationKey(voiceId), cancellationToken);
        if (result.IsFailure)
            return Result<Option<VoiceCalibration>>.Failure(result.Error);
        return result.Value.HasValue
            ? Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.Some(MapToDomain(result.Value.Value)))
            : Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None);
    }

    public async Task<Result<Option<VoiceCalibration>>> GetCalibrationAsync(
        string voiceId,
        VoiceSettings settings,
        CancellationToken cancellationToken,
        string modelId)
    {
        string modelKey = settings.GetCalibrationConfigurationKey(voiceId, modelId);
        var result = await GetEntityOrDefaultAsync<CalibrationEntity>(
            PartitionKeyPrefix, modelKey, cancellationToken);
        if (result.IsFailure)
            return Result<Option<VoiceCalibration>>.Failure(result.Error);
        if (result.Value.HasValue)
            return Result<Option<VoiceCalibration>>.Success(
                Option<VoiceCalibration>.Some(MapToDomain(result.Value.Value)));

        // A model-specific miss falls back to the bounded legacy row. This keeps existing
        // calibrations useful while new model-specific rows are populated.
        var legacy = await GetEntityOrDefaultAsync<CalibrationEntity>(
            PartitionKeyPrefix, settings.GetConfigurationKey(voiceId), cancellationToken);
        if (legacy.IsFailure)
            return Result<Option<VoiceCalibration>>.Failure(legacy.Error);
        return legacy.Value.HasValue
            ? Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.Some(MapToDomain(legacy.Value.Value)))
            : Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None);
    }

    public async Task<Result<IReadOnlyList<VoiceCalibration>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<CalibrationEntity>(
                $"PartitionKey eq '{PartitionKeyPrefix}'", cancellationToken);
            return entities.Select(MapToDomain).ToList() as IReadOnlyList<VoiceCalibration>;
        });
    }

    public async Task<Result> SaveCalibrationAsync(
        VoiceCalibration calibration,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var entity = MapToEntity(calibration);
            await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        });
    }

    private static VoiceCalibration MapToDomain(CalibrationEntity entity)
    {
        var settings = VoiceSettings.Create(
            entity.Stability,
            entity.SimilarityBoost,
            entity.Style,
            entity.UseSpeakerBoost,
            entity.Speed).Value;

        double syllablesPerSecond = entity.SyllablesPerSecond;
        double syllablesPerMinute = entity.SyllablesPerMinute > 0
            ? entity.SyllablesPerMinute
            : syllablesPerSecond * 60.0;
        double syllablesPerWord = entity.SyllablesPerWord > 0
            ? entity.SyllablesPerWord
            : CalibrationDefaults.SyllablesPerWord;
        double wordsPerMinute = entity.WordsPerMinute > 0
            ? entity.WordsPerMinute
            : syllablesPerWord > 0
                ? syllablesPerMinute / syllablesPerWord
                : 0;

        return new VoiceCalibration(entity.RowKey!, entity.VoiceId, settings, entity.ModelId)
        {
            Provider = entity.Provider,
            SyllablesPerSecond = syllablesPerSecond,
            SyllablesPerWord = syllablesPerWord,
            WordsPerMinute = wordsPerMinute,
            SyllablesPerMinute = syllablesPerMinute,
            RunCount = entity.RunCount,
            ReferenceSyllableCount = entity.ReferenceSyllableCount,
            ReferenceDurationSeconds = entity.ReferenceDurationSeconds,
            CalibratedAt = entity.CalibratedAt,
            ValidFor = TimeSpan.FromDays(entity.ValidForDays)
        };
    }

    private static CalibrationEntity MapToEntity(VoiceCalibration calibration)
    {
        return new CalibrationEntity
        {
            ModelId = calibration.ModelId,
            PartitionKey = PartitionKeyPrefix,
            RowKey = calibration.ConfigurationKey,
            VoiceId = calibration.VoiceId,
            Provider = calibration.Provider,
            SyllablesPerSecond = calibration.SyllablesPerSecond,
            SyllablesPerWord = calibration.SyllablesPerWord > 0
                ? calibration.SyllablesPerWord
                : CalibrationDefaults.SyllablesPerWord,
            WordsPerMinute = calibration.WordsPerMinute,
            SyllablesPerMinute = calibration.SyllablesPerMinute,
            RunCount = calibration.RunCount,
            ReferenceSyllableCount = calibration.ReferenceSyllableCount,
            ReferenceDurationSeconds = calibration.ReferenceDurationSeconds,
            CalibratedAt = calibration.CalibratedAt,
            ValidForDays = calibration.ValidFor.TotalDays,
            Stability = calibration.Settings.Stability,
            SimilarityBoost = calibration.Settings.SimilarityBoost,
            Style = calibration.Settings.Style,
            UseSpeakerBoost = calibration.Settings.UseSpeakerBoost,
            Speed = calibration.Settings.Speed
        };
    }

    internal sealed class CalibrationEntity : ITableEntity
    {
        public string VoiceId { get; set; } = string.Empty;
        public string ModelId { get; set; } = "unknown";
        public string Provider { get; set; } = ProviderNames.ElevenLabs;
        public double SyllablesPerSecond { get; set; }
        public double SyllablesPerWord { get; set; } = CalibrationDefaults.SyllablesPerWord;
        public double WordsPerMinute { get; set; }
        public double SyllablesPerMinute { get; set; }
        public int RunCount { get; set; }

        public int ReferenceSyllableCount { get; set; }
        public double ReferenceDurationSeconds { get; set; }
        public DateTime CalibratedAt { get; set; }
        public double ValidForDays { get; set; } = 30;

        // Flattened VoiceSettings
        public double Stability { get; set; }
        public double SimilarityBoost { get; set; }
        public double Style { get; set; }
        public bool UseSpeakerBoost { get; set; }
        public double Speed { get; set; }
        public string? PartitionKey { get; set; }
        public string? RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

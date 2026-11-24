using System.Collections.Concurrent;
using System.Globalization;
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
///     Azure Table Storage-backed synthesis history repository with in-memory partition caching.
///     Stores each <c>SynthesisRecord</c> as a separate row in the <c>synthesisHistory</c> table.
///     Partition key: <c>{voiceId}_{modelId}_{speed:F2}</c> — enables efficient querying
///     by voice+model+speed combination.
///     Row key: <c>{Timestamp:yyyy-MM-dd}_{Guid:N}</c> — enables chronological ordering.
///     Table is auto-created on first access via <see cref="TableRepositoryBase" />.
/// </summary>
public sealed class TableStorageSynthesisHistoryRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageSynthesisHistoryRepository> logger)
    : TableRepositoryBase(TableName, options, credential, logger), ISynthesisHistoryRepository
{
    private const string TableName = "synthesisHistory";
    private const int HistorySafetyCap = 10_000;
    private readonly ILogger<TableStorageSynthesisHistoryRepository> _logger = logger;
    private readonly ConcurrentDictionary<string, (DateTimeOffset CachedAt, List<SynthesisHistoryEntity> Entities)> _partitionCache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    // ─── ISynthesisHistoryRepository ──────────────────────────────────

    public int Count
    {
        get
        {
            var entities = QueryAllEntities();
            return entities.Count;
        }
    }

    public void Record(SynthesisRecord record)
    {
        var entity = MapToEntity(record);

        // Invalidate in-memory cache for this partition and record locally
        _partitionCache.AddOrUpdate(
            entity.PartitionKey,
            _ => (DateTimeOffset.UtcNow, [entity]),
            (_, existing) =>
            {
                var list = new List<SynthesisHistoryEntity>(existing.Entities) { entity };
                return (DateTimeOffset.UtcNow, list);
            });

        ExecuteAsync(async () =>
            {
                await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace);
            })
            .GetAwaiter()
            .GetResult();
    }

    public IReadOnlyList<SynthesisRecord> FindClosestBySyllables(
        string voiceId,
        string modelId,
        double speed,
        int syllableCount,
        int maxResults = 5)
    {
        var entities = QueryByPartitionKey(FormatPartitionKey(voiceId, modelId, speed));

        return entities
            .Select(MapToDomain)
            .OrderBy(r => Math.Abs(r.SyllableCount - syllableCount))
            .Take(maxResults)
            .ToList();
    }

    public IReadOnlyList<SynthesisRecord> FindClosestByDuration(
        string voiceId,
        string modelId,
        double speed,
        double targetDuration,
        int maxResults = 5)
    {
        var entities = QueryByPartitionKey(FormatPartitionKey(voiceId, modelId, speed));

        return entities
            .Select(MapToDomain)
            .OrderBy(r => Math.Abs(r.AchievedDuration - targetDuration))
            .Take(maxResults)
            .ToList();
    }

    public IReadOnlyList<SynthesisRecord> GetHistory(string voiceId, string modelId)
    {
        var entities = QueryByVoiceAndModel(voiceId, modelId);

        return entities
            .Select(MapToDomain)
            .OrderByDescending(r => r.Timestamp)
            .ToList();
    }

    // ─── Query helpers ────────────────────────────────────────────────

    /// <summary>
    ///     Queries all entities matching the exact partition key.
    ///     Used by <see cref="FindClosestBySyllables" /> and <see cref="FindClosestByDuration" />
    ///     where voice+model+speed are all known.
    /// </summary>
    private List<SynthesisHistoryEntity> QueryByPartitionKey(string partitionKey)
    {
        if (_partitionCache.TryGetValue(partitionKey, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < CacheTtl)
        {
            return cached.Entities;
        }

        string filter = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
        var results = RunQuery(filter);
        _partitionCache[partitionKey] = (DateTimeOffset.UtcNow, results);
        return results;
    }

    /// <summary>
    ///     Queries all entities for a given voice+model across all speeds.
    ///     Uses a prefix range scan on PartitionKey.
    /// </summary>
    private List<SynthesisHistoryEntity> QueryByVoiceAndModel(string voiceId, string modelId)
    {
        string prefix = $"{voiceId}_{modelId}_";
        // Use prefix-range scan: PartitionKey >= prefix AND PartitionKey < prefix + high-char
        // This captures all speeds for this voice+model combination.
        string filter = TableClient.CreateQueryFilter(
            $"PartitionKey ge {prefix} and PartitionKey lt {prefix}\uffff");
        return RunQuery(filter, maxRows: HistorySafetyCap);
    }

    /// <summary>
    ///     Returns all entities in the table (used for <see cref="Count" />).
    /// </summary>
    private List<SynthesisHistoryEntity> QueryAllEntities()
    {
        // Count only needs the key columns; avoid transferring synthesis payloads.
        return RunQuery(null, [
            nameof(SynthesisHistoryEntity.PartitionKey),
            nameof(SynthesisHistoryEntity.RowKey)]);
    }

    /// <summary>
    ///     Executes a query synchronously with error handling.
    /// </summary>
    private List<SynthesisHistoryEntity> RunQuery(
        string? filter,
        IEnumerable<string>? select = null,
        int? maxRows = null)
    {
        try
        {
            var query = maxRows.HasValue
                ? QueryBoundedAsync(filter, select, maxRows.Value)
                : select is null
                    ? QueryEntitiesAsync<SynthesisHistoryEntity>(filter)
                    : QueryEntitiesAsync<SynthesisHistoryEntity>(filter, CancellationToken.None, select);
            return query.GetAwaiter().GetResult();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex,
                "Table Storage query failed for synthesis history. Filter: {Filter}", filter);
            return [];
        }
    }

    private async Task<List<SynthesisHistoryEntity>> QueryBoundedAsync(
        string? filter,
        IEnumerable<string>? select,
        int maxRows)
    {
        await EnsureTableExistsAsync();
        var query = TableClient.QueryAsync<SynthesisHistoryEntity>(
            filter,
            maxPerPage: Math.Min(maxRows + 1, 1_000),
            select: select,
            cancellationToken: CancellationToken.None);
        var results = new List<SynthesisHistoryEntity>(Math.Min(maxRows, 1_000));
        await foreach (var entity in query)
        {
            results.Add(entity);
            if (results.Count <= maxRows)
                continue;

            _logger.LogWarning(
                "Synthesis history query reached safety cap of {Cap} rows; additional rows were ignored.",
                maxRows);
            results.RemoveRange(maxRows, results.Count - maxRows);
            break;
        }

        return results;
    }

    // ─── Mapping ──────────────────────────────────────────────────────

    private static string FormatPartitionKey(string voiceId, string modelId, double speed)
    {
        string speedFormatted = speed.ToString("F2", CultureInfo.InvariantCulture);
        return $"{voiceId}_{modelId}_{speedFormatted}";
    }

    private static SynthesisRecord MapToDomain(SynthesisHistoryEntity entity)
    {
        return new SynthesisRecord
        {
            VoiceId = entity.VoiceId,
            ModelId = entity.ModelId,
            Seed = entity.Seed,
            Speed = entity.Speed,
            SyllableCount = entity.SyllableCount,
            WordCount = entity.WordCount,
            AchievedDuration = entity.AchievedDuration,
            TargetDuration = entity.TargetDuration,
            ScriptPreview = entity.ScriptPreview ?? "",
            Timestamp = entity.SynthesizedAt,
            WasWithinTolerance = entity.WasWithinTolerance
        };
    }

    private static SynthesisHistoryEntity MapToEntity(SynthesisRecord record)
    {
        string partitionKey = FormatPartitionKey(record.VoiceId, record.ModelId, record.Speed);
        string rowKey = $"{record.Timestamp:yyyy-MM-dd}_{Guid.NewGuid():N}";

        return new SynthesisHistoryEntity
        {
            PartitionKey = partitionKey,
            RowKey = rowKey,
            VoiceId = record.VoiceId,
            ModelId = record.ModelId,
            Seed = record.Seed,
            Speed = record.Speed,
            SyllableCount = record.SyllableCount,
            WordCount = record.WordCount,
            AchievedDuration = record.AchievedDuration,
            TargetDuration = record.TargetDuration,
            ScriptPreview = record.ScriptPreview,
            SynthesizedAt = record.Timestamp,
            WasWithinTolerance = record.WasWithinTolerance
        };
    }

    // ─── Entity ───────────────────────────────────────────────────────

    internal sealed class SynthesisHistoryEntity : ITableEntity
    {
        // Domain data
        public string VoiceId { get; set; } = string.Empty;
        public string ModelId { get; set; } = string.Empty;
        public int? Seed { get; set; }
        public double Speed { get; set; }
        public int SyllableCount { get; set; }
        public int WordCount { get; set; }
        public double AchievedDuration { get; set; }
        public double TargetDuration { get; set; }
        public string? ScriptPreview { get; set; }
        public DateTimeOffset SynthesizedAt { get; set; }
        public bool WasWithinTolerance { get; set; }

        // ITableEntity
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

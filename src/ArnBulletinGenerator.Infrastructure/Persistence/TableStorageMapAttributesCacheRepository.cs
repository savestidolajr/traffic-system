using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ArnBulletinGenerator.Domain.Interfaces.Repositories;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
/// Azure Table Storage implementation of the persistent HERE map-attributes cache.
/// Rows use a stable 16-way shard keyed by HERE's segment id, so direction and offset variants
/// of the same physical segment share one persistent value.
/// </summary>
public sealed class TableStorageMapAttributesCacheRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageMapAttributesCacheRepository> logger,
    IOptions<MapAttributesCacheOptions>? cacheOptions = null)
    : TableRepositoryBase("mapAttributesCache", options, credential, logger), IMapAttributesCacheRepository
{
    private readonly TimeSpan _positiveTtl =
        TimeSpan.FromHours(Math.Max(1, cacheOptions?.Value.PositiveTtlHours ?? 24));

    private static string GetRowKey(string segmentRef) =>
        Uri.EscapeDataString(HereSegmentReference.CacheIdentity(segmentRef));

    /// <summary>Returns the stable shard for a segment id. The first SHA-256 nibble gives 16 shards.</summary>
    internal static string GetPartitionKey(string segmentRef)
    {
        byte hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(HereSegmentReference.CacheIdentity(segmentRef)))[0];
        return $"map-{hash >> 4:x1}";
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, SegmentRoad>> GetBatchAsync(
        IReadOnlyList<string> segmentRefs, CancellationToken cancellationToken = default)
    {
        var resolved = new Dictionary<string, SegmentRoad>(StringComparer.Ordinal);
        string[] requested = segmentRefs.Distinct(StringComparer.Ordinal)
            .Where(reference => !string.IsNullOrWhiteSpace(reference)).ToArray();
        var requestedByIdentity = requested
            .GroupBy(HereSegmentReference.CacheIdentity, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (requested.Length == 0)
            return resolved;
        DateTimeOffset freshAfter = DateTimeOffset.UtcNow - _positiveTtl;

        foreach (var group in requested.GroupBy(GetPartitionKey, StringComparer.Ordinal))
        {
            var entities = await QueryReferencesAsync(group.Key, group, cancellationToken);
            foreach (MapAttributesCacheEntity entity in entities.Where(entity => entity.CachedAtUtc >= freshAfter))
            {
                if (!requestedByIdentity.TryGetValue(GetEntityReference(entity), out string[]? references))
                    continue;
                foreach (string reference in references)
                    AddResolved(entity, reference, resolved);
            }

            if (requested.All(resolved.ContainsKey))
                break;
        }

        return resolved;
    }

    private async Task<List<MapAttributesCacheEntity>> QueryReferencesAsync(
        string partitionKey, IEnumerable<string> references, CancellationToken cancellationToken)
    {
        string[] rowKeys = references.Select(GetRowKey).Distinct(StringComparer.Ordinal).ToArray();
        if (rowKeys.Length == 0)
            return [];

        await EnsureTableExistsAsync(cancellationToken);
        var results = new ConcurrentBag<MapAttributesCacheEntity>();
        await Parallel.ForEachAsync(
            rowKeys.Chunk(32),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 16
            },
            async (rowKeyBatch, ct) =>
            {
                string keyFilter = string.Join(
                    " or ",
                    rowKeyBatch.Select(rowKey => $"RowKey eq '{EscapeOData(rowKey)}'"));
                string filter = $"PartitionKey eq '{EscapeOData(partitionKey)}' and ({keyFilter})";
                var entities = await QueryEntitiesAsync<MapAttributesCacheEntity>(
                    filter,
                    ct,
                    [nameof(MapAttributesCacheEntity.SegmentRef), nameof(MapAttributesCacheEntity.RoadName),
                     nameof(MapAttributesCacheEntity.FunctionalClass), nameof(MapAttributesCacheEntity.IsRamp),
                     nameof(MapAttributesCacheEntity.TravelDirection), nameof(MapAttributesCacheEntity.PhysicalLaneCount),
                     nameof(MapAttributesCacheEntity.CachedAtUtc)]);
                foreach (MapAttributesCacheEntity entity in entities)
                    results.Add(entity);
            });
        return [.. results];
    }

    private static string GetEntityReference(MapAttributesCacheEntity entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.SegmentRef))
            return entity.SegmentRef;
        return string.IsNullOrWhiteSpace(entity.RowKey) ? string.Empty : Uri.UnescapeDataString(entity.RowKey);
    }

    private void AddResolved(
        MapAttributesCacheEntity entity,
        string requestedReference,
        IDictionary<string, SegmentRoad> resolved)
    {
        var road = new SegmentRoad(entity.RoadName, entity.FunctionalClass, entity.IsRamp,
            entity.TravelDirection, entity.PhysicalLaneCount);
        resolved[requestedReference] = road;
    }

    /// <inheritdoc />
    public async Task SaveBatchAsync(
        IReadOnlyDictionary<string, SegmentRoad> segmentRoads, CancellationToken cancellationToken = default)
    {
        if (segmentRoads.Count == 0)
            return;



        var entities = segmentRoads
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .GroupBy(kv => HereSegmentReference.CacheIdentity(kv.Key), StringComparer.Ordinal)
            .Select(group =>
            {
                var road = group.First().Value;
                return new MapAttributesCacheEntity
                {
                    PartitionKey = GetPartitionKey(group.Key),
                    RowKey = GetRowKey(group.Key),
                    SegmentRef = group.Key,
                    RoadName = road.Name,
                    FunctionalClass = road.FunctionalClass,
                    IsRamp = road.IsRamp,
                    TravelDirection = road.TravelDirection,
                    PhysicalLaneCount = road.PhysicalLaneCount,
                    CachedAtUtc = DateTimeOffset.UtcNow
                };
            })
            .ToList();

        foreach (var group in entities.GroupBy(e => e.PartitionKey, StringComparer.Ordinal))
        {
            foreach (MapAttributesCacheEntity[] batch in group.Chunk(100))
            {
                var actions = batch.Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e)).ToList();
                try
                {
                    await TableClient.SubmitTransactionAsync(actions, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "[MapAttributesCache] Batch write failed for shard {PartitionKey}; falling back to row writes", group.Key);
                    foreach (MapAttributesCacheEntity entity in batch)
                    {
                        try
                        {
                            await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
                        }
                        catch (Exception rowEx) when (rowEx is not OperationCanceledException)
                        {
                            logger.LogWarning(rowEx, "[MapAttributesCache] Failed to persist segment road for {SegmentRef}", entity.SegmentRef);
                        }
                    }
                }
            }
        }
    }

    private static string EscapeOData(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    internal sealed class MapAttributesCacheEntity : ITableEntity
    {
        public string? RoadName { get; set; }
        public int FunctionalClass { get; set; }
        public bool IsRamp { get; set; }
        public string? TravelDirection { get; set; }
        public int? PhysicalLaneCount { get; set; }
        public string SegmentRef { get; set; } = string.Empty;
        public DateTimeOffset CachedAtUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

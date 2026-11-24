using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// Partition key format: {marketId} (e.g., "sydney", "melbourne") — enables fast lookup by market.
// Row key format: {incidentId} — unique incident identifier for idempotent updates.

public sealed class TableStorageIncidentStateRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageIncidentStateRepository> logger)
    : TableRepositoryBase("radioIncidentState", options, credential, logger), IIncidentStateRepository
{
    public async Task<Result<IEnumerable<IncidentState>>> GetCurrentByMarketAsync(
        string marketId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            string? filter = TableClient.CreateQueryFilter($"PartitionKey eq {marketId}");
            var entities = await QueryEntitiesAsync<IncidentStateEntity>(filter, ct);
            return entities.Select(MapToDomain).AsEnumerable();
        });
    }

    public async Task<Result> UpsertAsync(IncidentState state, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
            await TableClient.UpsertEntityAsync(MapToEntity(state), TableUpdateMode.Replace, ct));
    }

    public async Task<Result> UpsertBatchAsync(IEnumerable<IncidentState> states, CancellationToken ct = default)
    {
        var list = states.ToList();
        if (list.Count == 0) return Result.Success();

        return await ExecuteAsync(async () =>
        {
            // Group by partition key (Table Storage entity transactions must share the same PartitionKey)
            var byPartition = list.GroupBy(s => s.MarketId);
            foreach (var group in byPartition)
            {
                var partitionEntities = group.Select(MapToEntity).ToList();
                const int batchSize = 100; // Azure Table limit per transaction
                for (int i = 0; i < partitionEntities.Count; i += batchSize)
                {
                    var chunk = partitionEntities.Skip(i).Take(batchSize).ToList();
                    if (chunk.Count == 1)
                    {
                        await TableClient.UpsertEntityAsync(chunk[0], TableUpdateMode.Replace, ct);
                    }
                    else
                    {
                        var transactionActions = chunk
                            .Select(e => new TableTransactionAction(TableTransactionActionType.UpsertReplace, e))
                            .ToList();
                        await TableClient.SubmitTransactionAsync(transactionActions, ct);
                    }
                }
            }
        });
    }
    public async Task<Result> MarkClearedAsync(
        string marketId, string incidentId, DateTime clearedAtUtc, CancellationToken ct = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<IncidentStateEntity>(marketId, incidentId, ct);
        if (entityResult.IsFailure) return Result.Failure(entityResult.Error);
        if (entityResult.Value.IsNone) return Result.Success();

        var e = entityResult.Value.Value;
        var etag = e.ETag;
        e.Status = IncidentStateStatus.Cleared.ToString();
        e.UpdatedAt = clearedAtUtc;

        try
        {
            await TableClient.UpdateEntityAsync(e, etag, TableUpdateMode.Replace, ct);
            return Result.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return Result.Failure(
                $"Concurrent modification detected for incident '{incidentId}' in market '{marketId}'");
        }
        catch (RequestFailedException ex)
        {
            return Result.Failure(ex.Message);
        }
    }

    public async Task<Result> PurgeExpiredAsync(
        string marketId, DateTime olderThanUtc, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            string? filter = TableClient.CreateQueryFilter($"PartitionKey eq {marketId}");
            var entities = await QueryEntitiesAsync<IncidentStateEntity>(filter, ct);
            foreach (var e in entities.Where(e => e.LastSeenAt.UtcDateTime < olderThanUtc))
                await TableClient.DeleteEntityAsync(e.PartitionKey, e.RowKey, cancellationToken: ct);
        });
    }

    // ── Mapping ──────────────────────────────────────────────────────────

    private static IncidentState MapToDomain(IncidentStateEntity e)
    {
        return new IncidentState(e.RowKey, e.PartitionKey,
            e.FirstSeenAt.UtcDateTime, e.LastSeenAt.UtcDateTime,
            Enum.TryParse<IncidentStateStatus>(e.Status, out var s) ? s : IncidentStateStatus.Active,
            e.Type, e.Criticality, e.LengthMeters,
            e.DescriptionHash, e.Source, e.UpdatedAt.UtcDateTime)
        {
            LastReportedAt = e.LastReportedAt.ToUtcOption(),
            RoadName = e.RoadName.ToOption(),
            Suburb = e.Suburb.ToOption(),
            Direction = e.Direction.ToOption()
        };
    }

    private static IncidentStateEntity MapToEntity(IncidentState s)
    {
        return new IncidentStateEntity
        {
            PartitionKey = s.MarketId,
            RowKey = s.IncidentId,
            FirstSeenAt = s.FirstSeenAt,
            LastSeenAt = s.LastSeenAt,
            LastReportedAt = s.LastReportedAt.ToNullableOffset(),
            Status = s.Status.ToString(),
            RoadName = s.RoadName.ToNullable(),
            Suburb = s.Suburb.ToNullable(),
            Direction = s.Direction.ToNullable(),
            Type = s.Type,
            Criticality = s.Criticality,
            LengthMeters = s.LengthMeters,
            DescriptionHash = s.DescriptionHash,
            Source = s.Source,
            UpdatedAt = s.UpdatedAt
        };
    }

    internal sealed class IncidentStateEntity : ITableEntity
    {
        public DateTimeOffset FirstSeenAt { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
        public DateTimeOffset? LastReportedAt { get; set; }
        public string Status { get; set; } = IncidentStateStatus.Active.ToString();
        public string? RoadName { get; set; }
        public string? Suburb { get; set; }
        public string? Direction { get; set; }
        public string Type { get; set; } = string.Empty;
        public int Criticality { get; set; }
        public double LengthMeters { get; set; }
        public string DescriptionHash { get; set; } = string.Empty;
        public string Source { get; set; } = "HERE";
        public DateTimeOffset UpdatedAt { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

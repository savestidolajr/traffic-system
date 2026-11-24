using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.ValueObjects;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Microsoft.Extensions.Options;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Incremental scalar ledger buckets. One row per settled job, partitioned by normalized
///     market and UTC day and ordered by creation ticks, so the ledger can consume only the
///     requested window without scanning every aggregate row.
/// </summary>
public sealed class TableStorageLedgerBucketRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageLedgerBucketRepository> logger)
    : TableRepositoryBase("jobLedger", options, credential, logger), ILedgerBucketRepository
{
    public async Task<Result<LedgerBucketPage>> ReadPageAsync(
        string market,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var from = fromUtc.ToUniversalTime();
            var to = toUtc.ToUniversalTime();
            string lowerPartition = Partition(market, from.UtcDateTime);
            string upperPartition = Partition(market, to.UtcDateTime);
            string filter = TableClient.CreateQueryFilter(
                $"PartitionKey ge {lowerPartition} and PartitionKey le {upperPartition}");
            int boundedPageSize = Math.Clamp(pageSize, 1, 500);

            var items = new List<LedgerBucketEntry>(boundedPageSize);
            string? next = null;
            await foreach (var page in TableClient.QueryAsync<LedgerBucketEntity>(
                               filter, maxPerPage: boundedPageSize,
                               cancellationToken: cancellationToken).AsPages(
                                   continuationToken, boundedPageSize))
            {
                foreach (var entity in page.Values)
                {
                    if (entity.CreatedAt < from || entity.CreatedAt >= to)
                        continue;
                    items.Add(new LedgerBucketEntry(
                        entity.JobId, entity.Market, entity.Station, entity.CreatedAt,
                        entity.LastUpdatedAt, ParseStatus(entity.Status), entity.HasIncidents));
                }
                next = page.ContinuationToken;
                break;
            }

            // Azure Table returns rows in RowKey order within a partition. Do not materialise a
            // wider range or sort the market's job aggregates here; the scalar bucket cursor is
            // the ordering contract and EventLedgerFold performs its bounded fold order.
            return new LedgerBucketPage(items, next);
        });
    }

    public Task<Result> UpsertAsync(
        LedgerBucketEntry entry, CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            var entity = new LedgerBucketEntity
            {
                PartitionKey = Partition(entry.Market, entry.CreatedAt.UtcDateTime),
                RowKey = RowKey(entry.CreatedAt, entry.JobId),
                JobId = entry.JobId,
                Market = entry.Market,
                Station = entry.Station,
                CreatedAt = entry.CreatedAt,
                LastUpdatedAt = entry.LastUpdatedAt,
                Status = entry.Status.ToString(),
                HasIncidents = entry.HasIncidents
            };
            await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        });

    public async Task<Result> DeleteAsync(
        LedgerBucketEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureTableExistsAsync(cancellationToken);
            await TableClient.DeleteEntityAsync(
                Partition(entry.Market, entry.CreatedAt.UtcDateTime),
                RowKey(entry.CreatedAt, entry.JobId),
                cancellationToken: cancellationToken);
            return Result.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return Result.Success();
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex, "Failed to remove ledger bucket row for {JobId}", entry.JobId);
            return Result.Failure(TableRepositoryBase.SanitizeStorageMessage(ex.Message));
        }
    }

    internal static string Partition(string market, DateTime dateUtc) =>
        $"Ledger_{MarketKey.Normalize(market)}_{dateUtc:yyyyMMdd}";

    internal static string RowKey(DateTimeOffset createdAt, string jobId) =>
        $"{createdAt.UtcDateTime.Ticks:D19}|{jobId}";

    private static JobStatus ParseStatus(string? value) =>
        Enum.TryParse<JobStatus>(value, out var status) ? status : JobStatus.Requested;
}

internal sealed class LedgerBucketEntity : ITableEntity
{
    public string JobId { get; set; } = null!;
    public string Market { get; set; } = null!;
    public string Station { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public string Status { get; set; } = null!;
    public bool HasIncidents { get; set; }
    public string PartitionKey { get; set; } = null!;
    public string RowKey { get; set; } = null!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

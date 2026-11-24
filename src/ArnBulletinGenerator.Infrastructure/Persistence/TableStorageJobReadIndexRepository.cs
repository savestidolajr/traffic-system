using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.ValueObjects;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Scalar market/month and global/month job rows for bounded newest-first list reads. The
///     aggregate remains authoritative; rows are safe to rebuild and deletes are idempotent.
/// </summary>
public sealed class TableStorageJobReadIndexRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageJobReadIndexRepository> logger)
    : TableRepositoryBase("jobReadIndex", options, credential, logger), IJobReadIndexRepository
{
    private const int MaxIndexPartitions = 24;

    public async Task<Result> UpsertAsync(
        JobReadIndexItem item, CancellationToken cancellationToken = default)
        => await ExecuteAsync(async () =>
        {
            foreach (string partition in Partitions(item))
            {
                await TableClient.UpsertEntityAsync(ToEntity(item, partition),
                    TableUpdateMode.Replace, cancellationToken);
            }
        });

    public async Task<Result> DeleteAsync(
        JobReadIndexItem item, CancellationToken cancellationToken = default)
        => await ExecuteAsync(async () =>
        {
            foreach (string partition in Partitions(item))
            {
                try
                {
                    await TableClient.DeleteEntityAsync(
                        partition, RowKey(item), cancellationToken: cancellationToken);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // Reconciliation is idempotent when one side was never dual-written.
                }
            }
        });

    public async Task<Result<IReadOnlyList<JobReadIndexItem>>> FindByJobIdAsync(
        string jobId, CancellationToken cancellationToken = default)
        => await ExecuteAsync(async () =>
        {
            string escaped = jobId.Replace("'", "''", StringComparison.Ordinal);
            var entities = await QueryEntitiesAsync<JobReadIndexEntity>(
                $"JobId eq '{escaped}'", cancellationToken);
            return entities.Select(FromEntity).DistinctBy(item =>
                    $"{item.JobId}|{item.Market}|{item.CreatedAt.UtcDateTime.Ticks}")
                .ToList() as IReadOnlyList<JobReadIndexItem>;
        });

    public async Task<Result<IReadOnlyList<JobReadIndexItem>>> ReadPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default)
        => await ExecuteAsync(async () =>
        {
            int limit = Math.Min(Math.Max(query.Limit, 1), 2500);
            var partitions = MonthPartitions(query).ToList();
            if (partitions.Count > MaxIndexPartitions)
                return Array.Empty<JobReadIndexItem>() as IReadOnlyList<JobReadIndexItem>;

            var items = new List<JobReadIndexItem>(partitions.Count * limit);
            foreach (var partition in partitions)
            {
                string filter = BuildPartitionFilter(partition, query);
                var page = TableClient.QueryAsync<JobReadIndexEntity>(
                    filter, limit, null, cancellationToken);
                int fetched = 0;
                await foreach (var entity in page)
                {
                    items.Add(FromEntity(entity));
                    if (++fetched >= limit)
                        break;
                }
            }

            return items
                .Where(item => MatchesQuery(item, query))
                .GroupBy(item => item.JobId, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(item => item.LastUpdatedAt)
                    .First())
                .OrderByDescending(item => item.LastUpdatedAt)
                .ThenByDescending(item => item.JobId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToList();
        });
    public async Task<Result<JobReadIndexRetentionPage>> ReadRetentionPageAsync(
        DateTimeOffset olderThanUtc, string? continuationToken, int pageSize,
        CancellationToken cancellationToken = default)
        => await ExecuteAsync(async () =>
        {
            int boundedPageSize = Math.Clamp(pageSize, 1, 200);
            const string lowerPartition = "JobRead_global_";
            const string upperPartition = "JobRead_global_~";
            string filter = TableClient.CreateQueryFilter(
                $"PartitionKey ge {lowerPartition} and PartitionKey lt {upperPartition} and LastUpdatedAt lt {olderThanUtc}");
            await foreach (var page in TableClient.QueryAsync<JobReadIndexEntity>(
                               filter, boundedPageSize, cancellationToken: cancellationToken)
                               .AsPages(continuationToken, boundedPageSize))
            {
                var items = page.Values
                    .Select(FromEntity)
                    .GroupBy(item => item.JobId, StringComparer.Ordinal)
                    .Select(group => group.OrderByDescending(item => item.LastUpdatedAt).First())
                    .OrderBy(item => item.LastUpdatedAt)
                    .ThenBy(item => item.JobId, StringComparer.Ordinal)
                    .ToList();
                return new JobReadIndexRetentionPage(items, page.ContinuationToken);
            }
            return new JobReadIndexRetentionPage([], null);
        });

    private static IEnumerable<string> MonthPartitions(JobListQuery query)
    {
        var to = query.ToUtc.UtcDateTime;
        if (to <= DateTime.MinValue)
            yield break;
        var first = new DateTime(query.FromUtc.UtcDateTime.Year, query.FromUtc.UtcDateTime.Month, 1,
            0, 0, 0, DateTimeKind.Utc);
        var lastInstant = to.AddTicks(-1);
        var last = new DateTime(lastInstant.Year, lastInstant.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        string prefix = string.IsNullOrWhiteSpace(query.Market)
            ? "JobRead_global_"
            : $"JobRead_{MarketKey.Normalize(query.Market)}_";
        if (first > last)
            yield break;
        for (var month = first; ; month = month.AddMonths(1))
        {
            yield return $"{prefix}{month:yyyyMM}";
            if (month >= last)
                break;
        }
    }

    private static string BuildPartitionFilter(string partition, JobListQuery query)
    {
        long invertedTo = DateTime.MaxValue.Ticks - query.ToUtc.UtcDateTime.Ticks;
        long invertedFrom = DateTime.MaxValue.Ticks - query.FromUtc.UtcDateTime.Ticks;
        string lower = $"{invertedTo:D19}|";
        string upper = $"{invertedFrom:D19}|~";
        string rowFilter = $"RowKey gt '{lower}' and RowKey le '{upper}'";

        // Include only rows before the composite cursor at the timestamp boundary. Older
        // timestamps remain in the second range and are not affected by the row-key tie-breaker.
        if (!string.IsNullOrWhiteSpace(query.CursorJobId))
        {
            string cursorKey = $"{invertedTo:D19}|{Escape(query.CursorJobId)}";
            long olderInverted = invertedTo == long.MaxValue ? long.MaxValue : invertedTo + 1;
            string older = $"{olderInverted:D19}|";
            rowFilter = $"(RowKey gt '{lower}' and RowKey lt '{cursorKey}')"
                + $" or (RowKey ge '{older}' and RowKey le '{upper}')";
        }

        string statusFilter = query.Statuses is not { Count: > 0 }
            ? string.Empty
            : $" and ({string.Join(" or ", query.Statuses.Select(status => $"Status eq '{status}'"))})";
        return $"PartitionKey eq '{Escape(partition)}' and ({rowFilter}){statusFilter}";
    }

    private static bool MatchesQuery(JobReadIndexItem item, JobListQuery query)
    {
        if (item.LastUpdatedAt < query.FromUtc || item.LastUpdatedAt >= query.ToUtc)
            return false;
        if (query.CursorJobId is not null
            && item.LastUpdatedAt == query.ToUtc
            && string.CompareOrdinal(item.JobId, query.CursorJobId) >= 0)
            return false;
        return query.Statuses is not { Count: > 0 } || query.Statuses.Contains(item.Status);
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);


    internal static string MarketPartition(JobReadIndexItem item) =>
        $"JobRead_{MarketKey.Normalize(item.Market)}_{item.LastUpdatedAt:yyyyMM}";

    internal static string GlobalPartition(JobReadIndexItem item) =>
        $"JobRead_global_{item.LastUpdatedAt:yyyyMM}";

    internal static string RowKey(JobReadIndexItem item)
    {
        long invertedTicks = DateTime.MaxValue.Ticks - item.LastUpdatedAt.UtcDateTime.Ticks;
        return $"{invertedTicks:D19}|{item.JobId}";
    }

    private static IEnumerable<string> Partitions(JobReadIndexItem item)
    {
        yield return MarketPartition(item);
        yield return GlobalPartition(item);
    }

    private static JobReadIndexEntity ToEntity(JobReadIndexItem item, string partition) => new()
    {
        JobId = item.JobId,
        Market = item.Market,
        Station = item.Station,
        Status = item.Status.ToString(),
        CreatedAt = item.CreatedAt,
        LastUpdatedAt = item.LastUpdatedAt,
        MeasuredDurationSec = item.MeasuredDurationSec,
        AudioUrl = item.AudioUrl,
        AggregateEtag = item.ETag,
        PartitionKey = partition,
        RowKey = RowKey(item)
    };

    private static JobReadIndexItem FromEntity(JobReadIndexEntity entity) => new(
        entity.JobId, entity.Market, entity.Station,
        $"Job_{MarketKey.Normalize(entity.Market)}", entity.AggregateEtag,
        Enum.TryParse<JobStatus>(entity.Status, out var status) ? status : JobStatus.Requested,
        entity.CreatedAt, entity.LastUpdatedAt, entity.MeasuredDurationSec, entity.AudioUrl);
}

internal sealed class JobReadIndexEntity : ITableEntity
{
    public string JobId { get; set; } = null!;
    public string Market { get; set; } = null!;
    public string Station { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
    public double? MeasuredDurationSec { get; set; }
    public string? AudioUrl { get; set; }
    public string? AggregateEtag { get; set; }
    public string PartitionKey { get; set; } = null!;
    public string RowKey { get; set; } = null!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

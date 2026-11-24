using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>Azure Table implementation of the bounded scalar job work index.</summary>
public sealed class TableStorageJobWorkIndexRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageJobWorkIndexRepository> logger)
    : TableRepositoryBase(TableName, options, credential, logger), IJobWorkIndexRepository
{
    private const string TableName = "radioJobWorkIndex";
    private static readonly string[] Select =
    [
        "PartitionKey", "RowKey", "JobId", "Market", "Station", "Status", "DueUtc",
        "LastUpdatedAt", "FallbackDeadlineUtc", "ScheduledForUtc", "CurationDeadlineUtc", "HasAudio"
    ];

    public async Task<Result<JobWorkIndexPage>> ReadPageAsync(
        JobWorkKind kind,
        DateTime bucketUtc,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        var partition = JobWorkIndexProjection.PartitionKey(kind, bucketUtc);
        return await ExecuteAsync(async () =>
        {
            var query = TableClient.QueryAsync<JobWorkIndexEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {partition}"), pageSize, Select, cancellationToken);
            await foreach (var page in query.AsPages(continuationToken, pageSize))
            {
                return new JobWorkIndexPage(
                    page.Values.Select(Map).OrderBy(item => item.DueUtc).ThenBy(item => item.JobId, StringComparer.Ordinal)
                        .ToArray(),
                    page.ContinuationToken);
            }

            return new JobWorkIndexPage([], null);
        });
    }


    public Task<Result> UpsertAsync(JobWorkItem item, CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(Map(item), TableUpdateMode.Replace, cancellationToken);
        });

    public Task<Result> DeleteAsync(JobWorkItem item, CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            await TableClient.DeleteEntityAsync(
                JobWorkIndexProjection.PartitionKey(item.Kind, item.DueUtc),
                JobWorkIndexProjection.RowKey(item), cancellationToken: cancellationToken);
        });

    public async Task<Result<IReadOnlyList<JobWorkItem>>> FindByJobIdAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var filter = TableClient.CreateQueryFilter($"JobId eq {jobId}");
            var rows = new List<JobWorkItem>();
            await foreach (var entity in TableClient.QueryAsync<JobWorkIndexEntity>(filter, cancellationToken: cancellationToken))
                rows.Add(Map(entity));
            return (IReadOnlyList<JobWorkItem>)rows;
        });
    }

    private static JobWorkIndexEntity Map(JobWorkItem item)
    {
        var due = DateTime.SpecifyKind(item.DueUtc, DateTimeKind.Utc);
        return new JobWorkIndexEntity
        {
            PartitionKey = JobWorkIndexProjection.PartitionKey(item.Kind, due),
            RowKey = JobWorkIndexProjection.RowKey(item),
            JobId = item.JobId,
            Market = item.Market,
            Station = item.Station,
            Status = item.Status.ToString(),
            DueUtc = new DateTimeOffset(due),
            LastUpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(item.LastUpdatedAt, DateTimeKind.Utc)),
            FallbackDeadlineUtc = item.FallbackDeadlineUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(item.FallbackDeadlineUtc.Value, DateTimeKind.Utc)) : null,
            ScheduledForUtc = item.ScheduledForUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(item.ScheduledForUtc.Value, DateTimeKind.Utc)) : null,
            CurationDeadlineUtc = item.CurationDeadlineUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(item.CurationDeadlineUtc.Value, DateTimeKind.Utc)) : null,
            HasAudio = item.HasAudio
        };
    }

    private static JobWorkItem Map(JobWorkIndexEntity entity)
    {
        Enum.TryParse<JobWorkKind>(entity.PartitionKey.Split('_')[1], out var kind);
        Enum.TryParse<JobStatus>(entity.Status, out var status);
        return new JobWorkItem
        {
            Kind = kind,
            JobId = entity.JobId,
            Market = entity.Market,
            Station = entity.Station,
            Status = status,
            DueUtc = entity.DueUtc.UtcDateTime,
            LastUpdatedAt = entity.LastUpdatedAt.UtcDateTime,
            FallbackDeadlineUtc = entity.FallbackDeadlineUtc?.UtcDateTime,
            ScheduledForUtc = entity.ScheduledForUtc?.UtcDateTime,
            CurationDeadlineUtc = entity.CurationDeadlineUtc?.UtcDateTime,
            HasAudio = entity.HasAudio
        };
    }
}

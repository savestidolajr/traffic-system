using System.Security.Cryptography;
using System.Text;
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
///     Append-only storage for job status transitions and diagnostic log entries. Rows use a
///     per-job partition and an ordered timestamp/sequence key; duplicate appends are idempotent.
/// </summary>
public sealed class TableStorageJobTimelineRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageJobTimelineRepository> logger)
    : TableRepositoryBase("jobTimeline", options, credential, logger), IJobTimelineRepository
{
    public async Task<Result<JobTimelineSnapshot>> ReadAsync(
        string jobId, int pageSize, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var statuses = new List<StatusHistoryEntry>();
            var logs = new List<JobLog>();
            int remaining = Math.Clamp(pageSize, 1, 1000);
            var query = TableClient.QueryAsync<JobTimelineEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {Partition(jobId)}"),
                maxPerPage: remaining,
                cancellationToken: cancellationToken);
            await foreach (var page in query.AsPages(pageSizeHint: remaining))
            {
                foreach (var entity in page.Values)
                {
                    if (remaining-- <= 0)
                        break;
                    if (string.Equals(entity.Kind, "status", StringComparison.OrdinalIgnoreCase)
                        && entity.Status is not null)
                    {
                        statuses.Add(new StatusHistoryEntry(
                            ParseStatus(entity.Status), entity.AtUtc.UtcDateTime,
                            entity.By ?? "system", Option.OfWhitespace(entity.Note)));
                    }
                    else if (string.Equals(entity.Kind, "log", StringComparison.OrdinalIgnoreCase)
                             && entity.Message is not null)
                    {
                        logs.Add(new JobLog(
                            entity.AtUtc.UtcDateTime, ParseLevel(entity.Level), entity.Message));
                    }
                }
                break;
            }

            return new JobTimelineSnapshot(
                statuses.OrderBy(s => s.At)
                    .ThenBy(s => s.Status.ToString(), StringComparer.Ordinal)
                    .ThenBy(s => s.By, StringComparer.Ordinal)
                    .ToList(),
                logs.OrderBy(l => l.At)
                    .ThenBy(l => l.Level)
                    .ThenBy(l => l.Message, StringComparer.Ordinal)
                    .ToList());
        });
    }

    public async Task<Result> AppendAsync(
        string jobId, IReadOnlyList<JobTimelineEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0) return Result.Success();
        return await ExecuteAsync(async () =>
        {
            foreach (var entry in entries)
            {
                var entity = new JobTimelineEntity
                {
                    PartitionKey = Partition(jobId),
                    RowKey = RowKey(entry),
                    JobId = jobId,
                    Sequence = entry.Sequence,
                    AtUtc = entry.AtUtc.ToUniversalTime(),
                    Kind = entry.Kind,
                    Status = entry.Status?.ToString(),
                    By = entry.By,
                    Note = entry.Note,
                    Level = entry.Level?.ToString(),
                    Message = entry.Message
                };

                try
                {
                    await TableClient.AddEntityAsync(entity, cancellationToken);
                }
                catch (RequestFailedException ex) when (ex.Status == 409)
                {
                    // A retry of an already appended immutable row is success.
                }
            }
        });
    }

    public async Task<Result> DeleteAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            string partition = Partition(jobId);
            var query = TableClient.QueryAsync<JobTimelineEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {partition}"),
                cancellationToken: cancellationToken);
            await foreach (var entity in query)
                await TableClient.DeleteEntityAsync(partition, entity.RowKey, cancellationToken: cancellationToken);
        });
    }

    internal static string Partition(string jobId) => $"JobTimeline|{jobId}";

    internal static string RowKey(JobTimelineEntry entry)
    {
        string material = $"{entry.Kind}|{entry.Sequence}|{entry.AtUtc.UtcTicks}|{entry.Status}|{entry.By}|{entry.Message}";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..12];
        return $"{entry.AtUtc.UtcTicks:D19}|{entry.Sequence:D12}|{hash}";
    }

    private static JobStatus ParseStatus(string value) =>
        Enum.TryParse<JobStatus>(value, out var status) ? status : JobStatus.Requested;

    private static JobLogLevel ParseLevel(string? value) =>
        Enum.TryParse<JobLogLevel>(value, out var level) ? level : JobLogLevel.Info;
}

internal sealed class JobTimelineEntity : ITableEntity
{
    public string JobId { get; set; } = null!;
    public long Sequence { get; set; }
    public DateTimeOffset AtUtc { get; set; }
    public string Kind { get; set; } = null!;
    public string? Status { get; set; }
    public string? By { get; set; }
    public string? Note { get; set; }
    public string? Level { get; set; }
    public string? Message { get; set; }
    public string PartitionKey { get; set; } = null!;
    public string RowKey { get; set; } = null!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

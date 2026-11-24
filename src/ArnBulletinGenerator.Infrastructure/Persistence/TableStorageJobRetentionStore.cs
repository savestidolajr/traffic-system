using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>Bounded, ETag-guarded retention and orphan-reconciliation storage operations.</summary>
public sealed class TableStorageJobRetentionStore(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageJobRetentionStore> logger,
    IJobTimelineRepository? timelineRepository = null,
    IJobPayloadRepository? payloadRepository = null,
    IJobReadIndexRepository? readIndexRepository = null,
    IJobWorkIndexRepository? workIndexRepository = null,
    IJobAudioArtifactRepository? audioArtifactRepository = null,
    ILedgerBucketRepository? ledgerBucketRepository = null)
    : TableRepositoryBase("radioJobs", options, credential, logger), IJobRetentionStore
{
    private const int MaxPage = 200;

    public async Task<Result<JobRetentionPage>> ReadPageAsync(
        DateTimeOffset olderThanUtc,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (readIndexRepository is not null)
        {
            var indexed = await readIndexRepository.ReadRetentionPageAsync(
                olderThanUtc, continuationToken, Math.Clamp(pageSize, 1, MaxPage), cancellationToken);
            if (indexed.IsFailure)
                return Result<JobRetentionPage>.Failure(indexed.Error);
            return Result<JobRetentionPage>.Success(new JobRetentionPage(
                indexed.Value.Items.Select(ToCandidate).ToList(),
                indexed.Value.ContinuationToken));
        }

        return await ExecuteAsync(async () =>
        {
            string filter = TableClient.CreateQueryFilter(
                $"PartitionKey ge 'Job_' and PartitionKey lt 'Job`' and CreatedAt lt {olderThanUtc}");
            var rows = new List<JobRetentionCandidate>();
            int boundedPageSize = Math.Clamp(pageSize, 1, MaxPage);
            await foreach (var page in TableClient.QueryAsync<TableEntity>(
                               filter, boundedPageSize,
                               ["PartitionKey", "RowKey", "Station", "Market", "Status", "CreatedAt",
                                   "LastUpdatedAt", "AudioUrl", "PreviousJobId", "FallbackSourceJobId",
                                   "ScheduledForUtc", "FallbackDeadlineUtc", "FallbackAppliedAt",
                                   "AttemptHistoryJson", "RejectionHistoryJson", "StatusHistoryJson"],
                               cancellationToken).AsPages(continuationToken, boundedPageSize))
            {
                foreach (var entity in page.Values)
                    rows.Add(ToCandidate(entity));
                return new JobRetentionPage(rows, page.ContinuationToken);
            }
            return new JobRetentionPage(rows, null);
        });
    }

    public async Task<Result<JobRetentionCandidate>> RecheckAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var index = await GetEntityOrDefaultAsync<JobIndexEntity>("JobIndex", jobId, cancellationToken);
        if (index.IsFailure)
            return Result<JobRetentionCandidate>.Failure(index.Error);
        if (index.Value.IsNone)
            return Result<JobRetentionCandidate>.Failure("Job not found");
        return await ExecuteAsync(async () =>
        {
            var response = await TableClient.GetEntityAsync<TableEntity>(
                JobEntityMapper.JobPartition(index.Value.Value.Market), jobId,
                cancellationToken: cancellationToken);
            return ToCandidate(response.Value);
        });
    }

    public async Task<Result> DeleteAsync(
        JobRetentionCandidate candidate, CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        try
        {
            await TableClient.DeleteEntityAsync(
                candidate.PartitionKey, candidate.JobId,
                new ETag(candidate.ETag ?? string.Empty), cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // The row may have been removed by a previous idempotent sweep. Continue purging
            // its derived artifacts rather than leaving orphaned timeline/audio rows behind.
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            // A changed ETag is an active-row race. Never remove any derived artifacts because
            // the new row may have repointed them.
            return Result.Failure(ErrorMessages.Job.Conflict);
        }
        catch (RequestFailedException ex)
        {
            return Result.Failure(TableRepositoryBase.SanitizeStorageMessage(ex.Message));
        }

        if (ledgerBucketRepository is not null)
        {
            var result = await ledgerBucketRepository.DeleteAsync(new LedgerBucketEntry(
                candidate.JobId, candidate.Market, candidate.Station, candidate.CreatedAt,
                candidate.LastUpdatedAt, candidate.Status, HasIncidents: false), cancellationToken);
            if (result.IsFailure)
                failures.Add($"ledger: {result.Error}");
        }

        if (timelineRepository is not null)
        {
            var result = await timelineRepository.DeleteAsync(candidate.JobId, cancellationToken);
            if (result.IsFailure)
                failures.Add($"timeline: {result.Error}");
        }
        if (payloadRepository is not null)
        {
            var result = await payloadRepository.DeleteAsync(candidate.JobId, cancellationToken);
            if (result.IsFailure)
                failures.Add($"payload: {result.Error}");
        }
        if (audioArtifactRepository is not null
            && !string.IsNullOrWhiteSpace(candidate.AudioUrl))
        {
            var result = await audioArtifactRepository.DeleteAsync(
                candidate.AudioUrl, cancellationToken);
            if (result.IsFailure)
                failures.Add($"audio: {result.Error}");
        }
        if (readIndexRepository is not null)
        {
            var rows = await readIndexRepository.FindByJobIdAsync(
                candidate.JobId, cancellationToken);
            if (rows.IsFailure)
                failures.Add($"read-index: {rows.Error}");
            else
                foreach (var row in rows.Value)
                {
                    var result = await readIndexRepository.DeleteAsync(row, cancellationToken);
                    if (result.IsFailure)
                        failures.Add($"read-index: {result.Error}");
                }
        }
        if (workIndexRepository is not null)
        {
            var rows = await workIndexRepository.FindByJobIdAsync(
                candidate.JobId, cancellationToken);
            if (rows.IsFailure)
                failures.Add($"work-index: {rows.Error}");
            else
                foreach (var row in rows.Value)
                {
                    var result = await workIndexRepository.DeleteAsync(row, cancellationToken);
                    if (result.IsFailure)
                        failures.Add($"work-index: {result.Error}");
                }
        }

        await DeleteLastSuccessfulPointerAsync(candidate, failures, cancellationToken);

        try
        {
            await TableClient.DeleteEntityAsync(
                "JobIndex", candidate.JobId, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
        }
        catch (RequestFailedException ex)
        {
            failures.Add($"job-index: {TableRepositoryBase.SanitizeStorageMessage(ex.Message)}");
        }

        return failures.Count == 0
            ? Result.Success()
            : Result.Failure(string.Join("; ", failures));
    }

    private async Task DeleteLastSuccessfulPointerAsync(
        JobRetentionCandidate candidate,
        ICollection<string> failures,
        CancellationToken cancellationToken)
    {
        string rowKey = $"{SafeStorageKey(candidate.Market)}|{SafeStorageKey(candidate.Station)}";
        var pointer = await GetEntityOrDefaultAsync<LastSuccessfulJobEntity>(
            "LastSuccessful", rowKey, cancellationToken);
        if (pointer.IsFailure)
        {
            failures.Add($"last-successful: {pointer.Error}");
            return;
        }
        if (pointer.Value.IsNone
            || !string.Equals(pointer.Value.Value.JobId, candidate.JobId, StringComparison.Ordinal))
            return;
        try
        {
            await TableClient.DeleteEntityAsync(
                "LastSuccessful", rowKey, pointer.Value.Value.ETag, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412)
        {
            // Another sweep repaired or replaced the pointer; both outcomes are safe.
        }
        catch (RequestFailedException ex)
        {
            failures.Add($"last-successful: {TableRepositoryBase.SanitizeStorageMessage(ex.Message)}");
        }
    }

    private static string SafeStorageKey(string value) =>
        string.Concat((value ?? string.Empty).Trim().ToLowerInvariant()
            .Select(c => c is '/' or '\\' or '#' or '?' || char.IsControl(c) ? '_' : c));

    public async Task<Result<int>> ReconcileOrphansAsync(
        DateTimeOffset olderThanUtc,
        int pageSize,
        bool deleteOrphans = false,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            int orphans = 0;
            string filter = TableClient.CreateQueryFilter(
                $"PartitionKey eq 'JobIndex' and CreatedAt lt {olderThanUtc}");
            await foreach (var page in TableClient.QueryAsync<JobIndexEntity>(
                               filter, MaxPage,
                               ["PartitionKey", "RowKey", "Market", "CreatedAt"],
                               cancellationToken).AsPages(
                                   continuationToken: null, pageSizeHint: Math.Clamp(pageSize, 1, MaxPage)))
            {
                foreach (var index in page.Values)
                {
                    try
                    {
                        await TableClient.GetEntityAsync<TableEntity>(
                            JobEntityMapper.JobPartition(index.Market), index.RowKey,
                            select: ["PartitionKey", "RowKey"], cancellationToken: cancellationToken);
                    }
                    catch (RequestFailedException ex) when (ex.Status == 404)
                    {
                        orphans++;
                        if (deleteOrphans)
                        {
                            try
                            {
                                await TableClient.DeleteEntityAsync("JobIndex", index.RowKey,
                                    cancellationToken: cancellationToken);
                            }
                            catch (RequestFailedException deleteEx) when (deleteEx.Status == 404)
                            {
                            }
                        }
                    }
                }
                if (orphans >= MaxPage) break;
            }
            return orphans;
        });
    }

    private static JobRetentionCandidate ToCandidate(JobReadIndexItem item) => new(
        item.JobId,
        item.Market,
        item.Station,
        item.PartitionKey,
        item.ETag,
        item.Status,
        item.CreatedAt,
        item.LastUpdatedAt,
        item.AudioUrl,
        PreviousJobId: null,
        FallbackSourceJobId: null,
        ScheduledForUtc: null,
        FallbackDeadlineUtc: null,
        FallbackAppliedAt: null,
        HasAuditHistory: false);

    private static JobRetentionCandidate ToCandidate(TableEntity entity)
    {
        var status = Enum.TryParse<JobStatus>(StringValue(entity, "Status"), out var parsed)
            ? parsed : JobStatus.Requested;
        return new JobRetentionCandidate(
            entity.RowKey,
            StringValue(entity, "Market"),
            StringValue(entity, "Station"),
            entity.PartitionKey,
            entity.ETag.ToString(),
            status,
            DateValue(entity, "CreatedAt"),
            DateValue(entity, "LastUpdatedAt"),
            NullableString(entity, "AudioUrl"),
            NullableString(entity, "PreviousJobId"),
            NullableString(entity, "FallbackSourceJobId"),
            NullableDate(entity, "ScheduledForUtc"),
            NullableDate(entity, "FallbackDeadlineUtc"),
            NullableDate(entity, "FallbackAppliedAt"),
            // Every valid job starts with a Requested status-history entry. When timeline
            // offload is enabled the inline columns are intentionally removed, so their
            // absence cannot mean that no audit history exists.
            true);
    }

    private static string StringValue(TableEntity entity, string key) =>
        entity.TryGetValue(key, out var value) ? value?.ToString() ?? string.Empty : string.Empty;
    private static string? NullableString(TableEntity entity, string key) =>
        entity.TryGetValue(key, out var value) ? value?.ToString() : null;
    private static DateTimeOffset DateValue(TableEntity entity, string key) =>
        NullableDate(entity, key) ?? default;
    private static DateTimeOffset? NullableDate(TableEntity entity, string key) =>
        entity.TryGetValue(key, out var value)
            ? value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => new DateTimeOffset(dt.ToUniversalTime()),
                _ => DateTimeOffset.TryParse(value?.ToString(), out var parsed) ? parsed : null
            }
            : null;
}

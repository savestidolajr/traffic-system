using System.Collections.Concurrent;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
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

// Partition key format: "Job_{market}" where market = job.Market trimmed+lowercased; null/empty -> "Job_unknown"
// Row key format: {jobId} (GUID assigned by the system, unique per job)
// Lookup by bare jobId: via a JobIndex entity (PK="JobIndex", RK=jobId, Market column)
// Scan queries: prefix range PartitionKey ge 'Job_' and PartitionKey lt 'Job`'
// Migration from single-partition "Job": see scripts/migrate-job-partitions.py
public sealed class TableStorageJobRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    TimeProvider timeProvider,
    ILogger<TableStorageJobRepository> logger,
    IOptions<JobPersistenceOptions>? persistenceOptions = null,
    ILedgerBucketRepository? ledgerBucketRepository = null,
    IJobTimelineRepository? timelineRepository = null,
    IJobPayloadRepository? payloadRepository = null,
    IJobReadIndexRepository? readIndexRepository = null,
    IJobWorkIndexRepository? workIndexRepository = null,
    IOptions<JobWorkIndexOptions>? workIndexOptions = null)
    : TableRepositoryBase(TableName, options, credential, logger), IJobRepository, IJobStatusRepository,
    IJobWatchdogClaimRepository
{
    private const string TableName = "radioJobs";

    private const int MaxTotalResults = 1000;
    private const int SummaryPageSize = 1000;
    // Independent of MaxTotalResults above (which bounds the OLD unbounded overloads): this is
    // a pure safety trip-wire for the paginated path.
    private const int MaxPageScanResults = 2500;
    private const string WatchdogClaimUntilColumn = "WatchdogClaimUntilUtc";
    private static readonly TimeSpan WatchdogClaimLease = TimeSpan.FromMinutes(2);
    private readonly ILogger<TableStorageJobRepository> _logger = logger;
    private readonly JobEntityMapper _mapper = new(logger);
    private readonly JobPersistenceOptions _persistenceOptions =
        persistenceOptions?.Value ?? new();
    private readonly ILedgerBucketRepository? _ledgerBucketRepository = ledgerBucketRepository;
    private readonly IJobTimelineRepository? _timelineRepository = timelineRepository;
    private readonly JobIndexMaintainer? _jobIndexMaintainer =
        workIndexRepository is null
            ? null
            : new JobIndexMaintainer(workIndexRepository,
                workIndexOptions ?? Options.Create(new JobWorkIndexOptions()), logger);
    private readonly IJobPayloadRepository? _payloadRepository = payloadRepository;
    private readonly IJobReadIndexRepository? _readIndexRepository = readIndexRepository;
    private static readonly string[] ListItemSelect =
    [
        "PartitionKey",
        "RowKey",
        "Station",
        "Market",
        "TargetDurationSeconds",
        "CreatedAt",
        "LastUpdatedAt",
        "Status",
        "MeasuredDurationSec"
    ];
    private static readonly string[] LedgerSelect =
    [
        "PartitionKey",
        "RowKey",
        "Station",
        "Market",
        "TargetDurationSeconds",
        "CreatedAt",
        "LastUpdatedAt",
        "Status",
        "AllIncidentsJson",
        "CorridorsJson",
        "ScriptSentencesJson",
        "PreviousJobId"
    ];

    public async Task<Result<IEnumerable<JobListItem>>> ListJobSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var items = await QueryListItemsAsync(
                "PartitionKey ge 'Job_' and PartitionKey lt 'Job`'",
                MaxTotalResults,
                SummaryPageSize,
                cancellationToken);
            return items
                .OrderByDescending(item => item.CreatedAt)
                .AsEnumerable();
        });
    }

    public async Task<Result<IEnumerable<JobListItem>>> ListJobSummariesAsync(
        IReadOnlyCollection<JobStatus> statuses, CancellationToken cancellationToken = default)
    {
        if (statuses is null || statuses.Count == 0)
            return await ListJobSummariesAsync(cancellationToken);

        return await ExecuteAsync(async () =>
        {
            string statusClause = string.Join(" or ", statuses.Select(s => $"Status eq '{s}'"));
            var items = await QueryListItemsAsync(
                $"PartitionKey ge 'Job_' and PartitionKey lt 'Job`' and ({statusClause})",
                MaxTotalResults,
                SummaryPageSize,
                cancellationToken);
            return items
                .OrderByDescending(item => item.CreatedAt)
                .AsEnumerable();
        });
    }

    public async Task<Result<IReadOnlyList<JobListItem>>> ListJobSummariesPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default)
    {
        if (UseReadIndexRead)
        {
            var indexed = await ReadIndexPageAsync(query, cancellationToken);
            if (indexed.IsSuccess && indexed.Value.Count > 0)
            {
                var hydrated = await HydrateSummaryIndexPageAsync(indexed.Value, query, cancellationToken);
                // A short page is the signature of an incomplete index: rows exist for the jobs
                // written since it was enabled and for none of the history before it. The
                // authoritative scan decides in that case, so a partial backfill can never make
                // history look deleted.
                if (hydrated.IsSuccess && hydrated.Value.Count >= query.Limit)
                    return hydrated;
                if (hydrated.IsFailure)
                    _logger.LogWarning("[Jobs] Read-index summary hydration failed: {Error}", hydrated.Error);
            }
            else if (indexed.IsFailure)
            {

                _logger.LogWarning("[Jobs] Read-index summary page failed: {Error}", indexed.Error);
            }
        }

        // Migration fallback: the bounded scalar scan remains authoritative while the index is
        // being populated or when an index page is stale/unavailable.
        return await ExecuteAsync(async () =>
        {
            var filter = BuildPageFilter(query);
            var items = await QueryListItemsAsync(
                filter, MaxPageScanResults, SummaryPageSize, cancellationToken,
                continueAfterCap: true);
            return items
                .OrderByDescending(item => item.LastUpdatedAt)
                .ThenByDescending(item => item.JobId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToList() as IReadOnlyList<JobListItem>;
        });
    }
    private async Task<Result<IReadOnlyList<JobReadIndexItem>>> ReadIndexPageAsync(
        JobListQuery query, CancellationToken cancellationToken)
    {
        var indexQuery = query with
        {
            Limit = query.CursorJobId is null
                ? Math.Min(Math.Max(query.Limit, 1), MaxPageScanResults)
                : MaxPageScanResults,
            // The index repository applies a strict timestamp upper bound. Include one tick when
            // a composite cursor is present so tied rows can be filtered by row key below.
            ToUtc = query.CursorJobId is null || query.ToUtc == DateTimeOffset.MaxValue
                ? query.ToUtc
                : query.ToUtc.AddTicks(1)
        };
        return await _readIndexRepository!.ReadPageAsync(indexQuery, cancellationToken);
    }

    private async Task<Result<IReadOnlyList<JobListItem>>> HydrateSummaryIndexPageAsync(
        IReadOnlyList<JobReadIndexItem> indexed, JobListQuery query,
        CancellationToken cancellationToken)
    {
        var reads = indexed.Select(item => GetEntityOrDefaultAsync<TableEntity>(
            item.PartitionKey, item.JobId, ListItemSelect, cancellationToken)).ToArray();
        var entities = await Task.WhenAll(reads);
        var items = new List<JobListItem>(entities.Length);
        foreach (var result in entities)
        {
            if (result.IsFailure)
                return Result<IReadOnlyList<JobListItem>>.Failure(result.Error);
            if (result.Value.IsNone)
                return Result<IReadOnlyList<JobListItem>>.Failure("A read-index job row is missing.");
            items.Add(_mapper.MapToListItem(result.Value.Value));
        }

        return Result<IReadOnlyList<JobListItem>>.Success(
            FilterAndOrderPage(items, query));
    }

    private async Task<Result<IReadOnlyList<Job>>> HydrateFullIndexPageAsync(
        IReadOnlyList<JobReadIndexItem> indexed, JobListQuery query,
        CancellationToken cancellationToken)
    {
        var reads = indexed.Select(item => GetJobAsync(
            new JobIndex(item.JobId, item.Market, item.PartitionKey, item.ETag),
            cancellationToken)).ToArray();
        var jobs = await Task.WhenAll(reads);
        var hydrated = new List<Job>(jobs.Length);
        foreach (var result in jobs)
        {
            if (result.IsFailure)
                return Result<IReadOnlyList<Job>>.Failure(result.Error);
            hydrated.Add(result.Value);
        }

        return Result<IReadOnlyList<Job>>.Success(
            hydrated
                .Where(job => MatchesPageQuery(job.LastUpdatedAt, job.Status, job.JobId, query))
                .OrderByDescending(job => job.LastUpdatedAt)
                .ThenByDescending(job => job.JobId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToList() as IReadOnlyList<Job>);
    }

    private static IReadOnlyList<JobListItem> FilterAndOrderPage(
        IEnumerable<JobListItem> items, JobListQuery query) =>
        items
            .Where(item => MatchesPageQuery(item.LastUpdatedAt, item.Status, item.JobId, query))
            .OrderByDescending(item => item.LastUpdatedAt)
            .ThenByDescending(item => item.JobId, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToList();

    private static bool MatchesPageQuery(
        DateTime updatedAt, JobStatus status, string jobId, JobListQuery query)
    {
        DateTime from = query.FromUtc.UtcDateTime;
        DateTime to = query.ToUtc.UtcDateTime;
        if (updatedAt < from || updatedAt > to
            || (updatedAt == to
                && (query.CursorJobId is null
                    || string.CompareOrdinal(jobId, query.CursorJobId) >= 0)))
            return false;
        return query.Statuses is not { Count: > 0 } || query.Statuses.Contains(status);
    }

    public async Task<Result<JobLedgerProjection>> GetJobLedgerProjectionAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var indexResult = await GetJobIndexAsync(jobId, cancellationToken);
        if (indexResult.IsFailure)
            return Result<JobLedgerProjection>.Failure(indexResult.Error);

        var index = indexResult.Value;
        var entityResult = await GetEntityOrDefaultAsync<TableEntity>(
            JobEntityMapper.JobPartition(index.Market), jobId, LedgerSelect, cancellationToken);
        if (entityResult.IsFailure)
            return Result<JobLedgerProjection>.Failure(entityResult.Error);
        return entityResult.Value.Match(
            entity => Result<JobLedgerProjection>.Success(_mapper.MapToLedger(entity)),
            () => Result<JobLedgerProjection>.Failure(ErrorMessages.Job.NotFound));
    }
    public async Task<Result<IReadOnlyList<JobLedgerProjection>>> ListJobLedgerPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default)
    {
        if (UseLedgerRead && !string.IsNullOrWhiteSpace(query.Market))
        {
            var incremental = await ReadLedgerProjectionPageAsync(query, cancellationToken);
            if (incremental.IsSuccess && incremental.Value.Count > 0)
                return incremental;
        }

        // Migration fallback: the bounded legacy projection query remains available when a
        // market/day bucket is empty or unavailable.
        return await ExecuteAsync(async () =>
        {
            var projections = new List<JobLedgerProjection>();
            var tableQuery = TableClient.QueryAsync<TableEntity>(
                BuildPageFilter(query), SummaryPageSize, LedgerSelect, cancellationToken);
            await foreach (var entity in tableQuery)
            {
                projections.Add(_mapper.MapToLedger(entity));
                if (projections.Count >= MaxPageScanResults) break;
            }

            return projections
                .OrderByDescending(projection => projection.LastUpdatedAt)
                .ThenByDescending(projection => projection.JobId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToList() as IReadOnlyList<JobLedgerProjection>;
        });
    }

    private async Task<Result<IReadOnlyList<JobLedgerProjection>>> ReadLedgerProjectionPageAsync(
        JobListQuery query, CancellationToken cancellationToken)
    {
        var bucket = await _ledgerBucketRepository!.ReadPageAsync(
            query.Market!, query.FromUtc, query.ToUtc, null,
            Math.Clamp(query.Limit, 1, 500), cancellationToken);
        if (bucket.IsFailure)
            return Result<IReadOnlyList<JobLedgerProjection>>.Failure(bucket.Error);

        var projections = new List<JobLedgerProjection>(Math.Min(query.Limit, bucket.Value.Items.Count));
        foreach (var entry in bucket.Value.Items)
        {
            var projection = await GetJobLedgerProjectionAsync(entry.JobId, cancellationToken);
            if (projection.IsFailure)
                return Result<IReadOnlyList<JobLedgerProjection>>.Failure(projection.Error);
            projections.Add(projection.Value);
            if (projections.Count >= query.Limit)
                break;
        }
        return Result<IReadOnlyList<JobLedgerProjection>>.Success(projections);
    }

    private async Task<List<JobListItem>> QueryListItemsAsync(
        string filter, int maxResults, int maxPerPage, CancellationToken cancellationToken,
        bool continueAfterCap = false)
    {
        var items = new List<JobListItem>();
        var query = TableClient.QueryAsync<TableEntity>(filter, maxPerPage, ListItemSelect, cancellationToken);

        await foreach (var entity in query)
        {
            items.Add(_mapper.MapToListItem(entity));
            if (items.Count != maxResults)
                continue;

            if (maxResults == MaxPageScanResults)
                _logger.LogWarning(
                    "[Jobs] Summary page scan crossed the {Cap}-row safety threshold before sorting/truncating to Limit.",
                    MaxPageScanResults);
            if (!continueAfterCap)
                break;
        }

        return items;
    }

    private static string BuildPageFilter(JobListQuery query)
    {
        // Stable total order across pages: (LastUpdatedAt desc, row key desc). The composite
        // cursor pins a tied timestamp to one row, so paging cannot skip bulk-written jobs.
        string partitionFilter = string.IsNullOrWhiteSpace(query.Market)
            ? "PartitionKey ge 'Job_' and PartitionKey lt 'Job`'"
            : TableClient.CreateQueryFilter(
                $"PartitionKey eq {JobEntityMapper.JobPartition(query.Market)}");
        string timeFilter = query.CursorJobId is { Length: > 0 }
            ? TableClient.CreateQueryFilter(
                $"((LastUpdatedAt ge {query.FromUtc} and LastUpdatedAt lt {query.ToUtc}) or (LastUpdatedAt eq {query.ToUtc} and RowKey lt {query.CursorJobId}))")
            : TableClient.CreateQueryFilter(
                $"LastUpdatedAt ge {query.FromUtc} and LastUpdatedAt lt {query.ToUtc}");
        string filter = $"{partitionFilter} and {timeFilter}";

        if (query.Statuses is { Count: > 0 })
        {
            string statusClause = string.Join(" or ", query.Statuses.Select(s => $"Status eq '{s}'"));
            filter += $" and ({statusClause})";
        }

        return filter;
    }

    public async Task<Result<IEnumerable<Job>>> ListJobsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var jobs = new List<Job>();
            var query = TableClient.QueryAsync<JobEntity>("PartitionKey ge 'Job_' and PartitionKey lt 'Job`'", 50, null, cancellationToken);

            await foreach (var entity in query)
            {
                jobs.Add(_mapper.MapToDomain(entity));
                if (jobs.Count >= MaxTotalResults) break;
            }

            return jobs.OrderByDescending(j => j.CreatedAt).AsEnumerable();
        });
    }

    public async Task<Result<IEnumerable<Job>>> ListJobsAsync(
        IReadOnlyCollection<JobStatus> statuses, CancellationToken cancellationToken = default)
    {
        if (statuses is null || statuses.Count == 0)
            return await ListJobsAsync(cancellationToken);

        return await ExecuteAsync(async () =>
        {
            string statusClause = string.Join(" or ", statuses.Select(s => $"Status eq '{s}'"));
            var jobs = new List<Job>();
            var query = TableClient.QueryAsync<JobEntity>(
                $"PartitionKey ge 'Job_' and PartitionKey lt 'Job`' and ({statusClause})", 50, null, cancellationToken);

            await foreach (var entity in query)
            {
                jobs.Add(_mapper.MapToDomain(entity));
                if (jobs.Count >= MaxTotalResults) break;
            }

            return jobs.OrderByDescending(j => j.CreatedAt).AsEnumerable();
        });
    }

    public async Task<Result<IReadOnlyList<Job>>> ListJobsPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default)
    {
        if (UseReadIndexRead)
        {
            var indexed = await ReadIndexPageAsync(query, cancellationToken);
            if (indexed.IsSuccess && indexed.Value.Count > 0)
            {
                var hydrated = await HydrateFullIndexPageAsync(indexed.Value, query, cancellationToken);
                // Same completeness rule as the summary page: only a full page proves the index
                // knows the whole window.
                if (hydrated.IsSuccess && hydrated.Value.Count >= query.Limit)
                    return hydrated;
                if (hydrated.IsFailure)
                    _logger.LogWarning("[Jobs] Read-index full-page hydration failed: {Error}", hydrated.Error);
            }
            else if (indexed.IsFailure)
            {
                _logger.LogWarning("[Jobs] Read-index full page failed: {Error}", indexed.Error);
            }
        }

        // Migration fallback: retain a bounded legacy aggregate scan until the index is complete.
        return await ExecuteAsync(async () =>
        {
            var filter = BuildPageFilter(query);
            var jobs = new List<Job>();
            var tableQuery = TableClient.QueryAsync<JobEntity>(
                filter, SummaryPageSize, null, cancellationToken);
            await foreach (var entity in tableQuery)
            {
                jobs.Add(_mapper.MapToDomain(entity));
                if (jobs.Count >= MaxPageScanResults)
                {
                    _logger.LogWarning(
                        "[Jobs] Page scan for {From}..{To} hit the {Cap}-row safety cap before " +
                        "sorting/truncating to Limit={Limit} — narrow the window or shrink Limit.",
                        query.FromUtc, query.ToUtc, MaxPageScanResults, query.Limit);
                    break;
                }
            }

            // Ties are real (bulk writes stamp identical timestamps), so the page is truncated
            // deterministically by the same total order the cursor filter continues from.
            return jobs
                .OrderByDescending(j => j.LastUpdatedAt)
                .ThenByDescending(j => j.JobId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToList() as IReadOnlyList<Job>;
        });
    }

    public async Task<Result<JobIndex>> GetJobIndexAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var result = await GetEntityOrDefaultAsync<JobIndexEntity>(
            "JobIndex", jobId, cancellationToken);
        if (result.IsFailure)
            return Result<JobIndex>.Failure(result.Error);
        return result.Value.Match(
            entity => Result<JobIndex>.Success(new JobIndex(
                entity.RowKey, entity.Market, JobEntityMapper.JobPartition(entity.Market),
                entity.ETag.ToString())),
            () => Result<JobIndex>.Failure(ErrorMessages.Job.NotFound));
    }

    public async Task<Result<Job>> GetJobAsync(
        JobIndex index, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(index.JobId) || string.IsNullOrWhiteSpace(index.Market))
            return Result<Job>.Failure(ErrorMessages.Job.NotFound);
        var partition = string.IsNullOrWhiteSpace(index.PartitionKey)
            ? JobEntityMapper.JobPartition(index.Market)
            : index.PartitionKey;
        var entityResult = await GetEntityOrDefaultAsync<JobEntity>(
            partition, index.JobId, cancellationToken);
        if (entityResult.IsFailure)
            return Result<Job>.Failure(entityResult.Error);
        if (entityResult.Value.IsNone)
            return Result<Job>.Failure(ErrorMessages.Job.NotFound);
        var entity = entityResult.Value.Value;
        // Detail reads are the only path allowed to hydrate split models. The two bounded
        // reads are independent, so issue them together and merge deterministically below.
        var payloadTask = HydrateExternalPayloadsAsync(entity, cancellationToken);
        var timelineTask = ReadTimelineAsync(entity.RowKey, cancellationToken);
        await Task.WhenAll(payloadTask, timelineTask);

        var job = _mapper.MapToDomain(entity);
        var timeline = await timelineTask;
        if (timeline is not null)
            MergeTimeline(job, timeline);
        return Result<Job>.Success(job);
    }

    public async Task<Result<Job>> GetJobAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        var index = await GetJobIndexAsync(jobId, cancellationToken);
        return index.IsFailure
            ? Result<Job>.Failure(index.Error)
            : await GetJobAsync(index.Value, cancellationToken);
    }

    public async Task<Result> SaveJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        var previousIndexResult = await GetEntityOrDefaultAsync<JobIndexEntity>(
            "JobIndex", job.JobId, cancellationToken);
        var previousIndex = previousIndexResult.IsSuccess && previousIndexResult.Value.HasValue
            ? previousIndexResult.Value.Value
            : null;
        var indexEntity = new JobIndexEntity
        {
            PartitionKey = "JobIndex",
            RowKey = job.JobId,
            Market = job.Market,
            ReadIndexLastUpdatedAt = new DateTimeOffset(job.LastUpdatedAt, TimeSpan.Zero)
        };

        // Index first keeps bare-id reads routable even while a new aggregate is being written.
        try
        {
            await TableClient.UpsertEntityAsync(indexEntity, TableUpdateMode.Replace, cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to upsert job index for {JobId}.", job.JobId);
            return Result.Failure(TableRepositoryBase.SanitizeStorageMessage(ex.Message));
        }

        string partition = JobEntityMapper.JobPartition(job.Market);
        const int maxRetries = 3;
        if (string.IsNullOrWhiteSpace(job.ETag))
        {
            return await ExecuteAsync(async () =>
            {
                var entity = await PrepareEntityForWriteAsync(job, cancellationToken);
                var response = await TableClient.UpsertEntityAsync(
                    entity, TableUpdateMode.Replace, cancellationToken);
                job.ETag = ReadResponseEtag(response, job.ETag);
                job.ConcurrencyFingerprint = JobEntityMapper.ComputeFingerprint(entity);
                await AfterSaveAsync(job, cancellationToken, previousIndex);
            });
        }

        var etag = new Azure.ETag(job.ETag);
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var entity = await PrepareEntityForWriteAsync(job, cancellationToken);
                var response = await TableClient.UpdateEntityAsync(
                    entity, etag, TableUpdateMode.Replace, cancellationToken);
                job.ETag = ReadResponseEtag(response, job.ETag);
                job.ConcurrencyFingerprint = JobEntityMapper.ComputeFingerprint(entity);
                await AfterSaveAsync(job, cancellationToken, previousIndex);
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                _logger.LogWarning(
                    "ETag conflict on SaveJobAsync for job {JobId}, attempt {Attempt}. Reloading to reconcile.",
                    job.JobId, attempt + 1);
                var response = await TableClient.GetEntityAsync<JobEntity>(
                    partition, job.JobId, cancellationToken: cancellationToken);
                var stored = response.Value;
                if (JobEntityMapper.ComputeFingerprint(stored) != job.ConcurrencyFingerprint)
                    return Result.Failure(ErrorMessages.Job.Conflict);

                var storedLogs = SafeJson.Deserialize<List<JobLog>>(
                    _mapper.ExpandTableProperty(stored.LogsJson, "LogsJson", job.JobId),
                    _logger, $"job {job.JobId} logs", JobEntityMapper.JsonOptions) ?? [];
                job.SetLogs(MergeLogs(job.Logs, storedLogs));
                job.ETag = stored.ETag.ToString();
                etag = stored.ETag;
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                return Result.Failure(ErrorMessages.Job.Conflict);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to save job {JobId}.", job.JobId);
                return Result.Failure(TableRepositoryBase.SanitizeStorageMessage(ex.Message));
            }
        }

        return Result.Failure(
            $"Failed to save job '{job.JobId}' after {maxRetries} attempts due to concurrent modifications");
    }

    /// <summary>
    ///     Atomically leases a watchdog candidate before non-idempotent work. The point read
    ///     revalidates the ETag observed by the sweep and sees leases committed before this caller
    ///     reached the row; the conditional update resolves callers that read concurrently.
    ///     Successful lifecycle saves replace the row and remove the lease. A failed worker leaves
    ///     only a short lease, after which another sweep can recover the candidate.
    /// </summary>
    public async Task<Result<bool>> TryClaimWatchdogJobAsync(
        Job job, TimeSpan? minimumAge = null, CancellationToken cancellationToken = default)
    {
        if (job is null)
            return Result<bool>.Failure("Job is required.");
        if (string.IsNullOrWhiteSpace(job.ETag))
            return Result<bool>.Failure(ErrorMessages.Job.EtagRequired);

        try
        {
            var currentResponse = await TableClient.GetEntityAsync<TableEntity>(
                JobEntityMapper.JobPartition(job.Market), job.JobId,
                select: ["LastUpdatedAt", WatchdogClaimUntilColumn],
                cancellationToken: cancellationToken);
            var current = currentResponse.Value;
            if (!string.Equals(current.ETag.ToString(), job.ETag, StringComparison.Ordinal))
                return Result<bool>.Success(false);

            var now = timeProvider.GetUtcNow();
            var lastUpdatedAt = current.GetDateTimeOffset("LastUpdatedAt") ?? current.Timestamp;
            if (minimumAge.HasValue
                && lastUpdatedAt.HasValue
                && now - lastUpdatedAt.Value < minimumAge.Value)
                return Result<bool>.Success(false);

            var existingClaimUntil = current.GetDateTimeOffset(WatchdogClaimUntilColumn);
            if (existingClaimUntil.HasValue && existingClaimUntil.Value > now)
                return Result<bool>.Success(false);

            var patch = new TableEntity(JobEntityMapper.JobPartition(job.Market), job.JobId)
            {
                ["LastUpdatedAt"] = now,
                [WatchdogClaimUntilColumn] = now.Add(WatchdogClaimLease)
            };
            var response = await TableClient.UpdateEntityAsync(
                patch, current.ETag, TableUpdateMode.Merge, cancellationToken);
            job.LastUpdatedAt = now.UtcDateTime;
            job.ETag = ReadResponseEtag(response, current.ETag.ToString());
            return Result<bool>.Success(true);
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412)
        {
            return Result<bool>.Success(false);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to claim watchdog job {JobId}.", job.JobId);
            return Result<bool>.Failure(TableRepositoryBase.SanitizeStorageMessage(ex.Message));
        }
    }



    public async Task<Result<JobStatusUpdateResult>> UpdateStatusAsync(
        string jobId, JobStatus status, string by, string? message,
        CancellationToken cancellationToken = default)
    {
        var index = await GetJobIndexAsync(jobId, cancellationToken);
        if (index.IsFailure)
            return Result<JobStatusUpdateResult>.Failure(index.Error);

        int retries = Math.Max(1, _persistenceOptions.StatusMergeRetries);
        for (int attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                var response = await TableClient.GetEntityAsync<JobEntity>(
                    index.Value.PartitionKey, jobId, cancellationToken: cancellationToken);
                var entity = response.Value;
                var previous = Enum.TryParse<JobStatus>(entity.Status, out var parsed)
                    ? parsed : JobStatus.Requested;
                var previousLastUpdatedAt = entity.LastUpdatedAt;
                if (previous == JobStatus.FallbackUsed && status != JobStatus.FallbackUsed)
                {
                    return Result<JobStatusUpdateResult>.Success(new JobStatusUpdateResult(
                        previous, previous, entity.ETag.ToString()));
                }

                var history = SafeJson.Deserialize<List<StatusHistoryEntry>>(
                    _mapper.ExpandTableProperty(entity.StatusHistoryJson, "StatusHistoryJson", jobId),
                    _logger, $"job {jobId} status history", JobEntityMapper.JsonOptions) ?? [];
                var now = timeProvider.GetUtcNow();
                var statusEntry = new StatusHistoryEntry(
                    status, now.UtcDateTime, by, Option.OfWhitespace(message));
                history.Add(statusEntry);
                var patch = new TableEntity(index.Value.PartitionKey, jobId)
                {
                    ["Status"] = status.ToString(),
                    ["LastUpdatedAt"] = now
                };

                // During migration a legacy history that is still present continues to be
                // maintained. Newly offloaded rows omit these columns and rely on the timeline.
                if (!UseTimelineWrite || !string.IsNullOrWhiteSpace(entity.StatusHistoryJson))
                    patch["StatusHistoryJson"] = _mapper.FitJobHistoryJson(
                        history, h => h.At, "StatusHistoryJson", jobId);
                if (!string.IsNullOrWhiteSpace(message))
                {
                    var logs = SafeJson.Deserialize<List<JobLog>>(
                        _mapper.ExpandTableProperty(entity.LogsJson, "LogsJson", jobId),
                        _logger, $"job {jobId} logs", JobEntityMapper.JsonOptions) ?? [];
                    logs.Add(new JobLog(now.UtcDateTime, JobLogLevel.Info, message));
                    if (!UseTimelineWrite || !string.IsNullOrWhiteSpace(entity.LogsJson))
                        patch["LogsJson"] = _mapper.FitJobHistoryJson(
                            logs, l => l.At, "LogsJson", jobId);
                }

                var update = await TableClient.UpdateEntityAsync(
                    patch, entity.ETag, TableUpdateMode.Merge, cancellationToken);
                string etag = ReadResponseEtag(update, entity.ETag.ToString())
                    ?? entity.ETag.ToString();
                entity.Status = status.ToString();
                entity.LastUpdatedAt = now;
                entity.ETag = new ETag(etag);
                await AfterStatusUpdateAsync(
                    entity, previous, statusEntry, message, cancellationToken, previousLastUpdatedAt);
                return Result<JobStatusUpdateResult>.Success(new JobStatusUpdateResult(
                    previous, status, etag));
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt + 1 < retries)
            {
                // Re-read the full row on the next iteration and preserve any concurrent
                // lifecycle transition. The sparse merge never replaces unrelated columns.
            }
            catch (RequestFailedException ex)
            {
                return Result<JobStatusUpdateResult>.Failure(
                    TableRepositoryBase.SanitizeStorageMessage(ex.Message));
            }
        }
        return Result<JobStatusUpdateResult>.Failure(ErrorMessages.Job.Conflict);
    }

    public async Task<Result<JobStatusUpdateResult>> TryClaimDeliveryAsync(
        string jobId,
        string reviewedEtag,
        DeliveryQueueCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reviewedEtag))
            return Result<JobStatusUpdateResult>.Failure(ErrorMessages.Job.EtagRequired);
        if (!string.Equals(command.JobId, jobId, StringComparison.Ordinal))
            return Result<JobStatusUpdateResult>.Failure("Delivery command job id does not match the requested job.");
        if (!string.Equals(command.ReviewedEtag, reviewedEtag, StringComparison.Ordinal))
            return Result<JobStatusUpdateResult>.Failure(ErrorMessages.Job.RevisionMismatch);

        var index = await GetJobIndexAsync(jobId, cancellationToken);
        if (index.IsFailure)
            return Result<JobStatusUpdateResult>.Failure(index.Error);

        int retries = Math.Max(1, _persistenceOptions.StatusMergeRetries);
        bool fallbackClaim = command.Kind == DeliveryKind.Fallback;
        JobStatus expected = fallbackClaim ? JobStatus.WaitingApproval : JobStatus.Approved;
        for (int attempt = 0; attempt < retries; attempt++)
        {
            try
            {
                var response = await TableClient.GetEntityAsync<JobEntity>(
                    index.Value.PartitionKey, jobId, cancellationToken: cancellationToken);
                var entity = response.Value;
                var previous = Enum.TryParse<JobStatus>(entity.Status, out var parsed)
                    ? parsed
                    : JobStatus.Requested;
                var previousLastUpdatedAt = entity.LastUpdatedAt;

                // A retry of the same command is an idempotent success. The original
                // reviewed ETag is deliberately compared to the claim field rather than
                // the row ETag, which changed when the pending status was written.
                if (previous == JobStatus.DeliveryPending
                    && string.Equals(entity.DeliveryIdempotencyKey, command.IdempotencyKey,
                        StringComparison.Ordinal)
                    && string.Equals(entity.DeliveryReviewedEtag, reviewedEtag,
                        StringComparison.Ordinal))
                {
                    return Result<JobStatusUpdateResult>.Success(new JobStatusUpdateResult(
                        previous, previous, entity.ETag.ToString()));
                }

                if (previous != expected && !(fallbackClaim && previous == JobStatus.FallbackUsed))
                {
                    return Result<JobStatusUpdateResult>.Failure(
                        $"Conflict: job {jobId} is {previous}; expected {expected} before delivery.");
                }
                if (!string.Equals(entity.ETag.ToString(), reviewedEtag, StringComparison.Ordinal))
                    return Result<JobStatusUpdateResult>.Failure(ErrorMessages.Job.RevisionMismatch);

                var now = timeProvider.GetUtcNow();
                var history = SafeJson.Deserialize<List<StatusHistoryEntry>>(
                    _mapper.ExpandTableProperty(entity.StatusHistoryJson, "StatusHistoryJson", jobId),
                    _logger, $"job {jobId} status history", JobEntityMapper.JsonOptions) ?? [];
                var statusEntry = new StatusHistoryEntry(
                    JobStatus.DeliveryPending, now.UtcDateTime, command.RequestedBy,
                    Option<string>.Some($"Delivery claimed ({command.Kind})"));
                history.Add(statusEntry);

                var patch = new TableEntity(index.Value.PartitionKey, jobId)
                {
                    ["Status"] = JobStatus.DeliveryPending.ToString(),
                    ["LastUpdatedAt"] = now,
                    ["DeliveryIdempotencyKey"] = command.IdempotencyKey,
                    ["DeliveryReviewedEtag"] = reviewedEtag,
                    ["DeliveryKind"] = command.Kind.ToString(),
                    ["DeliveryRequestedBy"] = command.RequestedBy,
                    ["DeliveryRequestedByObjectId"] = command.RequestedByObjectId,
                    ["DeliveryFallbackSourceJobId"] = command.FallbackSourceJobId,
                    ["DeliveryFallbackAudioUrl"] = command.FallbackAudioUrl,
                    ["DeliveryFallbackReason"] = command.FallbackReason,
                    ["DeliveryFallbackNotes"] = command.FallbackNotes,
                    ["DeliveryFallbackDurationSeconds"] = command.FallbackDurationSeconds,
                    ["DeliveryAttemptCount"] = 0
                };

                if (!UseTimelineWrite || !string.IsNullOrWhiteSpace(entity.StatusHistoryJson))
                    patch["StatusHistoryJson"] = _mapper.FitJobHistoryJson(
                        history, h => h.At, "StatusHistoryJson", jobId);

                var update = await TableClient.UpdateEntityAsync(
                    patch, entity.ETag, TableUpdateMode.Merge, cancellationToken);
                string etag = ReadResponseEtag(update, entity.ETag.ToString())
                    ?? entity.ETag.ToString();
                entity.Status = JobStatus.DeliveryPending.ToString();
                entity.LastUpdatedAt = now;
                entity.DeliveryIdempotencyKey = command.IdempotencyKey;
                entity.DeliveryReviewedEtag = reviewedEtag;
                entity.DeliveryKind = command.Kind.ToString();
                entity.DeliveryRequestedBy = command.RequestedBy;
                entity.DeliveryRequestedByObjectId = command.RequestedByObjectId;
                entity.DeliveryFallbackSourceJobId = command.FallbackSourceJobId;
                entity.DeliveryFallbackAudioUrl = command.FallbackAudioUrl;
                entity.DeliveryFallbackReason = command.FallbackReason;
                entity.DeliveryFallbackNotes = command.FallbackNotes;
                entity.DeliveryFallbackDurationSeconds = command.FallbackDurationSeconds;
                entity.DeliveryAttemptCount = 0;
                entity.DeliveryLastAttemptAt = null;
                entity.DeliveryLastError = null;
                entity.ETag = new ETag(etag);
                await AfterStatusUpdateAsync(entity, previous, statusEntry,
                    $"Delivery claimed ({command.Kind})", cancellationToken, previousLastUpdatedAt);
                return Result<JobStatusUpdateResult>.Success(new JobStatusUpdateResult(
                    previous, JobStatus.DeliveryPending, etag));
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result<JobStatusUpdateResult>.Failure($"Job '{jobId}' not found");
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt + 1 < retries)
            {
                // Retry the conditional read/merge; a competing claim will be observed
                // as the idempotent same-key success or a typed status conflict.
            }
            catch (RequestFailedException ex)
            {
                return Result<JobStatusUpdateResult>.Failure(
                    TableRepositoryBase.SanitizeStorageMessage(ex.Message));
            }
        }

        return Result<JobStatusUpdateResult>.Failure(ErrorMessages.Job.Conflict);
    }

    public async Task<Result> AddLogAsync(string jobId, string message, JobLogLevel level = JobLogLevel.Info,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        var indexResult = await GetJobIndexAsync(jobId, cancellationToken);
        if (indexResult.IsFailure)
            return Result.Failure(indexResult.Error);
        var partition = indexResult.Value.PartitionKey;
        if (UseTimelineWrite)
            return await AppendTimelineLogAsync(jobId, partition, message, level, cancellationToken);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                var response = await TableClient.GetEntityAsync<JobEntity>(
                    partition, jobId, cancellationToken: cancellationToken);
                var entity = response.Value;
                var logs = SafeJson.Deserialize<List<JobLog>>(
                    _mapper.ExpandTableProperty(entity.LogsJson, "LogsJson", jobId),
                    _logger, $"job {jobId} logs", JobEntityMapper.JsonOptions) ?? [];
                logs.Add(new JobLog(timeProvider.GetUtcNow().UtcDateTime, level, message));
                entity.LogsJson = _mapper.FitJobHistoryJson(logs, l => l.At, "LogsJson", jobId);
                entity.LastUpdatedAt = timeProvider.GetUtcNow();
                await TableClient.UpdateEntityAsync(
                    entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result.Failure($"Job '{jobId}' not found");
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                _logger.LogWarning(
                    "ETag conflict on AddLogAsync for job {JobId}, attempt {Attempt}. Retrying.",
                    jobId, attempt + 1);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Table Storage operation failed in AddLogAsync for job {JobId}", jobId);
                return Result.Failure(TableRepositoryBase.SanitizeStorageMessage(ex.Message));
            }
        }

        return Result.Failure(
            $"Failed to add log to job '{jobId}' after {maxRetries} attempts due to concurrent modifications");
    }

    public async Task<Result<Option<Job>>> GetLatestBulletinForMarketTodayAsync(
        string market,
        string station,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            // Continuity ("listeners just heard it", "changes since your last bulletin") is a
            // claim about what actually went to air. WaitingApproval/Approved/FallbackUsed have
            // not necessarily aired yet (an operator may reject or never act on a waiting job) —
            // only Published is a valid same-day continuity baseline.
            var statuses = new[]
            {
                JobStatus.Published
            };

            var dayStart = new DateTimeOffset(dayStartUtc, TimeSpan.Zero);
            var dayEnd = new DateTimeOffset(dayEndUtc, TimeSpan.Zero);

            var partitionKey = JobEntityMapper.JobPartition(market);
            Option<Job> latest = default;
            foreach (var status in statuses)
            {
                string? filter = TableClient.CreateQueryFilter(
                    $"PartitionKey eq {partitionKey} and Station eq {station} and Status eq {status.ToString()} and CreatedAt ge {dayStart} and CreatedAt lt {dayEnd}");
                var query = TableClient.QueryAsync<JobEntity>(filter, null, null, cancellationToken);
                await foreach (var entity in query)
                {
                    var job = _mapper.MapToDomain(entity);
                    if (latest.IsNone || job.CreatedAt > latest.Value.CreatedAt) latest = Option<Job>.Some(job);
                }
            }

            return latest;
        });
    }

    public async Task<Result<Option<Job>>> GetLastSuccessfulJobForMarketAsync(
        string market,
        string station,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        string? excludeJobId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_persistenceOptions.LastSuccessfulReadEnabled || _persistenceOptions.KillSwitch)
            return await ReadLegacyLastSuccessfulAsync(
                market, station, windowStartUtc, windowEndUtc, excludeJobId, cancellationToken);

        return await ExecuteAsync(async () =>
        {
            string rowKey = LastSuccessfulRowKey(market, station);
            var pointer = await GetEntityOrDefaultAsync<LastSuccessfulJobEntity>(
                "LastSuccessful", rowKey, cancellationToken);
            if (pointer.IsSuccess && pointer.Value.HasValue)
            {
                var p = pointer.Value.Value;
                if (QualifiesPointer(p, market, station, windowStartUtc, windowEndUtc, excludeJobId))
                {
                    var loaded = await GetJobAsync(new JobIndex(
                        p.JobId, p.Market,
                        string.IsNullOrWhiteSpace(p.JobPartitionKey)
                            ? JobEntityMapper.JobPartition(p.Market)
                            : p.JobPartitionKey,
                        p.ETag.ToString()), cancellationToken);
                    if (loaded.IsSuccess
                        && QualifiesJob(loaded.Value, market, station,
                            windowStartUtc, windowEndUtc, excludeJobId))
                        return Option<Job>.Some(loaded.Value);
                }
                await DeletePointerIfCurrentAsync(p, cancellationToken);
            }

            var fallback = await ReadPointerFallbackAsync(
                market, station, windowStartUtc, windowEndUtc, excludeJobId, cancellationToken);
            if (fallback.HasValue && _persistenceOptions.LastSuccessfulWriteEnabled)
                await MaintainLastSuccessfulAsync(fallback.Value, cancellationToken);
            return fallback;
        });
    }

    private async Task<Result<Option<Job>>> ReadLegacyLastSuccessfulAsync(
        string market,
        string station,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        string? excludeJobId,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(async () =>
        {
            var windowStart = new DateTimeOffset(windowStartUtc, TimeSpan.Zero);
            var windowEnd = new DateTimeOffset(windowEndUtc, TimeSpan.Zero);
            var partition = JobEntityMapper.JobPartition(market);
            Option<Job> latest = default;
            foreach (var status in new[]
                     { JobStatus.Approved, JobStatus.Published, JobStatus.FallbackUsed })
            {
                FormattableString query = $"PartitionKey eq {partition} and Station eq {station} and Status eq {status} and CreatedAt ge {windowStart} and CreatedAt lt {windowEnd}";
                string filter = TableClient.CreateQueryFilter(query);
                await foreach (var entity in TableClient.QueryAsync<JobEntity>(
                                   filter, MaxPageScanResults, null, cancellationToken))
                {
                    if (excludeJobId is not null && entity.RowKey == excludeJobId)
                        continue;
                    var job = _mapper.MapToDomain(entity);
                    if (job.AudioUrl.HasValue && !string.IsNullOrWhiteSpace(job.AudioUrl.Value)
                        && (latest.IsNone || job.CreatedAt > latest.Value.CreatedAt))
                        latest = Option<Job>.Some(job);
                }
            }
            return latest;
        });
    }

    private async Task<Option<Job>> ReadPointerFallbackAsync(
        string market,
        string station,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        string? excludeJobId,
        CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(windowStartUtc, TimeSpan.Zero);
        var end = new DateTimeOffset(windowEndUtc, TimeSpan.Zero);
        var partition = JobEntityMapper.JobPartition(market);
        FormattableString query = $"PartitionKey eq {partition} and Station eq {station} and CreatedAt ge {start} and CreatedAt lt {end} and (Status eq 'Approved' or Status eq 'Published' or Status eq 'FallbackUsed')";
        string filter = TableClient.CreateQueryFilter(query);
        var candidates = new List<TableEntity>();
        await foreach (var entity in TableClient.QueryAsync<TableEntity>(
                           filter, SummaryPageSize,
                           ["PartitionKey", "RowKey", "Market", "Station", "Status",
                               "CreatedAt", "LastUpdatedAt", "AudioUrl"],
                           cancellationToken))
        {
            if (excludeJobId is not null && entity.RowKey == excludeJobId)
                continue;
            if (string.IsNullOrWhiteSpace(StringValue(entity, "AudioUrl")))
                continue;
            candidates.Add(entity);
            if (candidates.Count >= MaxPageScanResults)
                break;
        }

        foreach (var candidate in candidates
                     .OrderByDescending(e => DateValue(e, "CreatedAt"))
                     .ThenByDescending(e => SuccessfulStatusRank(StringValue(e, "Status")))
                     .ThenByDescending(e => e.RowKey, StringComparer.Ordinal))
        {
            var loaded = await GetJobAsync(new JobIndex(
                candidate.RowKey, market, partition, candidate.ETag.ToString()), cancellationToken);
            if (loaded.IsSuccess
                && QualifiesJob(loaded.Value, market, station,
                    windowStartUtc, windowEndUtc, excludeJobId))
                return Option<Job>.Some(loaded.Value);
        }
        return Option<Job>.None;
    }

    public async Task<Result<IEnumerable<Job>>> ListJobsNeedingFallbackAsync(
        DateTime deadlineHorizonUtc,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var jobs = new List<Job>();
            var horizon = new DateTimeOffset(deadlineHorizonUtc, TimeSpan.Zero);
            string? filter = TableClient.CreateQueryFilter(
                $"PartitionKey ge 'Job_' and PartitionKey lt 'Job`' and FallbackDeadlineUtc le {horizon} and Status ne 'Published' and Status ne 'FallbackUsed' and Status ne 'Missed'");

            var query = TableClient.QueryAsync<JobEntity>(filter, null, null, cancellationToken);
            await foreach (var entity in query)
            {
                jobs.Add(_mapper.MapToDomain(entity));
                if (jobs.Count >= MaxTotalResults) break;
            }

            return jobs.AsEnumerable();
        });
    }

    public async Task<Result<IEnumerable<Job>>> ListStaleRequestedJobsAsync(
        DateTime staleThreshold,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var jobs = new List<Job>();
            var threshold = new DateTimeOffset(staleThreshold, TimeSpan.Zero);
            string? filter = TableClient.CreateQueryFilter(
                $"PartitionKey ge 'Job_' and PartitionKey lt 'Job`' and Status eq {JobStatus.Requested.ToString()} and LastUpdatedAt lt {threshold}");

            var query = TableClient.QueryAsync<JobEntity>(filter, null, null, cancellationToken);
            await foreach (var entity in query)
            {
                jobs.Add(_mapper.MapToDomain(entity));
                if (jobs.Count >= MaxTotalResults) break;
            }

            return jobs.AsEnumerable();
        });
    }

    public async Task<Result> DeleteJobAsync(string jobId, string expectedEtag,
        CancellationToken cancellationToken = default)
    {
        var indexResult = await GetEntityOrDefaultAsync<JobIndexEntity>("JobIndex", jobId, cancellationToken);
        if (indexResult.IsFailure)
            return indexResult.Error != null
                ? Result.Failure(indexResult.Error)
                : Result.Failure($"Job '{jobId}' not found");
        if (indexResult.Value.IsNone)
            return Result.Failure($"Job '{jobId}' not found");
        var partition = JobEntityMapper.JobPartition(indexResult.Value.Value.Market);

        // Conditional delete: the row must still carry the revision the caller loaded, so a
        // concurrent lifecycle write (e.g. an approve that landed while the delete was in
        // flight) makes the delete fail with a conflict instead of destroying the approved
        // bulletin and its audio blind (observed live 2026-08-05: DELETE 204 won a race
        // against an in-flight approve). The 412/404 mapping happens here rather than inside
        // ExecuteAsync: a lambda that returns Result would bind to the generic overload and
        // come back wrapped in Result<Result>.Success, silently turning a conflict into a
        // success.
        try
        {
            await TableClient.DeleteEntityAsync(partition, jobId, new Azure.ETag(expectedEtag),
                cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return Result.Failure(ErrorMessages.Job.Conflict);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Row already gone (concurrent deletion): fail idempotently without touching the index.
            return Result.Failure($"Job '{jobId}' not found");
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to delete job entity {JobId}.", jobId);
            return Result.Failure(TableRepositoryBase.SanitizeStorageMessage(ex.Message));
        }

        var result = await ExecuteAsync(async () =>
        {
            await TableClient.DeleteEntityAsync("JobIndex", jobId, cancellationToken: cancellationToken);
        });
        if (result.IsSuccess)
            await CleanupAuxiliaryArtifactsAsync(
                jobId, indexResult.Value.Value.Market, cancellationToken);
        return result;
    }

    private bool UseTimelineWrite =>
        _timelineRepository is not null
        && _persistenceOptions.TimelineWriteEnabled
        && !_persistenceOptions.KillSwitch;

    private bool UseTimelineRead =>
        _timelineRepository is not null
        && _persistenceOptions.TimelineReadEnabled
        && !_persistenceOptions.KillSwitch;

    private bool UsePayloadWrite =>
        _payloadRepository is not null
        && _persistenceOptions.PayloadWriteEnabled
        && !_persistenceOptions.KillSwitch;

    private bool UsePayloadRead =>
        _payloadRepository is not null
        && _persistenceOptions.PayloadReadEnabled
        && !_persistenceOptions.KillSwitch;

    private bool UseReadIndexRead =>
        _readIndexRepository is not null
        && _persistenceOptions.ReadIndexReadEnabled
        && !_persistenceOptions.KillSwitch;

    private bool UseReadIndexWrite =>
        _readIndexRepository is not null
        && _persistenceOptions.ReadIndexWriteEnabled
        && !_persistenceOptions.KillSwitch;

    private bool UseLedgerWrite =>
        _ledgerBucketRepository is not null
        && _persistenceOptions.LedgerWriteEnabled
        && !_persistenceOptions.KillSwitch;
    private bool UseLedgerRead =>
        _ledgerBucketRepository is not null
        && _persistenceOptions.LedgerReadEnabled
        && !_persistenceOptions.KillSwitch;


    private bool UsePointerWrite =>
        _persistenceOptions.LastSuccessfulWriteEnabled
        && !_persistenceOptions.KillSwitch;

    private async Task<JobEntity> PrepareEntityForWriteAsync(
        Job job, CancellationToken cancellationToken)
    {
        var entity = _mapper.MapToEntity(job);
        if (!UsePayloadWrite)
            return entity;

        // Preserve references from the previous row when the domain aggregate does not carry
        // storage-only metadata. A missing payload write never removes an existing reference.
        if (!string.IsNullOrWhiteSpace(job.ETag))
        {
            try
            {
                var existing = await TableClient.GetEntityAsync<JobEntity>(
                    JobEntityMapper.JobPartition(job.Market), job.JobId,
                    select: ["GeneratorInputRef", "IncidentsRef", "AllIncidentsRef",
                        "CorridorsRef", "FlowRoadsRef", "ScriptSentencesRef", "WordTimingsRef"],
                    cancellationToken: cancellationToken);
                entity.GeneratorInputRef = existing.Value.GeneratorInputRef;
                entity.IncidentsRef = existing.Value.IncidentsRef;
                entity.AllIncidentsRef = existing.Value.AllIncidentsRef;
                entity.CorridorsRef = existing.Value.CorridorsRef;
                entity.FlowRoadsRef = existing.Value.FlowRoadsRef;
                entity.ScriptSentencesRef = existing.Value.ScriptSentencesRef;
                entity.WordTimingsRef = existing.Value.WordTimingsRef;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // The subsequent conditional write reports the authoritative lifecycle result.
            }
        }

        var payloads = new Dictionary<JobPayloadKind, string>();
        entity.GeneratorInputRef = AddPayload(payloads, JobPayloadKind.GeneratorInput,
            entity.GeneratorInputJson, v => entity.GeneratorInputJson = v, job.JobId, "GeneratorInputJson")
            ?? entity.GeneratorInputRef;
        entity.IncidentsRef = AddPayload(payloads, JobPayloadKind.Incidents,
            entity.IncidentsJson, v => entity.IncidentsJson = v, job.JobId, "IncidentsJson")
            ?? entity.IncidentsRef;
        entity.AllIncidentsRef = AddPayload(payloads, JobPayloadKind.AllIncidents,
            entity.AllIncidentsJson, v => entity.AllIncidentsJson = v, job.JobId, "AllIncidentsJson")
            ?? entity.AllIncidentsRef;
        entity.CorridorsRef = AddPayload(payloads, JobPayloadKind.Corridors,
            entity.CorridorsJson, v => entity.CorridorsJson = v, job.JobId, "CorridorsJson")
            ?? entity.CorridorsRef;
        entity.FlowRoadsRef = AddPayload(payloads, JobPayloadKind.FlowRoads,
            entity.FlowRoadsJson, v => entity.FlowRoadsJson = v, job.JobId, "FlowRoadsJson")
            ?? entity.FlowRoadsRef;
        entity.ScriptSentencesRef = AddPayload(payloads, JobPayloadKind.ScriptSentences,
            entity.ScriptSentencesJson, v => entity.ScriptSentencesJson = v, job.JobId,
            "ScriptSentencesJson") ?? entity.ScriptSentencesRef;
        entity.WordTimingsRef = AddPayload(payloads, JobPayloadKind.WordTimings,
            entity.WordTimingsJson, v => entity.WordTimingsJson = v, job.JobId, "WordTimingsJson")
            ?? entity.WordTimingsRef;

        if (payloads.Count == 0)
            return entity;
        var result = await _payloadRepository!.SaveAsync(job.JobId, payloads, cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Payload offload failed for job {JobId}; retaining inline data: {Error}",
                job.JobId, result.Error);
            return _mapper.MapToEntity(job);
        }
        if (UseTimelineRead && UseTimelineWrite)
        {
            entity.LogsJson = null;
            entity.StatusHistoryJson = null;
        }
        return entity;
    }

    private string? AddPayload(
        IDictionary<JobPayloadKind, string> payloads, JobPayloadKind kind, string? stored,
        Action<string?> setInline, string jobId, string column)
    {
        var expanded = _mapper.ExpandTableProperty(stored, column, jobId);
        if (expanded is null
            || expanded.Length <= Math.Max(1, _persistenceOptions.PayloadOffloadThresholdChars))
            return null;
        string hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(expanded)));
        payloads[kind] = expanded;
        setInline(null);
        return $"jobPayload:{jobId}:{kind}:{hash}";
    }
    private static void AddReference(
        IDictionary<JobPayloadKind, string> references,
        JobPayloadKind kind,
        string? reference)
    {
        if (!string.IsNullOrWhiteSpace(reference))
            references[kind] = reference;
    }

    private async Task HydrateExternalPayloadsAsync(
        JobEntity entity, CancellationToken cancellationToken)
    {
        if (!UsePayloadRead
            || string.IsNullOrWhiteSpace(entity.GeneratorInputRef)
                && string.IsNullOrWhiteSpace(entity.IncidentsRef)
                && string.IsNullOrWhiteSpace(entity.AllIncidentsRef)
                && string.IsNullOrWhiteSpace(entity.CorridorsRef)
                && string.IsNullOrWhiteSpace(entity.FlowRoadsRef)
                && string.IsNullOrWhiteSpace(entity.ScriptSentencesRef)
                && string.IsNullOrWhiteSpace(entity.WordTimingsRef))
            return;
        var references = new Dictionary<JobPayloadKind, string>();
        AddReference(references, JobPayloadKind.GeneratorInput, entity.GeneratorInputRef);
        AddReference(references, JobPayloadKind.Incidents, entity.IncidentsRef);
        AddReference(references, JobPayloadKind.AllIncidents, entity.AllIncidentsRef);
        AddReference(references, JobPayloadKind.Corridors, entity.CorridorsRef);
        AddReference(references, JobPayloadKind.FlowRoads, entity.FlowRoadsRef);
        AddReference(references, JobPayloadKind.ScriptSentences, entity.ScriptSentencesRef);
        AddReference(references, JobPayloadKind.WordTimings, entity.WordTimingsRef);
        var result = await _payloadRepository!.ReadAsync(entity.RowKey, references, cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Payload read failed for job {JobId}; using inline data: {Error}",
                entity.RowKey, result.Error);
            return;
        }
        foreach (var payload in result.Value)
        {
            switch (payload.Key)
            {
                case JobPayloadKind.GeneratorInput when entity.GeneratorInputRef is not null:
                    entity.GeneratorInputJson = payload.Value; break;
                case JobPayloadKind.Incidents when entity.IncidentsRef is not null:
                    entity.IncidentsJson = payload.Value; break;
                case JobPayloadKind.AllIncidents when entity.AllIncidentsRef is not null:
                    entity.AllIncidentsJson = payload.Value; break;
                case JobPayloadKind.Corridors when entity.CorridorsRef is not null:
                    entity.CorridorsJson = payload.Value; break;
                case JobPayloadKind.FlowRoads when entity.FlowRoadsRef is not null:
                    entity.FlowRoadsJson = payload.Value; break;
                case JobPayloadKind.ScriptSentences when entity.ScriptSentencesRef is not null:
                    entity.ScriptSentencesJson = payload.Value; break;
                case JobPayloadKind.WordTimings when entity.WordTimingsRef is not null:
                    entity.WordTimingsJson = payload.Value; break;
            }
        }
    }

    private async Task<JobTimelineSnapshot?> ReadTimelineAsync(
        string jobId, CancellationToken cancellationToken)
    {
        if (!UseTimelineRead)
            return null;
        var result = await _timelineRepository!.ReadAsync(
            jobId, Math.Clamp(_persistenceOptions.TimelinePageSize, 1, 1000), cancellationToken);
        if (result.IsFailure)
        {
            _logger.LogWarning("Timeline read failed for job {JobId}; using inline history: {Error}",
                jobId, result.Error);
            return null;
        }
        return result.Value;
    }

    private static void MergeTimeline(Job job, JobTimelineSnapshot snapshot)
    {
        var statuses = job.StatusHistory
            .Concat(snapshot.StatusHistory)
            .DistinctBy(s => (
                s.At,
                s.Status,
                s.By,
                Note: s.Note.Match(static value => (string?)value, static () => (string?)null)))
            .OrderBy(s => s.At)
            .ThenBy(s => s.Status.ToString(), StringComparer.Ordinal)
            .ThenBy(s => s.By, StringComparer.Ordinal)
            .ToList();
        var logs = job.Logs
            .Concat(snapshot.Logs)
            .DistinctBy(l => (l.At, l.Level, l.Message))
            .OrderBy(l => l.At)
            .ThenBy(l => l.Level)
            .ThenBy(l => l.Message, StringComparer.Ordinal)
            .ToList();
        job.SetStatusHistory(statuses);
        job.SetLogs(logs);
    }

    private async Task<Result> AppendTimelineLogAsync(
        string jobId, string partition, string message, JobLogLevel level,
        CancellationToken cancellationToken)
    {
        var at = timeProvider.GetUtcNow();
        var append = await _timelineRepository!.AppendAsync(jobId,
            [new JobTimelineEntry(jobId, 0, at, "log", Level: level, Message: message)],
            cancellationToken);
        if (append.IsFailure)
            return append;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var response = await TableClient.GetEntityAsync<JobEntity>(
                    partition, jobId, cancellationToken: cancellationToken);
                var patch = new TableEntity(partition, jobId)
                {
                    ["LastUpdatedAt"] = at
                };
                if (!UseTimelineWrite || !string.IsNullOrWhiteSpace(response.Value.LogsJson))
                {
                    var logs = SafeJson.Deserialize<List<JobLog>>(
                        _mapper.ExpandTableProperty(response.Value.LogsJson, "LogsJson", jobId),
                        _logger, $"job {jobId} logs", JobEntityMapper.JsonOptions) ?? [];
                    logs.Add(new JobLog(at.UtcDateTime, level, message));
                    patch["LogsJson"] = _mapper.FitJobHistoryJson(
                        logs, l => l.At, "LogsJson", jobId);
                }
                await TableClient.UpdateEntityAsync(
                    patch, response.Value.ETag, TableUpdateMode.Merge, cancellationToken);
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < 2)
            {
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return Result.Failure($"Job '{jobId}' not found");
            }

            catch (RequestFailedException ex)
            {
                return Result.Failure(TableRepositoryBase.SanitizeStorageMessage(ex.Message));
            }
        }
        return Result.Failure(ErrorMessages.Job.Conflict);
    }
    private async Task AfterStatusUpdateAsync(
        JobEntity entity,
        JobStatus previous,
        StatusHistoryEntry statusEntry,
        string? message,
        CancellationToken cancellationToken,
        DateTimeOffset? previousLastUpdatedAt = null)
    {
        if (UseTimelineWrite)
        {
            var entries = new List<JobTimelineEntry>(message is null ? 1 : 2)
            {
                new(entity.RowKey, 0, new DateTimeOffset(statusEntry.At, TimeSpan.Zero),
                    "status", statusEntry.Status, statusEntry.By,
                    statusEntry.Note.Match(static value => (string?)value, static () => (string?)null))
            };
            if (!string.IsNullOrWhiteSpace(message))
            {
                entries.Add(new JobTimelineEntry(
                    entity.RowKey, 0, new DateTimeOffset(statusEntry.At, TimeSpan.Zero),
                    "log", Level: JobLogLevel.Info, Message: message));
            }
            var timeline = await _timelineRepository!.AppendAsync(
                entity.RowKey, entries, cancellationToken);
            if (timeline.IsFailure)
                _logger.LogWarning("Timeline maintenance failed for {JobId}: {Error}",
                    entity.RowKey, timeline.Error);
        }
        await UpsertReadIndexAsync(entity, cancellationToken, previousLastUpdatedAt);
        await UpdateReadIndexLocatorAsync(entity, cancellationToken);
        await UpsertLedgerAsync(entity, statusEntry.Status, cancellationToken);
        await MaintainLastSuccessfulAsync(entity, cancellationToken);

        _ = previous; // retained for the transition hook's diagnostic call contract
        await MaintainWorkIndexAfterSaveAsync(_mapper.MapToDomain(entity), cancellationToken);
    }

    private async Task DeletePreviousReadIndexAsync(
        Job job, JobIndexEntity? previousIndex, CancellationToken cancellationToken)
    {
        if (previousIndex is null || previousIndex.ReadIndexLastUpdatedAt is null
            || !UseReadIndexWrite)
            return;
        await DeleteReadIndexRowsAsync(job.JobId, previousIndex.Market,
            previousIndex.ReadIndexLastUpdatedAt.Value, cancellationToken);
    }

    private async Task DeleteReadIndexRowsAsync(
        string jobId, string market, DateTimeOffset previousLastUpdatedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var previous = new JobReadIndexItem(
                jobId, market, string.Empty, JobEntityMapper.JobPartition(market), null,
                JobStatus.Requested, previousLastUpdatedAt, previousLastUpdatedAt, null, null);
            var result = await _readIndexRepository!.DeleteAsync(previous, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Read-index stale-row cleanup failed for {JobId}: {Error}",
                    jobId, result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Read-index stale-row cleanup threw for {JobId}.", jobId);
        }
    }

    private async Task UpdateReadIndexLocatorAsync(
        JobEntity entity, CancellationToken cancellationToken)
    {
        if (!UseReadIndexWrite)
            return;
        try
        {
            await TableClient.UpsertEntityAsync(new JobIndexEntity
            {
                PartitionKey = "JobIndex",
                RowKey = entity.RowKey,
                Market = entity.Market,
                ReadIndexLastUpdatedAt = entity.LastUpdatedAt
            }, TableUpdateMode.Replace, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Job index locator update failed for {JobId}.", entity.RowKey);
        }
    }

    private async Task UpsertReadIndexAsync(
        JobEntity entity, CancellationToken cancellationToken,
        DateTimeOffset? previousLastUpdatedAt = null)
    {
        if (!UseReadIndexWrite)
            return;
        if (previousLastUpdatedAt is { } previous)
            await DeleteReadIndexRowsAsync(entity.RowKey, entity.Market, previous, cancellationToken);
        var result = await _readIndexRepository!.UpsertAsync(new JobReadIndexItem(
            entity.RowKey, entity.Market, entity.Station, entity.PartitionKey,
            entity.ETag.ToString(),
            ParseStatus(entity.Status),
            entity.CreatedAt, entity.LastUpdatedAt,
            entity.MeasuredDurationSec, entity.AudioUrl), cancellationToken);
        if (result.IsFailure)
            _logger.LogWarning("Read index maintenance failed for {JobId}: {Error}",
                entity.RowKey, result.Error);
    }

    private async Task UpsertLedgerAsync(
        JobEntity entity, JobStatus status, CancellationToken cancellationToken)
    {
        if (!UseLedgerWrite)
            return;
        bool hasIncidents = !string.IsNullOrWhiteSpace(entity.AllIncidentsRef)
            || SafeJson.Deserialize<List<JobIncident>>(
                _mapper.ExpandTableProperty(entity.AllIncidentsJson, "AllIncidentsJson", entity.RowKey),
                _logger, $"job {entity.RowKey} allIncidents", JobEntityMapper.JsonOptions)?.Count > 0;
        var result = await _ledgerBucketRepository!.UpsertAsync(new LedgerBucketEntry(
            entity.RowKey, entity.Market, entity.Station, entity.CreatedAt,
            entity.LastUpdatedAt, status, hasIncidents), cancellationToken);
        if (result.IsFailure)
            _logger.LogWarning("Ledger bucket maintenance failed for {JobId}: {Error}",
                entity.RowKey, result.Error);
    }

    private static JobStatus ParseStatus(string? value) =>
        Enum.TryParse<JobStatus>(value, out var status) ? status : JobStatus.Requested;

    private async Task MaintainWorkIndexAfterSaveAsync(
        Job job, CancellationToken cancellationToken)
    {
        if (_jobIndexMaintainer is null)
            return;
        try
        {
            var result = await _jobIndexMaintainer.AfterSaveAsync(job, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Work index maintenance failed for {JobId}: {Error}",
                    job.JobId, result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Work index maintenance threw for {JobId}; authoritative save succeeded.",
                job.JobId);
        }
    }
    private async Task MaintainWorkIndexAfterDeleteAsync(
        string jobId, string market, CancellationToken cancellationToken)
    {
        if (_jobIndexMaintainer is null)
            return;
        try
        {
            var result = await _jobIndexMaintainer.AfterDeleteAsync(jobId, market, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Work index cleanup failed for {JobId}: {Error}", jobId, result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Work index cleanup threw for {JobId}; authoritative delete succeeded.",
                jobId);
        }
    }


    private async Task AfterSaveAsync(
        Job job, CancellationToken cancellationToken, JobIndexEntity? previousIndex = null)
    {
        if (_readIndexRepository is not null
            && _persistenceOptions.ReadIndexWriteEnabled
            && !_persistenceOptions.KillSwitch)
        {
            await DeletePreviousReadIndexAsync(job, previousIndex, cancellationToken);
            var item = new JobReadIndexItem(
                job.JobId, job.Market, job.Station, JobEntityMapper.JobPartition(job.Market),
                job.ETag, job.Status, new DateTimeOffset(job.CreatedAt, TimeSpan.Zero),
                new DateTimeOffset(job.LastUpdatedAt, TimeSpan.Zero),
                job.MeasuredDurationSec.Match(v => (double?)v, () => null),
                job.AudioUrl.Match(v => (string?)v, () => null));
            var result = await _readIndexRepository.UpsertAsync(item, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Read index maintenance failed for {JobId}: {Error}",
                    job.JobId, result.Error);
        }

        if (_ledgerBucketRepository is not null
            && _persistenceOptions.LedgerWriteEnabled
            && !_persistenceOptions.KillSwitch)
        {
            var result = await _ledgerBucketRepository.UpsertAsync(new LedgerBucketEntry(
                job.JobId, job.Market, job.Station,
                new DateTimeOffset(job.CreatedAt, TimeSpan.Zero),
                new DateTimeOffset(job.LastUpdatedAt, TimeSpan.Zero),
                job.Status, job.AllIncidents.Count > 0), cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Ledger bucket maintenance failed for {JobId}: {Error}",
                    job.JobId, result.Error);
        }

        if (UsePointerWrite)
            await MaintainLastSuccessfulAsync(job, cancellationToken);

        if (UseTimelineWrite)
        {
            var entries = new List<JobTimelineEntry>(job.StatusHistory.Count + job.Logs.Count);
            long sequence = 1;
            foreach (var status in job.StatusHistory)
                entries.Add(new JobTimelineEntry(job.JobId, sequence++,
                    new DateTimeOffset(status.At, TimeSpan.Zero), "status",
                    status.Status, status.By, status.Note.Match(
                        static value => (string?)value, static () => (string?)null)));
            sequence = 1_000_000;
            foreach (var log in job.Logs)
                entries.Add(new JobTimelineEntry(job.JobId, sequence++,
                    new DateTimeOffset(log.At, TimeSpan.Zero), "log",
                    Level: log.Level, Message: log.Message));
            var result = await _timelineRepository!.AppendAsync(job.JobId, entries, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Timeline maintenance failed for {JobId}: {Error}",
                    job.JobId, result.Error);
        }
        await MaintainWorkIndexAfterSaveAsync(job, cancellationToken);
    }

    private static string? ReadResponseEtag(Response response, string? fallback) =>
        response.Headers.TryGetValue("ETag", out var etag) && !string.IsNullOrWhiteSpace(etag)
            ? etag : fallback;
    private async Task MaintainLastSuccessfulAsync(
        JobEntity entity, CancellationToken cancellationToken)
    {
        if (!UsePointerWrite)
            return;

        string rowKey = LastSuccessfulRowKey(entity.Market, entity.Station);
        var current = await GetEntityOrDefaultAsync<LastSuccessfulJobEntity>(
            "LastSuccessful", rowKey, cancellationToken);
        if (current.IsFailure)
        {
            _logger.LogWarning("LastSuccessful pointer read failed for {JobId}: {Error}",
                entity.RowKey, current.Error);
            return;
        }

        var status = ParseStatus(entity.Status);
        bool qualifies = status is JobStatus.Approved or JobStatus.Published or JobStatus.FallbackUsed
            && !string.IsNullOrWhiteSpace(entity.AudioUrl);
        if (!qualifies)
        {
            if (current.Value.HasValue
                && string.Equals(current.Value.Value.JobId, entity.RowKey, StringComparison.Ordinal))
                await DeletePointerIfCurrentAsync(current.Value.Value, cancellationToken);
            return;
        }

        var candidate = new LastSuccessfulJobEntity
        {
            PartitionKey = "LastSuccessful",
            RowKey = rowKey,
            JobId = entity.RowKey,
            Market = entity.Market,
            Station = entity.Station,
            Status = status.ToString(),
            CreatedAt = entity.CreatedAt,
            LastUpdatedAt = entity.LastUpdatedAt,
            AudioUrl = entity.AudioUrl!,
            JobPartitionKey = entity.PartitionKey
        };
        await UpsertLastSuccessfulPointerAsync(candidate, current.Value, cancellationToken);
    }

    private async Task MaintainLastSuccessfulAsync(
        Job job, CancellationToken cancellationToken)
    {
        if (!UsePointerWrite
            || job.Status is not (JobStatus.Approved or JobStatus.Published or JobStatus.FallbackUsed)
            || !job.AudioUrl.HasValue
            || string.IsNullOrWhiteSpace(job.AudioUrl.Value))
            return;

        string rowKey = LastSuccessfulRowKey(job.Market, job.Station);
        var candidate = new LastSuccessfulJobEntity
        {
            PartitionKey = "LastSuccessful",
            RowKey = rowKey,
            JobId = job.JobId,
            Market = job.Market,
            Station = job.Station,
            Status = job.Status.ToString(),
            CreatedAt = new DateTimeOffset(job.CreatedAt, TimeSpan.Zero),
            LastUpdatedAt = new DateTimeOffset(job.LastUpdatedAt, TimeSpan.Zero),
            AudioUrl = job.AudioUrl.Value,
            JobPartitionKey = JobEntityMapper.JobPartition(job.Market)
        };
        var current = await GetEntityOrDefaultAsync<LastSuccessfulJobEntity>(
            "LastSuccessful", rowKey, cancellationToken);
        if (current.IsFailure)
        {
            _logger.LogWarning("LastSuccessful pointer read failed for {JobId}: {Error}",
                job.JobId, current.Error);
            return;
        }
        await UpsertLastSuccessfulPointerAsync(candidate, current.Value, cancellationToken);
    }

    private async Task UpsertLastSuccessfulPointerAsync(
        LastSuccessfulJobEntity candidate,
        Option<LastSuccessfulJobEntity> current,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (current.HasValue && PointerAtLeastAsNew(current.Value, candidate))
                return;
            try
            {
                if (current.IsNone)
                    await TableClient.AddEntityAsync(candidate, cancellationToken);
                else
                    await TableClient.UpdateEntityAsync(
                        candidate, current.Value.ETag, TableUpdateMode.Replace, cancellationToken);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412 && attempt < 2)
            {
                var refreshed = await GetEntityOrDefaultAsync<LastSuccessfulJobEntity>(
                    candidate.PartitionKey, candidate.RowKey, cancellationToken);
                if (refreshed.IsFailure)
                    return;
                current = refreshed.Value;
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex, "LastSuccessful pointer update failed for {JobId}",
                    candidate.JobId);
                return;
            }
        }
    }

    private async Task DeletePointerIfCurrentAsync(
        LastSuccessfulJobEntity pointer, CancellationToken cancellationToken)
    {
        try
        {
            await TableClient.DeleteEntityAsync(
                pointer.PartitionKey, pointer.RowKey, pointer.ETag, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status is 404 or 412)
        {
        }
    }

    private static bool QualifiesPointer(
        LastSuccessfulJobEntity pointer, string market, string station,
        DateTime fromUtc, DateTime toUtc, string? excludeJobId) =>
        (excludeJobId is null || pointer.JobId != excludeJobId)
        && string.Equals(pointer.Market, market, StringComparison.OrdinalIgnoreCase)
        && string.Equals(pointer.Station, station, StringComparison.OrdinalIgnoreCase)
        && pointer.Status is "Approved" or "Published" or "FallbackUsed"
        && !string.IsNullOrWhiteSpace(pointer.AudioUrl)
        && pointer.CreatedAt.UtcDateTime >= fromUtc
        && pointer.CreatedAt.UtcDateTime < toUtc;

    private static bool QualifiesJob(
        Job job, string market, string station,
        DateTime fromUtc, DateTime toUtc, string? excludeJobId) =>
        (excludeJobId is null || job.JobId != excludeJobId)
        && string.Equals(job.Market, market, StringComparison.OrdinalIgnoreCase)
        && string.Equals(job.Station, station, StringComparison.OrdinalIgnoreCase)
        && job.Status is JobStatus.Approved or JobStatus.Published or JobStatus.FallbackUsed
        && job.AudioUrl.HasValue && !string.IsNullOrWhiteSpace(job.AudioUrl.Value)
        && job.CreatedAt >= fromUtc && job.CreatedAt < toUtc;

    private static bool PointerAtLeastAsNew(
        LastSuccessfulJobEntity current, LastSuccessfulJobEntity candidate) =>
        current.CreatedAt > candidate.CreatedAt
        || current.CreatedAt == candidate.CreatedAt
            && string.CompareOrdinal(current.JobId, candidate.JobId) >= 0;

    private static string LastSuccessfulRowKey(string market, string station) =>
        $"{SafeStorageKey(MarketKey.Normalize(market))}|{SafeStorageKey(station)}";

    private static string SafeStorageKey(string value) =>
        string.Concat(value.Select(c => c is '/' or '\\' or '#' or '?' || char.IsControl(c) ? '_' : c));

    private static DateTime DateValue(TableEntity entity, string name) =>
        entity.TryGetValue(name, out var value)
            ? value switch
            {
                DateTimeOffset dto => dto.UtcDateTime,
                DateTime dt => dt.ToUniversalTime(),
                _ => DateTime.TryParse(value?.ToString(), out var parsed)
                    ? parsed.ToUniversalTime() : default
            }
            : default;

    private static string StringValue(TableEntity entity, string name) =>
        entity.TryGetValue(name, out var value) ? value?.ToString() ?? string.Empty : string.Empty;

    private static int SuccessfulStatusRank(string value) =>
        value switch
        {
            "Published" => 3,
            "Approved" => 2,
            "FallbackUsed" => 1,
            _ => 0
        };
    private async Task CleanupAuxiliaryArtifactsAsync(
        string jobId, string market, CancellationToken cancellationToken)
    {
        await MaintainWorkIndexAfterDeleteAsync(jobId, market, cancellationToken);
        if (_timelineRepository is not null)
        {
            var result = await _timelineRepository.DeleteAsync(jobId, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Timeline cleanup failed for {JobId}: {Error}", jobId, result.Error);
        }
        if (_payloadRepository is not null)
        {
            var result = await _payloadRepository.DeleteAsync(jobId, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Payload cleanup failed for {JobId}: {Error}", jobId, result.Error);
        }
        if (_readIndexRepository is null) return;
        var rows = await _readIndexRepository.FindByJobIdAsync(jobId, cancellationToken);
        if (rows.IsFailure) return;
        foreach (var row in rows.Value)
        {
            var result = await _readIndexRepository.DeleteAsync(row, cancellationToken);
            if (result.IsFailure)
                _logger.LogWarning("Read-index cleanup failed for {JobId}: {Error}", jobId, result.Error);
        }
    }
    /// <summary>
    ///     Unions two log lists (this job's own appended entries and whatever a concurrent
    ///     <see cref="AddLogAsync"/> wrote underneath a 412-conflicted save), deduping on
    ///     timestamp and message and sorting chronologically so neither writer's entries are
    ///     lost when <see cref="SaveJobAsync"/> reconciles and retries.
    /// </summary>
    private static List<JobLog> MergeLogs(IEnumerable<JobLog> ours, IEnumerable<JobLog> theirs)
    {
        var seen = new HashSet<(DateTime At, string Message)>();
        var merged = new List<JobLog>();
        foreach (var log in ours.Concat(theirs).OrderBy(l => l.At))
            if (seen.Add((log.At, log.Message)))
                merged.Add(log);
        return merged;
    }
}

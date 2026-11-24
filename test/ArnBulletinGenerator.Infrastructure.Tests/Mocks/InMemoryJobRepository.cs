using System.Collections.Concurrent;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Tests.Mocks;

/// <summary>
///     In-memory job repository for local development.
///     Does not persist jobs across restarts.
/// </summary>
public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    public Task<Result<IEnumerable<Job>>> ListJobsAsync(CancellationToken cancellationToken = default)
    {
        var jobs = _jobs.Values.OrderByDescending(j => j.CreatedAt).ToList();
        return Task.FromResult(Result<IEnumerable<Job>>.Success(jobs));
    }

    public Task<Result<IEnumerable<JobListItem>>> ListJobSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var items = _jobs.Values
            .OrderByDescending(j => j.CreatedAt)
            .Select(ToListItem)
            .ToList();
        return Task.FromResult(Result<IEnumerable<JobListItem>>.Success(items));
    }

    public Task<Result<IEnumerable<JobListItem>>> ListJobSummariesAsync(
        IReadOnlyCollection<JobStatus> statuses, CancellationToken cancellationToken = default)
    {
        if (statuses is null || statuses.Count == 0)
            return ListJobSummariesAsync(cancellationToken);

        var set = statuses.ToHashSet();
        var items = _jobs.Values
            .Where(j => set.Contains(j.Status))
            .OrderByDescending(j => j.CreatedAt)
            .Select(ToListItem)
            .ToList();
        return Task.FromResult(Result<IEnumerable<JobListItem>>.Success(items));
    }

    public Task<Result<IReadOnlyList<JobListItem>>> ListJobSummariesPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default)
    {
        var statusSet = query.Statuses is { Count: > 0 } ? query.Statuses.ToHashSet() : null;
        var items = _jobs.Values
            .Where(j =>
                (j.LastUpdatedAt >= query.FromUtc.UtcDateTime && j.LastUpdatedAt < query.ToUtc.UtcDateTime)
                || (query.CursorJobId is { Length: > 0 }
                    && j.LastUpdatedAt == query.ToUtc.UtcDateTime
                    && string.CompareOrdinal(j.JobId, query.CursorJobId) < 0))
            .Where(j => statusSet is null || statusSet.Contains(j.Status))
            .OrderByDescending(j => j.LastUpdatedAt)
            .ThenByDescending(j => j.JobId, StringComparer.Ordinal)
            .Take(query.Limit)
            .Select(ToListItem)
            .ToList() as IReadOnlyList<JobListItem>;
        return Task.FromResult(Result<IReadOnlyList<JobListItem>>.Success(items));
    }

    public Task<Result<JobLedgerProjection>> GetJobLedgerProjectionAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        return _jobs.TryGetValue(jobId, out var job)
            ? Task.FromResult(Result<JobLedgerProjection>.Success(JobLedgerProjection.FromJob(job)))
            : Task.FromResult(Result<JobLedgerProjection>.Failure("Job not found"));
    }

    public Task<Result<IReadOnlyList<JobLedgerProjection>>> ListJobLedgerPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default)
    {
        var statusSet = query.Statuses is { Count: > 0 } ? query.Statuses.ToHashSet() : null;
        var items = _jobs.Values
            .Where(j => string.IsNullOrWhiteSpace(query.Market)
                        || string.Equals(j.Market, query.Market, StringComparison.OrdinalIgnoreCase))
            .Where(j =>
                (j.LastUpdatedAt >= query.FromUtc.UtcDateTime && j.LastUpdatedAt < query.ToUtc.UtcDateTime)
                || (query.CursorJobId is { Length: > 0 }
                    && j.LastUpdatedAt == query.ToUtc.UtcDateTime
                    && string.CompareOrdinal(j.JobId, query.CursorJobId) < 0))
            .Where(j => statusSet is null || statusSet.Contains(j.Status))
            .OrderByDescending(j => j.LastUpdatedAt)
            .ThenByDescending(j => j.JobId, StringComparer.Ordinal)
            .Take(query.Limit)
            .Select(j => JobLedgerProjection.FromJob(j))
            .ToList() as IReadOnlyList<JobLedgerProjection>;
        return Task.FromResult(Result<IReadOnlyList<JobLedgerProjection>>.Success(items));
    }

    private static JobListItem ToListItem(Job job) => JobListItem.FromJob(job);

    public Task<Result<IEnumerable<Job>>> ListJobsAsync(
        IReadOnlyCollection<JobStatus> statuses, CancellationToken cancellationToken = default)
    {
        if (statuses is null || statuses.Count == 0)
            return ListJobsAsync(cancellationToken);

        var set = statuses.ToHashSet();
        var jobs = _jobs.Values
            .Where(j => set.Contains(j.Status))
            .OrderByDescending(j => j.CreatedAt)
            .ToList();
        return Task.FromResult(Result<IEnumerable<Job>>.Success(jobs));
    }

    public Task<Result<IReadOnlyList<Job>>> ListJobsPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default)
    {
        var statusSet = query.Statuses is { Count: > 0 } ? query.Statuses.ToHashSet() : null;
        var jobs = _jobs.Values
            .Where(j =>
            {
                if (j.LastUpdatedAt >= query.FromUtc.UtcDateTime && j.LastUpdatedAt < query.ToUtc.UtcDateTime)
                    return true;
                // Composite cursor: continue strictly after the boundary row in the stable total
                // order (LastUpdatedAt desc, then row key desc), mirroring the storage filter.
                return query.CursorJobId is { Length: > 0 }
                    && j.LastUpdatedAt == query.ToUtc.UtcDateTime
                    && string.CompareOrdinal(j.JobId, query.CursorJobId) < 0;
            })
            .Where(j => statusSet is null || statusSet.Contains(j.Status))
            .OrderByDescending(j => j.LastUpdatedAt)
            .ThenByDescending(j => j.JobId, StringComparer.Ordinal)
            .Take(query.Limit)
            .ToList() as IReadOnlyList<Job>;
        return Task.FromResult(Result<IReadOnlyList<Job>>.Success(jobs));
    }
    public Task<Result<JobIndex>> GetJobIndexAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        return _jobs.TryGetValue(jobId, out var job)
            ? Task.FromResult(Result<JobIndex>.Success(new JobIndex(
                job.JobId, job.Market, $"Job_{job.Market.Trim().ToLowerInvariant()}", job.ETag)))
            : Task.FromResult(Result<JobIndex>.Failure("Job not found"));
    }

    public Task<Result<Job>> GetJobAsync(
        JobIndex index, CancellationToken cancellationToken = default)
    {
        return _jobs.TryGetValue(index.JobId, out var job)
            ? Task.FromResult(Result<Job>.Success(job))
            : Task.FromResult(Result<Job>.Failure("Job not found"));
    }

    public Task<Result<Job>> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        return _jobs.TryGetValue(jobId, out var job)
            ? Task.FromResult(Result<Job>.Success(job))
            : Task.FromResult(Result<Job>.Failure("Job not found"));
    }

    public Task<Result> SaveJobAsync(Job job, CancellationToken cancellationToken = default)
    {
        _jobs[job.JobId] = job;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> AddLogAsync(string jobId, string message, JobLogLevel level = JobLogLevel.Info,
        CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            job.AddLog(new JobLog(DateTime.UtcNow, level, message));
            job.LastUpdatedAt = DateTime.UtcNow;
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result<Option<Job>>> GetLatestBulletinForMarketTodayAsync(
        string market,
        string station,
        DateTime dayStartUtc,
        DateTime dayEndUtc,
        CancellationToken cancellationToken = default)
    {
        var startOfDay = dayStartUtc;
        var endOfDay = dayEndUtc;

        // Mirrors TableStorageJobRepository: only Published counts as a genuine same-day
        // continuity baseline (see rationale there).
        var validStatuses = new[]
        {
            JobStatus.Published
        };

        var latestJob = _jobs.Values
            .Where(j => j.Market == market
                        && j.Station == station
                        && validStatuses.Contains(j.Status)
                        && j.CreatedAt >= startOfDay
                        && j.CreatedAt < endOfDay)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefault();

        return Task.FromResult(
            Result<Option<Job>>.Success(latestJob is null ? Option<Job>.None : Option<Job>.Some(latestJob)));
    }

    public Task<Result<Option<Job>>> GetLastSuccessfulJobForMarketAsync(
        string market,
        string station,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        string? excludeJobId = null,
        CancellationToken cancellationToken = default)
    {
        var validStatuses = new[]
        {
            JobStatus.Approved,
            JobStatus.Published,
            JobStatus.FallbackUsed
        };

        var latestJob = _jobs.Values
            .Where(j => j.Market == market
                        && j.Station == station
                        && validStatuses.Contains(j.Status)
                        && j.AudioUrl.HasValue
                        && (excludeJobId is null || j.JobId != excludeJobId)
                        && j.CreatedAt >= windowStartUtc
                        && j.CreatedAt < windowEndUtc)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefault();

        return Task.FromResult(
            Result<Option<Job>>.Success(latestJob is null ? Option<Job>.None : Option<Job>.Some(latestJob)));
    }

    public Task<Result<IEnumerable<Job>>> ListJobsNeedingFallbackAsync(
        DateTime deadlineHorizonUtc,
        CancellationToken cancellationToken = default)
    {
        var candidates = _jobs.Values
            .Where(j => j.FallbackDeadlineUtc.HasValue && j.FallbackDeadlineUtc.Value <= deadlineHorizonUtc)
            .ToList();

        return Task.FromResult(Result<IEnumerable<Job>>.Success(candidates));
    }

    public Task<Result<IEnumerable<Job>>> ListStaleRequestedJobsAsync(
        DateTime staleThreshold,
        CancellationToken cancellationToken = default)
    {
        var staleJobs = _jobs.Values
            .Where(j => j.Status == JobStatus.Requested && j.LastUpdatedAt < staleThreshold)
            .ToList();

        return Task.FromResult(Result<IEnumerable<Job>>.Success(staleJobs.AsEnumerable()));
    }

    public Task<Result> DeleteJobAsync(string jobId, string expectedEtag,
        CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId, out var existing) && existing.ETag != expectedEtag)
            return Task.FromResult(Result.Failure(ErrorMessages.Job.Conflict));
        _jobs.TryRemove(jobId, out _);
        return Task.FromResult(Result.Success());
    }
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Persists and queries bulletin Job aggregates, including the lookups the fallback and dedupe workflows use to find a market's prior or in-flight jobs.</summary>
public interface IJobRepository
{
    Task<Result<Job>> GetJobAsync(string jobId, CancellationToken cancellationToken = default);
    /// <summary>Reads only the canonical JobIndex row for authorization/routing.</summary>
    Task<Result<JobIndex>> GetJobIndexAsync(
        string jobId, CancellationToken cancellationToken = default);

    /// <summary>Reads an aggregate using already-resolved canonical routing information.</summary>
    Task<Result<Job>> GetJobAsync(
        JobIndex index, CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<Job>>> ListJobsAsync(CancellationToken cancellationToken = default);
    Task<Result<IEnumerable<Job>>> ListJobsAsync(
        IReadOnlyCollection<JobStatus> statuses, CancellationToken cancellationToken = default);

    /// <summary>
    ///     A single bounded, newest-first page of settled jobs by settle time, for the History
    ///     log. See <see cref="JobListQuery" /> for the paging contract.
    /// </summary>
    Task<Result<IReadOnlyList<Job>>> ListJobsPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default);
    /// <summary>Reads list rows without reconstructing full job aggregates or their JSON snapshots.</summary>
    Task<Result<IEnumerable<JobListItem>>> ListJobSummariesAsync(
        CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<JobListItem>>> ListJobSummariesAsync(
        IReadOnlyCollection<JobStatus> statuses, CancellationToken cancellationToken = default);

    /// <summary>Reads one bounded list page using the lightweight summary projection.</summary>
    Task<Result<IReadOnlyList<JobListItem>>> ListJobSummariesPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads only the fields required by ledger folding and delta comparison.</summary>
    Task<Result<JobLedgerProjection>> GetJobLedgerProjectionAsync(
        string jobId, CancellationToken cancellationToken = default);

    /// <summary>Reads a bounded ledger page without reconstructing unrelated job snapshots.</summary>
    Task<Result<IReadOnlyList<JobLedgerProjection>>> ListJobLedgerPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default);
    Task<Result> SaveJobAsync(Job job, CancellationToken cancellationToken = default);

    Task<Result> AddLogAsync(string jobId, string message, JobLogLevel level = JobLogLevel.Info,
        CancellationToken cancellationToken = default);

    Task<Result<Option<Job>>> GetLatestBulletinForMarketTodayAsync(
        string market, string station, DateTime dayStartUtc, DateTime dayEndUtc,
        CancellationToken cancellationToken = default);

    Task<Result<Option<Job>>> GetLastSuccessfulJobForMarketAsync(
        string market, string station, DateTime windowStartUtc, DateTime windowEndUtc,
        string? excludeJobId = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Unsettled jobs whose slot deadline (<see cref="Job.FallbackDeadlineUtc" />) is at or
    ///     before <paramref name="deadlineHorizonUtc" />. The horizon is NOT "now": the sweep
    ///     reaches into the future by <c>BroadcastOptions.ApprovedDeliveryLeadSeconds</c> so an
    ///     approved bulletin can be delivered while the playout guard still permits it. Callers
    ///     MUST therefore re-check each job's deadline against the real clock before acting on
    ///     anything but that delivery.
    /// </summary>
    Task<Result<IEnumerable<Job>>> ListJobsNeedingFallbackAsync(
        DateTime deadlineHorizonUtc, CancellationToken cancellationToken = default);

    Task<Result<IEnumerable<Job>>> ListStaleRequestedJobsAsync(
        DateTime staleThreshold, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Deletes a job ONLY if the row still carries <paramref name="expectedEtag" /> — the
    ///     revision the caller loaded. A concurrent lifecycle write (approve/publish/reject)
    ///     bumps the stored revision, so the delete fails with a conflict instead of destroying
    ///     a bulletin that changed underneath the caller (observed live 2026-08-05: a blind
    ///     DELETE 204 won a race against an in-flight approve and deleted the just-approved
    ///     bulletin and its audio unrecoverably).
    /// </summary>
    Task<Result> DeleteJobAsync(string jobId, string expectedEtag, CancellationToken cancellationToken = default);
}

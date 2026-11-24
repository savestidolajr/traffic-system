namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Filter/paging parameters for browsing a bounded newest-first job window, mirroring
///     <see cref="ActivityLogQuery" />'s from/to/limit contract. The History log and the Event
///     Ledger page backward in time by re-issuing with <c>ToUtc</c> set to the oldest previously
///     returned job's <c>LastUpdatedAt</c>.
///     <para>
///         Pages are ordered newest-first by a stable total order: <c>LastUpdatedAt</c>
///         descending, then row key (job id) descending within equal timestamps. Bulk writes
///         (poller batches, bulk status changes) stamp identical timestamps, so a timestamp-only
///         boundary is ambiguous: a strict <c>LastUpdatedAt lt cursor</c> filter silently skips
///         every job sharing the boundary timestamp that fell outside the truncated page
///         (observed live 2026-08-04: 4 of 6 tied jobs never appeared across pages). A composite
///         cursor therefore pins the boundary to a specific row: <c>CursorJobId</c> carries the
///         row key of the page's oldest item, and the page filter becomes
///         <c>LastUpdatedAt lt ToUtc or (LastUpdatedAt eq ToUtc and RowKey lt CursorJobId)</c>.
///     </para>
///     <para>
///         Deliberately separate from the other <c>IJobRepository.ListJobsAsync</c> overloads,
///         which background services rely on returning the complete, unbounded set for
///         correctness (e.g. an escalation scan must see every waiting job, not just a recent
///         page of them).
///     </para>
/// </summary>
public sealed record JobListQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int Limit,
    IReadOnlyCollection<JobStatus>? Statuses = null,
    /// <summary>
    ///     Row key of the page's oldest item, for the composite cursor. Null when the caller
    ///     pages by timestamp alone (legacy/backward-compatible contract).
    /// </summary>
    string? CursorJobId = null,
    /// <summary>
    ///     Optional market value used to restrict the scan to its normalized partition. Null keeps
    ///     the all-market History contract.
    string? Market = null);

/// <summary>
///     The scalar fields needed by operator job lists. This deliberately avoids reconstructing a
///     complete <see cref="Job" /> aggregate when a list response only needs summary data.
/// </summary>
public sealed record JobListItem(
    string JobId,
    string Station,
    string Market,
    double TargetDurationSeconds,
    JobStatus Status,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    string? ETag,
    double? MeasuredDurationSec)
{
    public double? DeltaSeconds => MeasuredDurationSec is { } measured
        ? measured - TargetDurationSeconds
        : null;

    public static JobListItem FromJob(Job job) => new(
        job.JobId,
        job.Station,
        job.Market,
        job.TargetDurationSeconds,
        job.Status,
        job.CreatedAt,
        job.LastUpdatedAt,
        job.ETag,
        job.MeasuredDurationSec.Match(value => (double?)value, () => null));
}

/// <summary>
///     The persisted fields needed to fold the event ledger and calculate a job delta. It keeps
///     large unrelated job snapshots out of those read paths.
/// </summary>
public sealed record JobLedgerProjection(
    string JobId,
    string Station,
    string Market,
    double TargetDurationSeconds,
    JobStatus Status,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    IReadOnlyList<JobIncident> AllIncidents,
    IReadOnlyList<JobCorridor> Corridors,
    IReadOnlyList<JobScriptSentence> ScriptSentences,
    string? PreviousJobId = null)
{
    public static JobLedgerProjection FromJob(Job job) => new(
        job.JobId,
        job.Station,
        job.Market,
        job.TargetDurationSeconds,
        job.Status,
        job.CreatedAt,
        job.LastUpdatedAt,
        job.AllIncidents,
        job.Corridors,
        job.ScriptSentences);

    public Job ToJob()
    {
        var job = Job.Reconstitute(
            JobId,
            Station,
            Market,
            TargetDurationSeconds,
            [new StatusHistoryEntry(Status, CreatedAt, "storage")]);
        job.LastUpdatedAt = LastUpdatedAt;
        job.SetAllIncidents(AllIncidents);
        job.SetCorridors(Corridors);
        job.PreviousJobId = Option.OfWhitespace(PreviousJobId);
        job.SetScriptSentences(ScriptSentences);
        return job;
    }
}

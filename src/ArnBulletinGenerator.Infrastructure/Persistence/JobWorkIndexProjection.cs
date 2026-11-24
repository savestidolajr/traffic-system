using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>Projects a job into exactly the watchdog eligibility sets.</summary>
internal static class JobWorkIndexProjection
{
    private static readonly JobStatus[] MissableStatuses =
    [
        JobStatus.Requested,
        JobStatus.HereFetched,
        JobStatus.IncidentsSelected,
        JobStatus.AwaitingCuration,
        JobStatus.ScriptReady,
        JobStatus.SynthesizingAudio,
        JobStatus.AudioReady,
        JobStatus.WaitingApproval,
        JobStatus.Approved,
        JobStatus.Failed
    ];

    /// <summary>
    ///     Uses the same status and deadline predicates as the six watchdog repository queries.
    ///     Missing legacy deadlines are indexed against the last known touch so they are repaired
    ///     promptly without widening the indexed read to an unbounded table scan.
    /// </summary>
    internal static IReadOnlyList<JobWorkItem> For(Job job)
    {
        var items = new List<JobWorkItem>(capacity: 6);

        if (job.Status == JobStatus.AwaitingCuration)
        {
            items.Add(Create(job, JobWorkKind.CurationDue,
                job.CurationDeadlineUtc.GetValueOr(job.LastUpdatedAt)));
        }

        if (job.Status is not (JobStatus.Published or JobStatus.FallbackUsed or JobStatus.Missed)
            && job.FallbackDeadlineUtc.HasValue)
        {
            items.Add(Create(job, JobWorkKind.FallbackDue, job.FallbackDeadlineUtc.Value));
        }

        if (job.Status == JobStatus.WaitingApproval && job.ScheduledForUtc.HasValue)
            items.Add(Create(job, JobWorkKind.Escalation, job.ScheduledForUtc.Value));

        if (MissableStatuses.Contains(job.Status))
        {
            var due = job.FallbackDeadlineUtc.GetValueOr(
                job.Status == JobStatus.WaitingApproval ? job.CreatedAt : job.LastUpdatedAt);
            items.Add(Create(job, JobWorkKind.Missable, due));
        }

        if (job.Status == JobStatus.Requested)
            items.Add(Create(job, JobWorkKind.StaleRequested, job.LastUpdatedAt.AddMinutes(3)));

        if (job.Status == JobStatus.FallbackUsed && job.FallbackAppliedAt.HasValue)
            items.Add(Create(job, JobWorkKind.UndeliveredCovered, job.FallbackAppliedAt.Value));

        return items;
    }

    private static JobWorkItem Create(Job job, JobWorkKind kind, DateTime dueUtc)
    {
        return new JobWorkItem
        {
            Kind = kind,
            JobId = job.JobId,
            Market = job.Market,
            Station = job.Station,
            Status = job.Status,
            DueUtc = DateTime.SpecifyKind(dueUtc, DateTimeKind.Utc),
            LastUpdatedAt = DateTime.SpecifyKind(job.LastUpdatedAt, DateTimeKind.Utc),
            FallbackDeadlineUtc = job.FallbackDeadlineUtc.HasValue
                ? DateTime.SpecifyKind(job.FallbackDeadlineUtc.Value, DateTimeKind.Utc) : null,
            ScheduledForUtc = job.ScheduledForUtc.HasValue
                ? DateTime.SpecifyKind(job.ScheduledForUtc.Value, DateTimeKind.Utc) : null,
            CurationDeadlineUtc = job.CurationDeadlineUtc.HasValue
                ? DateTime.SpecifyKind(job.CurationDeadlineUtc.Value, DateTimeKind.Utc) : null,
            HasAudio = job.AudioUrl.HasValue
        };
    }

    internal static string PartitionKey(JobWorkKind kind, DateTime bucketUtc)
        => $"Work_{kind}_{bucketUtc.ToUniversalTime():yyyyMMddHHmm}";

    internal static string RowKey(JobWorkItem item)
        => $"{item.DueUtc.Ticks:D19}_{item.JobId}";
}

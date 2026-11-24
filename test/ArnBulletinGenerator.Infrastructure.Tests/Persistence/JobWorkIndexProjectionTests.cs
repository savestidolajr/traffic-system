using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class JobWorkIndexProjectionTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 1, 2, 3, DateTimeKind.Utc);

    [Theory]
[InlineData(JobStatus.Requested, false, true, true, false, false)]
    [InlineData(JobStatus.HereFetched, false, true, false, false, false)]
    [InlineData(JobStatus.IncidentsSelected, false, true, false, false, false)]
    [InlineData(JobStatus.ScriptReady, false, true, false, false, false)]
    [InlineData(JobStatus.SynthesizingAudio, false, true, false, false, false)]
    [InlineData(JobStatus.AudioReady, false, true, false, false, false)]
    [InlineData(JobStatus.WaitingApproval, false, true, false, true, false)]
    [InlineData(JobStatus.Approved, false, true, false, false, false)]
    [InlineData(JobStatus.Failed, false, true, false, false, false)]
    [InlineData(JobStatus.AwaitingCuration, true, true, false, false, false)]
    [InlineData(JobStatus.Rejected, false, true, false, false, false)]
    [InlineData(JobStatus.FallbackUsed, false, false, false, false, true)]
    [InlineData(JobStatus.Published, false, false, false, false, false)]
    [InlineData(JobStatus.Missed, false, false, false, false, false)]
    public void For_matches_watchdog_status_and_field_filters(
        JobStatus status,
        bool curation,
        bool fallback,
        bool stale,
        bool escalation,
        bool undelivered)
    {
        var job = Job.Reconstitute("job-1", "station", "market", 90,
            [new StatusHistoryEntry(status, Now.AddMinutes(-10), "test")],
            createdAt: Now.AddMinutes(-10), status: status);
        job.LastUpdatedAt = Now.AddMinutes(-10);
        job.CurationDeadlineUtc = Option<DateTime>.Some(Now.AddMinutes(-1));
        job.FallbackDeadlineUtc = Option<DateTime>.Some(Now.AddMinutes(-5));
        job.ScheduledForUtc = Option<DateTime>.Some(Now.AddMinutes(5));
        job.AudioUrl = Option<string>.Some("audio");
        if (status == JobStatus.FallbackUsed)
            job.FallbackAppliedAt = Option<DateTime>.Some(Now.AddMinutes(-1));

        var kinds = JobWorkIndexProjection.For(job).Select(item => item.Kind).ToHashSet();

        kinds.Contains(JobWorkKind.CurationDue).ShouldBe(curation);
        kinds.Contains(JobWorkKind.FallbackDue).ShouldBe(fallback);
        kinds.Contains(JobWorkKind.StaleRequested).ShouldBe(stale);
        kinds.Contains(JobWorkKind.Escalation).ShouldBe(escalation);
        kinds.Contains(JobWorkKind.UndeliveredCovered).ShouldBe(undelivered);
        kinds.Contains(JobWorkKind.Missable).ShouldBe(
            status is JobStatus.Requested or JobStatus.HereFetched or JobStatus.IncidentsSelected
                or JobStatus.AwaitingCuration or JobStatus.ScriptReady or JobStatus.SynthesizingAudio
                or JobStatus.AudioReady or JobStatus.WaitingApproval or JobStatus.Approved or JobStatus.Failed);
    }

    [Fact]
    public void For_uses_due_ticks_row_key_ordering_contract()
    {
        var job = Job.Reconstitute("job-2", "station", "market", 90,
            [new StatusHistoryEntry(JobStatus.Requested, Now, "test")],
            createdAt: Now, status: JobStatus.Requested);
        job.LastUpdatedAt = Now;
        var item = JobWorkIndexProjection.For(job).Single(i => i.Kind == JobWorkKind.StaleRequested);

        JobWorkIndexProjection.PartitionKey(item.Kind, item.DueUtc)
            .ShouldBe("Work_StaleRequested_202609020105");
        JobWorkIndexProjection.RowKey(item)
            .ShouldBe($"{item.DueUtc.Ticks:D19}_job-2");
    }
}

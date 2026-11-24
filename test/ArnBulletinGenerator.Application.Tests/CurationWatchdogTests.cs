using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class CurationWatchdogTests
{
    private static readonly DateTime Now = new(2026, 8, 19, 1, 0, 0, DateTimeKind.Utc);
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly IQueuePublisher _queue = Substitute.For<IQueuePublisher>();
    private readonly JobWatchdog _sut;

    public CurationWatchdogTests()
    {
        _queue.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var options = Options.Create(new BroadcastOptions { CurationWindowMinutes = 5 });
        var resolver = new StationConfigResolver(Substitute.For<IModelSelector>(),
            Substitute.For<IVoiceProfileService>(), Substitute.For<IVoiceProvider>(),
            Substitute.For<IDeliveryStyleService>(), Substitute.For<IBulletinPlaybookService>(),
            Substitute.For<IBookendService>(),
            Substitute.For<IPronunciationDictionaryService>(), options, TimeProvider.System,
            Substitute.For<ILogger>());
        _sut = new JobWatchdog(Substitute.For<IScheduleRepository>(), _jobs, Substitute.For<IJobService>(),
            Substitute.For<INotificationService>(), Substitute.For<IBulletinFallbackService>(),
            Substitute.For<ICronParser>(), _queue, options, TimeProvider.System,
            new NoOpBroadcastMetrics(), Substitute.For<ILogger<SchedulePollerService>>(), resolver);
    }

    private static Job AwaitingJob(DateTime scheduledForUtc, DateTime deadlineUtc, string id = "curation-job")
    {
        var job = Job.Create(id, "Station", "sydney", 30, Now).Value;
        job.ScheduledForUtc = Option<DateTime>.Some(scheduledForUtc);
        job.CurationDeadlineUtc = Option<DateTime>.Some(deadlineUtc);
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig("elevenlabs", "voice-1", "Voice", "eleven_v3"));
        job.ETag = "etag-1";
        job.TransitionTo(JobStatus.HereFetched, utcNow: Now);
        job.TransitionTo(JobStatus.IncidentsSelected, utcNow: Now);
        job.TransitionTo(JobStatus.AwaitingCuration, utcNow: Now);
        return job;
    }

    [Fact]
    public async Task ReloadedJobWithPastPersistedDeadline_ProceedsExactlyOnceAcrossRepeatedSweeps()
    {
        // ListJobsAsync returns the row after repository reload, which is the path that previously
        // lost CurationDeadlineUtc before this watchdog sweep saw it.
        var job = AwaitingJob(Now.AddMinutes(10), Now.AddMinutes(-1));
        _jobs.ListJobsAsync(Arg.Any<IReadOnlyCollection<JobStatus>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<IEnumerable<Job>>.Success(job.Status == JobStatus.AwaitingCuration ? [job] : []));
        _jobs.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobs.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.ProceedPastCurationDeadlineAsync(Now, CancellationToken.None);
        await _sut.ProceedPastCurationDeadlineAsync(Now, CancellationToken.None);

        job.Status.ShouldBe(JobStatus.IncidentsSelected);
        await _queue.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m => m.JobId == job.JobId && m.Phase == Phase.Generate),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsideCurationWindow_DoesNotProceed()
    {
        var job = AwaitingJob(Now.AddMinutes(10), Now.AddMinutes(5));
        _jobs.ListJobsAsync(Arg.Any<IReadOnlyCollection<JobStatus>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success([job]));

        await _sut.ProceedPastCurationDeadlineAsync(Now, CancellationToken.None);

        await _queue.DidNotReceive().PublishBroadcastMessageAsync(
            Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
        job.Status.ShouldBe(JobStatus.AwaitingCuration);
    }

    [Fact]
    public async Task ReloadedJobWithoutPersistedDeadline_UsesScheduledSlotCutoffAndProceeds()
    {
        // This models the legacy row after a save/reload: the deadline was present in memory when
        // the job parked, but the row reloads without it. The watchdog must derive the same
        // cutoff as the poller rather than skip the slot forever.
        var reloadedJob = AwaitingJob(Now.AddMinutes(-1), Now);
        reloadedJob.CurationDeadlineUtc = Option<DateTime>.None;
        _jobs.ListJobsAsync(Arg.Any<IReadOnlyCollection<JobStatus>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success([reloadedJob]));
        _jobs.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobs.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.ProceedPastCurationDeadlineAsync(Now, CancellationToken.None);

        reloadedJob.Status.ShouldBe(JobStatus.IncidentsSelected);
        await _queue.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m => m.JobId == reloadedJob.JobId && m.Phase == Phase.Generate),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingDeadlineAndScheduledTime_ProceedsImmediatelyToProtectSlot()
    {
        var reloadedJob = AwaitingJob(Now, Now);
        reloadedJob.ScheduledForUtc = Option<DateTime>.None;
        reloadedJob.CurationDeadlineUtc = Option<DateTime>.None;
        _jobs.ListJobsAsync(Arg.Any<IReadOnlyCollection<JobStatus>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success([reloadedJob]));
        _jobs.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobs.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.ProceedPastCurationDeadlineAsync(Now, CancellationToken.None);

        reloadedJob.Status.ShouldBe(JobStatus.IncidentsSelected);
        await _queue.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m => m.JobId == reloadedJob.JobId && m.Phase == Phase.Generate),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RepeatedUpdatesDuringCuration_KeepSlotAnchoredDeadline()
    {
        var scheduled = Now.AddMinutes(15);
        var deadline = scheduled.AddMinutes(-5);
        var job = AwaitingJob(scheduled, deadline);

        job.LastUpdatedAt = Now.AddMinutes(1);
        job.LastUpdatedAt = Now.AddMinutes(2);
        job.LastUpdatedAt = Now.AddMinutes(4);

        job.CurationDeadlineUtc.Value.ShouldBe(deadline);
        job.CurationDeadlineUtc.Value.ShouldBe(job.ScheduledForUtc.Value.AddMinutes(-5));
    }

    [Fact]
    public void OperatorExclusionAndPriorityChangeOnlySelectionOrder()
    {
        var excluded = new TrafficIncident("excluded", "Excluded", 1, "M1", "congestion");
        var reprioritised = new TrafficIncident("reprioritised", "Reprioritised", 3, "M1", "accident");
        var movedAhead = new TrafficIncident("moved-ahead", "Moved ahead", 2, "M2", "roadworks");
        var m1 = new TrafficCorridor("M1", 3, 1, 1000, 87, "accident", "Incidents on M1", [excluded, reprioritised]);
        var m2 = new TrafficCorridor("M2", 2, 2, 800, 42, "roadworks", "Roadworks on M2", [movedAhead]);
        var snapshot = new GeneratorInputSnapshot
        {
            TargetDurationSeconds = 30,
            Location = "sydney",
            Corridors = [m1, m2],
            EditedIncidents =
            [
                new EditedIncidentDto("excluded", Included: false),
                new EditedIncidentDto("reprioritised", Priority: 2),
                new EditedIncidentDto("moved-ahead", Priority: 1)
            ]
        };

        var context = snapshot.ToContext("resume");

        context.Corridors.Select(c => c.RoadName).ShouldBe(["M2", "M1"]);
        context.Incidents.Select(i => i.Id).ShouldBe(["moved-ahead", "reprioritised"]);
        context.Corridors.Single(c => c.RoadName == "M1").ImpactScore.ShouldBe(87);
        context.Corridors.Single(c => c.RoadName == "M2").ImpactScore.ShouldBe(42);
        context.Incidents.Single(i => i.Id == "reprioritised").Severity.ShouldBe(3);
        context.Incidents.Single(i => i.Id == "moved-ahead").Severity.ShouldBe(2);
    }
}

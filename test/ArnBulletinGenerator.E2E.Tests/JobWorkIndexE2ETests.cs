using System.Reflection;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.E2E.Tests;

[Trait("Category", "E2E")]
public sealed class JobWorkIndexE2ETests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact(Timeout = 300_000)]
    public async Task ConcurrentPollerSweeps_TransitionEachEligibleJobOnce_AndRemoveTerminalIndexRows()
    {
        var runId = Guid.NewGuid().ToString("N");
        var storage = Options.Create(new StorageOptions
        {
            AccountName = StorageOptions.LocalEmulatorAccount,
            ContainerName = "broadcasts",
            MaxRetries = 0
        });
        var credential = new AzuriteTokenCredential();
        var time = new FixedTimeProvider(Now);
        var index = new TableStorageJobWorkIndexRepository(storage, credential,
            NullLogger<TableStorageJobWorkIndexRepository>.Instance);
        var workIndexOptions = Options.Create(new JobWorkIndexOptions
        {
            WriteEnabled = true,
            ReadEnabled = true
        });
        var repository = new TableStorageJobRepository(storage, credential, time,
            NullLogger<TableStorageJobRepository>.Instance,
            workIndexRepository: index, workIndexOptions: workIndexOptions);
        var queue = new RecordingQueuePublisher();
        var jobs = new RecordingJobService();
        var fallback = new RecordingFallbackService(repository, time);
        var schedules = new EmptyScheduleRepository();
        var options = Options.Create(new BroadcastOptions
        {
            ApprovedDeliveryLeadSeconds = 0,
            MissedAfterDeadlineMinutes = 1,
            ExpireWaitingApprovalHours = 1,
            SchedulePollerStartJitterSeconds = 0
        });
        var poller = new SchedulePollerService(schedules, repository, jobs,
            new NoopNotificationService(), fallback, new NoopCronParser(), Proxy<IModelSelector>(),
            Proxy<IVoiceProfileService>(), Proxy<IVoiceProvider>(), Proxy<IDeliveryStyleService>(),
            Proxy<IBulletinPlaybookService>(), Proxy<IBookendService>(), Proxy<IPronunciationDictionaryService>(),
            queue, options, time, new NoOpBroadcastMetrics(),
            NullLogger<SchedulePollerService>.Instance, index, workIndexOptions);

        var seeded = await SeedJobsAsync(repository, runId);
        var baselineHistory = seeded.ToDictionary(job => job.JobId, job => job.StatusHistory.Count);

        await Task.WhenAll(
            poller.ProcessDueSchedulesAsync(CancellationToken.None),
            poller.ProcessDueSchedulesAsync(CancellationToken.None));

        var curation = seeded.Single(job => job.JobId.EndsWith("-curation", StringComparison.Ordinal));
        var fallbackJob = seeded.Single(job => job.JobId.EndsWith("-fallback", StringComparison.Ordinal));
        var missable = seeded.Single(job => job.JobId.EndsWith("-missable", StringComparison.Ordinal));
        var stale = seeded.Single(job => job.JobId.EndsWith("-stale", StringComparison.Ordinal));
        var covered = seeded.Single(job => job.JobId.EndsWith("-covered", StringComparison.Ordinal));

        var reloaded = new Dictionary<string, Job>();
        foreach (var job in seeded)
            reloaded[job.JobId] = (await repository.GetJobAsync(job.JobId)).Value;

        reloaded[curation.JobId].Status.ShouldBe(JobStatus.IncidentsSelected);
        reloaded[fallbackJob.JobId].Status.ShouldBe(JobStatus.FallbackUsed);
        reloaded[missable.JobId].Status.ShouldBe(JobStatus.Missed);
        reloaded[stale.JobId].Status.ShouldBe(JobStatus.Requested);
        reloaded[covered.JobId].Status.ShouldBe(JobStatus.FallbackUsed);

        reloaded[curation.JobId].StatusHistory.Count.ShouldBe(baselineHistory[curation.JobId] + 1);
        reloaded[fallbackJob.JobId].StatusHistory.Count.ShouldBe(baselineHistory[fallbackJob.JobId] + 1);
        reloaded[missable.JobId].StatusHistory.Count.ShouldBe(baselineHistory[missable.JobId] + 1);
        reloaded[stale.JobId].StatusHistory.Count.ShouldBe(baselineHistory[stale.JobId]);
        reloaded[covered.JobId].StatusHistory.Count.ShouldBe(baselineHistory[covered.JobId]);

        queue.Messages.Select(message => message.JobId)
            .Where(id => id == curation.JobId || id == stale.JobId)
            .GroupBy(id => id).ShouldAllBe(group => group.Count() == 1);
        queue.Messages.Count(message => message.JobId == curation.JobId).ShouldBe(1);
        queue.Messages.Count(message => message.JobId == stale.JobId).ShouldBe(1);
        jobs.Deliveries.Count(jobId => jobId == covered.JobId).ShouldBe(1);

        var terminal = seeded.Single(job => job.JobId.EndsWith("-terminal", StringComparison.Ordinal));
        (await index.FindByJobIdAsync(terminal.JobId)).Value.ShouldBeEmpty();
        (await index.FindByJobIdAsync(curation.JobId)).Value.ShouldNotBeEmpty();

        var leaseJob = MakeJob($"{runId}-lease", $"work-index-{runId}", JobStatus.Requested,
            Now.AddMinutes(-1));
        (await repository.SaveJobAsync(leaseJob)).IsSuccess.ShouldBeTrue();
        (await repository.TryClaimWatchdogJobAsync(leaseJob)).Value.ShouldBeTrue();
        var loadedAfterClaim = (await repository.GetJobAsync(leaseJob.JobId)).Value;
        (await repository.TryClaimWatchdogJobAsync(loadedAfterClaim)).Value.ShouldBeFalse();
    }

    private static async Task<IReadOnlyList<Job>> SeedJobsAsync(TableStorageJobRepository repository, string runId)
    {
        var market = $"work-index-{runId}";
        var jobs = new List<Job>();
        jobs.Add(MakeJob($"{runId}-curation", market, JobStatus.AwaitingCuration, Now.AddMinutes(-1)));
        jobs[^1].ScheduledForUtc = Option<DateTime>.Some(Now.AddMinutes(1));
        jobs[^1].CurationDeadlineUtc = Option<DateTime>.Some(Now.AddMinutes(-1));
        jobs[^1].VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig("provider", "voice", "Voice", "model"));
        jobs.Add(MakeJob($"{runId}-fallback", market, JobStatus.HereFetched, Now.AddMinutes(-3)));
        jobs[^1].FallbackDeadlineUtc = Option<DateTime>.Some(Now.AddSeconds(-30));
        jobs.Add(MakeJob($"{runId}-missable", market, JobStatus.WaitingApproval, Now.AddMinutes(-20)));
        jobs[^1].FallbackDeadlineUtc = Option<DateTime>.Some(Now.AddSeconds(-90));
        jobs.Add(MakeJob($"{runId}-stale", market, JobStatus.Requested, Now.AddMinutes(-4)));
        jobs[^1].FallbackDeadlineUtc = Option<DateTime>.Some(Now.AddHours(1));
        jobs.Add(MakeJob($"{runId}-covered", market, JobStatus.FallbackUsed, Now.AddMinutes(-1)));
        jobs[^1].FallbackAppliedAt = Option<DateTime>.Some(Now.AddMinutes(-1));
        jobs[^1].ScheduledForUtc = Option<DateTime>.Some(Now.AddMinutes(1));
        jobs.Add(MakeJob($"{runId}-terminal", market, JobStatus.Requested, Now.AddMinutes(-1)));

        foreach (var job in jobs)
        {
            var save = await repository.SaveJobAsync(job);
            save.IsSuccess.ShouldBeTrue(save.Error);
        }
        var terminal = jobs[^1];
        terminal.TransitionTo(JobStatus.Published, "system", utcNow: Now);
        (await repository.SaveJobAsync(terminal)).IsSuccess.ShouldBeTrue();
        return jobs;
    }

    private static Job MakeJob(string id, string market, JobStatus status, DateTime updated)
    {
        var job = Job.Create(id, "Station", market, 30, updated,
            Option<double>.Some(-37.8), Option<double>.Some(144.9), Option<double>.Some(5),
            Option<string>.Some("Australia/Melbourne")).Value;
        if (status != JobStatus.Requested)
            job.TransitionTo(status, utcNow: updated);
        job.LastUpdatedAt = updated;
        return job;
    }

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, EmptyProxy<T>>();

    public class EmptyProxy<T> : DispatchProxy where T : class
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private sealed class AzuriteTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("azurite-test-token", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class RecordingQueuePublisher : IQueuePublisher
    {
        private readonly object _gate = new();
        internal List<BroadcastQueueMessage> Messages { get; } = [];
        public Task<Result> PublishBroadcastMessageAsync(BroadcastQueueMessage message, CancellationToken cancellationToken = default)
        { lock (_gate) Messages.Add(message); return Task.FromResult(Result.Success()); }
        public Task<Result> PublishRegenerationMessageAsync(RegenerationQueueMessage message, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> PublishDeliveryMessageAsync(DeliveryQueueCommand message, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> PublishLessonLearningMessageAsync(LessonLearningQueueMessage message, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> PublishLessonLearningPoisonMessageAsync(LessonLearningQueueMessage message, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> PublishActivityAuditMessageAsync(ActivityAuditMessage message, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class RecordingJobService : IJobService
    {
        private readonly object _gate = new();
        internal List<string> Deliveries { get; } = [];
        public Task<Result> DeliverCoveredSlotAsync(string jobId, string what, string by, CancellationToken cancellationToken = default)
        { lock (_gate) { if (Deliveries.Contains(jobId)) return Task.FromResult(Result.Success()); Deliveries.Add(jobId); } return Task.FromResult(Result.Success()); }
        public Task<Result> EscalateJobAsync(string jobId, string reason, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> ApproveJobAsync(string jobId, string by = "unknown", string? expectedEtag = null, string? byObjectId = null, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> PublishJobAsync(string jobId, string by = "unknown", string? expectedEtag = null, string? byObjectId = null, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> ProceedPastCurationAsync(string jobId, string by = "unknown", string? expectedEtag = null, IReadOnlyList<EditedIncidentDto>? editedIncidents = null, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> RejectJobAsync(RejectJobCommand command, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> RejectToFallbackAsync(RejectToFallbackCommand command, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<Job>> CreateJobAsync(CreateJobCommand command, CancellationToken cancellationToken = default) => Task.FromResult(Result<Job>.Failure("unused"));
        public Task<Result> SetAirSourceAsync(SetAirSourceCommand command, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<RegenerationQueueMessage>> RegenerateJobAsync(RegenerateJobCommand command, string by = "unknown", CancellationToken cancellationToken = default) => Task.FromResult(Result<RegenerationQueueMessage>.Failure("unused"));
        public Task<Result> PublishBroadcastJobAsync(string jobId, BroadcastQueueMessage message, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> PublishRegenerationJobAsync(string jobId, RegenerationQueueMessage message, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> MarkJobFailedAsync(string jobId, string error, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class RecordingFallbackService(TableStorageJobRepository repository, TimeProvider time) : IBulletinFallbackService
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _applied = [];
        public async Task<FallbackOutcome> TryApplyFallbackAsync(Job job, string reason, CancellationToken cancellationToken = default)
        {
            lock (_gate) if (!_applied.Add(job.JobId)) return new FallbackOutcome(FallbackKind.None);
            job.ApplyFillerFallback("filler.wav", time.GetUtcNow().UtcDateTime);
            var saved = await repository.SaveJobAsync(job, cancellationToken);
            return saved.IsSuccess ? new FallbackOutcome(FallbackKind.FillerCart) : new FallbackOutcome(FallbackKind.None);
        }
        public Task<Result> ApplyFillerCartAsync(Job job, string reason, string by = "system", CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class EmptyScheduleRepository : IScheduleRepository
    {
        public Task<Result<IEnumerable<MarketSchedule>>> ListSchedulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<IEnumerable<MarketSchedule>>.Success([]));
        public Task<Result<IEnumerable<MarketSchedule>>> GetDueSchedulesAsync(DateTime thresholdUtc, CancellationToken cancellationToken = default) => Task.FromResult(Result<IEnumerable<MarketSchedule>>.Success([]));
        public Task<Result<MarketSchedule>> GetScheduleAsync(string marketId, CancellationToken cancellationToken = default) => Task.FromResult(Result<MarketSchedule>.Failure("unused"));
        public Task<Result> SaveScheduleAsync(MarketSchedule schedule, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> DeleteScheduleAsync(string marketId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> TryStartJobAsync(string marketId, string jobId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> CompleteJobAsync(string marketId, Option<DateTime> nextRunUtc, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class NoopNotificationService : INotificationService
    {
        public Task<Result> SendJobNotificationAsync(Job job, NotificationKind? kind = null, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result> SendAlertNotificationAsync(string market, string station, string alertMessage, AlertSeverity severity, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }

    private sealed class NoopCronParser : ICronParser
    {
        public bool TryParseCron(string expression) => true;
        public Option<DateTime> CalculateNextRun(string cronExpression, string timezone) => Option<DateTime>.None;
        public Option<DateTime> CalculateNextRun(string cronExpression, string timezone, DateTime afterUtc) => Option<DateTime>.None;
    }
}

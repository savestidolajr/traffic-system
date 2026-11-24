using ArnBulletinGenerator.Application.Tests;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Scheduling;

public sealed class JobWatchdogPopulationTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task IndexedPopulation_IsPagedOldestFirst_AndOnlyDueCandidatesAreActedOn()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(Now);
        var index = new RecordingWorkIndexRepository();
        var jobs = new RecordingJobRepository();
        var queue = Substitute.For<IQueuePublisher>();
        var jobService = Substitute.For<IJobService>();
        var fallback = Substitute.For<IBulletinFallbackService>();
        var options = new BroadcastOptions { MissedAfterDeadlineMinutes = 1, ExpireWaitingApprovalHours = 1 };
        var workOptions = new JobWorkIndexOptions
        {
            ReadEnabled = true,
            LookbackMinutes = 2,
            HorizonMinutes = 5,
            PageSize = 500
        };

        var curation = SeedJobs(index, jobs, JobWorkKind.CurationDue, JobStatus.AwaitingCuration, 300);
        var fallbackJobs = SeedJobs(index, jobs, JobWorkKind.FallbackDue, JobStatus.HereFetched, 300);
        var missable = SeedJobs(index, jobs, JobWorkKind.Missable, JobStatus.WaitingApproval, 300);
        var stale = SeedJobs(index, jobs, JobWorkKind.StaleRequested, JobStatus.Requested, 300);
        var covered = SeedJobs(index, jobs, JobWorkKind.UndeliveredCovered, JobStatus.FallbackUsed, 300);
        var escalation = SeedJobs(index, jobs, JobWorkKind.Escalation, JobStatus.WaitingApproval, 300);

        // The first row of every kind is due. Rows after the short horizon remain in the fake and
        // prove that the watchdog does not point-read or act on them.

        var terminalId = curation[1].JobId;
        jobs.Get(terminalId).TransitionTo(JobStatus.Published, utcNow: Now);
        jobs.Get(terminalId).FallbackDeadlineUtc = Option<DateTime>.Some(Now.AddMinutes(-1));


        fallback.TryApplyFallbackAsync(Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var job = call.Arg<Job>();
                job.TransitionTo(JobStatus.FallbackUsed, "system", utcNow: Now);
                return new FallbackOutcome(FallbackKind.FillerCart);
            });
        jobService.DeliverCoveredSlotAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        jobService.EscalateJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        queue.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var schedule = MarketSchedule.Create("population-market", "Population", "3AW", "Australia/Melbourne",
            "0 * * * *", true, CoverageArea.Create(-37.8, 144.9, 5).Value, "voice", "/tmp", 5, 2, 1,
            Now.AddDays(-1), Now.AddDays(-1)).Value;
        schedule.EscalationBeforePlayoutMinutes = 10;
        schedule.NotifyBeforePlayoutMinutes = 10;
        schedule.NextRunUtc = Option<DateTime>.Some(Now.AddMinutes(1));

        var scheduleRepository = Substitute.For<IScheduleRepository>();
        scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([schedule]));
        var watchdog = CreateWatchdog(jobs, queue, jobService, fallback, scheduleRepository, time, options, index, workOptions);

        await watchdog.ProceedPastCurationDeadlineAsync(Now, CancellationToken.None);
        await watchdog.ProcessFallbacksAsync(Now, CancellationToken.None);
        await watchdog.MarkMissedExpiredSlotsAsync(Now, CancellationToken.None);
        await watchdog.RecoverUndeliveredCoveredSlotsAsync(Now, CancellationToken.None);
        await watchdog.RecoverStaleJobsAsync(Now, CancellationToken.None);
        await watchdog.EscalateOverdueApprovalsAsync(Now, CancellationToken.None);

        index.Requests.ShouldNotBeEmpty();
        index.Requests.ShouldAllBe(request => request.PageSize <= 500 &&
                                             request.ReturnedIds.Count <= 500);
        foreach (var kind in Enum.GetValues<JobWorkKind>())
        {
            var requests = index.Requests.Where(request => request.Kind == kind).ToList();
            requests.Select(request => request.BucketUtc).Distinct().ShouldBeInOrder();
        }

        await queue.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(message => message.JobId == curation[0].JobId), Arg.Any<CancellationToken>());
        await queue.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(message => message.JobId == stale[0].JobId), Arg.Any<CancellationToken>());
        jobs.Get(curation[0].JobId).Status.ShouldBe(JobStatus.IncidentsSelected);
        jobs.Get(missable[0].JobId).Status.ShouldBe(JobStatus.Missed);
        await jobService.Received(1).DeliverCoveredSlotAsync(covered[0].JobId,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await jobService.Received(1).EscalateJobAsync(escalation[0].JobId, Arg.Any<string>(), Arg.Any<CancellationToken>());

        jobs.PointReads.ShouldContain(curation[0].JobId);
        jobs.PointReads.ShouldContain(terminalId);
        jobs.PointReads.ShouldNotContain(curation[2].JobId);
        jobs.PointReads.ShouldNotContain(fallbackJobs[2].JobId);
        jobs.PointReads.ShouldNotContain(missable[2].JobId);
        jobs.PointReads.ShouldNotContain(stale[2].JobId);
        jobs.PointReads.ShouldNotContain(covered[2].JobId);
        jobs.PointReads.ShouldNotContain(escalation[2].JobId);
        index.Requests.SelectMany(request => request.ReturnedIds)
            .ShouldContain(terminalId);
    }

    [Fact]
    public async Task CompareEnabled_LogsExactlyOneWarningForCandidateSetDifference()
    {
        var time = new FakeTimeProvider();
        time.SetUtcNow(Now);
        var index = new RecordingWorkIndexRepository();
        var jobs = new RecordingJobRepository();
        var logger = Substitute.For<ILogger<SchedulePollerService>>();
        var indexed = MakeJob("indexed", JobStatus.WaitingApproval, Now.AddMinutes(-20));
        var legacyOnly = MakeJob("legacy-only", JobStatus.WaitingApproval, Now.AddMinutes(-20));
        jobs.Set(indexed.JobId, indexed);
        jobs.Set(legacyOnly.JobId, legacyOnly);
        index.Add(new JobWorkItem
        {
            Kind = JobWorkKind.Missable, JobId = indexed.JobId, Market = indexed.Market, Station = indexed.Station,
            Status = indexed.Status, DueUtc = Now.AddMinutes(-1), LastUpdatedAt = indexed.LastUpdatedAt
        });
        jobs.LegacyStatuses = [legacyOnly];
        var watchdog = CreateWatchdog(jobs, Substitute.For<IQueuePublisher>(), Substitute.For<IJobService>(),
            Substitute.For<IBulletinFallbackService>(), Substitute.For<IScheduleRepository>(), time,
            new BroadcastOptions { MissedAfterDeadlineMinutes = 1 }, index,
            new JobWorkIndexOptions { ReadEnabled = true, CompareEnabled = true, LookbackMinutes = 2, HorizonMinutes = 2 },
            logger);

        await watchdog.MarkMissedExpiredSlotsAsync(Now, CancellationToken.None);

        var warnings = logger.ReceivedCalls().Count(call =>
            call.GetMethodInfo().Name == nameof(ILogger.Log) &&
            call.GetArguments()[0] is LogLevel.Warning &&
            call.GetArguments()[2]?.ToString()?.Contains("[WorkIndexCompare]", StringComparison.Ordinal) == true);
        warnings.ShouldBe(1);
    }

    private static JobWatchdog CreateWatchdog(
        RecordingJobRepository jobs,
        IQueuePublisher queue,
        IJobService jobService,
        IBulletinFallbackService fallback,
        IScheduleRepository schedules,
        FakeTimeProvider time,
        BroadcastOptions options,
        IJobWorkIndexRepository index,
        JobWorkIndexOptions workOptions,
        ILogger<SchedulePollerService>? logger = null)
    {
        var broadcastOptions = Options.Create(options);
        var resolver = new StationConfigResolver(Substitute.For<IModelSelector>(), Substitute.For<IVoiceProfileService>(),
            Substitute.For<IVoiceProvider>(), Substitute.For<IDeliveryStyleService>(), Substitute.For<IBulletinPlaybookService>(),
            Substitute.For<IBookendService>(), Substitute.For<IPronunciationDictionaryService>(), broadcastOptions, time,
            logger ?? Substitute.For<ILogger<SchedulePollerService>>());
        return new JobWatchdog(schedules, jobs, jobService, Substitute.For<INotificationService>(), fallback,
            Substitute.For<ICronParser>(), queue, broadcastOptions, time, new NoOpBroadcastMetrics(),
            logger ?? Substitute.For<ILogger<SchedulePollerService>>(), resolver, index, Options.Create(workOptions));
    }

    private static List<JobWorkItem> SeedJobs(RecordingWorkIndexRepository index, RecordingJobRepository jobs,
        JobWorkKind kind, JobStatus status, int count)
    {
        var result = new List<JobWorkItem>(count);
        for (var i = 0; i < count; i++)
        {
            var due = i < 2 ? Now.AddMinutes(-1).AddSeconds(i) : Now.AddMinutes(6 + i);
            var job = MakeJob($"{kind}-{i:D4}", status, due.AddMinutes(-10));
            if (kind == JobWorkKind.StaleRequested)
                job.FallbackDeadlineUtc = Option<DateTime>.Some(Now.AddHours(1));
            if (kind == JobWorkKind.CurationDue)
            {
                job.ScheduledForUtc = Option<DateTime>.Some(Now.AddMinutes(1));
                job.CurationDeadlineUtc = Option<DateTime>.Some(due);
                job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig("provider", "voice", "Voice", "model"));
            }
            if (kind == JobWorkKind.Escalation)
                job.ScheduledForUtc = Option<DateTime>.Some(Now.AddMinutes(1));
            if (kind == JobWorkKind.UndeliveredCovered)
            {
                job.FallbackAppliedAt = Option<DateTime>.Some(due);
                job.ScheduledForUtc = Option<DateTime>.Some(Now.AddMinutes(1));
            }
            jobs.Set(job.JobId, job);
            var item = new JobWorkItem
            {
                Kind = kind, JobId = job.JobId, Market = job.Market, Station = job.Station, Status = status,
                DueUtc = due, LastUpdatedAt = job.LastUpdatedAt,
                FallbackDeadlineUtc = job.FallbackDeadlineUtc.Match(v => (DateTime?)v, () => null),
                ScheduledForUtc = job.ScheduledForUtc.Match(v => (DateTime?)v, () => null),
                CurationDeadlineUtc = job.CurationDeadlineUtc.Match(v => (DateTime?)v, () => null),
                HasAudio = job.AudioUrl.HasValue
            };
            index.Add(item);
            result.Add(item);
        }
        return result;
    }

    private static void ConfigureJobsForActions(JobWorkItem item, DateTime lastUpdated)
    {
        _ = item;
        _ = lastUpdated;
    }

    private static Job MakeJob(string id, JobStatus status, DateTime updated)
    {
        var job = Job.Create(id, "Station", "population-market", 30, updated,
            Option<double>.Some(-37.8), Option<double>.Some(144.9), Option<double>.Some(5),
            Option<string>.Some("Australia/Melbourne")).Value;
        if (status != JobStatus.Requested)
            job.TransitionTo(status, utcNow: updated);
        job.LastUpdatedAt = updated;
        job.FallbackDeadlineUtc = Option<DateTime>.Some(updated.AddMinutes(1));
        return job;
    }

    private sealed class RecordingWorkIndexRepository : IJobWorkIndexRepository
    {
        internal readonly List<JobWorkItem> Items = [];
        internal readonly List<PageRequest> Requests = [];

        public void Add(JobWorkItem item) => Items.Add(item);

        public Task<Result<JobWorkIndexPage>> ReadPageAsync(JobWorkKind kind, DateTime bucketUtc,
            string? continuationToken, int pageSize, CancellationToken cancellationToken = default)
        {
            var rows = Items.Where(item => item.Kind == kind &&
                    FloorMinute(item.DueUtc) == FloorMinute(bucketUtc))
                .OrderBy(item => item.DueUtc).ThenBy(item => item.JobId, StringComparer.Ordinal).ToList();
            var offset = string.IsNullOrEmpty(continuationToken) ? 0 : int.Parse(continuationToken);
            var take = Math.Min(Math.Min(pageSize, 500), Math.Max(rows.Count - offset, 0));
            var page = rows.Skip(offset).Take(take).ToArray();
            var next = offset + page.Length < rows.Count ? (offset + page.Length).ToString() : null;
            Requests.Add(new PageRequest(kind, bucketUtc, pageSize, page.Select(item => item.JobId).ToArray()));
            return Task.FromResult(Result<JobWorkIndexPage>.Success(new JobWorkIndexPage(page, next)));
        }

        public Task<Result> UpsertAsync(JobWorkItem item, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public Task<Result> DeleteAsync(JobWorkItem item, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public Task<Result<IReadOnlyList<JobWorkItem>>> FindByJobIdAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<JobWorkItem>>.Success(Items.Where(item => item.JobId == jobId).ToArray()));

        private static DateTime FloorMinute(DateTime value) =>
            new(DateTime.SpecifyKind(value, DateTimeKind.Utc).Ticks -
                DateTime.SpecifyKind(value, DateTimeKind.Utc).Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);
    }

    private sealed record PageRequest(JobWorkKind Kind, DateTime BucketUtc, int PageSize, IReadOnlyList<string> ReturnedIds);

    private sealed class RecordingJobRepository : IJobRepository
    {
        private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
        internal readonly List<string> PointReads = [];
        internal IReadOnlyList<Job> LegacyStatuses { get; set; } = [];
        internal void Set(string id, Job job) => _jobs[id] = job;
        internal Job Get(string id) => _jobs[id];

        public Task<Result<Job>> GetJobAsync(string jobId, CancellationToken cancellationToken = default)
        {
            PointReads.Add(jobId);
            return Task.FromResult(_jobs.TryGetValue(jobId, out var job)
                ? Result<Job>.Success(job) : Result<Job>.Failure("not found"));
        }
        public Task<Result<JobIndex>> GetJobIndexAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_jobs.TryGetValue(jobId, out var job)
                ? Result<JobIndex>.Success(new JobIndex(job.JobId, job.Market,
                    $"Job_{job.Market.Trim().ToLowerInvariant()}", job.ETag))
                : Result<JobIndex>.Failure("not found"));
        public Task<Result<Job>> GetJobAsync(JobIndex index, CancellationToken cancellationToken = default) =>
            GetJobAsync(index.JobId, cancellationToken);
        public Task<Result<IEnumerable<Job>>> ListJobsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IEnumerable<Job>>.Success(_jobs.Values));
        public Task<Result<IEnumerable<Job>>> ListJobsAsync(IReadOnlyCollection<JobStatus> statuses, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IEnumerable<Job>>.Success(LegacyStatuses.Count > 0
                ? LegacyStatuses : _jobs.Values.Where(job => statuses.Contains(job.Status))));
        public Task<Result<IReadOnlyList<Job>>> ListJobsPageAsync(JobListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<Job>>.Success([]));
        public Task<Result<IEnumerable<JobListItem>>> ListJobSummariesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IEnumerable<JobListItem>>.Success([]));
        public Task<Result<IEnumerable<JobListItem>>> ListJobSummariesAsync(IReadOnlyCollection<JobStatus> statuses, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IEnumerable<JobListItem>>.Success([]));
        public Task<Result<IReadOnlyList<JobListItem>>> ListJobSummariesPageAsync(JobListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<JobListItem>>.Success([]));
        public Task<Result<JobLedgerProjection>> GetJobLedgerProjectionAsync(string jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<JobLedgerProjection>.Failure("not implemented"));
        public Task<Result<IReadOnlyList<JobLedgerProjection>>> ListJobLedgerPageAsync(JobListQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<JobLedgerProjection>>.Success([]));
        public Task<Result> SaveJobAsync(Job job, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public Task<Result> AddLogAsync(string jobId, string message, JobLogLevel level = JobLogLevel.Info, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
        public Task<Result<Option<Job>>> GetLatestBulletinForMarketTodayAsync(string market, string station, DateTime dayStartUtc, DateTime dayEndUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Option<Job>>.Success(Option<Job>.None));
        public Task<Result<Option<Job>>> GetLastSuccessfulJobForMarketAsync(string market, string station, DateTime windowStartUtc, DateTime windowEndUtc, string? excludeJobId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Option<Job>>.Success(Option<Job>.None));
        public Task<Result<IEnumerable<Job>>> ListJobsNeedingFallbackAsync(DateTime deadlineHorizonUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IEnumerable<Job>>.Success([]));
        public Task<Result<IEnumerable<Job>>> ListStaleRequestedJobsAsync(DateTime staleThreshold, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IEnumerable<Job>>.Success([]));
        public Task<Result> DeleteJobAsync(string jobId, string expectedEtag, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}

using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Mocks;

public class InMemoryJobRepositoryTests
{
    private readonly InMemoryJobRepository _sut = new();

    private static Job CreateJob(
        string jobId = "job-1",
        string market = "Sydney",
        string station = "2GB",
        double target = 90)
    {
        return Job.Create(jobId, station, market, target, DateTime.UtcNow).Value;
    }

    // ── SaveJobAsync / GetJobAsync round-trip ──

    [Fact]
    public async Task SaveAndGet_RoundTrip_ReturnsSameJob()
    {
        var job = CreateJob();
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetJobAsync("job-1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobId.ShouldBe("job-1");
        result.Value.Market.ShouldBe("Sydney");
    }

    [Fact]
    public async Task GetJobAsync_Missing_ReturnsFailure()
    {
        var result = await _sut.GetJobAsync("nonexistent");

        result.IsFailure.ShouldBeTrue();
    }

    // ── ListJobsAsync ──

    [Fact]
    public async Task ListJobsAsync_ReturnsAllJobsOrderedByCreatedAtDesc()
    {
        var older = CreateJob("job-old");
        older.SetCreatedAt(DateTime.UtcNow.AddHours(-2));
        var newer = CreateJob("job-new");
        newer.SetCreatedAt(DateTime.UtcNow);
        await _sut.SaveJobAsync(older);
        await _sut.SaveJobAsync(newer);

        var result = await _sut.ListJobsAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.First().JobId.ShouldBe("job-new");
    }

    // ── ListJobsPageAsync ──

    private static Job PagedJob(string jobId, DateTime lastUpdatedAtUtc, JobStatus status = JobStatus.WaitingApproval,
        string market = "Sydney")
    {
        var job = CreateJob(jobId, market);
        job.SetStatus(status);
        job.LastUpdatedAt = lastUpdatedAtUtc;
        return job;
    }

    [Fact]
    public async Task ListJobsPageAsync_FromInclusive_ToExclusive_RespectsBounds()
    {
        var from = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        var beforeWindow = PagedJob("before", from.AddMinutes(-1));
        var atFrom = PagedJob("at-from", from); // inclusive lower bound: must be included
        var midWindow = PagedJob("mid", from.AddHours(12));
        var atTo = PagedJob("at-to", to); // exclusive upper bound: must NOT be included
        await _sut.SaveJobAsync(beforeWindow);
        await _sut.SaveJobAsync(atFrom);
        await _sut.SaveJobAsync(midWindow);
        await _sut.SaveJobAsync(atTo);

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(new DateTimeOffset(from, TimeSpan.Zero), new DateTimeOffset(to, TimeSpan.Zero), 10));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(j => j.JobId).ShouldBe(new[] { "mid", "at-from" });
    }

    [Fact]
    public async Task ListJobsPageAsync_NullStatuses_ReturnsAllStatuses()
    {
        var now = DateTime.UtcNow;
        await _sut.SaveJobAsync(PagedJob("waiting", now.AddMinutes(-1), JobStatus.WaitingApproval));
        await _sut.SaveJobAsync(PagedJob("published", now.AddMinutes(-2), JobStatus.Published));
        await _sut.SaveJobAsync(PagedJob("failed", now.AddMinutes(-3), JobStatus.Failed));

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(new DateTimeOffset(now.AddDays(-1)), new DateTimeOffset(now.AddDays(1)), 10));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(j => j.JobId).ShouldBe(new[] { "waiting", "published", "failed" }, ignoreOrder: true);
    }

    [Fact]
    public async Task ListJobsPageAsync_WithStatusFilter_ReturnsOnlyMatchingStatuses()
    {
        var now = DateTime.UtcNow;
        await _sut.SaveJobAsync(PagedJob("waiting", now.AddMinutes(-1), JobStatus.WaitingApproval));
        await _sut.SaveJobAsync(PagedJob("published", now.AddMinutes(-2), JobStatus.Published));
        await _sut.SaveJobAsync(PagedJob("failed", now.AddMinutes(-3), JobStatus.Failed));

        var result = await _sut.ListJobsPageAsync(new JobListQuery(
            new DateTimeOffset(now.AddDays(-1)), new DateTimeOffset(now.AddDays(1)), 10,
            new[] { JobStatus.WaitingApproval, JobStatus.Failed }));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(j => j.JobId).ShouldBe(new[] { "waiting", "failed" }, ignoreOrder: true);
    }

    [Fact]
    public async Task ListJobsPageAsync_SortsNewestFirstAndCapsAtLimit()
    {
        var now = DateTime.UtcNow;
        // Saved out of chronological order to prove the repo sorts rather than trusting insertion order.
        await _sut.SaveJobAsync(PagedJob("mid", now.AddMinutes(-2)));
        await _sut.SaveJobAsync(PagedJob("oldest", now.AddMinutes(-3)));
        await _sut.SaveJobAsync(PagedJob("newest", now.AddMinutes(-1)));

        var result = await _sut.ListJobsPageAsync(
            new JobListQuery(new DateTimeOffset(now.AddDays(-1)), new DateTimeOffset(now.AddDays(1)), 2));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.Select(j => j.JobId).ShouldBe(new[] { "newest", "mid" });
    }

    // ── GetLatestBulletinForMarketTodayAsync ──

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_ReturnsPublishedJobFromToday()
    {
        var today = DateTime.UtcNow.Date;
        var job = CreateJob("pub-1");
        job.SetCreatedAt(today.AddHours(6));
        job.TransitionTo(JobStatus.Published, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLatestBulletinForMarketTodayAsync("Sydney", "2GB", today, today.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        result.Value.Value.JobId.ShouldBe("pub-1");
    }

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_IgnoresYesterdaysJobs()
    {
        var today = DateTime.UtcNow.Date;
        var job = CreateJob("old-pub");
        job.SetCreatedAt(today.AddDays(-1).AddHours(10));
        job.TransitionTo(JobStatus.Published, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLatestBulletinForMarketTodayAsync("Sydney", "2GB", today, today.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_IgnoresWrongMarket()
    {
        var today = DateTime.UtcNow.Date;
        var job = CreateJob("pub-melb", "Melbourne", "3AW");
        job.SetCreatedAt(today.AddHours(6));
        job.TransitionTo(JobStatus.Published, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLatestBulletinForMarketTodayAsync("Sydney", "2GB", today, today.AddDays(1));

        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_ExcludesWaitingApprovalJob()
    {
        // A bulletin awaiting approval has not aired — it must never be used as a same-day
        // continuity baseline ("listeners just heard it" would be false).
        var today = DateTime.UtcNow.Date;
        var job = CreateJob("waiting-1");
        job.SetCreatedAt(today.AddHours(7));
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLatestBulletinForMarketTodayAsync("Sydney", "2GB", today, today.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_ExcludesApprovedButNotYetPublishedJob()
    {
        var today = DateTime.UtcNow.Date;
        var job = CreateJob("approved-1");
        job.SetCreatedAt(today.AddHours(7));
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        job.Approve(DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLatestBulletinForMarketTodayAsync("Sydney", "2GB", today, today.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_ExcludesFallbackUsedJob()
    {
        var today = DateTime.UtcNow.Date;
        var job = CreateJob("fallback-1");
        job.SetCreatedAt(today.AddHours(7));
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        job.ApplyFillerFallback("https://filler.example/cart.wav", DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLatestBulletinForMarketTodayAsync("Sydney", "2GB", today, today.AddDays(1));

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLatestBulletinForMarketTodayAsync_AustralianMorning_FindsPriorFromPreviousUtcDay()
    {
        // Regression: a prior bulletin created in the Sydney morning has a CreatedAt on the
        // PREVIOUS UTC day. The Sydney-local-day window ([29 14:00, 30 14:00) UTC) must include it;
        // the old UTC-midnight window ([30 00:00, 1 Jul 00:00) UTC) wrongly excluded it.
        var referenceUtc = new DateTime(2026, 6, 30, 0, 8, 0, DateTimeKind.Utc);
        // Sydney (AEST, UTC+10) local-day window for that instant, as proven in TimeZoneHelperTests.
        var dayStartUtc = new DateTime(2026, 6, 29, 14, 0, 0, DateTimeKind.Utc);
        var dayEndUtc = new DateTime(2026, 6, 30, 14, 0, 0, DateTimeKind.Utc);

        var prior = CreateJob("prior-morning");
        prior.SetCreatedAt(new DateTime(2026, 6, 29, 23, 56, 54, DateTimeKind.Utc));
        prior.TransitionTo(JobStatus.WaitingApproval, utcNow: referenceUtc);
        prior.Approve(referenceUtc);
        prior.Publish(referenceUtc);
        await _sut.SaveJobAsync(prior);

        var found = await _sut.GetLatestBulletinForMarketTodayAsync("Sydney", "2GB", dayStartUtc, dayEndUtc);
        found.Value.HasValue.ShouldBeTrue();
        found.Value.Value.JobId.ShouldBe("prior-morning");

        // The buggy UTC-midnight window would have missed it.
        var buggyStart = referenceUtc.Date;
        var missed = await _sut.GetLatestBulletinForMarketTodayAsync("Sydney", "2GB", buggyStart, buggyStart.AddDays(1));
        missed.Value.IsNone.ShouldBeTrue();
    }

    // ── GetLastSuccessfulJobForMarketAsync ──

    [Fact]
    public async Task GetLastSuccessfulJobForMarketAsync_ReturnsJobWithAudioInWindow()
    {
        var start = DateTime.UtcNow.AddHours(-4);
        var end = DateTime.UtcNow;
        var job = CreateJob("succ-1");
        job.SetCreatedAt(start.AddHours(1));
        job.AudioUrl = Option<string>.Some("https://blob/audio.mp3");
        job.TransitionTo(JobStatus.Approved, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLastSuccessfulJobForMarketAsync("Sydney", "2GB", start, end);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasValue.ShouldBeTrue();
        result.Value.Value.JobId.ShouldBe("succ-1");
    }

    [Fact]
    public async Task GetLastSuccessfulJobForMarketAsync_IgnoresJobWithoutAudio()
    {
        var start = DateTime.UtcNow.AddHours(-4);
        var end = DateTime.UtcNow;
        var job = CreateJob("no-audio");
        job.SetCreatedAt(start.AddHours(1));
        job.TransitionTo(JobStatus.Approved, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLastSuccessfulJobForMarketAsync("Sydney", "2GB", start, end);

        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLastSuccessfulJobForMarketAsync_IgnoresJobOutsideWindow()
    {
        var start = DateTime.UtcNow.AddHours(-2);
        var end = DateTime.UtcNow;
        var job = CreateJob("outside");
        job.SetCreatedAt(start.AddHours(-5));
        job.AudioUrl = Option<string>.Some("https://blob/audio.mp3");
        job.TransitionTo(JobStatus.Published, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(job);

        var result = await _sut.GetLastSuccessfulJobForMarketAsync("Sydney", "2GB", start, end);

        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLastSuccessfulJobForMarketAsync_ExcludesWaitingApprovalAndGivenJob()
    {
        // Regression: operator reject-to-fallback used to apply the filler cart every time
        // because this lookup returned the newest eligible job — which was the WaitingApproval
        // job being rejected itself. A waiting bulletin has not aired (not a valid source) and
        // the job being rejected must be excluded, so the prior APPROVED bulletin is returned.
        var start = DateTime.UtcNow.AddHours(-4);
        var end = DateTime.UtcNow.AddHours(1);

        var approved = CreateJob("approved-1");
        approved.SetCreatedAt(start.AddHours(1));
        approved.AudioUrl = Option<string>.Some("https://blob/approved.mp3");
        approved.TransitionTo(JobStatus.Approved, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(approved);

        // Newer waiting-approval bulletin — the one an operator would reject to fallback.
        var waiting = CreateJob("waiting-2");
        waiting.SetCreatedAt(start.AddHours(2));
        waiting.AudioUrl = Option<string>.Some("https://blob/waiting.mp3");
        waiting.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(waiting);

        // Even without excluding by id, a WaitingApproval bulletin is not a valid source.
        var noExclude = await _sut.GetLastSuccessfulJobForMarketAsync("Sydney", "2GB", start, end);
        noExclude.Value.HasValue.ShouldBeTrue();
        noExclude.Value.Value.JobId.ShouldBe("approved-1");

        // Excluding the rejected job still returns the prior approved bulletin (not None).
        var excludeWaiting = await _sut.GetLastSuccessfulJobForMarketAsync(
            "Sydney", "2GB", start, end, excludeJobId: "waiting-2");
        excludeWaiting.Value.HasValue.ShouldBeTrue();
        excludeWaiting.Value.Value.JobId.ShouldBe("approved-1");

        // Exclude-self isolation: excluding the only eligible (approved) bulletin leaves no
        // valid source — proving the lookup honours excludeJobId even for an aired status.
        var excludeApproved = await _sut.GetLastSuccessfulJobForMarketAsync(
            "Sydney", "2GB", start, end, excludeJobId: "approved-1");
        excludeApproved.Value.IsNone.ShouldBeTrue();
    }

    // ── ListJobsNeedingFallbackAsync ──

    [Fact]
    public async Task ListJobsNeedingFallbackAsync_ReturnsPastDeadlineJobs()
    {
        var now = DateTime.UtcNow;
        var pastDeadline = CreateJob("fb-1");
        pastDeadline.FallbackDeadlineUtc = Option<DateTime>.Some(now.AddMinutes(-10));
        var futureDeadline = CreateJob("fb-2");
        futureDeadline.FallbackDeadlineUtc = Option<DateTime>.Some(now.AddMinutes(10));
        var noDeadline = CreateJob("fb-3");
        await _sut.SaveJobAsync(pastDeadline);
        await _sut.SaveJobAsync(futureDeadline);
        await _sut.SaveJobAsync(noDeadline);

        var result = await _sut.ListJobsNeedingFallbackAsync(now);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(j => j.JobId == "fb-1");
    }

    // ── ListStaleRequestedJobsAsync ──

    [Fact]
    public async Task ListStaleRequestedJobsAsync_ReturnsStaleRequestedJobs()
    {
        var threshold = DateTime.UtcNow;
        var stale = CreateJob("stale-1");
        stale.LastUpdatedAt = threshold.AddMinutes(-30);
        // Status is Requested by default from constructor
        var fresh = CreateJob("fresh-1");
        fresh.LastUpdatedAt = threshold.AddMinutes(5);
        var nonRequested = CreateJob("done-1");
        nonRequested.LastUpdatedAt = threshold.AddMinutes(-30);
        nonRequested.TransitionTo(JobStatus.HereFetched, utcNow: DateTime.UtcNow);
        await _sut.SaveJobAsync(stale);
        await _sut.SaveJobAsync(fresh);
        await _sut.SaveJobAsync(nonRequested);

        var result = await _sut.ListStaleRequestedJobsAsync(threshold);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldContain(j => j.JobId == "stale-1");
    }

    [Fact]
    public async Task ListStaleRequestedJobsAsync_EmptyRepo_ReturnsEmptyList()
    {
        var result = await _sut.ListStaleRequestedJobsAsync(DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    // ── AddLogAsync ──

    [Fact]
    public async Task AddLogAsync_AppendsLogToExistingJob()
    {
        var job = CreateJob("log-1");
        await _sut.SaveJobAsync(job);

        await _sut.AddLogAsync("log-1", "Test message", JobLogLevel.Warn);

        var fetched = await _sut.GetJobAsync("log-1");
        fetched.Value.Logs.ShouldContain(l => l.Message == "Test message" && l.Level == JobLogLevel.Warn);
    }

    [Fact]
    public async Task AddLogAsync_MissingJob_SucceedsAsNoOp()
    {
        var result = await _sut.AddLogAsync("missing", "Ignored");

        result.IsSuccess.ShouldBeTrue();
    }
}

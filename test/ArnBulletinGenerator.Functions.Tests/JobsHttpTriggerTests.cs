using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class JobsHttpTriggerTests
{
    private readonly IAudioUrlProvider _audioUrlProvider = Substitute.For<IAudioUrlProvider>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly IVoiceCalibrationService _voiceCalibration = Substitute.For<IVoiceCalibrationService>();

    private readonly JobsHttpTrigger _sut;

    public JobsHttpTriggerTests()
    {
        _sut = new JobsHttpTrigger(_jobRepository, _audioUrlProvider,
            _marketAccess,
            _voiceCalibration,
            Options.Create(new BroadcastOptions()),
            Options.Create(new SpeechDurationOptions()),
            TimeProvider.System,
            Substitute.For<ILogger<JobsHttpTrigger>>());
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    private static Job TestJob(string id, JobStatus status = JobStatus.WaitingApproval)
    {
        var job = Job.Create(id, "Station1", "Melbourne", 30, DateTime.UtcNow).Value;
        job.SetStatus(status);
        return job;
    }

    private static JobListItem Summary(Job job) => JobListItem.FromJob(job);

    private static IReadOnlyList<JobListItem> Summaries(params Job[] jobs) =>
        jobs.Select(Summary).ToList();

    private void SetupJob(string id, JobStatus status = JobStatus.WaitingApproval, Action<Job>? configure = null)
    {
        var job = TestJob(id, status);
        configure?.Invoke(job);
        SetupLoadedJob(job);
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private void SetupLoadedJob(Job job)
    {
        var index = new JobIndex(job.JobId, job.Market, "Job_market");
        _jobRepository.GetJobIndexAsync(job.JobId, Arg.Any<CancellationToken>())
            .Returns(Result<JobIndex>.Success(index));
        _jobRepository.GetJobAsync(Arg.Is<JobIndex>(i => i.JobId == job.JobId), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
    }

    private void SetupJobNotFound(string id)
    {
        _jobRepository.GetJobIndexAsync(id, Arg.Any<CancellationToken>())
            .Returns(Result<JobIndex>.Failure("Job not found"));
    }

    // === ListJobs ===

    [Fact]
    public async Task ListJobs_ReturnsOkWithItems()
    {
        _jobRepository.ListJobSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<JobListItem>>.Success(Summaries(TestJob("job-1"), TestJob("job-2"))));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListJobs(MockHttpRequestData.CreateGet("http://localhost/api/jobs"), Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("job-1");
        body.ShouldContain("job-2");
    }

    [Fact]
    public async Task ListJobs_EmptyList_ReturnsZeroTotal()
    {
        _jobRepository.ListJobSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<JobListItem>>.Success(Summaries()));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListJobs(MockHttpRequestData.CreateGet("http://localhost/api/jobs"), Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Total\":0");
    }

    [Fact]
    public async Task ListJobs_RepositoryFailure_Returns500()
    {
        _jobRepository.ListJobSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<JobListItem>>.Failure("Storage unavailable"));

        var resp = await _sut.ListJobs(MockHttpRequestData.CreateGet("http://localhost/api/jobs"), Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task ListJobs_WithStatusFilter_QueriesOnlyThoseStatuses()
    {
        IReadOnlyCollection<JobStatus>? captured = null;
        _jobRepository
            .ListJobSummariesAsync(Arg.Any<IReadOnlyCollection<JobStatus>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<IReadOnlyCollection<JobStatus>>();
                return Result<IEnumerable<JobListItem>>.Success(
                    Summaries(TestJob("waiting-1", JobStatus.WaitingApproval)));
            });

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListJobs(
                MockHttpRequestData.CreateGet(
                    "http://localhost/api/jobs?status=WAITING_APPROVAL,FAILED"),
                Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("waiting-1");
        captured.ShouldBe(new[] { JobStatus.WaitingApproval, JobStatus.Failed });
        await _jobRepository.DidNotReceive().ListJobsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListJobs_WithUnrecognizedStatusFilter_Returns400()
    {
        // A typo'd status must not silently degrade to "no filter" (returning everything).
        var resp = await _sut.ListJobs(
            MockHttpRequestData.CreateGet("http://localhost/api/jobs?status=Bogus"), Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _jobRepository.DidNotReceive().ListJobsAsync(Arg.Any<CancellationToken>());
    }

    // --- Paging (History): ?limit= opt-in path ---

    private static Job PagedJob(string id, string market, DateTime lastUpdatedAtUtc,
        JobStatus status = JobStatus.WaitingApproval)
    {
        var job = Job.Create(id, "Station1", market, 30, lastUpdatedAtUtc).Value;
        job.SetStatus(status);
        job.LastUpdatedAt = lastUpdatedAtUtc;
        return job;
    }

    private static void AddScopedOperatorPrincipal(HttpRequestData req)
    {
        req.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                """{"auth_typ":"aad","claims":[{"typ":"oid","val":"scoped-op"},{"typ":"roles","val":"Operator"}]}""")));
    }

    [Fact]
    public async Task ListJobs_NonAdminWithNoAssignment_IsUnrestricted()
    {
        _jobRepository.ListJobSummariesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<JobListItem>>.Success(
                Summaries(PagedJob("job-sydney", "Sydney", DateTime.UtcNow), PagedJob("job-melbourne", "Melbourne", DateTime.UtcNow))));
        _marketAccess.GetAllowedMarketsAsync("scoped-op", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.None);

        var request = MockHttpRequestData.CreateGet("http://localhost/api/jobs");
        AddScopedOperatorPrincipal(request);
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListJobs(request, Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Total\":2");
        body.ShouldContain("job-sydney");
        body.ShouldContain("job-melbourne");
    }

    [Fact]
    public async Task ListJobs_WithLimit_FullPage_UnrestrictedCaller_ReturnsCappedNewestFirstAndHasMoreTrue()
    {
        var to = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        // Repo hands back a full page (== limit), deliberately out of order, to prove the
        // trigger re-sorts newest-first rather than trusting repo order.
        var older = PagedJob("job-older", "Melbourne", to.UtcDateTime.AddHours(-2));
        var newer = PagedJob("job-newer", "Melbourne", to.UtcDateTime.AddHours(-1));
        _jobRepository.ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<JobListItem>>.Success(Summaries(older, newer)));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListJobs(
                MockHttpRequestData.CreateGet(
                    $"http://localhost/api/jobs?limit=2&to={Uri.EscapeDataString(to.ToString("o"))}"),
                Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Total\":2");
        body.ShouldContain("\"HasMore\":true");
        body.IndexOf("job-newer", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("job-older", StringComparison.Ordinal));
        // Unrestricted (admin/unscoped) caller always does exactly one round, even though this
        // page was full (not exhausted) — backfilling is only for market-scoped callers.
        await _jobRepository.Received(1).ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListJobs_WithLimit_ShortPage_ReturnsHasMoreFalse()
    {
        var to = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var only = PagedJob("job-only", "Melbourne", to.UtcDateTime.AddHours(-1));
        _jobRepository.ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<JobListItem>>.Success(Summaries(only)));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ListJobs(
                MockHttpRequestData.CreateGet(
                    $"http://localhost/api/jobs?limit=5&to={Uri.EscapeDataString(to.ToString("o"))}"),
                Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"Total\":1");
        body.ShouldContain("\"HasMore\":false");
        await _jobRepository.Received(1).ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListJobs_WithLimit_MarketScopedCaller_BackfillsAndNarrowsCursor()
    {
        var to = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "Sydney" }));

        // Round 1 (cursor = to): a full raw page of 2, only 1 of which is in the caller's market.
        var r1Melbourne = PagedJob("r1-melb", "Melbourne", to.UtcDateTime.AddHours(-1));
        var r1Sydney = PagedJob("r1-syd", "Sydney", to.UtcDateTime.AddHours(-2));
        // Round 2 (cursor narrows to round 1's oldest RAW item, -2h): another full page of 2.
        var r2Melbourne = PagedJob("r2-melb", "Melbourne", to.UtcDateTime.AddHours(-3));
        var r2Sydney = PagedJob("r2-syd", "Sydney", to.UtcDateTime.AddHours(-4));

        var capturedQueries = new List<JobListQuery>();
        _jobRepository
            .ListJobSummariesPageAsync(Arg.Do<JobListQuery>(q => capturedQueries.Add(q)), Arg.Any<CancellationToken>())
            .Returns(
                Result<IReadOnlyList<JobListItem>>.Success(Summaries(r1Melbourne, r1Sydney)),
                Result<IReadOnlyList<JobListItem>>.Success(Summaries(r2Melbourne, r2Sydney)));

        var req = MockHttpRequestData.CreateGet(
            $"http://localhost/api/jobs?limit=2&to={Uri.EscapeDataString(to.ToString("o"))}");
        AddScopedOperatorPrincipal(req);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(await _sut.ListJobs(req, Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        // Round 1's single Sydney match under-fills a 2-item page, so the market-scoped caller
        // triggers exactly one backfill round (2 total), and stops once the page is full.
        await _jobRepository.Received(2).ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>());
        capturedQueries.Count.ShouldBe(2);
        capturedQueries[0].Market.ShouldBe("Sydney");
        capturedQueries[1].Market.ShouldBe("Sydney");
        capturedQueries[0].ToUtc.ShouldBe(to);
        // The cursor narrows to round 1's oldest RAW (pre-market-filter) item, not the oldest
        // filtered one — the trigger must look further back than just its own filtered results.
        capturedQueries[1].ToUtc.ShouldBe(new DateTimeOffset(r1Sydney.LastUpdatedAt, TimeSpan.Zero));

        body.ShouldNotContain("r1-melb");
        body.ShouldNotContain("r2-melb");
        body.ShouldContain("r1-syd");
        body.ShouldContain("r2-syd");
        body.IndexOf("r1-syd", StringComparison.Ordinal)
            .ShouldBeLessThan(body.IndexOf("r2-syd", StringComparison.Ordinal));
        body.ShouldContain("\"Total\":2");
        body.ShouldContain("\"HasMore\":true"); // round 2's raw page was still full (not exhausted)
    }

    [Fact]
    public async Task ListJobs_WithLimit_BackfillCollectsMoreThanPage_TotalIsGrandCount()
    {
        // Regression for L3: when backfilling pulls in more matching jobs than fit on the
        // returned page, Total must report the full matched count seen so far, not just the
        // sliced page size.
        var to = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "Sydney" }));

        // Round 1: 1 Sydney match out of 2 raw — under-fills the limit=2 page, triggers backfill.
        var r1Sydney = PagedJob("r1-syd", "Sydney", to.UtcDateTime.AddHours(-1));
        var r1Melbourne = PagedJob("r1-melb", "Melbourne", to.UtcDateTime.AddHours(-2));
        // Round 2: both raw jobs are Sydney matches, pushing the collected set to 3 — past the
        // page size of 2 — so the loop stops after this round.
        var r2SydneyA = PagedJob("r2-syd-a", "Sydney", to.UtcDateTime.AddHours(-3));
        var r2SydneyB = PagedJob("r2-syd-b", "Sydney", to.UtcDateTime.AddHours(-4));

        _jobRepository.ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<IReadOnlyList<JobListItem>>.Success(Summaries(r1Sydney, r1Melbourne)),
                Result<IReadOnlyList<JobListItem>>.Success(Summaries(r2SydneyA, r2SydneyB)));

        var req = MockHttpRequestData.CreateGet(
            $"http://localhost/api/jobs?limit=2&to={Uri.EscapeDataString(to.ToString("o"))}");
        AddScopedOperatorPrincipal(req);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(await _sut.ListJobs(req, Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        // 3 matching jobs were found across both rounds (the full matched set), even though
        // only 2 fit on the returned page.
        body.ShouldContain("\"Total\":3");
        body.ShouldContain("r1-syd");
        body.ShouldContain("r2-syd-a");
    }

    [Fact]
    public async Task ListJobs_WithLimit_MarketScopedCaller_NeverFills_StopsAtBackfillRoundCeiling()
    {
        var to = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        _marketAccess.GetAllowedMarketsAsync(Arg.Any<string>(), false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(new[] { "Sydney" }));

        // Every round returns a full (non-exhausted) raw page with zero matches for the caller's
        // market: the caller's page never fills. MaxBackfillRounds (4, private to the trigger)
        // must still cap the total repository round-trips rather than looping forever.
        var rounds = Enumerable.Range(0, 6)
            .Select(i => new List<Job>
            {
                PagedJob($"r{i}-a", "Melbourne", to.UtcDateTime.AddHours(-1 - i * 2)),
                PagedJob($"r{i}-b", "Melbourne", to.UtcDateTime.AddHours(-2 - i * 2)),
            })
            .ToArray();

        _jobRepository.ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Result<IReadOnlyList<JobListItem>>.Success(Summaries(rounds[0].ToArray())),
                _ => Result<IReadOnlyList<JobListItem>>.Success(Summaries(rounds[1].ToArray())),
                _ => Result<IReadOnlyList<JobListItem>>.Success(Summaries(rounds[2].ToArray())),
                _ => Result<IReadOnlyList<JobListItem>>.Success(Summaries(rounds[3].ToArray())),
                _ => Result<IReadOnlyList<JobListItem>>.Success(Summaries(rounds[4].ToArray())),
                _ => Result<IReadOnlyList<JobListItem>>.Success(Summaries(rounds[5].ToArray())));

        var req = MockHttpRequestData.CreateGet(
            $"http://localhost/api/jobs?limit=2&to={Uri.EscapeDataString(to.ToString("o"))}");
        AddScopedOperatorPrincipal(req);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(await _sut.ListJobs(req, Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        // Exactly 4 rounds — never more, even though the page never filled and was never exhausted.
        await _jobRepository.Received(4).ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>());
        body.ShouldContain("\"Total\":0");
        body.ShouldContain("\"HasMore\":true");
    }

    [Fact]
    public async Task ListJobs_WithCompositeCursor_ForwardsCursorJobIdToRepository()
    {
        // The console's "Load older" cursor is {lastUpdatedAt}|{jobId}; the trigger must
        // forward the row-key tiebreaker so the repository continues strictly after the
        // boundary row and cannot skip jobs sharing the boundary timestamp.
        JobListQuery? captured = null;
        _jobRepository.ListJobSummariesPageAsync(Arg.Do<JobListQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<JobListItem>>.Success(Summaries()));
        var boundary = new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero);
        var resp = await _sut.ListJobs(
            MockHttpRequestData.CreateGet(
                $"http://localhost/api/jobs?limit=5&to={Uri.EscapeDataString($"{boundary:o}|job-42")}"),
            Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.ToUtc.ShouldBe(boundary);
        captured.CursorJobId.ShouldBe("job-42");
    }

    [Fact]
    public async Task ListJobs_WithBareTimestampCursor_ForwardsNoCursorJobId()
    {
        // Backward-compatible contract: a bare ISO timestamp (legacy clients) pages by
        // timestamp alone — no row-key tiebreaker.
        JobListQuery? captured = null;
        _jobRepository.ListJobSummariesPageAsync(Arg.Do<JobListQuery>(q => captured = q), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<JobListItem>>.Success(Summaries()));
        var to = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);
        var resp = await _sut.ListJobs(
            MockHttpRequestData.CreateGet(
                $"http://localhost/api/jobs?limit=5&to={Uri.EscapeDataString(to.ToString("o"))}"),
            Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.ToUtc.ShouldBe(to);
        captured.CursorJobId.ShouldBeNull();
    }

    [Fact]
    public async Task ListJobs_TiedTimestamps_EveryRowAppearsExactlyOnceAcrossPages()
    {
        // The bug this regression guards (bug-bash 2026-08-05 #11): 6 jobs sharing one
        // LastUpdatedAt, page size 2. Before the composite-cursor fix the strict `lt` filter
        // skipped the 4 tied jobs that fell outside the first page. With the composite cursor
        // the walk must visit every job exactly once. The fake repository applies the same
        // composite semantics as the storage filter (LastUpdatedAt lt boundary, or equal with a
        // lower row key), so this proves the trigger's cursor derivation round-trips exactly.
        var shared = new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc);
        var all = Enumerable.Range(1, 6)
            .Select(i => PagedJob($"job-{i}", "Melbourne", shared))
            .ToList();
        _jobRepository.ListJobSummariesPageAsync(Arg.Any<JobListQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var q = ci.Arg<JobListQuery>();
                var matches = all
                    .Where(j =>
                        (j.LastUpdatedAt >= q.FromUtc.UtcDateTime && j.LastUpdatedAt < q.ToUtc.UtcDateTime)
                        || (q.CursorJobId is { Length: > 0 }
                            && j.LastUpdatedAt == q.ToUtc.UtcDateTime
                            && string.CompareOrdinal(j.JobId, q.CursorJobId) < 0))
                    .OrderByDescending(j => j.LastUpdatedAt)
                    .ThenByDescending(j => j.JobId, StringComparer.Ordinal)
                    .Take(q.Limit)
                    .Select(Summary)
                    .ToList() as IReadOnlyList<JobListItem>;
                return Result<IReadOnlyList<JobListItem>>.Success(matches);
            });

        var seen = new List<string>();
        string? cursor = $"{shared.AddDays(1):o}";
        int pages = 0;
        while (cursor is not null && pages < 10)
        {
            var resp = await _sut.ListJobs(
                MockHttpRequestData.CreateGet(
                    $"http://localhost/api/jobs?limit=2&to={Uri.EscapeDataString(cursor)}"),
                Ctx());
            resp.StatusCode.ShouldBe(HttpStatusCode.OK);
            (_, var body) = await MockHttpRequestData.ReadResponseAsync(resp);
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var items = doc.RootElement.GetProperty("Items");
            var ids = items.EnumerateArray().Select(e => e.GetProperty("JobId").GetString()!).ToList();
            seen.AddRange(ids);
            var last = items.EnumerateArray().LastOrDefault();
            cursor = last.ValueKind == System.Text.Json.JsonValueKind.Undefined
                ? null
                : $"{last.GetProperty("LastUpdatedAt").GetString()}|{last.GetProperty("JobId").GetString()}";
            pages++;
        }

        seen.ShouldBe(new[] { "job-6", "job-5", "job-4", "job-3", "job-2", "job-1" });
        seen.Distinct().Count().ShouldBe(6);
    }

    [Fact]
    public async Task ListJobs_WithLimit_ToBeforeFrom_Returns400()
    {
        var resp = await _sut.ListJobs(
            MockHttpRequestData.CreateGet("http://localhost/api/jobs?limit=10&from=2026-06-10&to=2026-06-01"),
            Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ListJobs_WithLimit_RangeOverMaxDays_Returns400()
    {
        var resp = await _sut.ListJobs(
            MockHttpRequestData.CreateGet("http://localhost/api/jobs?limit=10&from=2020-01-01&to=2026-07-01"),
            Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // === GetJob ===

    [Fact]
    public async Task GetJob_Existing_ReturnsOk()
    {
        SetupJob("job-123");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetJob(MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-123"), "job-123", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("job-123");
    }

    [Fact]
    public async Task GetJob_NonExistent_Returns404()
    {
        SetupJobNotFound("missing-id");
        var resp = await _sut.GetJob(MockHttpRequestData.CreateGet("http://localhost/api/jobs/missing-id"),
            "missing-id", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetJob_WithAudioUrl_ResolvesUrl()
    {
        var job = TestJob("job-audio");
        job.AudioUrl = Option<string>.Some("audio/file.mp3");
        SetupLoadedJob(job);
        _audioUrlProvider.GetAudioUrlAsync("audio/file.mp3", Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://storage.blob.core.windows.net/audio/file.mp3?sv=sas"));

        var resp = await _sut.GetJob(MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-audio"), "job-audio",
            Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // === Empty/Whitespace jobId edge cases ===

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetJob_EmptyJobId_Returns400(string jobId)
    {
        var resp = await _sut.GetJob(MockHttpRequestData.CreateGet("http://localhost/api/jobs/x"), jobId, Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // === GetJob market scoping ===

    [Fact]
    public async Task GetJob_ScopedOperatorNonAllowedMarket_Returns403()
    {
        // TestJob creates with station="Station1", market="Melbourne"
        SetupJob("job-scoped");

        // Scoped operator only has access to "Sydney", not "Melbourne"
        _marketAccess.GetAllowedMarketsAsync("scoped-op", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["Sydney"]));

        var request = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-scoped");
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("""{"auth_typ":"aad","claims":[{"typ":"oid","val":"scoped-op"},{"typ":"roles","val":"Operator"}]}""")));

        var resp = await _sut.GetJob(request, "job-scoped", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetJob_AdminWithRestrictedJob_Passes()
    {
        SetupJob("job-admin");

        _marketAccess.GetAllowedMarketsAsync("admin-op", true, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.None);

        var request = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-admin");
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes("""{"auth_typ":"aad","claims":[{"typ":"oid","val":"admin-op"},{"typ":"roles","val":"Admin"}]}""")));

        var resp = await _sut.GetJob(request, "job-admin", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetJob_UnauthorizedIndexMarket_DoesNotLoadAggregateOrResolveAudio()
    {
        var index = new JobIndex("job-hidden", "Melbourne", "Job_melbourne");
        _jobRepository.GetJobIndexAsync("job-hidden", Arg.Any<CancellationToken>())
            .Returns(Result<JobIndex>.Success(index));
        _marketAccess.GetAllowedMarketsAsync("scoped-op", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["Sydney"]));

        var request = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-hidden");
        AddScopedOperatorPrincipal(request);
        var response = await _sut.GetJob(request, "job-hidden", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await _jobRepository.DidNotReceive().GetJobAsync(
            Arg.Any<JobIndex>(), Arg.Any<CancellationToken>());
        await _audioUrlProvider.DidNotReceive().GetAudioUrlAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJobAudio_UnauthorizedIndexMarket_DoesNotLoadAggregateOrTouchAudio()
    {
        var index = new JobIndex("job-audio-hidden", "Melbourne", "Job_melbourne");
        _jobRepository.GetJobIndexAsync("job-audio-hidden", Arg.Any<CancellationToken>())
            .Returns(Result<JobIndex>.Success(index));
        _marketAccess.GetAllowedMarketsAsync("scoped-op", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["Sydney"]));

        var request = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-audio-hidden/audio");
        AddScopedOperatorPrincipal(request);
        var response = await _sut.GetJobAudio(request, "job-audio-hidden", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await _jobRepository.DidNotReceive().GetJobAsync(
            Arg.Any<JobIndex>(), Arg.Any<CancellationToken>());
        _audioUrlProvider.DidNotReceive().ResolveOwnBlobName(Arg.Any<string>());
        await _audioUrlProvider.DidNotReceive().OpenAudioStreamAsync(
            Arg.Any<string>(), Arg.Any<AudioByteRange?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJob_StaleIndex_UsesLoadedGuardWithoutAnotherAggregateRead()
    {
        var job = Job.Create("job-stale", "Station1", "Sydney", 30, DateTime.UtcNow).Value;
        job.SetStatus(JobStatus.WaitingApproval);
        var index = new JobIndex(job.JobId, "Melbourne", "Job_melbourne");
        _jobRepository.GetJobIndexAsync(job.JobId, Arg.Any<CancellationToken>())
            .Returns(Result<JobIndex>.Success(index));
        _jobRepository.GetJobAsync(Arg.Is<JobIndex>(i => i.JobId == job.JobId), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _marketAccess.GetAllowedMarketsAsync("scoped-op", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["Melbourne"]));

        var request = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-stale");
        AddScopedOperatorPrincipal(request);
        var response = await _sut.GetJob(request, job.JobId, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        await _jobRepository.Received(1).GetJobIndexAsync(job.JobId, Arg.Any<CancellationToken>());
        await _jobRepository.Received(1).GetJobAsync(
            Arg.Is<JobIndex>(i => i.JobId == job.JobId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJob_RepeatedAuthorizedReads_ReuseCalibrationMetadata()
    {
        var job = TestJob("job-calibration");
        job.ScriptText = Option<string>.Some("A script ready for an operator edit.");
        job.VoiceConfig = Option<JobVoiceConfig>.Some(
            new JobVoiceConfig("elevenlabs", "voice-1", "Dolly", "eleven_v3",
                Option<VoiceSettings>.Some(VoiceSettings.Default)));
        SetupLoadedJob(job);
        _marketAccess.GetAllowedMarketsAsync("admin-op", true, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.None);
        _voiceCalibration.GetCalibrationMetricsAsync(
                "voice-1", Arg.Any<Option<VoiceSettings>>(), false, Arg.Any<CancellationToken>())
            .Returns(new CalibrationMetrics(150, 5, 2));

        var firstRequest = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-calibration");
        firstRequest.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                """{"auth_typ":"aad","claims":[{"typ":"oid","val":"admin-op"},{"typ":"roles","val":"Admin"}]}""")));
        var secondRequest = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-calibration");
        secondRequest.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(
                """{"auth_typ":"aad","claims":[{"typ":"oid","val":"admin-op"},{"typ":"roles","val":"Admin"}]}""")));

        (await _sut.GetJob(firstRequest, job.JobId, Ctx())).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await _sut.GetJob(secondRequest, job.JobId, Ctx())).StatusCode.ShouldBe(HttpStatusCode.OK);

        await _voiceCalibration.Received(1).GetCalibrationMetricsAsync(
            "voice-1", Arg.Any<Option<VoiceSettings>>(), false, Arg.Any<CancellationToken>());
    }

    // === GetJobAudio (server-side audio proxy) ===

    [Theory]
    [InlineData("bytes=0-1023", 0L, 1023L)]
    [InlineData("bytes=200-", 200L, null)]
    public void ParseRangeHeader_ValidSingleRange_Parses(string header, long from, long? to)
    {
        var range = JobsHttpTrigger.ParseRangeHeader(header);
        range.ShouldNotBeNull();
        range.Value.From.ShouldBe(from);
        range.Value.To.ShouldBe(to);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bytes=-500")] // suffix form unsupported -> full body
    [InlineData("bytes=abc")]
    [InlineData("bytes=5-2")]
    [InlineData("bytes=0-1,5-9")] // multi-range unsupported
    [InlineData("items=0-5")]
    public void ParseRangeHeader_UnsupportedOrMalformed_ReturnsNull(string? header)
    {
        JobsHttpTrigger.ParseRangeHeader(header).ShouldBeNull();
    }

    private void SetupAudioJob(string id, string audioUrl = "bulletin.wav")
    {
        SetupJob(id, configure: j => j.AudioUrl = Option<string>.Some(audioUrl));
        _audioUrlProvider.ResolveOwnBlobName(audioUrl).Returns(Option<string>.Some(audioUrl));
    }

    [Fact]
    public async Task GetJobAudio_NoRange_Returns200FullBody()
    {
        SetupAudioJob("job-a1");
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        _audioUrlProvider.OpenAudioStreamAsync("bulletin.wav", null, Arg.Any<CancellationToken>())
            .Returns(Result<AudioStreamHandle>.Success(
                new AudioStreamHandle(new MemoryStream(bytes), 0, 5, 5, "audio/wav")));

        var resp = await _sut.GetJobAudio(
            MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-a1/audio"), "job-a1", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        resp.Headers.GetValues("Accept-Ranges").ShouldContain("bytes");
        resp.Headers.Contains("Content-Range").ShouldBeFalse();
    }

    [Fact]
    public async Task GetJobAudio_WithRange_Returns206WithContentRange()
    {
        SetupAudioJob("job-a2");
        _audioUrlProvider.OpenAudioStreamAsync(
                "bulletin.wav", new AudioByteRange(0, 3), Arg.Any<CancellationToken>())
            .Returns(Result<AudioStreamHandle>.Success(
                new AudioStreamHandle(new MemoryStream(new byte[] { 1, 2, 3, 4 }), 0, 4, 10, "audio/wav")));

        var req = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-a2/audio");
        req.Headers.Add("Range", "bytes=0-3");
        var resp = await _sut.GetJobAudio(req, "job-a2", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        resp.Headers.GetValues("Content-Range").ShouldContain("bytes 0-3/10");
        resp.Headers.GetValues("Accept-Ranges").ShouldContain("bytes");
    }

    [Fact]
    public async Task GetJobAudio_MalformedRange_Returns200FullBody()
    {
        SetupAudioJob("job-a3");
        _audioUrlProvider.OpenAudioStreamAsync("bulletin.wav", null, Arg.Any<CancellationToken>())
            .Returns(Result<AudioStreamHandle>.Success(
                new AudioStreamHandle(new MemoryStream(new byte[] { 9 }), 0, 1, 1, "audio/wav")));

        var req = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-a3/audio");
        req.Headers.TryAddWithoutValidation("Range", "bytes=oops").ShouldBeTrue();
        var resp = await _sut.GetJobAudio(req, "job-a3", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _audioUrlProvider.Received(1)
            .OpenAudioStreamAsync("bulletin.wav", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJobAudio_ExternalAudioUrl_RedirectsToSource()
    {
        const string external = "https://cdn.example.com/filler-cart.wav";
        SetupJob("job-a4", configure: j => j.AudioUrl = Option<string>.Some(external));
        _audioUrlProvider.ResolveOwnBlobName(external).Returns(Option<string>.None);

        var resp = await _sut.GetJobAudio(
            MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-a4/audio"), "job-a4", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        resp.Headers.GetValues("Location").ShouldContain(external);
    }

    [Fact]
    public async Task GetJobAudio_JobWithoutAudio_Returns404()
    {
        SetupJob("job-a5");
        var resp = await _sut.GetJobAudio(
            MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-a5/audio"), "job-a5", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetJob_UseAudioProxy_ReturnsRelativeProxyPathWithRenderToken()
    {
        var job = TestJob("job-a6");
        job.AudioUrl = Option<string>.Some("bulletin.wav");
        SetupLoadedJob(job);
        _audioUrlProvider.ResolveOwnBlobName("bulletin.wav").Returns(Option<string>.Some("bulletin.wav"));

        var sut = new JobsHttpTrigger(_jobRepository, _audioUrlProvider,
            _marketAccess, Substitute.For<IVoiceCalibrationService>(),
            Options.Create(new BroadcastOptions { UseAudioProxy = true }),
            Options.Create(new SpeechDurationOptions()), TimeProvider.System,
            Substitute.For<ILogger<JobsHttpTrigger>>());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await sut.GetJob(MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-a6"), "job-a6", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        // The blob filename MUST ride the path: the console only rebinds <audio src> when the
        // path changes, so a bare jobs/{id}/audio stranded the player on the pre-re-voice take.
        body.ShouldContain("jobs/job-a6/audio/bulletin.wav");
        await _audioUrlProvider.DidNotReceive().GetAudioUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetJob_FillerFallbackJob_StillReportsTheBulletinThatDidNotAir()
    {
        // The exact shape measured on deployed dev + test (2026-08-24): a covered slot's aired
        // script is EMPTY, because ApplyFillerFallback cleared it. The generated read has to reach
        // the operator through SupersededTake or it is unrecoverable from this response.
        var job = TestJob("job-a7");
        job.ScriptText = Option<string>.Some("The M4 is slow citybound near Parramatta.");
        job.AudioUrl = Option<string>.Some("broadcast_20260824_014619_a7.wav");
        job.LastEstimatedDurationSec = Option<double>.Some(29.1);
        job.SetScriptSentences([new JobScriptSentence("The M4 is slow citybound near Parramatta.", ["M4"])]);
        job.ApplyFillerFallback("filler-cart.wav", DateTime.UtcNow, reason: "missed-window")
            .IsSuccess.ShouldBeTrue();
        SetupLoadedJob(job);
        _audioUrlProvider.GetAudioUrlAsync("filler-cart.wav", Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://blob.test/filler-cart.wav"));
        _audioUrlProvider.GetAudioUrlAsync("broadcast_20260824_014619_a7.wav", Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("https://blob.test/broadcast_20260824_014619_a7.wav?sas"));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetJob(MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-a7"), "job-a7", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        var dto = JsonDocument.Parse(body).RootElement;
        dto.GetProperty("Status").GetString().ShouldBe("FALLBACK_USED");
        dto.GetProperty("Script").GetProperty("Data").GetProperty("Text").GetString().ShouldBe(string.Empty);
        var take = dto.GetProperty("SupersededTake");
        take.GetProperty("Text").GetString().ShouldBe("The M4 is slow citybound near Parramatta.");
        take.GetProperty("Reason").GetString().ShouldBe("missed-window");
        take.GetProperty("CapturedAt").GetString().ShouldNotBeNullOrWhiteSpace();
        take.GetProperty("EstimatedDurationSec").GetDouble().ShouldBe(29.1);
        take.GetProperty("Sentences").GetArrayLength().ShouldBe(1);
        // The take's audio is signed from ITS OWN blob, never the job's current (cart) audio —
        // the proxy route always streams job.AudioUrl, so reusing it would play the cart here.
        take.GetProperty("AudioUrl").GetString()
            .ShouldBe("https://blob.test/broadcast_20260824_014619_a7.wav?sas");
    }

    [Fact]
    public async Task GetJob_JobThatNeverFellBack_ReportsNoSupersededTake()
    {
        SetupJob("job-a8", JobStatus.WaitingApproval,
            j => j.ScriptText = Option<string>.Some("A clean read that aired."));

        (_, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetJob(MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-a8"), "job-a8", Ctx()));

        // The key is always present (DefaultIgnoreCondition = Never) and must be null, which is
        // what the web schema's nullish() is written against.
        JsonDocument.Parse(body).RootElement.GetProperty("SupersededTake").ValueKind
            .ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public void RenderToken_KeepsOnlyTheEscapedLeafSegment()
    {
        JobsHttpTrigger.RenderToken("broadcast_20260730_064529_abc.wav").ShouldBe("broadcast_20260730_064529_abc.wav");
        JobsHttpTrigger.RenderToken("nested/dir/take two.wav").ShouldBe("take%20two.wav");
    }
}

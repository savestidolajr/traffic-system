using System.Net;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Content rewrite from an operator instruction. The contract that matters is the same as the
///     retone endpoint — text back for review, never voiced, never saved — plus content-safety
///     protection. Reference data is supplied to the lightweight editor, but there is no grounding
///     verdict or HERE re-fetch: the operator owns the requested content change.
/// </summary>
public sealed class ScriptRewriteHttpTriggerTests
{
    private const string JobId = "job-rewrite-1";
    private const string Approved =
        "The Monash Freeway is closed citybound at Toorak Road after a fatal crash. Take Burke Road instead.";

    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly IScriptRewriter _rewriter = Substitute.For<IScriptRewriter>();
    private readonly IScriptReviewer _reviewer = Substitute.For<IScriptReviewer>();
    private readonly IScriptSafetyReviewer _safetyReviewer = Substitute.For<IScriptSafetyReviewer>();
    private readonly IVoiceCalibrationService _voiceCalibration = Substitute.For<IVoiceCalibrationService>();
    private readonly IContentSafetyFilter _contentSafety = Substitute.For<IContentSafetyFilter>();
    private readonly IFillerStatementsRepository _fillerStatements =
        Substitute.For<IFillerStatementsRepository>();
    private readonly IBookendRepository _bookends = Substitute.For<IBookendRepository>();
    private readonly InteractiveWorkGateSpy _gate = new();
    private readonly ScriptRewriteHttpTrigger _sut;

    public ScriptRewriteHttpTriggerTests()
    {
        _fillerStatements.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _bookends.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Bookend>>.Success(Option<Bookend>.None));

        _sut = new ScriptRewriteHttpTrigger(
            _jobRepository, _marketAccess, _rewriter, _voiceCalibration, _safetyReviewer, _contentSafety,
            Options.Create(new BroadcastOptions()), Options.Create(new SpeechDurationOptions()),
            _fillerStatements, _bookends,
            Substitute.For<ILogger<ScriptRewriteHttpTrigger>>(), _gate);

        var job = Job.Create(JobId, "Station1", "melbourne", 30, DateTime.UtcNow).Value;
        job.SetAllIncidents([
            new JobIncident("inc-1", 2, "accident on Monash Freeway", "Monash Freeway",
                Direction: Option<string>.Some("citybound"))
        ]);
        job.SetIncidents(job.AllIncidents);
        job.SetCorridors([
            new JobCorridor("Monash Freeway", 2, 100, "accident",
                "accident on Monash Freeway", 1, 2.0, 1, true, IncidentKeys: ["inc-1"])
        ]);
        _jobRepository.GetJobAsync(JobId, Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));

        _contentSafety.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        _safetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));
    }

    private void StubRewrite(string script, int before = 17, int after = 18) =>
        _rewriter.RewriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BroadcastWithTraffic>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<RewriteWordBudget?>())
            .Returns(Result<ScriptRewrite>.Success(new ScriptRewrite(script, before, after)));

    [Fact]
    public async Task Rewrite_Valid_ReturnsTheRewrittenTextWithoutGroundingReview()
    {
        const string rewritten =
            "The Monash Freeway is CLOSED citybound at Toorak Road after a fatal crash. Burke Road instead.";
        StubRewrite(rewritten);

        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, "Lead with the closure and drop the weather."),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await MockHttpRequestData.ReadResponseJsonAsync<RewriteScriptResponseDto>(res);
        body!.Script.ShouldBe(rewritten);
        body.WordsBefore.ShouldBe(17);
        body.WordsAfter.ShouldBe(18);
        await _reviewer.DidNotReceive().ReviewScriptAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(),
            Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
        await _rewriter.Received(1).RewriteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BroadcastWithTraffic>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>(),
            Arg.Any<RewriteWordBudget?>());
    }
    [Fact]
    public async Task Rewrite_DoesNotRejectOperatorRequestedContent()
    {
        const string rewritten = "The Hume Freeway is closed outbound at Campbellfield.";
        StubRewrite(rewritten);

        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, "Use the Hume Freeway closure instead."),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await MockHttpRequestData.ReadResponseJsonAsync<RewriteScriptResponseDto>(res);
        body!.Script.ShouldBe(rewritten);
        await _reviewer.DidNotReceive().ReviewScriptAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(),
            Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
    }

    [Fact]
    public async Task Rewrite_StationFillerTail_IsNeverShownToTheRewriterAndComesBackVerbatim()
    {
        // Filler is station-authored copy: it stays outside the lightweight rewrite body and returns
        // verbatim at the tail.
        const string filler =
            "Roadworks and changed traffic conditions are updated weekly on the Cairns Regional Council website.";
        const string promo =
            "Download the iHeartRadio app to take us anywhere, search KIIS or 4CA Cairns.";
        _fillerStatements.GetStatementsAsync("melbourne", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success(
                [new FillerStatement("f1", filler), new FillerStatement("f2", promo)]));
        StubRewrite("The Monash Freeway is CLOSED citybound at Toorak Road.");

        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto($"{Approved} {filler} {promo}", "Lead with the closure."),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);

        await _rewriter.Received(1).RewriteAsync(
            Arg.Is<string>(s => !s.Contains("iHeartRadio") && !s.Contains("Regional Council")
                                && s.Contains("Monash Freeway")),
            Arg.Any<string>(), Arg.Any<BroadcastWithTraffic>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>(), Arg.Any<RewriteWordBudget?>());

        var body = await MockHttpRequestData.ReadResponseJsonAsync<RewriteScriptResponseDto>(res);
        body!.Script.ShouldBe($"The Monash Freeway is CLOSED citybound at Toorak Road. {filler} {promo}");
        body.PreservedLines.ShouldBe(2);
        body.WordsBefore.ShouldBe(17 + 27);
        body.WordsAfter.ShouldBe(18 + 27);
    }

    [Fact]
    public async Task Rewrite_StationBookendPair_IsPeeledFromBothEndsEvenWithUnresolvedTokens()
    {
        // A bookend template carries {city}-style tokens the schedule substituted at dispatch; the
        // job records only the id and version, so the aired text is matched on literal fragments.
        var bookend = Bookend.Create("Cairns pair", null,
            new BookendTemplate("Traffic across {city}, brought to you by Bob's Tyres.",
                "That's your traffic, back to the music."),
            null, null, DateTime.UtcNow, "bk-1").Value;
        _bookends.GetAsync("bk-1", Arg.Any<CancellationToken>())
            .Returns(Result<Option<Bookend>>.Success(Option<Bookend>.Some(bookend)));
        var job = Job.Create(JobId, "Station1", "melbourne", 30, DateTime.UtcNow).Value;
        job.BookendId = Option<string>.Some("bk-1");
        job.BookendVersion = Option<int>.Some(1);
        _jobRepository.GetJobAsync(JobId, Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        StubRewrite("The Monash Freeway is CLOSED citybound at Toorak Road.");

        const string intro = "Traffic across Cairns, brought to you by Bob's Tyres.";
        const string outro = "That's your traffic, back to the music.";
        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto($"{intro} {Approved} {outro}", "Lead with the closure."),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _rewriter.Received(1).RewriteAsync(
            Arg.Is<string>(s => !s.Contains("Bob's Tyres") && !s.Contains("back to the music")),
            Arg.Any<string>(), Arg.Any<BroadcastWithTraffic>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>(), Arg.Any<RewriteWordBudget?>());

        var body = await MockHttpRequestData.ReadResponseJsonAsync<RewriteScriptResponseDto>(res);
        body!.Script.ShouldBe($"{intro} The Monash Freeway is CLOSED citybound at Toorak Road. {outro}");
        body.PreservedLines.ShouldBe(2);
    }

    [Fact]
    public async Task Rewrite_ContentSafetyViolation_IsRefusedWithTheReason()
    {
        StubRewrite("The Monash Freeway is closed citybound after a fatal crash.");
        _contentSafety.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Blocked(["blocked-test-word: speculative cause"]));

        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, "Make the crash the lead."),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (_, text) = await MockHttpRequestData.ReadResponseAsync(res);
        text.ShouldContain("blocked-test-word");
    }

    [Fact]
    public async Task Rewrite_AiSafetyReviewerDown_FailsOpen()
    {
        // Mirrors ScriptReviewLoop.CheckContentSafetyAsync: the deterministic wordlist passed and a
        // human still reviews the preview, so a transient LLM outage must not block the endpoint.
        StubRewrite("The Monash Freeway is CLOSED citybound.");
        _safetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Failure("safety agent down"));

        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, "Lead with the closure."),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rewrite_DoesNotMutateTheJob()
    {
        // Same contract as retone: the operator reads the rewrite first. If this ever starts saving
        // or synthesising, changed words could reach air unreviewed.
        StubRewrite("The Monash Freeway is CLOSED citybound.");

        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, "Lead with the closure."),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        await _sut.Rewrite(req, JobId, Ctx());

        await _jobRepository.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rewrite_WithoutAnInstruction_IsARequestError()
    {
        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, "   "),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _rewriter.DidNotReceive().RewriteAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rewrite_UnknownJob_Returns404()
    {
        _jobRepository.GetJobAsync("missing", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("not found"));

        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, "Lead with the closure."),
            "http://localhost/api/jobs/missing/rewrite");
        var res = await _sut.Rewrite(req, "missing", Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rewrite_OverlongInstruction_Returns400()
    {
        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, new string('x', 3001)),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _rewriter.DidNotReceive().RewriteAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(),
            Arg.Any<RewriteWordBudget?>());
    }
    [Fact]
    public async Task Rewrite_GatesSafetyBeforeReturningAndScopesTheRevision()
    {
        const string rewritten = "The Monash Freeway is CLOSED citybound at Toorak Road.";
        StubRewrite(rewritten);
        var order = new List<string>();
        _contentSafety.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("wordlist");
                return ContentSafetyResult.Allowed();
            });
        _safetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                order.Add("ai-safety");
                return Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe());
            });

        var req = MockHttpRequestData.CreatePost(
            new RewriteScriptRequestDto(Approved, "Lead with the closure."),
            "http://localhost/api/jobs/job-rewrite-1/rewrite");
        var res = await _sut.Rewrite(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        order.ShouldBe(["wordlist", "ai-safety"]);
        var key = _gate.LlmKeys.Single();
        key.Operation.ShouldBe("rewrite");
        key.PrincipalId.ShouldBe("anonymous");
        key.Revision.ShouldNotBeNullOrWhiteSpace();
        key.Input.ShouldBe(Approved);
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();
}

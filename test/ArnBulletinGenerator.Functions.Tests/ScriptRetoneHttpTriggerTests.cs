using System.Net;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Triggers.Http;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Rewrite-for-register. The contract that matters is that this endpoint hands text BACK for
///     review and never voices anything: it changes the spoken words, so a human must read them and
///     the safety reviewer must see them on submit.
/// </summary>
public sealed class ScriptRetoneHttpTriggerTests
{
    private const string JobId = "job-retone-1";
    private const string Approved =
        "The Monash Freeway is closed citybound at Toorak Road after a fatal crash. Take Burke Road instead.";

    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly IDeliveryWriter _writer = Substitute.For<IDeliveryWriter>();
    private readonly IVoiceCalibrationService _voiceCalibration = Substitute.For<IVoiceCalibrationService>();
    private readonly IFillerStatementsRepository _fillerStatements =
        Substitute.For<IFillerStatementsRepository>();
    private readonly IBookendRepository _bookends = Substitute.For<IBookendRepository>();
    private readonly InteractiveWorkGateSpy _gate = new();
    private readonly ScriptRetoneHttpTrigger _sut;

    public ScriptRetoneHttpTriggerTests()
    {
        _voiceCalibration.GetCalibrationMetricsAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new CalibrationMetrics(150, 5.8, 1.5));
        _fillerStatements.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _bookends.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Bookend>>.Success(Option<Bookend>.None));


        _sut = new ScriptRetoneHttpTrigger(
            _jobRepository, _marketAccess, _writer, _voiceCalibration,
            Options.Create(new BroadcastOptions()), Options.Create(new SpeechDurationOptions()),
            _fillerStatements, _bookends,
            Options.Create(new ElevenLabsOptions { ModelId = "eleven_v3" }),
            Substitute.For<ILogger<ScriptRetoneHttpTrigger>>(), _gate);

        var job = Job.Create(JobId, "Station1", "melbourne", 30, DateTime.UtcNow).Value;
        job.VoiceConfig = Option<JobVoiceConfig>.Some(
            new JobVoiceConfig("elevenlabs", "voice-1", "Dolly", "eleven_v3"));
        _jobRepository.GetJobAsync(JobId, Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
    }

    private void StubRewrite(string script, int before = 17, int after = 18) =>
        _writer.RewriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<RewriteWordBudget?>())
            .Returns(Result<DeliveryRewrite>.Success(
                new DeliveryRewrite(script, before, after, ["[quietly]"], [])));

    [Fact]
    public async Task Retone_Valid_ReturnsTheRewrittenText()
    {
        const string rewritten = "[quietly] Some sad news. The Monash Freeway is CLOSED citybound at Toorak Road.";
        StubRewrite(rewritten);

        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, "Talk about this more calmly, it's about a death."),
            "http://localhost/api/jobs/job-retone-1/retone");
        var res = await _sut.Retone(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await MockHttpRequestData.ReadResponseJsonAsync<RetoneScriptResponseDto>(res);
        body!.Script.ShouldBe(rewritten);
    }

    [Fact]
    public async Task Retone_LeavesTheStationsOwnFillerLineAlone()
    {
        // A read direction is about how the TRAFFIC is read. A station's fixed promo is ARN
        // editorial content that never passes through an LLM stage on the way to air, so the
        // delivery writer must not be handed it — re-registering "Download the iHeartRadio app"
        // is not the operator's ask and not this endpoint's business.
        const string promo = "Download the iHeartRadio app to take us anywhere, search KIIS or 4CA Cairns.";
        _fillerStatements.GetStatementsAsync("melbourne", Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([new FillerStatement("f1", promo)]));
        StubRewrite("[quietly] Some sad news. The Monash Freeway is CLOSED citybound at Toorak Road.");

        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto($"{Approved} {promo}", "calmer please"),
            "http://localhost/api/jobs/job-retone-1/retone");
        var res = await _sut.Retone(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _writer.Received(1).RewriteAsync(
            Arg.Is<string>(s => !s.Contains("iHeartRadio")), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<RewriteWordBudget?>());

        var body = await MockHttpRequestData.ReadResponseJsonAsync<RetoneScriptResponseDto>(res);
        body!.Script.ShouldBe(
            $"[quietly] Some sad news. The Monash Freeway is CLOSED citybound at Toorak Road. {promo}");
        body.PreservedLines.ShouldBe(1);
    }

    [Fact]
    public async Task Retone_DoesNotVoiceOrMutateTheJob()
    {
        // The whole point of a separate endpoint: the operator reads the rewrite first. If this ever
        // starts saving or synthesising, changed words could reach air unreviewed.
        StubRewrite("[quietly] Some sad news. The Monash Freeway is CLOSED.");

        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, "calmer please"),
            "http://localhost/api/jobs/job-retone-1/retone");
        await _sut.Retone(req, JobId, Ctx());

        await _jobRepository.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Retone_WithoutAnIntent_IsARequestError()
    {
        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, "   "),
            "http://localhost/api/jobs/job-retone-1/retone");
        var res = await _sut.Retone(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _writer.DidNotReceive().RewriteAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(),
            Arg.Any<RewriteWordBudget?>());
    }

    [Fact]
    public async Task Retone_WriterRejection_SurfacesAsAClientErrorWithTheReason()
    {
        // The writer refuses a rewrite that used a banned tag, lost a fact, or blew the slot budget.
        _writer.RewriteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>(), Arg.Any<RewriteWordBudget?>())
            .Returns(Result<DeliveryRewrite>.Failure(
                "Could not produce a usable rewrite: it dropped or altered these facts: Toorak Road."));

        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, "calmer please"),
            "http://localhost/api/jobs/job-retone-1/retone");
        var res = await _sut.Retone(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var (_, text) = await MockHttpRequestData.ReadResponseAsync(res);
        text.ShouldContain("Toorak Road");
    }

    [Fact]
    public async Task Retone_UnknownJob_Returns404()
    {
        _jobRepository.GetJobAsync("missing", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("not found"));

        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, "calmer please"),
            "http://localhost/api/jobs/missing/retone");
        var res = await _sut.Retone(req, "missing", Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Retone_UsesTheJobsOwnTtsModel()
    {
        // Tags are only honoured on v3, so the writer must know what the bulletin will be read with.
        StubRewrite("[quietly] The Monash Freeway is CLOSED.");

        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, "calmer please"),
            "http://localhost/api/jobs/job-retone-1/retone");
        await _sut.Retone(req, JobId, Ctx());

        await _writer.Received().RewriteAsync(Arg.Any<string>(), Arg.Any<string>(),
            "eleven_v3", JobId, Arg.Any<CancellationToken>(), Arg.Any<RewriteWordBudget?>());
    }

    [Fact]
    public async Task Retone_OverlongIntent_Returns400()
    {
        // The Intent is interpolated into the LLM prompt verbatim, so it must carry the same
        // ceiling as the script payload — an unbounded intent bills an arbitrarily large prompt
        // per call (bug-bash 2026-08-05 #40: 13k chars accepted).
        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, new string('x', 3001)),
            "http://localhost/api/jobs/job-retone-1/retone");
        var res = await _sut.Retone(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _writer.DidNotReceive().RewriteAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>(),
            Arg.Any<RewriteWordBudget?>());
    }

    [Fact]
    public async Task Retone_AtLimitIntent_IsAccepted()
    {
        // The cap is inclusive: a 3000-character intent is a legitimate (if verbose) instruction.
        StubRewrite(Approved);
        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, new string('x', 3000)),
            "http://localhost/api/jobs/job-retone-1/retone");
        var res = await _sut.Retone(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Retone_UsesRevisionScopedLlmGate()
    {
        StubRewrite(Approved);
        var req = MockHttpRequestData.CreatePost(
            new RetoneScriptRequestDto(Approved, "read it more calmly"),
            "http://localhost/api/jobs/job-retone-1/retone");

        var res = await _sut.Retone(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var key = _gate.LlmKeys.Single();
        key.Operation.ShouldBe("retone");
        key.PrincipalId.ShouldBe("anonymous");
        key.Revision.ShouldNotBeNullOrWhiteSpace();
        key.Input.ShouldBe(Approved);
        key.Model.ShouldBe("eleven_v3");
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();
}

using System.Net;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Triggers.Http;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Direct-script endpoint tests. The contract that matters: the intent is operator free-text
///     interpolated into the LLM prompt, so it must carry the same character ceiling as the
///     script payload rather than billing an arbitrarily large prompt per call.
/// </summary>
public sealed class ScriptDirectionHttpTriggerTests
{
    private const string JobId = "job-direct-1";
    private const string Script =
        "The Monash Freeway is closed citybound at Toorak Road after a fatal crash. Take Burke Road instead.";

    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly IScriptDirector _director = Substitute.For<IScriptDirector>();
    private readonly InteractiveWorkGateSpy _gate = new();
    private readonly ScriptDirectionHttpTrigger _sut;

    public ScriptDirectionHttpTriggerTests()
    {
        _sut = new ScriptDirectionHttpTrigger(
            _jobRepository, _marketAccess, _director,
            Options.Create(new ElevenLabsOptions { ModelId = "eleven_v3" }),
            Substitute.For<ILogger<ScriptDirectionHttpTrigger>>(), _gate);

        var job = Job.Create(JobId, "Station1", "melbourne", 30, DateTime.UtcNow).Value;
        job.ScriptText = Option<string>.Some(Script);
        job.VoiceConfig = Option<JobVoiceConfig>.Some(
            new JobVoiceConfig("elevenlabs", "voice-1", "Dolly", "eleven_v3"));
        _jobRepository.GetJobAsync(JobId, Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
    }

    private void StubDirected(string directed) =>
        _director.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(),
                Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(directed));

    [Fact]
    public async Task Direct_OverlongIntent_Returns400()
    {
        // A 13,000-char intent was accepted on dev (bug-bash 2026-08-05 #40); the intent is
        // rendered into the LLM prompt verbatim, so it must be capped like the script payload.
        var req = MockHttpRequestData.CreatePost(
            new DirectScriptRequestDto(Script, new string('x', 3001)),
            "http://localhost/api/jobs/job-direct-1/direct");
        var res = await _sut.Direct(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await _director.DidNotReceive().DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(),
            Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Direct_AtLimitIntent_IsAccepted()
    {
        // The cap is inclusive: a 3000-character intent is a legitimate (if verbose) instruction.
        StubDirected(Script);
        var req = MockHttpRequestData.CreatePost(
            new DirectScriptRequestDto(Script, new string('x', 3000)),
            "http://localhost/api/jobs/job-direct-1/direct");
        var res = await _sut.Direct(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _director.Received(1).DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(),
            Arg.Is<string>(i => i.Length == 3000), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Direct_UsesRevisionScopedLlmGate()
    {
        StubDirected(Script);
        var req = MockHttpRequestData.CreatePost(
            new DirectScriptRequestDto(Script, "read it more calmly"),
            "http://localhost/api/jobs/job-direct-1/direct");

        var res = await _sut.Direct(req, JobId, Ctx());

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var key = _gate.LlmKeys.Single();
        key.Operation.ShouldBe("direct");
        key.PrincipalId.ShouldBe("anonymous");
        key.Revision.ShouldNotBeNullOrWhiteSpace();
        key.Input.ShouldBe(Script);
        key.Model.ShouldBe("eleven_v3");
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();
}

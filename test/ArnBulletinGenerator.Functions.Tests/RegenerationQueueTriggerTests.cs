using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Errors;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Functions.Triggers.Queues;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class RegenerationQueueTriggerTests
{
    private readonly ILogger<RegenerationQueueTrigger> _logger = Substitute.For<ILogger<RegenerationQueueTrigger>>();
    private readonly IBroadcastOrchestrator _orchestrator = Substitute.For<IBroadcastOrchestrator>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly RegenerationQueueTrigger _sut;

    public RegenerationQueueTriggerTests()
    {
        // Default the dedup lookup to NotFound so tests exercise the "process"
        // branch unless they explicitly stub a different outcome.
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure(ArnBulletinGenerator.Domain.Errors.ErrorMessages.Job.NotFound));
        _sut = new RegenerationQueueTrigger(_orchestrator, _jobRepository, _logger);
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    private static string ValidJson(string jobId = "j1") =>
        $$"""
           {
               "JobId": "{{jobId}}",
               "Location": "Melbourne",
               "TargetDurationSeconds": 30,
               "Traffic": "Light traffic on M1",
               "Incidents": []
           }
           """;

    [Fact]
    public async Task Run_ValidMessage_CallsOrchestrator()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Success(
                new BroadcastResult("regen.mp3", "ok", new BroadcastDebugInfo("gen", "synth", 30.0))));

        await _sut.Run(ValidJson(), Ctx());

        await _orchestrator.Received(1).OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_JobExistsInRequestedState_Processes()
    {
        var job = Job.Create("j-req", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-req", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Success(
                new BroadcastResult("regen.mp3", "ok", new BroadcastDebugInfo("gen", "synth", 30.0))));

        await _sut.Run(ValidJson("j-req"), Ctx());
        await _orchestrator.Received(1).OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_JobAlreadyAdvanced_SkipsAsDuplicate()
    {
        var job = Job.Create("j-dup", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        _jobRepository.GetJobAsync("j-dup", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));

        await _sut.Run(ValidJson("j-dup"), Ctx());
        await _orchestrator.DidNotReceive().OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_InvalidJson_ThrowsForPoisonRouting()
    {
        var act = () => _sut.Run("{{{bad", Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
        await _orchestrator.DidNotReceive()
            .OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_EmptyMessage_ThrowsForPoisonRouting()
    {
        var act = () => _sut.Run("", Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
        await _orchestrator.DidNotReceive()
            .OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WhitespaceMessage_ThrowsForPoisonRouting()
    {
        var act = () => _sut.Run("   ", Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
        await _orchestrator.DidNotReceive()
            .OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_OrchestratorTransientFailure_Throws()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.Retryable("tts-timeout", "Audio synthesis timeout")));

        var act = () => _sut.Run(ValidJson("j2"), Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
        await _orchestrator.Received(1).OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_OrchestratorPermanentTtsFailure_DoesNotThrow()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.Permanent("tts-bad-request", "bad request: invalid voice")));

        var act = () => _sut.Run(ValidJson("j3"), Ctx());

        await Should.NotThrowAsync(act);
        await _orchestrator.Received(1).OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_OrchestratorContentLimited_DoesNotThrow()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.ContentLimited(
                "content-limited", "Content-limited: best spoken 18.2s could not fill the 30s slot")));

        var act = () => _sut.Run(ValidJson("j-cl"), Ctx());

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task Run_OrchestratorContentSafetyViolation_DoesNotThrow()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.SafetyRejected("safety-blocked", "Content safety violation: banned word found")));

        var act = () => _sut.Run(ValidJson("j-cs"), Ctx());

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task Run_OrchestratorTerminalState_DoesNotThrow()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.Conflict("terminal-state", "Job already in terminal state: Published")));

        var act = () => _sut.Run(ValidJson("j-ts"), Ctx());

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task Run_DedupLookupFailure_ThrowsForRetry()
    {
        _jobRepository.GetJobAsync("j-dedup", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("Table storage unavailable"));

        var act = () => _sut.Run(ValidJson("j-dedup"), Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task Run_DedupLookupNotFound_Processes()
    {
        _jobRepository.GetJobAsync("j-nf", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure(ArnBulletinGenerator.Domain.Errors.ErrorMessages.Job.NotFound));
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Success(
                new BroadcastResult("regen.mp3", "ok", new BroadcastDebugInfo("gen", "synth", 30.0))));

        await _sut.Run(ValidJson("j-nf"), Ctx());
        await _orchestrator.Received(1).OrchestrateAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>());
    }
}

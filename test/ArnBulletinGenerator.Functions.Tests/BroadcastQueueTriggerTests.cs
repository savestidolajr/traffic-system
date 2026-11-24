using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Functions.Triggers.Queues;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class BroadcastQueueTriggerTests
{
    private readonly ILogger<BroadcastQueueTrigger> _logger = Substitute.For<ILogger<BroadcastQueueTrigger>>();
    private readonly IBroadcastOrchestrator _orchestrator = Substitute.For<IBroadcastOrchestrator>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly BroadcastQueueTrigger _sut;

    public BroadcastQueueTriggerTests()
    {
        // Default the dedup lookup to NotFound so tests exercise the "process"
        // branch unless they explicitly stub a different outcome.
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure(ArnBulletinGenerator.Domain.Errors.ErrorMessages.Job.NotFound));
        _sut = new BroadcastQueueTrigger(_orchestrator, _jobRepository, _logger);
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }
    private static string ValidJson(string jobId = "j1", Phase phase = Phase.FetchAndGenerate) =>
        $$"""
           {
               "JobId": "{{jobId}}",
               "MarketId": "mel",
               "Location": "Melbourne",
               "TargetDurationSeconds": 30,
               "Latitude": -37.8,
               "Longitude": 144.9,
               "RadiusKm": 5.0,
               "VoiceId": "dolly",
               "Model": "eleven_v3",
               "Phase": {{(int)phase}}
           }
           """;
    [Fact]
    public async Task Run_ValidMessage_CallsOrchestrator()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Success(
                new BroadcastResult("file.mp3", "ok", new BroadcastDebugInfo("gen", "synth", 30.0))));

        await _sut.Run(ValidJson("j-phase", Phase.Generate), Ctx());

        await _orchestrator.Received(1).OrchestrateAsync(
            Arg.Any<BroadcastRequest>(), Arg.Is<Phase>(p => p == Phase.Generate), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_InvalidJson_ThrowsForPoisonRouting()
    {
        var act = () => _sut.Run("not-json!!!", Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
        await _orchestrator.DidNotReceive().OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_EmptyMessage_ThrowsForPoisonRouting()
    {
        var act = () => _sut.Run("", Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
        await _orchestrator.DidNotReceive().OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_WhitespaceMessage_ThrowsForPoisonRouting()
    {
        var act = () => _sut.Run("   ", Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
        await _orchestrator.DidNotReceive().OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_OrchestratorTransientFailure_Throws()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.Retryable("tts-timeout", "Audio synthesis timeout")));

        var act = () => _sut.Run(ValidJson("j2"), Ctx());

        await Should.ThrowAsync<InvalidOperationException>(act);
        await _orchestrator.Received(1).OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_OrchestratorPermanentTtsFailure_DoesNotThrow()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.Permanent("tts-bad-request", "bad request: invalid voice")));

        var act = () => _sut.Run(ValidJson("j3"), Ctx());

        await Should.NotThrowAsync(act);
        await _orchestrator.Received(1).OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_OrchestratorContentLimited_DoesNotThrow()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.ContentLimited(
                "content-limited", "Content-limited: best spoken 18.2s could not fill the 30s slot")));

        var act = () => _sut.Run(ValidJson("j-cl"), Ctx());

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task Run_OrchestratorContentSafetyViolation_DoesNotThrow()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.SafetyRejected("safety-blocked", "Content safety violation: banned word found")));

        var act = () => _sut.Run(ValidJson("j-cs"), Ctx());

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task Run_OrchestratorTerminalState_DoesNotThrow()
    {
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Failure(OperationalFailure.Conflict("terminal-state", "Job already in terminal state: Published")));

        var act = () => _sut.Run(ValidJson("j-ts"), Ctx());

        await Should.NotThrowAsync(act);
    }

    [Fact]
    public async Task Run_InFlightWithActiveLease_Skips()
    {
        var job = Job.Create("j1", "Station", "Market", 30, DateTime.UtcNow).Value;
        job.LastUpdatedAt = DateTime.UtcNow;
        job.SetStatus(JobStatus.HereFetched);
        _jobRepository.GetJobAsync("j1", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));

        var act = () => _sut.Run(ValidJson("j1"), Ctx());
        await Should.NotThrowAsync(act);
        await _orchestrator.DidNotReceive().OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_InFlightWithExpiredLease_Reprocesses()
    {
        var job = Job.Create("j1", "Station", "Market", 30, DateTime.UtcNow).Value;
        job.LastUpdatedAt = DateTime.UtcNow.AddMinutes(-20);
        job.SetStatus(JobStatus.IncidentsSelected);
        _jobRepository.GetJobAsync("j1", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Success(
                new BroadcastResult("file.mp3", "ok", new BroadcastDebugInfo("gen", "synth", 30.0))));

        await _sut.Run(ValidJson("j1"), Ctx());
        await _orchestrator.Received(1).OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_JobWaitingApproval_Skips()
    {
        var job = Job.Create("j1", "Station", "Market", 30, DateTime.UtcNow).Value;
        job.SetStatus(JobStatus.WaitingApproval);
        _jobRepository.GetJobAsync("j1", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));

        var act = () => _sut.Run(ValidJson("j1"), Ctx());
        await Should.NotThrowAsync(act);
        await _orchestrator.DidNotReceive().OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_GeneratePhaseOnAwaitingCurationJob_ResumesWithoutLeaseSkip()
    {
        var job = Job.Create("j-curation", "Station", "Market", 30, DateTime.UtcNow).Value;
        job.SetStatus(JobStatus.AwaitingCuration);
        _jobRepository.GetJobAsync("j-curation", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _orchestrator.OrchestrateAsync(Arg.Any<BroadcastRequest>(), Arg.Any<Phase>(), Arg.Any<CancellationToken>())
            .Returns(Result<BroadcastResult>.Success(
                new BroadcastResult("file.mp3", "ok", new BroadcastDebugInfo("gen", "synth", 30.0))));

        await _sut.Run(ValidJson("j-curation", Phase.Generate), Ctx());

        await _orchestrator.Received(1).OrchestrateAsync(
            Arg.Any<BroadcastRequest>(), Arg.Is<Phase>(p => p == Phase.Generate), Arg.Any<CancellationToken>());
    }
}

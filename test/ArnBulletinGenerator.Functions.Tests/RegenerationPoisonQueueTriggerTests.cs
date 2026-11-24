using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Functions.Triggers.Queues;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class RegenerationPoisonQueueTriggerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly ILogger<PoisonQueueHandler> _logger =
        Substitute.For<ILogger<PoisonQueueHandler>>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly IBroadcastMetrics _metrics = Substitute.For<IBroadcastMetrics>();
    private readonly RegenerationPoisonQueueTrigger _sut;
    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();


    public RegenerationPoisonQueueTriggerTests()
    {
        var handler = new PoisonQueueHandler(
            _jobRepository, _notificationService, new FakeTimeProvider(FixedNow), _metrics, _logger);
        _sut = new RegenerationPoisonQueueTrigger(handler);
    }

    [Fact]
    public async Task RunAsync_ValidJobId_MarksJobFailedAndSendsAlert()
    {
        var job = Job.Create("j-poison", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-poison", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        string json = """{"JobId": "j-poison", "Location": "Sydney"}""";

        await _sut.RunAsync(json, Ctx());

        job.Status.ShouldBe(JobStatus.Failed);
        await _jobRepository.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed), Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendAlertNotificationAsync(
            "System", "Regeneration Pipeline",
            Arg.Is<string>(s => s.Contains("j-poison")),
            AlertSeverity.Critical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_InvalidJson_DoesNotThrow()
    {
        var act = () => _sut.RunAsync("{{{bad", Ctx());

        await Should.NotThrowAsync(act);
        await _jobRepository.DidNotReceive().GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jobRepository.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_EmptyMessage_HandlesGracefully()
    {
        var act = () => _sut.RunAsync("", Ctx());

        await Should.NotThrowAsync(act);
        await _jobRepository.DidNotReceive().GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MessageWithoutJobId_SendsAlertButDoesNotSave()
    {
        string json = """{"Location": "Sydney"}""";

        await _sut.RunAsync(json, Ctx());

        await _jobRepository.DidNotReceive().GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jobRepository.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendAlertNotificationAsync(
            "System", "Regeneration Pipeline",
            Arg.Is<string>(s => s.Contains("could not be parsed")),
            AlertSeverity.Critical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SaveFails_LogsErrorAndSendsAlert()
    {
        var job = Job.Create("j-fail-save", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-fail-save", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Storage timeout"));

        string json = """{"JobId": "j-fail-save"}""";

        await _sut.RunAsync(json, Ctx());

        job.Status.ShouldBe(JobStatus.Failed);
        await _jobRepository.Received(1).SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendAlertNotificationAsync(
            "System", "Regeneration Pipeline",
            Arg.Is<string>(s => s.Contains("j-fail-save")),
            AlertSeverity.Critical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ValidJobId_RecordsFailureThroughStateMachine()
    {
        var job = Job.Create("j-poison-sm", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-poison-sm", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.RunAsync("""{"JobId": "j-poison-sm"}""", Ctx());

        // Failure must go through Job.MarkFailed so the audit trail carries the
        // error, the pre-failure status, and a StatusHistory entry.
        job.ErrorMessage.HasValue.ShouldBeTrue();
        job.FailedAtStatus.HasValue.ShouldBeTrue();
        job.FailedAtStatus.Value.ShouldBe(JobStatus.Requested);
        job.StatusHistory[^1].Status.ShouldBe(JobStatus.Failed);
        job.StatusHistory[^1].By.ShouldBe("system");
    }

    [Fact]
    public async Task RunAsync_TerminalJob_IsLeftUntouchedButStillAlerts()
    {
        var job = Job.Create("j-poison-term", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        job.TransitionTo(JobStatus.Published);
        _jobRepository.GetJobAsync("j-poison-term", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));

        await _sut.RunAsync("""{"JobId": "j-poison-term"}""", Ctx());

        // A stale poison message must never clobber an already-terminal job.
        job.Status.ShouldBe(JobStatus.Published);
        await _jobRepository.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendAlertNotificationAsync(
            "System", "Regeneration Pipeline",
            Arg.Is<string>(s => s.Contains("j-poison-term")),
            AlertSeverity.Critical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ValidJobId_PinsFailureTimestampToInjectedClock()
    {
        var job = Job.Create("j-poison-ts", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-poison-ts", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.RunAsync("""{"JobId": "j-poison-ts"}""", Ctx());

        // The domain timestamp must match the injected clock, not DateTime.UtcNow.
        job.StatusHistory[^1].At.ShouldBe(FixedNow.UtcDateTime);
    }

    [Fact]
    public async Task RunAsync_ValidJobId_EmitsExactlyOneMetric()
    {
        var job = Job.Create("j-poison-met", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-poison-met", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.RunAsync("""{"JobId": "j-poison-met"}""", Ctx());

        _metrics.Received(1).TrackPipelineStage(
            "PoisonQueue.Regeneration", 0.0, "system", false);
    }

    [Fact]
    public async Task RunAsync_MessageWithoutJobId_EmitsExactlyOneMetric()
    {
        await _sut.RunAsync("""{"Location": "Sydney"}""", Ctx());

        _metrics.Received(1).TrackPipelineStage(
            "PoisonQueue.Regeneration", 0.0, "system", false);
    }

}

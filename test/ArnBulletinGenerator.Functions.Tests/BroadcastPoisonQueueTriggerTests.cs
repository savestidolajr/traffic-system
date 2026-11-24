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

public sealed class BroadcastPoisonQueueTriggerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly ILogger<PoisonQueueHandler> _logger =
        Substitute.For<ILogger<PoisonQueueHandler>>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly IBroadcastMetrics _metrics = Substitute.For<IBroadcastMetrics>();
    private readonly BroadcastPoisonQueueTrigger _sut;
    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    public BroadcastPoisonQueueTriggerTests()
    {
        var handler = new PoisonQueueHandler(
            _jobRepository, _notificationService, new FakeTimeProvider(FixedNow), _metrics, _logger);
        _sut = new BroadcastPoisonQueueTrigger(handler);
    }

    [Fact]
    public async Task RunAsync_ValidJobId_RecordsFailureThroughStateMachine()
    {
        var job = Job.Create("j-bpoison-sm", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-bpoison-sm", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.RunAsync("""{"JobId": "j-bpoison-sm"}""", Ctx());

        // Failure must go through Job.MarkFailed so the audit trail carries the
        // error, the pre-failure status, and a StatusHistory entry.
        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.HasValue.ShouldBeTrue();
        job.FailedAtStatus.HasValue.ShouldBeTrue();
        job.FailedAtStatus.Value.ShouldBe(JobStatus.Requested);
        job.StatusHistory[^1].Status.ShouldBe(JobStatus.Failed);
        await _jobRepository.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed), Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendAlertNotificationAsync(
            "System", "Broadcast Pipeline",
            Arg.Is<string>(s => s.Contains("j-bpoison-sm")),
            AlertSeverity.Critical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TerminalJob_IsLeftUntouchedButStillAlerts()
    {
        var job = Job.Create("j-bpoison-term", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        job.TransitionTo(JobStatus.Published);
        _jobRepository.GetJobAsync("j-bpoison-term", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));

        await _sut.RunAsync("""{"JobId": "j-bpoison-term"}""", Ctx());

        // A stale poison message must never clobber an already-terminal job.
        job.Status.ShouldBe(JobStatus.Published);
        await _jobRepository.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendAlertNotificationAsync(
            "System", "Broadcast Pipeline",
            Arg.Is<string>(s => s.Contains("j-bpoison-term")),
            AlertSeverity.Critical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MessageWithoutJobId_SendsAlertButDoesNotSave()
    {
        await _sut.RunAsync("""{"Location": "Sydney"}""", Ctx());

        await _jobRepository.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendAlertNotificationAsync(
            "System", "Broadcast Pipeline",
            Arg.Any<string>(),
            AlertSeverity.Critical,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ValidJobId_PinsFailureTimestampToInjectedClock()
    {
        var job = Job.Create("j-bpoison-ts", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-bpoison-ts", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.RunAsync("""{"JobId": "j-bpoison-ts"}""", Ctx());

        // The domain timestamp must match the injected clock, not DateTime.UtcNow.
        job.StatusHistory[^1].At.ShouldBe(FixedNow.UtcDateTime);
    }

    [Fact]
    public async Task RunAsync_ValidJobId_EmitsExactlyOneMetric()
    {
        var job = Job.Create("j-bpoison-met", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-bpoison-met", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.RunAsync("""{"JobId": "j-bpoison-met"}""", Ctx());

        _metrics.Received(1).TrackPipelineStage(
            "PoisonQueue.Broadcast", 0.0, "system", false);
    }

    [Fact]
    public async Task RunAsync_MessageWithoutJobId_EmitsExactlyOneMetric()
    {
        await _sut.RunAsync("""{"Location": "Sydney"}""", Ctx());

        _metrics.Received(1).TrackPipelineStage(
            "PoisonQueue.Broadcast", 0.0, "system", false);
    }

    [Fact]
    public async Task RunAsync_ValidJobId_ForwardsContextCancellationTokenToRepository()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var context = Substitute.For<FunctionContext>();
        context.CancellationToken.Returns(token);

        var job = Job.Create("j-bpoison-ct", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        _jobRepository.GetJobAsync("j-bpoison-ct", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _sut.RunAsync("""{"JobId": "j-bpoison-ct"}""", context);

        await _jobRepository.Received(1).GetJobAsync("j-bpoison-ct", token);
        await _jobRepository.Received(1).SaveJobAsync(Arg.Any<Job>(), token);
    }

}

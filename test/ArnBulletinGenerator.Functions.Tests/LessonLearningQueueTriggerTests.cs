using System.Text.Json;
using ArnBulletinGenerator.Application.Learning;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Functions.Triggers.Queues;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class LessonLearningQueueTriggerTests
{
    private readonly ILessonLearningService _service = Substitute.For<ILessonLearningService>();
    private readonly IBroadcastMetrics _metrics = Substitute.For<IBroadcastMetrics>();
    private readonly LessonLearningQueueTrigger _sut;

    public LessonLearningQueueTriggerTests()
    {
        _sut = new LessonLearningQueueTrigger(
            _service,
            Substitute.For<ILogger<LessonLearningQueueTrigger>>(),
            _metrics);
    }

    private static Microsoft.Azure.Functions.Worker.FunctionContext Context() =>
        MockHttpRequestData.CreateFunctionContext();

    private static string MessageJson() => JsonSerializer.Serialize(new LessonLearningQueueMessage(
        "job-1", "sydney", "The M4 has delays", "The M4 is crawling", DateTime.UtcNow, "operator"));

    [Fact]
    public async Task MalformedMessage_ThrowsForHostPoisonRouting()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            _sut.Run("not-json", Context()));

        exception.Message.ShouldContain("Malformed lesson-learning queue message");
        await _service.DidNotReceive().ProcessAsync(
            Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>());
        _metrics.Received(1).TrackQueueNoExecution(QueueNames.LessonLearning, "malformed");
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "begin");
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "end");
    }

    [Fact]
    public async Task RetryableServiceFailure_ThrowsForHostRetry()
    {
        _service.ProcessAsync(Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("summariser unavailable"));

        await Should.ThrowAsync<InvalidOperationException>(() => _sut.Run(MessageJson(), Context()));

        _metrics.Received(1).TrackQueueNoExecution(QueueNames.LessonLearning, "retry");
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "begin");
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "end");
    }

    [Fact]
    public async Task UnexpectedServiceException_ThrowsAndRecordsRetry()
    {
        _service.ProcessAsync(Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("storage exploded"));

        await Should.ThrowAsync<InvalidOperationException>(() => _sut.Run(MessageJson(), Context()));

        _metrics.Received(1).TrackQueueNoExecution(QueueNames.LessonLearning, "retry");
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "begin");
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "end");
    }

    [Fact]
    public async Task ContentLimitedServiceFailure_Acknowledges()
    {
        _service.ProcessAsync(Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(OperationalFailure.ContentLimited("lesson.content_limited", "invalid instruction")));

        await _sut.Run(MessageJson(), Context());

        _metrics.DidNotReceive().TrackQueueNoExecution(
            QueueNames.LessonLearning, Arg.Any<string>());
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "begin");
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "end");
    }

    [Fact]
    public async Task PermanentServiceFailure_Acknowledges()
    {
        _service.ProcessAsync(Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(OperationalFailure.Permanent("lesson.permanent", "not applicable")));

        await _sut.Run(MessageJson(), Context());

        _metrics.DidNotReceive().TrackQueueNoExecution(
            QueueNames.LessonLearning, Arg.Any<string>());
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "begin");
        _metrics.Received(1).TrackQueueInFlight(QueueNames.LessonLearning, "end");
    }
}

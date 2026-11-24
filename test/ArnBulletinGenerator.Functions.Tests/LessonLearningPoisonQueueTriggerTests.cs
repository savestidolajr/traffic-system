using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Functions.Triggers.Queues;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class LessonLearningPoisonQueueTriggerTests
{
    [Fact]
    public async Task DuplicateTerminalDeliveries_AreAcknowledgedWithoutThrowing()
    {
        var metrics = Substitute.For<IBroadcastMetrics>();
        var sut = new LessonLearningPoisonQueueTrigger(
            metrics,
            Substitute.For<ILogger<LessonLearningPoisonQueueTrigger>>());
        var context = MockHttpRequestData.CreateFunctionContext();

        await sut.RunAsync("{ malformed: true }", context);
        await sut.RunAsync("{ malformed: true }", context);

        metrics.Received(2).TrackLessonPoison("retry_exhausted");
    }
}

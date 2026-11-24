using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Constants;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Acknowledges lesson-learning messages after the queue host has exhausted retries.
///     Poison payloads are retained by Storage Queue for operator inspection; this terminal
///     handler has no lesson-store side effects, so duplicate poison deliveries are idempotent.
/// </summary>
public sealed class LessonLearningPoisonQueueTrigger(
    IBroadcastMetrics metrics,
    ILogger<LessonLearningPoisonQueueTrigger> logger)
{
    [Function("LessonLearningPoisonQueue")]
    public Task RunAsync(
        [QueueTrigger(QueueNames.LessonLearningPoison, Connection = "AzureWebJobsStorage")]
        string message,
        FunctionContext context)
    {
        logger.LogCritical(
            "[PoisonQueue] Lesson-learning message permanently failed after retries. Payload: {Payload}",
            message);
        metrics.TrackLessonPoison("retry_exhausted");
        return Task.CompletedTask;
    }
}

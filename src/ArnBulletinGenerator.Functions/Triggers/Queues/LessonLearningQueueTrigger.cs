using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Learning;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Consumes published operator edits and turns them into station-lesson store changes.
///     Malformed and retryable failures are thrown so the queue host retries and routes exhausted
///     messages to the lesson-learning poison queue. Content-limited and permanent results are
///     acknowledged without retrying.
/// </summary>
public sealed class LessonLearningQueueTrigger(
    ILessonLearningService lessonLearningService,
    ILogger<LessonLearningQueueTrigger> logger,
    IBroadcastMetrics metrics)
{
    [Function("LessonLearningQueueProcessor")]
    public async Task Run(
        [QueueTrigger(QueueNames.LessonLearning, Connection = "AzureWebJobsStorage")]
        string messageJson,
        FunctionContext context)
    {
        metrics.TrackQueueInFlight(QueueNames.LessonLearning, "begin");
        try
        {
            var parseResult = RequestParser.DeserializeQueueMessage<LessonLearningQueueMessage>(
                messageJson, logger, nameof(LessonLearningQueueTrigger));
            if (parseResult.IsFailure)
            {
                metrics.TrackQueueNoExecution(QueueNames.LessonLearning, "malformed");
                throw new InvalidOperationException(
                    $"Malformed lesson-learning queue message: {parseResult.Error}");
            }

            var message = parseResult.Value;
            logger.LogInformation(
                "[LessonLearning] Processing published edit. JobId: {JobId}, Station: {Station}",
                message.JobId, message.Station);

            Result result;
            try
            {
                result = await lessonLearningService.ProcessAsync(message, context.CancellationToken);
            }
            catch (Exception ex)
            {
                metrics.TrackQueueNoExecution(QueueNames.LessonLearning, "retry");
                logger.LogWarning(ex,
                    "[LessonLearning] Lesson learning failed; throwing so the queue retries.");
                throw;
            }

            if (result.IsFailure)
            {
                try
                {
                    QueueFailurePolicy.ClassifyOrThrow(
                        result, message.JobId, nameof(LessonLearningQueueTrigger));
                }
                catch
                {
                    metrics.TrackQueueNoExecution(QueueNames.LessonLearning, "retry");
                    throw;
                }
            }
        }
        finally
        {
            metrics.TrackQueueInFlight(QueueNames.LessonLearning, "end");
        }
    }
}

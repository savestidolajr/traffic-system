using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Constants;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Timers;

/// <summary>
///     Samples Azure Storage queue depth and oldest-ready-message age every five minutes.
///     The generic depth metric covers every queue for operations and poison-queue alerts;
///     generation-capacity metrics are deliberately emitted only for the broadcast and
///     regeneration ready queues. Missing queues or failed reads are not represented as zero.
/// </summary>
public sealed class QueueDepthMetricsTrigger(
    QueueServiceClient queueServiceClient,
    IBroadcastMetrics metrics,
    ILogger<QueueDepthMetricsTrigger> logger)
{
    private const int ReadyPeekLimit = 32;

    // Keep this list derived from QueueNames, including lesson and poison queues. Only
    // GenerationQueueNames below can produce the autoscale inputs.
    private static readonly string[] QueueNamesToSample =
    [
        QueueNames.Broadcast,
        QueueNames.BroadcastPoison,
        QueueNames.Regeneration,
        QueueNames.RegenerationPoison,
        QueueNames.LessonLearning,
        QueueNames.LessonLearningPoison,
        QueueNames.ActivityAudit,
        QueueNames.ActivityAuditPoison
    ];

    private static readonly HashSet<string> GenerationQueueNames =
        new(StringComparer.Ordinal)
        {
            QueueNames.Broadcast,
            QueueNames.Regeneration
        };

    [Function("QueueDepthMetrics")]
    public async Task Run(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timerInfo,
        FunctionContext functionContext)
    {
        var cancellationToken = functionContext.CancellationToken;
        int? broadcastReadyDepth = null;
        double? broadcastOldestAge = null;
        int? regenerationReadyDepth = null;
        double? regenerationOldestAge = null;

        foreach (var queueName in QueueNamesToSample)
        {
            QueueClient queueClient = queueServiceClient.GetQueueClient(queueName);
            try
            {
                if (!await queueClient.ExistsAsync(cancellationToken))
                    continue;

                var properties = await queueClient.GetPropertiesAsync(cancellationToken);
                metrics.TrackQueueDepth(queueName, properties.Value.ApproximateMessagesCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Host shutdown — let it propagate as cancellation, not failure.
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Queue-depth sample failed for {QueueName}", queueName);
                continue;
            }

            try
            {
                // Peek does not alter visibility or dequeue counts. Azure caps this operation at
                // 32 messages; that is enough to distinguish the autoscale threshold (>4) while
                // retaining an honest lower bound when a burst is larger than the cap.
                var peeked = await queueClient.PeekMessagesAsync(ReadyPeekLimit, cancellationToken);
                var messages = peeked.Value;
                int readyDepth = messages.Length;
                double oldestAge = OldestAgeSeconds(messages);
                metrics.TrackQueueOldestAge(queueName, oldestAge);

                // These two signals are the only queue metrics consumed by generation autoscale.
                // Lesson, activity-audit, and every poison queue are intentionally excluded.
                if (GenerationQueueNames.Contains(queueName))
                {
                    metrics.TrackQueueReadyDepth(queueName, readyDepth);
                    if (queueName == QueueNames.Broadcast)
                    {
                        broadcastReadyDepth = readyDepth;
                        broadcastOldestAge = oldestAge;
                    }
                    else if (queueName == QueueNames.Regeneration)
                    {
                        regenerationReadyDepth = readyDepth;
                        regenerationOldestAge = oldestAge;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A properties read already produced a valid account/depth metric. Do not turn a
                // failed peek into an invented zero, which could make autoscale scale in.
                logger.LogWarning(ex, "Queue-oldest-age sample failed for {QueueName}", queueName);
            }
        }
        if (broadcastReadyDepth == 0
            && broadcastOldestAge.HasValue
            && broadcastOldestAge.Value < 10
            && regenerationReadyDepth == 0
            && regenerationOldestAge.HasValue
            && regenerationOldestAge.Value < 10)
        {
            // This is a positive eligibility pulse, not a zero-valued heartbeat. If either queue
            // was unavailable, no pulse is emitted and autoscale must not scale in.
            metrics.TrackGenerationScaleInEligible();
        }
    }

    private static double OldestAgeSeconds(
        IReadOnlyList<Azure.Storage.Queues.Models.PeekedMessage> messages)
    {
        if (messages.Count == 0)
            return 0;

        DateTimeOffset oldest = messages[0].InsertedOn ?? DateTimeOffset.UtcNow;
        for (int i = 1; i < messages.Count; i++)
        {
            if (messages[i].InsertedOn is { } insertedOn && insertedOn < oldest)
                oldest = insertedOn;
        }

        return Math.Max(0, (DateTimeOffset.UtcNow - oldest).TotalSeconds);
    }
}

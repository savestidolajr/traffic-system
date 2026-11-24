using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Constants;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Observes activity audit messages that exhausted queue retries. Audit payloads are retained
///     in the poison queue for operator inspection while the failure is emitted as a metric.
/// </summary>
public sealed class ActivityAuditPoisonQueueTrigger(
    IBroadcastMetrics metrics,
    ILogger<ActivityAuditPoisonQueueTrigger> logger)
{
    [Function("ActivityAuditPoisonQueue")]
    public Task RunAsync(
        [QueueTrigger(QueueNames.ActivityAuditPoison)] string message,
        FunctionContext context)
    {
        logger.LogCritical(
            "[PoisonQueue] Activity audit message permanently failed. Payload: {Payload}", message);
        metrics.TrackPipelineStage("PoisonQueue.ActivityAudit", 0.0, "system", false);
        return Task.CompletedTask;
    }
}

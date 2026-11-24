using ArnBulletinGenerator.Domain.Constants;
using Microsoft.Azure.Functions.Worker;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
/// Consumes delivery commands after the queue host exhausts retries, marks the pending job failed,
/// and alerts operators without attempting playout again.
/// </summary>
public sealed class DeliveryPoisonQueueTrigger(PoisonQueueHandler poisonQueueHandler)
{
    [Function("DeliveryPoisonQueue")]
    public Task RunAsync(
        [QueueTrigger(QueueNames.DeliveryPoison, Connection = "AzureWebJobsStorage")] string message,
        FunctionContext context)
        => poisonQueueHandler.HandleAsync(message, "Delivery", context);
}

using ArnBulletinGenerator.Domain.Constants;
using Microsoft.Azure.Functions.Worker;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Consumes the broadcast queue's poison sub-queue, handing the failed message to
///     PoisonQueueHandler to fail the job and alert operators.
/// </summary>
public sealed class BroadcastPoisonQueueTrigger(PoisonQueueHandler poisonQueueHandler)
{
    [Function("BroadcastPoisonQueue")]
    public Task RunAsync(
        [QueueTrigger(QueueNames.BroadcastPoison)] string message,
        FunctionContext context)
        => poisonQueueHandler.HandleAsync(message, "Broadcast", context);
}

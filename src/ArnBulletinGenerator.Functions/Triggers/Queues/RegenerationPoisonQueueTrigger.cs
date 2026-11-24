using ArnBulletinGenerator.Domain.Constants;
using Microsoft.Azure.Functions.Worker;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Consumes the regeneration queue's poison sub-queue, handing the failed message to
///     PoisonQueueHandler to fail the job and alert operators.
/// </summary>
public sealed class RegenerationPoisonQueueTrigger(PoisonQueueHandler poisonQueueHandler)
{
    [Function("RegenerationPoisonQueue")]
    public Task RunAsync(
        [QueueTrigger(QueueNames.RegenerationPoison, Connection = "AzureWebJobsStorage")] string message,
        FunctionContext context)
        => poisonQueueHandler.HandleAsync(message, "Regeneration", context);
}

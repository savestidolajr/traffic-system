using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
/// Consumes durable approved/covered delivery commands. The application processor owns
/// idempotency, attempts, external delivery, and terminal status transitions; this trigger
/// only parses the wire message and applies the queue retry policy.
/// </summary>
public sealed class DeliveryQueueTrigger(
    IJobDeliveryProcessor processor,
    ILogger<DeliveryQueueTrigger> logger)
{
    [Function("DeliveryQueueProcessor")]
    public async Task Run(
        [QueueTrigger(QueueNames.Delivery, Connection = "AzureWebJobsStorage")]
        string messageJson,
        FunctionContext context)
    {
        var parseResult = RequestParser.DeserializeQueueMessage<DeliveryQueueCommand>(
            messageJson, logger, nameof(DeliveryQueueTrigger));
        if (parseResult.IsFailure)
        {
            throw new InvalidOperationException(
                $"Malformed delivery queue message: {parseResult.Error}");
        }

        var command = parseResult.Value;
        logger.LogInformation(
            "Processing delivery command {IdempotencyKey} for job {JobId}.",
            command.IdempotencyKey, command.JobId);

        var result = await processor.ProcessDeliveryAsync(command, context.CancellationToken);
        if (result.IsFailure)
            QueueFailurePolicy.ClassifyOrThrow(result, command.JobId, nameof(DeliveryQueueTrigger));
    }
}

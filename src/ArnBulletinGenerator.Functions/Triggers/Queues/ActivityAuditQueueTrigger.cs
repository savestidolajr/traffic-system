using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Consumes durable activity audit messages and records them at least once. The repository
///     receives a stable request/actor/path key so queue redelivery upserts the same table row.
/// </summary>
public sealed class ActivityAuditQueueTrigger(
    IActivityLogRepository activityLog,
    IOptions<AuditQueueOptions> options,
    ILogger<ActivityAuditQueueTrigger> logger)
{
    [Function("ActivityAuditQueueProcessor")]
    public Task Run(
        [QueueTrigger(QueueNames.ActivityAudit, Connection = "AzureWebJobsStorage")]
        string messageJson,
        FunctionContext context)
        => ProcessAsync(messageJson, context.CancellationToken);

    /// <summary>Processes one message without requiring a Functions host context.</summary>
    public async Task ProcessAsync(string messageJson, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogWarning("Activity audit queue message ignored because Audit:Queue:Enabled is false.");
            return;
        }

        var parseResult = RequestParser.DeserializeQueueMessage<ActivityAuditMessage>(
            messageJson, logger, nameof(ActivityAuditQueueTrigger));
        if (parseResult.IsFailure)
            throw new InvalidOperationException($"Malformed activity audit queue message: {parseResult.Error}");

        var message = parseResult.Value;
        if (string.IsNullOrWhiteSpace(message.RequestId))
            throw new InvalidOperationException(
                "Malformed activity audit queue message: RequestId is required for idempotent delivery.");
        var entry = new ActivityLogEntry(
            message.AtUtc,
            message.ActorName,
            message.ActorObjectId,
            message.Method,
            message.Path,
            message.Resource,
            message.EntityId,
            message.StatusCode,
            message.RequestBodyJson,
            message.RequestId);
        var result = await activityLog.RecordAsync(entry, cancellationToken);
        if (result.IsFailure)
            throw new InvalidOperationException($"Activity audit record failed: {result.Error}");

        logger.LogInformation("Recorded activity audit for {Method} {Path} ({RequestId})",
            message.Method, message.Path, message.RequestId);
    }
}

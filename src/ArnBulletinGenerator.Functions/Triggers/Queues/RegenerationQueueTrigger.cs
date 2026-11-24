using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Consumes the regeneration queue, skipping messages whose job left Requested status
///     (duplicate delivery) before orchestrating and classifying failures.
/// </summary>
public sealed class RegenerationQueueTrigger(
    IBroadcastOrchestrator orchestrator,
    IJobRepository jobRepository,
    ILogger<RegenerationQueueTrigger> logger,
    IJobClaimRepository? claimRepository = null,
    IOptions<QueueClaimOptions>? claimOptions = null,
    IBroadcastMetrics? metrics = null)
{
    [Function("RegenerationQueueProcessor")]
    public async Task Run(
        [QueueTrigger(QueueNames.Regeneration, Connection = "AzureWebJobsStorage")]
        string messageJson,
        FunctionContext context)
    {
        var cancellationToken = context.CancellationToken;
        metrics?.TrackQueueInFlight(QueueNames.Regeneration, "begin");
        JobClaim? activeClaim = null;
        try
        {

        var parseResult =
            RequestParser.DeserializeQueueMessage<RegenerationQueueMessage>(messageJson, logger,
                nameof(RegenerationQueueTrigger));
        if (parseResult.IsFailure)
        {
            metrics?.TrackQueueNoExecution(QueueNames.Regeneration, "malformed");
            throw new InvalidOperationException(
                $"Malformed regeneration queue message: {parseResult.Error}");
        }

        var message = parseResult.Value;
        logger.LogInformation("Processing queued regeneration. JobId: {JobId}, Location: {Location}",
            message.JobId, message.Location);
        if (claimOptions?.Value.Enabled == true && claimRepository is not null)
        {
            var claimResult = await claimRepository.TryClaimAsync(
                message.JobId, context.InvocationId, TimeSpan.FromMinutes(8), cancellationToken);
            if (claimResult.IsFailure)
            {
                metrics?.TrackQueueNoExecution(QueueNames.Regeneration, "claim_failed");
                throw new InvalidOperationException(
                    $"Claim failed for job {message.JobId}: {claimResult.Error}");
            }

            activeClaim = claimResult.Value;
            if (activeClaim is null)
            {
                metrics?.TrackQueueNoExecution(QueueNames.Regeneration, "claim_active");
                return;
            }
        }

        // Deduplication — unlike BroadcastQueueTrigger (where the job must not
        // exist yet), a regeneration's job ALWAYS exists: the HTTP handler reset
        // it to Requested before queueing. "Requested" is therefore the expected
        // pre-state; any other status means orchestration already ran for this
        // message (duplicate delivery) and it must be skipped. A failed lookup
        // (transient outage) must NOT be treated as "not a duplicate"; throw so
        // the queue retries instead of producing a duplicate regeneration.
        var existingJob = await jobRepository.GetJobAsync(message.JobId, cancellationToken);
        if (existingJob.IsSuccess && existingJob.Value is not null &&
            existingJob.Value.Status != Domain.Models.JobStatus.Requested)
        {
            logger.LogWarning(
                "Regeneration for {JobId} already processed (status {Status}). Skipping.",
                message.JobId, existingJob.Value.Status);
            metrics?.TrackQueueNoExecution(QueueNames.Regeneration, "already_processed");
            return;
        }
        if (existingJob.IsFailure && existingJob.Error != Domain.Errors.ErrorMessages.Job.NotFound)
        {
            logger.LogError(
                "Dedup lookup failed for {JobId} ({Error}). Throwing so the queue retries.",
                message.JobId, existingJob.Error);
            metrics?.TrackQueueNoExecution(QueueNames.Regeneration, "job_lookup_failed");
            throw new InvalidOperationException(
                $"Dedup lookup failed for job {message.JobId}: {existingJob.Error}");
        }

        var result = await orchestrator.OrchestrateAsync(message.ToBroadcastWithTraffic(), cancellationToken);

        if (result.IsFailure)
            QueueFailurePolicy.ClassifyOrThrow(result, message.JobId, nameof(RegenerationQueueTrigger));
        else
            logger.LogInformation("Regeneration completed for JobId {JobId}. File: {FileName}",
                message.JobId, result.Value.FileName);
        }
        finally
        {
            if (activeClaim is not null && claimRepository is not null)
            {
                var release = await claimRepository.ReleaseAsync(activeClaim, CancellationToken.None);
                if (release.IsFailure)
                    logger.LogWarning("Failed to release queue claim for {JobId}: {Error}.",
                        activeClaim.JobId, release.Error);
            }
            metrics?.TrackQueueInFlight(QueueNames.Regeneration, "end");
        }
    }
}

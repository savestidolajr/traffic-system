using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Consumes the broadcast queue, deduplicating redelivered or stale-worker messages before
///     orchestrating and classifying failures for retry or poison routing.
/// </summary>
public sealed class BroadcastQueueTrigger(
    IBroadcastOrchestrator orchestrator,
    IJobRepository jobRepository,
    ILogger<BroadcastQueueTrigger> logger,
    IJobClaimRepository? claimRepository = null,
    IOptions<QueueClaimOptions>? claimOptions = null,
    IBroadcastMetrics? metrics = null)
{
    private const int ProcessingLeaseMinutes = 8;

    [Function("BroadcastQueueProcessor")]
    public async Task Run(
        [QueueTrigger(QueueNames.Broadcast, Connection = "AzureWebJobsStorage")]
        string messageJson,
        FunctionContext context)
    {
        var ct = context.CancellationToken;
        metrics?.TrackQueueInFlight(QueueNames.Broadcast, "begin");
        JobClaim? activeClaim = null;
        try
        {
            var parseResult =
                RequestParser.DeserializeQueueMessage<BroadcastQueueMessage>(messageJson, logger,
                    nameof(BroadcastQueueTrigger));
            if (parseResult.IsFailure)
            {
                metrics?.TrackQueueNoExecution(QueueNames.Broadcast, "malformed");
                throw new InvalidOperationException(
                    $"Malformed broadcast queue message: {parseResult.Error}");
            }

        var message = parseResult.Value;
        logger.LogInformation("Processing broadcast. JobId: {JobId}, Market: {Market}", message.JobId,
            message.MarketId);
        if (claimOptions?.Value.Enabled == true && claimRepository is not null)
        {
            var claimResult = await claimRepository.TryClaimAsync(
                message.JobId, context.InvocationId, TimeSpan.FromMinutes(ProcessingLeaseMinutes), ct);
            if (claimResult.IsFailure)
            {
                metrics?.TrackQueueNoExecution(QueueNames.Broadcast, "claim_failed");
                throw new InvalidOperationException(
                    $"Claim failed for job {message.JobId}: {claimResult.Error}");
            }

            activeClaim = claimResult.Value;
            if (activeClaim is null)
            {
                metrics?.TrackQueueNoExecution(QueueNames.Broadcast, "claim_active");
                return;
            }
        }
        // Lease-based deduplication. Protects against queue message redelivery
        // while allowing stale/crashed workers to be reprocessed.
        //
        // Done/waiting statuses (WaitingApproval, Approved, Published, FallbackUsed, Rejected, Missed):
        //   always skip — these are final or operator-facing states.
        // AwaitingCuration is admitted only for the explicit Generate resume phase; a FetchAndGenerate
        // redelivery is stale and must not accidentally regenerate an operator-visible draft.
        // In-flight statuses (HereFetched, IncidentsSelected, ScriptReady):
        //   skip if LastUpdatedAt is within ProcessingLeaseMinutes (active worker).
        //   Reprocess if older (stale/crashed worker).
        // Requested and Failed: always reprocess.
        // NotFound: process normally (schedule poller path).
        // Transient lookup failure: throw so the queue retries.
        var existingJob = await jobRepository.GetJobAsync(message.JobId, ct);
        if (existingJob.IsSuccess && existingJob.Value is not null)
        {
            var job = existingJob.Value;
            var status = job.Status;
            if (status == JobStatus.AwaitingCuration && message.Phase != Phase.Generate)
            {
                logger.LogInformation(
                    "Skipping job {JobId}: awaiting operator curation; phase={Phase}.",
                    message.JobId, message.Phase);
                metrics?.TrackQueueNoExecution(QueueNames.Broadcast, "awaiting_curation");
                return;
            }

            if (message.Phase == Phase.Generate
                && status is JobStatus.AwaitingCuration or JobStatus.IncidentsSelected)
            {
                // Explicit resume messages are already conditionally claimed by the watchdog or
                // operator action; do not apply the normal in-flight lease guard.
            }
            else
                // Done/waiting statuses — always skip
                if (status is JobStatus.WaitingApproval or JobStatus.Approved
                    or JobStatus.Published or JobStatus.FallbackUsed or JobStatus.Rejected or JobStatus.Missed)
                {
                    logger.LogInformation(
                        "Skipping job {JobId}: already in final/operator state {Status}.",
                        message.JobId, status);
                    metrics?.TrackQueueNoExecution(QueueNames.Broadcast, "already_final");
                    return;
                }

            if (message.Phase != Phase.Generate
                && status is (JobStatus.HereFetched or JobStatus.IncidentsSelected or JobStatus.ScriptReady))
            {
                var leaseExpiry = job.LastUpdatedAt.AddMinutes(ProcessingLeaseMinutes);
                if (DateTime.UtcNow < leaseExpiry)
                {
                    logger.LogWarning(
                        "Skipping job {JobId}: still within processing lease (status={Status}, lastUpdated={LastUpdated}).",
                        message.JobId, status, job.LastUpdatedAt);
                    metrics?.TrackQueueNoExecution(QueueNames.Broadcast, "lease_active");
                    return;
                }

                logger.LogWarning(
                    "Reprocessing job {JobId}: lease expired (status={Status}, lastUpdated={LastUpdated}).",
                    message.JobId, status, job.LastUpdatedAt);
                // Fall through to reprocess
            }

            // Requested or Failed — reprocess (fall through)
        }
        if (existingJob.IsFailure && existingJob.Error != Domain.Errors.ErrorMessages.Job.NotFound)
        {
            logger.LogError(
                "Lease lookup failed for {JobId} ({Error}). Throwing so the queue retries.",
                message.JobId, existingJob.Error);
            metrics?.TrackQueueNoExecution(QueueNames.Broadcast, "job_lookup_failed");
            throw new InvalidOperationException(
                $"Lease lookup failed for job {message.JobId}: {existingJob.Error}");
        }

        // A message with unusable coverage is corrupt — throw so the host's retry
        // policy routes it to the poison queue rather than silently dropping it.
        var requestResult = message.ToBroadcastRequest();
        if (requestResult.IsFailure)
        {
            metrics?.TrackQueueNoExecution(QueueNames.Broadcast, "corrupt");
            throw new InvalidOperationException(
                $"Corrupt broadcast queue message for job {message.JobId}: {requestResult.Error}");
        }

        var result = await orchestrator.OrchestrateAsync(requestResult.Value, message.Phase, ct);
        if (result.IsFailure)
            QueueFailurePolicy.ClassifyOrThrow(result, message.JobId, nameof(BroadcastQueueTrigger));
        else
            logger.LogInformation("Broadcast completed for JobId {JobId}. File: {FileName}", message.JobId,
                result.Value.FileName);
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
            metrics?.TrackQueueInFlight(QueueNames.Broadcast, "end");
        }
    }
}

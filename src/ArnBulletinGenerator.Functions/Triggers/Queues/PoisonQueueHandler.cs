using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Metrics;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Shared poison-queue handler that marks a job Failed with an audit entry and sends a
///     critical alert when a message exhausts its retries.
/// </summary>
public sealed class PoisonQueueHandler(
    IJobRepository jobRepository,
    INotificationService notificationService,
    TimeProvider timeProvider,
    IBroadcastMetrics metrics,
    ILogger<PoisonQueueHandler> logger)
{
    public async Task HandleAsync(
        string message,
        string pipelineName,
        FunctionContext context)
    {
        logger.LogCritical(
            "[PoisonQueue] {PipelineName} message permanently failed. Payload: {Payload}", pipelineName, message);
        metrics.TrackPipelineStage($"PoisonQueue.{pipelineName}", 0.0, "system", false);

        string? jobId = TryExtractJobId(message);

        if (jobId is not null)
        {
            var jobResult = await jobRepository.GetJobAsync(jobId, context.CancellationToken);
            if (jobResult.IsSuccess && jobResult.Value is not null)
            {
                var job = jobResult.Value;
                // Fail through the domain state machine so the audit trail carries the
                // error, the pre-failure status, and a StatusHistory entry. A terminal
                // job (e.g. a stale poison message for an already-published bulletin)
                // is left untouched.
                var failResult = job.MarkFailed(
                    $"{pipelineName} message failed after max dequeue attempts (poison queue).",
                    timeProvider.GetUtcNow().UtcDateTime);
                if (failResult.IsFailure)
                {
                    logger.LogWarning(
                        "Poison-queue job {JobId} is already terminal ({Status}); leaving it untouched.",
                        jobId, job.Status);
                }
                else
                {
                    var saveResult = await jobRepository.SaveJobAsync(job, context.CancellationToken);
                    if (saveResult.IsFailure)
                        logger.LogError("Failed to mark poison-queue job {JobId} as Failed: {Error}",
                            jobId, saveResult.Error);
                }
            }

            await notificationService.SendAlertNotificationAsync(
                "System", $"{pipelineName} Pipeline",
                $"{pipelineName} job {jobId} failed after max dequeue attempts. Check poison queue.",
                AlertSeverity.Critical,
                CancellationToken.None);
        }
        else
        {
            logger.LogError(
                "Could not parse job id from poison-queue message. Payload (truncated): {Payload}",
                message.Length > 500 ? message[..500] : message);

            await notificationService.SendAlertNotificationAsync(
                "System", $"{pipelineName} Pipeline",
                $"{pipelineName} message failed and job ID could not be parsed from poison queue message.",
                AlertSeverity.Critical,
                CancellationToken.None);
        }
    }

    private static string? TryExtractJobId(string message)
    {
        try
        {
            var parsed = System.Text.Json.JsonSerializer
                .Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(message);
            if (parsed is null) return null;
            if (!parsed.TryGetValue("jobId", out var jobIdEl)
                && !parsed.TryGetValue("JobId", out jobIdEl))
                return null;
            if (jobIdEl.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            string? value = jobIdEl.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}

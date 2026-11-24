using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Owns every job-status transition, read-modify-write persistence, and log-line append used
///     across the broadcast pipeline. The other collaborators and the shell reach here whenever
///     they need job persistence/logging, so this class deliberately has no dependency on any of
///     them. Extracted from <see cref="BroadcastOrchestrator" />.
/// </summary>
internal sealed class JobStatusUpdater(
    IJobRepository jobRepository,
    INotificationService notificationService,
    TimeProvider timeProvider,
    ILogger<BroadcastOrchestrator> logger,
    IGenerationProgressReporterFactory? progressReporterFactory = null)
{
    private static readonly Regex DurationPattern = new(@"Actual duration ([\d.]+)s", RegexOptions.Compiled);

    internal async Task UpdateJobStatus(string jobId, JobStatus status, string message,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[StatusTransition] Job {JobId} transitioning to {NewStatus} — {Message}",
            jobId, status, message);

        bool failedTransitionApplied = false;
        Result updateResult;
        if (jobRepository is IJobStatusRepository statusRepository)
        {
            var targeted = await statusRepository.UpdateStatusAsync(
                jobId, status, "system", message, cancellationToken);
            updateResult = targeted;
            failedTransitionApplied = targeted.IsSuccess
                && targeted.Value.PreviousStatus != JobStatus.Failed
                && targeted.Value.CurrentStatus == JobStatus.Failed;
        }
        else
        {
            updateResult = await UpdateJobAsync(jobId, job =>
            {
                if (status == JobStatus.Failed && job.Status != JobStatus.Failed)
                {
                    logger.LogWarning(
                        "[StatusTransition] Job {JobId} FAILED at status {FailedAtStatus}",
                        jobId, job.Status);
                    job.MarkFailed(message, timeProvider.GetUtcNow().UtcDateTime);
                    failedTransitionApplied = true;
                    return;
                }

                job.TransitionTo(status, "system",
                    string.IsNullOrWhiteSpace(message) ? default : Option<string>.Some(message),
                    timeProvider.GetUtcNow().UtcDateTime);
            }, message, cancellationToken);
        }

        if (updateResult.IsSuccess && progressReporterFactory?.TryGet(jobId) is { } progress)
        {
            try
            {
                await progress.StatusChanged(status);
            }
            catch (Exception exception)
            {
                // Progress is diagnostic only. A reporter outage must not turn a status transition
                // that already succeeded into a failed broadcast.
                logger.LogWarning(exception,
                    "[StatusTransition] Job {JobId} progress status update failed", jobId);
            }
        }

        // Failure notification (SoW: "failure notifications") — the poison-queue
        // trigger alerts on retry exhaustion, but a DETERMINISTIC failure (stale
        // traffic, synthesis error, content-limited with no fallback) previously
        // ended silent. Notify exactly once, on the transition itself.
        if (updateResult.IsSuccess && failedTransitionApplied)
        {
            var failedJob = await jobRepository.GetJobAsync(jobId, cancellationToken);
            if (failedJob.IsSuccess)
                await notificationService.SendJobNotificationAsync(failedJob.Value, NotificationKind.FailedNotCovered,
                    cancellationToken);
        }
    }

    internal async Task<Result> UpdateJobAsync(string jobId, Action<Job> updateAction, string? logMessage,
        CancellationToken cancellationToken)
    {
        var jobResult = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (!jobResult.IsSuccess || jobResult.Value.Status == JobStatus.FallbackUsed)
            return Result.Success();

        var job = jobResult.Value;
        updateAction(job);
        job.LastUpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        if (logMessage != null)
            job.AddLog(new JobLog(timeProvider.GetUtcNow().UtcDateTime, JobLogLevel.Info, logMessage));

        var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveResult.IsFailure)
        {
            logger.LogWarning("[UpdateJob] Job {JobId} failed to save: {Error}", jobId, saveResult.Error);
            return Result.Failure(saveResult.Error);
        }

        return Result.Success();
    }

    internal async Task LogJobAsync(string jobId, string message, CancellationToken cancellationToken)
    {
        await jobRepository.AddLogAsync(jobId, message, JobLogLevel.Info, cancellationToken);
    }

    internal async Task MarkJobFailedAsync(string jobId, string error, CancellationToken cancellationToken)
    {
        logger.LogError("[Failure] Job {JobId} — {Error}", jobId, error);
        await UpdateJobStatus(jobId, JobStatus.Failed, $"Failed: {error}", cancellationToken);
    }

    internal static Option<double> ExtractDurationFromError(string error)
    {
        var match = DurationPattern.Match(error);
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double duration)
            ? Option<double>.Some(duration)
            : Option<double>.None;
    }
}

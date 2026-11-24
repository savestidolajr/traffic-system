using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Functions.Triggers.Queues;

/// <summary>
///     Pure Functions-boundary policy for classifying queue orchestration failures.
///     Both <see cref="BroadcastQueueTrigger" /> and <see cref="RegenerationQueueTrigger" />
///     use this to decide whether to acknowledge (return, no throw) or retry (throw for host redelivery).
/// </summary>
internal static class QueueFailurePolicy
{
    /// <summary>
    ///     Classifies an orchestrator failure by its typed metadata and either returns
    ///     (acknowledge, no retry) or throws (host retry, eventual poison routing).
    /// </summary>
    /// <param name="failedResult">The failed Result carrying optional typed failure metadata.</param>
    /// <param name="jobId">Job identifier for exception context.</param>
    /// <param name="caller">Trigger name for exception context (e.g. "Broadcast", "Regeneration").</param>
    internal static void ClassifyOrThrow(Result failedResult, string jobId, string caller)
    {
        if (failedResult.IsSuccess)
            throw new InvalidOperationException("ClassifyOrThrow called on a successful result.");

        if (failedResult.FailureMetadata.HasValue)
        {
            switch (failedResult.FailureMetadata.Value.Disposition)
            {
                case FailureDisposition.ContentLimited:
                case FailureDisposition.SafetyRejected:
                case FailureDisposition.Conflict:
                case FailureDisposition.Permanent:
                    return;

                case FailureDisposition.Cancelled:
                    throw new OperationCanceledException(
                        $"Cancelled {caller} for job {jobId}: {failedResult.Error}");

                case FailureDisposition.Retryable:
                case FailureDisposition.InvalidInput:
                default:
                    throw new InvalidOperationException(
                        $"Transient {caller} failure for job {jobId}: {failedResult.Error}");
            }
        }

        // Legacy untyped failure — no metadata. Throw so the host retries and
        // eventually routes to poison.
        throw new InvalidOperationException(
            $"Transient {caller} failure for job {jobId}: {failedResult.Error}");
    }
}

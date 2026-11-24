using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Applies a lifecycle status transition with a sparse ETag-guarded table merge.</summary>
public interface IJobStatusRepository
{
    Task<Result<JobStatusUpdateResult>> UpdateStatusAsync(
        string jobId,
        JobStatus status,
        string by,
        string? message,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Atomically claims the reviewed revision for asynchronous delivery. A repeated claim with
    ///     the same command key is an idempotent success; a different revision or command conflicts.
    /// </summary>
    Task<Result<JobStatusUpdateResult>> TryClaimDeliveryAsync(
        string jobId,
        string reviewedEtag,
        DeliveryQueueCommand command,
        CancellationToken cancellationToken = default);
}

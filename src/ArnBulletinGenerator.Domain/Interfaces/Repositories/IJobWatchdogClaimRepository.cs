using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Atomically claims a job before a watchdog sweep performs non-idempotent work.</summary>
public interface IJobWatchdogClaimRepository
{
    /// <summary>
    ///     Attempts to persist a short lease after revalidating the job's loaded ETag. The result
    ///     is successful with <see langword="true" /> only when this caller committed the
    ///     conditional update; an active lease or concurrent winner is a successful
    ///     <see langword="false" /> result. A minimum age is supplied for stale-job recovery, where
    ///     touching a recently active worker would be incorrect. Unlike the general save path,
    ///     this operation never reconciles and retries a 412 because doing so would make two
    ///     watchdog sweeps both believe they owned the job.
    /// </summary>
    Task<Result<bool>> TryClaimWatchdogJobAsync(
        Job job,
        TimeSpan? minimumAge = null,
        CancellationToken cancellationToken = default);
}

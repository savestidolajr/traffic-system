using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Atomically claims a queued job so duplicate deliveries cannot run concurrently.</summary>
public interface IJobClaimRepository
{
    /// <summary>
    ///     Attempts to claim <paramref name="jobId" /> for <paramref name="owner" />. A fresh claim
    ///     held by another owner is represented by a successful result containing <see langword="null" />.
    /// </summary>
    Task<Result<JobClaim?>> TryClaimAsync(
        string jobId,
        string owner,
        TimeSpan lease,
        CancellationToken cancellationToken = default);

    /// <summary>Renews a claim using its current ETag.</summary>
    Task<Result> RenewAsync(JobClaim claim, CancellationToken cancellationToken = default);

    /// <summary>Releases a claim using its current ETag.</summary>
    Task<Result> ReleaseAsync(JobClaim claim, CancellationToken cancellationToken = default);
}

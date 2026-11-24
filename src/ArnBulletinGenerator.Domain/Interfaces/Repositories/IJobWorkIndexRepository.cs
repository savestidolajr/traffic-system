using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Persists the bounded scalar work projection consumed by watchdog sweeps.</summary>
public interface IJobWorkIndexRepository
{
    Task<Result<JobWorkIndexPage>> ReadPageAsync(
        JobWorkKind kind,
        DateTime bucketUtc,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Result> UpsertAsync(JobWorkItem item, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(JobWorkItem item, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<JobWorkItem>>> FindByJobIdAsync(
        string jobId, CancellationToken cancellationToken = default);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Bounded candidate/recheck/delete seam used by the retention policy service.</summary>
public interface IJobRetentionStore
{
    Task<Result<JobRetentionPage>> ReadPageAsync(
        DateTimeOffset olderThanUtc,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Result<JobRetentionCandidate>> RecheckAsync(
        string jobId, CancellationToken cancellationToken = default);

    /// <summary>Deletes only artifacts for a row that still carries the supplied ETag.</summary>
    Task<Result> DeleteAsync(
        JobRetentionCandidate candidate, CancellationToken cancellationToken = default);

    /// <summary>Finds dangling index/read-model/blob references without deleting them.</summary>
    Task<Result<int>> ReconcileOrphansAsync(
        DateTimeOffset olderThanUtc, int pageSize,
        bool deleteOrphans = false,
        CancellationToken cancellationToken = default);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Stores immutable compressed payloads that do not belong on the hot job row.</summary>
public interface IJobPayloadRepository
{
    Task<Result<IReadOnlyDictionary<JobPayloadKind, string>>> ReadAsync(
        string jobId, CancellationToken cancellationToken = default);
    /// <summary>
    ///     Reads only the immutable rows named by the aggregate's references. Implementations
    ///     should point-read these rows; the default preserves compatibility with migration
    ///     adapters that only expose the bounded per-job read.
    /// </summary>
    Task<Result<IReadOnlyDictionary<JobPayloadKind, string>>> ReadAsync(
        string jobId,
        IReadOnlyDictionary<JobPayloadKind, string> references,
        CancellationToken cancellationToken = default)
        => ReadAsync(jobId, cancellationToken);

    Task<Result> SaveAsync(
        string jobId, IReadOnlyDictionary<JobPayloadKind, string> payloads,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string jobId, CancellationToken cancellationToken = default);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Maintains the bounded scalar index for list/history reads.</summary>
public interface IJobReadIndexRepository
{
    Task<Result> UpsertAsync(JobReadIndexItem item, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(JobReadIndexItem item, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<JobReadIndexItem>>> FindByJobIdAsync(
        string jobId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<JobReadIndexItem>>> ReadPageAsync(
        JobListQuery query, CancellationToken cancellationToken = default);
    Task<Result<JobReadIndexRetentionPage>> ReadRetentionPageAsync(
        DateTimeOffset olderThanUtc,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default);
}

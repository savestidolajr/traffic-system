using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Appends and reads the split job lifecycle/log history rows.</summary>
public interface IJobTimelineRepository
{
    Task<Result<JobTimelineSnapshot>> ReadAsync(
        string jobId, int pageSize, CancellationToken cancellationToken = default);

    Task<Result> AppendAsync(
        string jobId, IReadOnlyList<JobTimelineEntry> entries,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(string jobId, CancellationToken cancellationToken = default);
}

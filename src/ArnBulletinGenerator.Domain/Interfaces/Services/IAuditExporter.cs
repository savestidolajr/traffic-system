using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Snapshots the job store to a durable dated compliance export, independent of the operator-facing storage backend.</summary>
public interface IAuditExporter
{
    /// <summary>
    ///     Snapshots the job store (scripts, approvals, rejections, publish evidence)
    ///     to a durable dated export. Idempotent per day — re-running overwrites the
    ///     same snapshot. Returns the number of exported records.
    /// </summary>
    Task<Result<int>> ExportJobsAsync(CancellationToken cancellationToken = default);
}

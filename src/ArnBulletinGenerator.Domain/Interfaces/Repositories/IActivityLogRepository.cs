using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Cross-cutting change/access audit trail — who mutated what admin/operator config,
///     when, and with what request body. Written by <c>ActivityLoggingMiddleware</c>,
///     read by the <c>/api/activity</c> compliance surface.
/// </summary>
public interface IActivityLogRepository
{
    Task<Result> RecordAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Matching entries, newest first.</summary>
    Task<Result<IReadOnlyList<ActivityLogEntry>>> QueryAsync(
        ActivityLogQuery query, CancellationToken cancellationToken = default);
}

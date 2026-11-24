namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Filter/paging parameters for querying the activity log. <see cref="CursorRowKey" /> is an
///     optional opaque continuation tiebreaker paired with <see cref="ToUtc" />; null preserves
///     the legacy timestamp-only cursor used by existing clients.
/// </summary>
public sealed record ActivityLogQuery(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string? Resource,
    string? EntityId,
    int Limit,
    string? CursorRowKey = null);

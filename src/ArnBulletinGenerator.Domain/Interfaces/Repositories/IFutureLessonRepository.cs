using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
/// Explicit, station-scoped editorial lessons — taught by operators or inferred from published
/// script edits — replayed into later generations until they expire. Operational rejection
/// notes are never stored here.
/// </summary>
public interface IFutureLessonRepository
{
    /// <summary>Adds a lesson or merges it with the same normalized category and text.</summary>
    Task<Result> AddAsync(FutureLessonEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Newest active lessons for a station, excluding expired rows.</summary>
    Task<Result<IReadOnlyList<FutureLessonEntry>>> GetRecentAsync(
        string station,
        int maxEntries,
        CancellationToken cancellationToken = default);

    /// <summary>Dismisses one lesson from a station; missing rows are already dismissed.</summary>
    Task<Result> DeleteAsync(
        string station,
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>Sets a lesson's status; missing rows are a no-op success.</summary>
    Task<Result> SetStatusAsync(
        string station,
        string id,
        FutureLessonStatus status,
        CancellationToken cancellationToken = default);
}

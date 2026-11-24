using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces.Repositories;

/// <summary>
///     Persistent repository for caching static HERE segment road attributes (road name, functional class,
///     ramp flag, travel direction, physical lanes) in Azure Table Storage across instances and cold starts.
/// </summary>
public interface IMapAttributesCacheRepository
{
    /// <summary>
    ///     Fetches cached segment road attributes for the specified segment references.
    ///     Returns a dictionary of resolved segments keyed by the segment reference.
    /// </summary>
    Task<IReadOnlyDictionary<string, SegmentRoad>> GetBatchAsync(
        IReadOnlyList<string> segmentRefs, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Persists a batch of resolved segment road attributes.
    /// </summary>
    Task SaveBatchAsync(
        IReadOnlyDictionary<string, SegmentRoad> segmentRoads, CancellationToken cancellationToken = default);
}

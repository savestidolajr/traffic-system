using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Resolves what the map says about specific road segments — the road identity that belongs to an
///     incident's OWN geometry, rather than whatever happens to be near its coordinates.
///     <para>
///         Fail-soft by contract: a failure means "no segment knowledge", and callers keep the name
///         they already had. It must never be a reason to fail a bulletin.
///     </para>
/// </summary>
public interface IMapAttributesProvider
{
    /// <summary>
    ///     Looks up the named segments, returning only those the map could resolve, keyed by the
    ///     segment identifier as passed in.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, SegmentRoad>>> GetSegmentRoadsAsync(
        IEnumerable<string> segmentRefs, CancellationToken cancellationToken = default);
}

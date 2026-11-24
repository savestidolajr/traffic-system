namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     What the map says about one road segment, keyed to the segment an incident itself references.
///     <para>
///         This is the only road identity HERE gives us that is tied to the incident's own geometry.
///         Reverse geocoding answers "what is near this point", which is how a closure on a local road
///         20 m from a motorway aired as a closed motorway (Sydney, 2026-08-13).
///     </para>
/// </summary>
/// <param name="Name">
///     The road's name with any leading route code stripped ("A28 / Hume Hwy" → "Hume Hwy"), or null
///     when the map has no name for the segment.
/// </param>
/// <param name="FunctionalClass">The map's own road class, 1 (motorway) to 5 (local).</param>
/// <param name="IsRamp">True when the segment is a ramp rather than a through carriageway.</param>
/// <param name="TravelDirection">
///     The map's <c>TRAVEL_DIRECTION</c>: <c>F</c>/<c>T</c> for a one-way carriageway, <c>B</c> for
///     bidirectional, null when the map did not say. Fetched so a direction claim can be gated on the
///     road being one-way rather than on a bearing guess.
/// </param>
/// <param name="PhysicalLaneCount">The map's <c>PHYSICAL_NUM_LANES</c>, or null. HERE's flow feed
///     reports no lane count anywhere in Australia (0 of 28,673 rows), so the map is the only source.</param>
public readonly record struct SegmentRoad(
    string? Name,
    int FunctionalClass,
    bool IsRamp,
    string? TravelDirection = null,
    int? PhysicalLaneCount = null);

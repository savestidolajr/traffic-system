namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     What a reverse geocode actually resolved at a coordinate, with the ROAD and the SUBURB kept
///     apart.
///     <para>
///         Collapsing them into one string is what put suburbs on air as roads: HERE returns a bare
///         house number or a route code as the "street" for an isolated stretch, the old fallback
///         substituted the district, and the pipeline had no way to tell that "Arundel",
///         "Greensborough" or "Laverton" were localities rather than roads — all three were voiced as
///         roads (dev corpus, 22 Jul – 13 Aug).
///     </para>
///     <see cref="RoadName" /> is None when HERE gave no usable street name. That is not a gap to be
///     filled: an incident with no road name is reported by its suburb, or not at all.
/// </summary>
/// <param name="RoadName">The street HERE named at this point, when it is speakable as a road.</param>
/// <param name="Suburb">The locality HERE named at this point, for anchoring rather than naming.</param>
/// <param name="NearestStreets">
///     The streets HERE ranks nearest this point, nearest first. Used ONLY to corroborate a road
///     identity resolved from the incident's own segments — never as a name source on its own, which
///     measurement rejected: nearest-street naming changed 33 of 173 live incident names and roughly
///     half moved AWAY from the segment-confirmed road.
/// </param>
public readonly record struct GeocodedPlace(
    Option<string> RoadName,
    Option<string> Suburb,
    IReadOnlyList<string>? NearestStreets = null)
{
    /// <summary>True when HERE gave a usable road name for this point.</summary>
    public bool HasRoadName => RoadName.HasValue;

    /// <summary>True when HERE ranks <paramref name="roadName" /> among the streets nearest this point.</summary>
    public bool IsNearby(string roadName) =>
        NearestStreets is { Count: > 0 }
        && NearestStreets.Any(s => Services.RoadNameFormatter.IsSameRoad(s, roadName));
}

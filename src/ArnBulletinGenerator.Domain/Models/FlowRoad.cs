namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A road the live HERE flow feed reports as currently flowing freely (low jam factor) — the
///     data-backed pool of "good run" candidates an operator can pick from in the review Flow tab.
///     <see cref="IsWeighted" /> marks roads the station has prioritised (its good-run list or the
///     market priority roads); weighted roads stay eligible for an on-air clear-run mention even
///     when the bulletin has incidents to report, while generic roads only fill a quiet slot.
/// </summary>
/// <param name="RoadName">Speakable road name (e.g. "M1", "Gold Coast Highway").</param>
/// <param name="JamFactor">Worst HERE jam factor (0 clear … 10 blocked) across the road's segments.</param>
/// <param name="LengthMeters">Total monitored length of the road's free-flowing segments.</param>
/// <param name="IsWeighted">True when the road is on the station good-run list or market priority roads.</param>
/// <param name="FunctionalClass">Road importance (1-2 = motorways, 3 = arterials, 4-5 = local). Flow is
///     fetched for FC 1-3 only; defaults to arterial when the road is not a known motorway.</param>
/// <param name="Score">Comparable rank score on the same scale as a corridor's impact score — road
///     importance + monitored length (a clear road has no severity/delay), with the market priority
///     boost folded in. Lets a major flowing road be ranked against incident corridors in one table.</param>
public sealed record FlowRoad(
    string RoadName,
    double JamFactor,
    double LengthMeters,
    bool IsWeighted = false,
    int FunctionalClass = 3,
    double Score = 0,
    bool PriorityBoosted = false,
    double? BaseScore = null,
    double? BoostAmount = null,
    /// <summary>
    ///     True when <see cref="RoadName" /> is the map's name for HERE's own segment rather than
    ///     HERE's free-text <c>Location.Description</c> label. A label may never be treated as identity.
    /// </summary>
    bool NameFromSegment = false,
    /// <summary>
    ///     A representative coordinate for the road's longest flowing reading, carried so the suburb
    ///     below can be resolved. Null when HERE returned no usable shape for the row.
    /// </summary>
    (double Lat, double Lng)? Point = null,
    /// <summary>
    ///     The suburb this road is flowing through. A clear-run mention is the one place a bare road
    ///     name is most ambiguous — ARN, 2026-08-24: "There's another clear run on the M4 Western
    ///     Motorway, its service centre, Victoria Road and Elizabeth Drive" drew "Victoria and
    ///     Elizabeth are still a bit of a curly", because Sydney has many of both. Null until the
    ///     reverse-geocode pass names it, and on markets where the lookup is unavailable.
    /// </summary>
    string? Suburb = null);

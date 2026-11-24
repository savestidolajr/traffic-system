namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The canonical registry of road-authority feed ids a station may switch off, and the only
///     place that set is defined. Every other reference to these ids (feed <c>SourceId</c>
///     properties, <see cref="IncidentClassCrosswalk" /> keys, <see cref="AuthorityPriorityNormalizer" />
///     cases) is a literal in its own file; this type exists so the ENABLE/DISABLE vocabulary has
///     exactly one owner rather than repeating the six-site sprawl the 2026-07-30 configurability
///     audit found behind <c>MaxCorridors</c> (F-2), where two of three copies of the same number
///     could drift apart silently with nothing asserting they agreed.
///     <para>
///         HERE is deliberately absent. It is the primary traffic provider rather than a
///         corroborating authority, and whether a bulletin carries traffic at all is already a
///         per-station decision via <c>BulletinContentSelection.Traffic</c>. Exposing HERE here
///         would give an operator two different switches for "no traffic", one of which would
///         produce an empty board while still claiming traffic was selected.
///     </para>
/// </summary>
public static class OfficialTrafficSources
{
    public const string LiveTrafficNsw = "livetraffic-nsw";
    public const string QldTraffic = "qldtraffic";
    public const string VicTraffic = "victraffic";
    public const string MainRoadsWa = "mainroads-wa";
    public const string TrafficSa = "trafficsa";
    public const string TasAlert = "tasalert";

    /// <summary>Every disableable authority feed id, lowercase, in stable display order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        LiveTrafficNsw,
        QldTraffic,
        VicTraffic,
        MainRoadsWa,
        TrafficSa,
        TasAlert
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="sourceId" /> names a feed a station is allowed to disable.</summary>
    public static bool IsKnown(string? sourceId) =>
        !string.IsNullOrWhiteSpace(sourceId) && Known.Contains(sourceId.Trim());

    /// <summary>
    ///     Canonical storage form: trimmed and lowercased. Applied on the write path so a stored
    ///     row can be compared with a feed's own <c>SourceId</c> by ordinal equality at broadcast
    ///     time, rather than paying a case-insensitive comparison per feed per bulletin.
    /// </summary>
    public static string Normalize(string sourceId) =>
        sourceId.Trim().ToLowerInvariant();
}

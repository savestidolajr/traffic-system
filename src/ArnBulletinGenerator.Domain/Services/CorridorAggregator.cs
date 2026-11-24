using System.Collections.Frozen;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Aggregates individual traffic incidents into corridor groups for more natural reporting.
///     Groups incidents by road name, then uses spatial single-linkage clustering to keep distant
///     observations on a long arterial from sharing one meaningless centroid.
/// </summary>
public static partial class CorridorAggregator
{
    private const int DefaultMaxCorridors = 5;
    private const int DefaultTypePriority = 99;
    private const double DegToRad = Math.PI / 180.0;

    /// <summary>
    ///     How much an ambient incident's extent/segment-count counts toward impact, relative
    ///     to a live queue. A roadworks or planned-event footprint is mostly free-flowing, so
    ///     its kilometres are not commuter delay — they score at this fraction.
    /// </summary>
    private const double AmbientImpactFactor = 0.4;

    /// <summary>
    ///     Flowing "good run" roads score in a band BELOW incidents — a clear road is less newsworthy
    ///     than a problem. Their structural score is scaled by this factor so flow reads as slot-filler
    ///     (used when incidents are sparse), while a market priority boost can still lift a well-flowing
    ///     priority road into contention with incidents.
    /// </summary>
    private const double FlowImpactFactor = 0.5;

    /// <summary>Pre-ordered array of source-generated regex delegates (priority order).</summary>
    private static readonly Func<Regex>[] MajorRoadPatterns =
    [
        MotorwayCodeRegex,
        MelbourneFreewayRegex,
        WestGateRegex,
        TollwayRegex,
        TullamarineRegex,
        RegionalFreewayRegex,
        HarbourRegex,
        SydneyBridgeRegex,
        SydneyDistributorRegex,
        ConnexRegex,
        SydneyArterialRegex,
        GenericRoadRegex,
        RingRoadRegex
    ];

    /// <summary>
    ///     Direction keywords to extract from descriptions.
    /// </summary>
    private static readonly FrozenDictionary<string, string> DirectionKeywords =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["citybound"] = "citybound",
            ["city-bound"] = "citybound",
            ["city bound"] = "citybound",
            ["inbound"] = "citybound",
            ["outbound"] = "outbound",
            ["eastbound"] = "eastbound",
            ["east-bound"] = "eastbound",
            ["westbound"] = "westbound",
            ["west-bound"] = "westbound",
            ["northbound"] = "northbound",
            ["north-bound"] = "northbound",
            ["southbound"] = "southbound",
            ["south-bound"] = "southbound"
        }.ToFrozenDictionary();

    /// <summary>
    ///     Type priority for determining primary incident type.
    ///     Lower = higher priority.
    /// </summary>
    private static readonly FrozenDictionary<string, int> TypePriority =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["accident"] = 1,
            ["roadClosure"] = 2,
            ["disabledVehicle"] = 3,
            ["roadHazard"] = 4,
            ["laneRestriction"] = 5,
            ["construction"] = 6,
            ["plannedEvent"] = 7,
            ["congestion"] = 8
        }.ToFrozenDictionary();

    /// <summary>
    ///     Road-name abbreviation/nickname table, keyed by the form that appears in raw HERE
    ///     text or natural script prose ("Tulla", "Westgate", "Rd") mapping to the canonical
    ///     display form ("Tullamarine", "West Gate", "Road"). Internal so
    ///     <see cref="ScriptProvenanceMapper" /> can reverse-match a corridor's canonical
    ///     <c>RoadName</c> against a colloquialism the script may have used instead.
    /// </summary>
    internal static readonly FrozenDictionary<string, string> AbbreviationMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rd"] = "Road",
            ["St"] = "Street",
            ["Hwy"] = "Highway",
            ["Fwy"] = "Freeway",
            ["Br"] = "Bridge",
            ["Ave"] = "Avenue",
            ["Tce"] = "Terrace",
            ["Pde"] = "Parade",
            ["Dr"] = "Drive",
            ["Pl"] = "Place",
            ["Cres"] = "Crescent",
            ["Ct"] = "Court",
            ["Ln"] = "Lane",
            ["Blvd"] = "Boulevard",
            ["Cl"] = "Close",
            ["Gr"] = "Grove",
            ["Cct"] = "Circuit",
            ["Mtwy"] = "Motorway",
            ["Mwy"] = "Motorway",
            ["Byp"] = "Bypass",
            ["Westgate"] = "West Gate",
            ["Tulla"] = "Tullamarine"
        }.ToFrozenDictionary();

    // ── Source-generated regex patterns for road name extraction ──

    [GeneratedRegex(@"\b(M\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MotorwayCodeRegex();

    [GeneratedRegex(@"\b(Monash|Eastern|Western|South Eastern)\s+(Freeway|Motorway)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MelbourneFreewayRegex();

    [GeneratedRegex(@"\b(West Gate|Westgate)\s*(Bridge|Tunnel|Freeway)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex WestGateRegex();

    [GeneratedRegex(@"\b(CityLink|EastLink)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TollwayRegex();

    [GeneratedRegex(@"\b(Tullamarine|Tulla)\s*(Freeway)?\b", RegexOptions.IgnoreCase)]
    private static partial Regex TullamarineRegex();

    [GeneratedRegex(@"\b(Calder|Hume|Princes|Great Western)\s+(Highway|Freeway)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RegionalFreewayRegex();

    [GeneratedRegex(@"\b(Harbour|Sydney Harbour)\s*(Bridge|Tunnel)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HarbourRegex();

    [GeneratedRegex(@"\b(Anzac|Lane Cove)\s*(Bridge|Tunnel)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SydneyBridgeRegex();

    [GeneratedRegex(@"\b(Cross City|Eastern Distributor|Western Distributor)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SydneyDistributorRegex();

    [GeneratedRegex(@"\b(WestConnex|NorthConnex)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ConnexRegex();

    [GeneratedRegex(@"\b(Rozelle|Parramatta Road)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SydneyArterialRegex();

    // Multi-word road names ("Gold Coast Highway", "Old Illawarra Road", "Sir Donald Bradman
    // Drive") were silently truncated to their LAST word + suffix by a single-word capture group —
    // "Gold Coast Highway" became the nonexistent "Coast Highway" (live bug reported on the pilot,
    // TDEMO-001/RISK-32, confirmed 2026-07-02: the writer's SOURCE DATA already carried the
    // truncated name, so this is a deterministic extraction bug, not an LLM fabrication). The
    // exclusion is entirely lookahead-driven (the pattern is IgnoreCase, matching HERE's mixed
    // Title-Case/ALL-CAPS incident text) — a negative lookahead excludes known connector/
    // preposition words HERE's templated incident phrasing uses ("Ramp to X", "At X", "Between
    // X and Y") from starting or extending the captured run, so "Ramp to
    // Gold Coast Highway" resolves to "Gold Coast Highway", not "Ramp Gold Coast Highway". Validated
    // against all incident/location strings recorded live across all 6 markets (2026-07-02): fixes
    // 23 previously-truncated real road names, zero regressions, zero over-captures.
    [GeneratedRegex(
        @"\b(?:(?!(?:At|Ramp|To|Between|And|Near|On)\b)[A-Za-z][\w']*\s+)*(?:(?!(?:At|Ramp|To|Between|And|Near|On)\b)[A-Za-z][\w']*)\s+(Road|Rd|Street|St|Highway|Hwy|Freeway|Fwy|Motorway|Mtwy|Mwy|Bridge|Br|Tunnel|Avenue|Ave|Terrace|Tce|Parade|Pde|Drive|Dr|Place|Pl|Crescent|Cres|Court|Ct|Lane|Ln|Boulevard|Blvd|Close|Cl|Grove|Gr|Circuit|Cct|Way)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex GenericRoadRegex();

    [GeneratedRegex(@"\b(Ring Road|Bypass)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RingRoadRegex();

    /// <summary>
    ///     Aggregates incidents into corridor groups using the documented default grouping gap.
    ///     Providers with an operator-configured gap must call the overload that takes
    ///     <paramref name="corridorGroupingMaxGapMeters" /> explicitly.
    /// </summary>
    public static IReadOnlyList<TrafficCorridor> AggregateByRoad(
        IEnumerable<TrafficIncident> incidents,
        int maxCorridors = DefaultMaxCorridors) =>
        AggregateByRoad(incidents, maxCorridors, BroadcastOptions.DefaultCorridorGroupingMaxGapMeters);

    /// <summary>
    ///     Aggregates incidents into corridor groups, splitting one road into spatially distinct
    ///     clusters while retaining road identity and editorial fields for each cluster.
    /// </summary>
    public static IReadOnlyList<TrafficCorridor> AggregateByRoad(
        IEnumerable<TrafficIncident> incidents,
        int maxCorridors,
        int corridorGroupingMaxGapMeters)
    {
        var incidentList = DeduplicateHereEvents(incidents.ToList())
            .Where(IsAboveCongestionFloor)
            .ToList();
        if (incidentList.Count == 0)
            return [];

        // Group by normalized road name + direction, then split each road group by proximity.
        // The later same-road merge repeats the same single-linkage rule so direction/recovery
        // variants can still merge when they are genuinely the same nearby corridor.
        var roadGroups = incidentList
            .Select(i => (Incident: i, RoadKey: ExtractRoadKey(i)))
            .Where(x => !string.IsNullOrEmpty(x.RoadKey.RoadName))
            .GroupBy(x => x.RoadKey, new RoadKeyComparer())
            .SelectMany(g => ClusterIncidents(g.Select(x => x.Incident).ToList(), corridorGroupingMaxGapMeters)
                .Select(cluster => CreateCorridor(g.Key, cluster)))
            .ToList();

        roadGroups = MergeSameRoadCorridors(roadGroups, corridorGroupingMaxGapMeters);

        return roadGroups
            .OrderByDescending(c => c.ImpactScore)
            .Take(maxCorridors)
            .ToList();
    }

    /// <summary>
    ///     Collapses the SAME HERE event reported more than once, before anything counts it.
    ///     <para>
    ///         HERE revises an incident in place and its <c>id</c> changes with every revision, while
    ///         <see cref="TrafficIncident.OriginalId" /> stays fixed for the life of the event
    ///         (measured live: one Kennedy Hwy hazard changed id between two fetches three hours
    ///         apart). HERE also reports one physical works zone once per carriageway — same type,
    ///         same wording, same segments, opposite direction (24 corpus jobs carried records with
    ///         byte-identical segment sets inside a single corridor).
    ///     </para>
    ///     Both shapes previously counted as separate incidents, and <see cref="CalculateImpactScore" />
    ///     scores density from that count, so one event ranked a road as two. Only records HERE itself
    ///     ties together are collapsed: a genuine hazard and a genuine works zone on one road keep
    ///     their own entries, and records with no segment references are never merged on wording alone.
    /// </summary>
    private static List<TrafficIncident> DeduplicateHereEvents(List<TrafficIncident> incidents)
    {
        if (incidents.Count <= 1)
            return incidents;

        // Worst-first so the survivor of each duplicate pair is the most reportable record.
        var ordered = incidents
            .OrderBy(i => i.Severity)
            .ThenByDescending(i => i.LengthMeters)
            .ToList();

        var keptChains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptConditions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TrafficIncident>(incidents.Count);

        foreach (var incident in ordered)
        {
            string? eventKey = SourceScopedEventKey(incident);
            if (eventKey is not null && !keptChains.Add(eventKey))
                continue;

            var segments = incident.SegmentRefs;
            if (segments is { Count: > 0 })
            {
                string conditionKey = string.Join('|',
                    incident.Type,
                    incident.RoadClosed,
                    incident.Description.Trim(),
                    string.Join(',', segments.Select(StripSegmentDirection).OrderBy(s => s, StringComparer.Ordinal)));
                if (!keptConditions.Add(conditionKey))
                    continue;
            }

            result.Add(incident);
        }

        return result;
    }
    /// <summary>
    ///     Returns a source-qualified event-chain key. Provider record IDs are scoped to their feed,
    ///     so a bare official ID such as "123" must never collapse an unrelated record from another
    ///     authority. Source-native identities take precedence; legacy incidents fall back to their
    ///     explicit source list, with HERE as the historical default.
    /// </summary>
    private static string? SourceScopedEventKey(TrafficIncident incident)
    {
        string? stableKey = SourceRecordIdentity.StableEventKey(incident.SourceRecordIdentities);
        if (stableKey is not null)
            return stableKey;

        if (string.IsNullOrWhiteSpace(incident.OriginalId))
            return null;

        var sources = incident.Sources is { Count: > 0 }
            ? incident.Sources
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [EvidenceSources.Here];

        return $"{string.Join(',', sources)}:{incident.OriginalId}";
    }


    /// <summary>
    ///     The incident-path congestion floor, mirroring the flow path's 400 m hot-spot floor:
    ///     a queue at the lights is not a traffic item. HERE's incident feed emits congestion
    ///     records down to ~100 m; a 130 m "Queuing traffic" record at James Ruse Drive anchored
    ///     an aired "M4 chockers at Clyde" claim while the 419 m record at the M7 — the real
    ///     story, at Eastern Creek — sat beside it (prod 2026-08-18, pilot day-2 feedback).
    ///     Only congestion is floored: a short crash or closure record is still news.
    /// </summary>
    internal const double MinCongestionIncidentMeters = 300;

    private static bool IsAboveCongestionFloor(TrafficIncident incident) =>
        !incident.Type.Equals("congestion", StringComparison.OrdinalIgnoreCase)
        || incident.LengthMeters >= MinCongestionIncidentMeters;

    /// <summary>
    ///     Drops the direction suffix from a segment reference, so the same physical works reported on
    ///     each carriageway ("…#+" and "…#-") compares equal.
    /// </summary>
    private static string StripSegmentDirection(string segmentRef)
    {
        int hash = segmentRef.IndexOf('#');
        return hash < 0 ? segmentRef : segmentRef[..hash];
    }

    /// <summary>
    ///     Builds standalone congestion corridors from live flow rows: high-jamFactor roads with NO
    ///     incident — the peak-hour volume crawl ATN reports ("the M4's chockers, just volume").
    ///     Rows are grouped by ROAD IDENTITY (see <see cref="FlowRoadKey" />), deduped against incident
    ///     corridors (an incident on a road wins; congestion only informs its severity), and scored on
    ///     the same impact scale so they compete in the unified ranked pool.
    /// </summary>
    public static IReadOnlyList<TrafficCorridor> BuildCongestionCorridors(
        IReadOnlyList<CongestionSegment> segments,
        IReadOnlyList<TrafficCorridor> incidentCorridors,
        double jamFactorThreshold,
        int functionalClass = 3)
    {
        if (segments.Count == 0)
            return [];

        return segments
            // HERE's own reading, not an average that hides a standstill, and only where HERE says the
            // reading is real-time — a historical or speed-limit estimate is not a current condition.
            .Where(s => s.IsRealTime
                && s.EffectiveJamFactor >= jamFactorThreshold
                && IsSpeakableRoadName(VoiceableRoadName(s))
                && RoadNamePlausibility.IsPlausible(VoiceableRoadName(s)))
            .GroupBy(FlowRoadKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Road: GroupRoadName(g), Segments: g.ToList()))
            .Where(g => !incidentCorridors.Any(c => RoadNamesOverlap(c.RoadName, g.Road)))
            .Select(g => CreateCongestionCorridor(g.Road, g.Segments, GroupFunctionalClass(g.Segments, functionalClass)))
            // Admission is per ROAD, not per reading. A row qualifies the road for INSPECTION (and
            // keeps it out of the good-run pool, which still reads the max); staying on the board takes
            // either a road that is genuinely slow taken as a whole, or a queue long enough to name.
            // Measured live (Sydney, 2026-08-15): Canterbury Road entered on 363 m of 7,683 m at a
            // weighted 2.5 and aired as "at a standstill". A road that is 95% free-flowing is not news.
            .Where(c => c.Severity <= 2 || c.HotSpotMeters.HasValue)
            .OrderByDescending(c => c.ImpactScore)
            .ToList();
    }

    /// <summary>
    ///     The road name a flow row may be voiced under: the map's name for its own segments, falling
    ///     back to HERE's free-text label ONLY for a row HERE gave no segments for. The provider drops
    ///     rows whose segments exist and disagree, so a label never stands in for a road we could have
    ///     named and did not.
    /// </summary>
    private static string VoiceableRoadName(CongestionSegment segment) =>
        segment.ResolvedRoadName ?? segment.RoadName;

    /// <summary>
    ///     Grouping key that collapses HERE's aliases for one road: case, a leading route code and a
    ///     trailing directional parenthetical all vary row to row. Measured: 74 groups of Sydney labels
    ///     were spellings of one road, which is how "M1 EASTERN DISTRIBUTOR (NORTHBOUND)", "M1 Eastern
    ///     Distributor" and "Eastern Distributor" aired as three separate slow items in one bulletin.
    /// </summary>
    private static string FlowRoadKey(CongestionSegment segment) =>
        CanonicalRoadKey(VoiceableRoadName(segment));

    public static string CanonicalRoadKey(string roadName)
    {
        string stripped = ParentheticalRegex().Replace(roadName ?? string.Empty, " ").Trim();
        stripped = NormalizeRoadName(stripped);

        // A leading route code identifies the road but is not part of its name ("M1 Eastern
        // Distributor" and "Eastern Distributor" are one road), so it is dropped — UNLESS nothing
        // identifying survives it. When the remainder is only a road-type word the code WAS the
        // whole identity, and dropping it merges every such road into one bucket: dev sydney
        // carries "M7 Motorway" x14, "M5 Motorway" x7, "M11 Motorway (NorthConnex)" x2 and
        // "M12 Motorway" x1, all of which collapsed onto the single key "Motorway" and had their
        // delays and lengths SUMMED into one corridor voiced under one name. The M2/M4/M8 forms
        // escaped only because RoadDesignationAliases happens to carry explicit entries for them,
        // so a gap in a hand-maintained table was silently causing a merge rather than merely
        // losing a designation. Collapse to the code itself, which also unifies it with the other
        // spellings the table already resolves ("Westlink M7" -> "M7").
        string[] words = stripped.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1 && RoadNameFormatter.IsRouteCodeOnly(words[0]))
        {
            stripped = words.Skip(1).All(RoadNameFormatter.IsRoadTypeToken)
                ? words[0]
                : string.Join(' ', words.Skip(1));
        }

        return stripped.Trim();
    }

    /// <summary>
    ///     The name to voice for a collapsed group: a map-resolved name always wins, then the longest
    ///     label that is not SHOUTING, so the group reads as HERE's best spelling of the road.
    /// </summary>
    private static string GroupRoadName(IEnumerable<CongestionSegment> group)
    {
        var rows = group.ToList();
        return rows
            .Select(VoiceableRoadName)
            .OrderBy(n => rows.Any(r => r.ResolvedRoadName == n) ? 0 : 1)
            .ThenBy(n => n.Any(char.IsLower) ? 0 : 1)
            .ThenByDescending(n => n.Length)
            .First()
            .Trim();
    }

    /// <summary>
    ///     HERE's own class for the group, when any row carried one. Flow is fetched for FC 1-3, and
    ///     defaulting every row to arterial put 219 of 235 corridors at FC3/Severity1 regardless of
    ///     what HERE reported.
    /// </summary>
    private static int GroupFunctionalClass(List<CongestionSegment> segments, int fallback)
    {
        var reported = segments.Where(s => s.HereFunctionalClass is > 0 and <= 5).ToList();
        return reported.Count > 0 ? reported.Min(s => s.HereFunctionalClass) : fallback;
    }

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex ParentheticalRegex();

    /// <summary>
    ///     Ranks named major roads that are flowing freely (low jam factor) into the data-backed
    ///     "good run" pool — the truthful content a reporter leans on in quiet periods ("the M1's
    ///     running well"). Excludes roads already presented as congestion corridors, prefers longer
    ///     (more major) roads, and returns at most <paramref name="max" /> <see cref="FlowRoad" />s
    ///     ordered by total monitored length. The weighted flag is applied downstream by the caller
    ///     (it depends on station/market config the aggregator does not see).
    /// </summary>
    public static IReadOnlyList<FlowRoad> RankFreeFlowingRoads(
        IReadOnlyList<CongestionSegment> segments,
        IReadOnlyList<TrafficCorridor> congestionCorridors,
        int max,
        double freeFlowJamBelow = 4.0)
    {
        if (segments.Count == 0 || max <= 0)
            return [];

        return segments
            // A road only counts as flowing when HERE's reading is real-time AND nothing inside the
            // segment contradicts it. Sydney 2026-08-13: 21 roads carried high-jam subSegments inside
            // parents whose own jamFactor was low, and every one was eligible to air as a good run.
            .Where(s => s.IsRealTime
                && s.EffectiveJamFactor < freeFlowJamBelow
                && IsSpeakableRoadName(VoiceableRoadName(s))
                && RoadNamePlausibility.IsPlausible(VoiceableRoadName(s)))
            .GroupBy(FlowRoadKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => !congestionCorridors.Any(c => RoadNamesOverlap(c.RoadName, GroupRoadName(g))))
            // One contradicting reading anywhere on the road disqualifies it: a road is either
            // reportably clear or it is not mentioned as clear at all.
            .Where(g => !segments.Any(s =>
                string.Equals(FlowRoadKey(s), g.Key, StringComparison.OrdinalIgnoreCase)
                && (!s.IsRealTime || s.EffectiveJamFactor >= freeFlowJamBelow)))
            .Select(g => new FlowRoad(
                GroupRoadName(g),
                g.Max(s => s.EffectiveJamFactor),
                g.Sum(s => s.LengthMeters),
                FunctionalClass: GroupFunctionalClass(g.ToList(), 0),
                NameFromSegment: g.Any(s => s.ResolvedRoadName is not null),
                // Same rule the congestion corridor uses: the longest reading is the one whose
                // location is honest to name, because a group shares only a road name.
                Point: g.OrderByDescending(s => s.LengthMeters).FirstOrDefault().StartPoint))
            .OrderByDescending(f => f.LengthMeters)
            .Take(max)
            .ToList();
    }

    /// <summary>
    ///     Builds a flow-derived corridor from every reading HERE has for one road.
    ///     <para>
    ///     The band a listener hears describes the ROAD, so it comes from the length-weighted reading,
    ///     not from the worst point on it. Measured live (Sydney, 2026-08-15) the old max-anywhere rule
    ///     called 52 roads "at a standstill" at once; the median one was stopped over 16% of its
    ///     measured length, and 11 of them had a parent reading below the band entirely — Botany Road
    ///     read 5.1 overall and was aired as stopped on 5% of 4.5 km. ATN's own definition of the band
    ///     is "stopped, crawling or bumper-to-bumper FOR AN EXTENDED STRETCH".
    ///     </para>
    ///     <para>
    ///     A real queue inside an otherwise-moving road is still news, so it survives as its own fact:
    ///     <see cref="TrafficCorridor.HotSpotMeters" />, gated on
    ///     <see cref="CongestionSegment.MinHotSpotMeters" />. Two facts, never merged into one word.
    ///     </para>
    /// </summary>
    private static TrafficCorridor CreateCongestionCorridor(
        string road, List<CongestionSegment> segments, int functionalClass)
    {
        // The LONGEST single congested reading, never the sum. HERE groups nothing for us: these
        // segments share only a road NAME, with no contiguity, so a sum spans the whole coverage area
        // — Cairns 2026-08-09 published "the queue stretching about forty-five kays" off 45.8 km of
        // summed Port Douglas Road segments. The sum is not a length, so it cannot rank one either.
        // The LONGEST reading is also the one whose ENDS are honest to name: the group's segments
        // share only a road name, so the ends of any other pairing could sit kilometres apart on
        // unrelated stretches. Everything located about this corridor - suburb, extent, map pin -
        // therefore comes from this one segment.
        CongestionSegment longest = segments.OrderByDescending(s => s.LengthMeters).First();
        double longestSegment = longest.LengthMeters;

        double measuredMeters = segments.Sum(s => s.LengthMeters);
        double weightedJam = measuredMeters > 0
            ? segments.Sum(s => s.WeightedJamMeters) / measuredMeters
            : segments.Max(s => s.EffectiveJamFactor);
        double standstillMeters = segments.Sum(s => s.StandstillLengthMeters);

        int severity = weightedJam >= CongestionSegment.StandstillJamFactor ? 1
            : weightedJam >= CongestionSegment.HeavyJamFactor ? 2
            : 3;
        double impactScore = CalculateImpactScore(severity, functionalClass, longestSegment, 1, "congestion");

        // Flow-derived delay: time at current speed − time at free flow over congested segments. This
        // IS additive: each segment's delay is time a driver actually loses on that stretch.
        double delaySeconds = segments
            .Where(s => s.SpeedMps > 0.1 && s.FreeFlowMps > s.SpeedMps)
            .Sum(s => s.LengthMeters / s.SpeedMps - s.LengthMeters / s.FreeFlowMps);
        int delayMinutes = (int)Math.Round(delaySeconds / 60.0);

        // Spoken, not labelled. "heavy congestion" is a banded severity label that
        // EvidenceLabelVocabulary bans outright, and it reaches the writer as the corridor's
        // Description — a FOURTH emitter of the same defect, found on dev brisbane 8ba3b1b5.
        string description = weightedJam >= CongestionSegment.StandstillJamFactor ? "at a standstill"
            : weightedJam >= CongestionSegment.HeavyJamFactor ? "crawling"
            : "slow-moving";

        // A road already called stopped IS the queue; naming a hot spot inside it says nothing new.
        bool hasHotSpot = severity > 1 && standstillMeters >= CongestionSegment.MinHotSpotMeters;

        return new TrafficCorridor(
            road, severity, functionalClass, longestSegment, impactScore, "congestion", description, [],
            DescriptionIsDerived: true,
            Latitude: longest.StartPoint is { } pin ? Option<double>.Some(pin.Lat) : default,
            Longitude: longest.StartPoint is { } pinLng ? Option<double>.Some(pinLng.Lng) : default,
            StartPoint: longest.StartPoint is { } s0 ? Option<(double, double)>.Some(s0) : default,
            EndPoint: longest.EndPoint is { } e0 ? Option<(double, double)>.Some(e0) : default,
            FlowDelayMinutes: delayMinutes >= 1 ? Option<int>.Some(delayMinutes) : default,
            MaxObservationMeters: Option<double>.Some(longestSegment),
            HotSpotMeters: hasHotSpot ? Option<double>.Some(standstillMeters) : default,
            Sources: EvidenceSources.HereOnly);
    }

    /// <summary>
    ///     True when an incident corridor and a flow group are the SAME road, so the flow group must
    ///     not become a second corridor for it.
    ///     <para>
    ///         Deliberately not a substring test in either direction. "Bay Street" contains-matched
    ///         HERE's "Botany Bay Street" and suppressed a genuinely congested road from the bulletin
    ///         entirely — the mirror image of the substring bug that handed one road's flow delay to
    ///         another. Equality is abbreviation-aware, and a route-code name ("M4") still matches its
    ///         fuller form ("M4 Western Motorway") because that IS the same road.
    ///     </para>
    /// </summary>
    public static bool RoadNamesOverlap(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;
        if (RoadNameFormatter.IsSameRoad(a, b))
            return true;

        // A bare route code identifies the road on its own: HERE names the same motorway "M4" on an
        // incident and "M4 Western Motorway" on a flow row.
        string? codeA = RouteCodePrefix(a);
        string? codeB = RouteCodePrefix(b);
        return codeA is not null && codeB is not null
            && string.Equals(codeA, codeB, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The leading route code of a road name ("M4 Western Motorway" → "M4"), or null.</summary>
    private static string? RouteCodePrefix(string roadName)
    {
        string first = roadName.Trim().Split(' ', 2)[0];
        return RoadNameFormatter.IsRouteCodeOnly(first) ? first : null;
    }

    /// <summary>
    ///     Enhanced aggregation using the documented default spatial grouping gap.
    /// </summary>
    public static IReadOnlyList<TrafficCorridor> AggregateByRoad(
        IEnumerable<TrafficIncident> incidents,
        int maxCorridors,
        DateTime? currentTimeUtc,
        TimeZoneInfo? timeZone,
        CorridorSelectionOptions? selectionOptions = null) =>
        AggregateByRoad(incidents, maxCorridors, currentTimeUtc, timeZone,
            BroadcastOptions.DefaultCorridorGroupingMaxGapMeters, selectionOptions);

    /// <summary>
    ///     Enhanced aggregation with an explicit operator-configured spatial grouping gap.
    /// </summary>
    public static IReadOnlyList<TrafficCorridor> AggregateByRoad(
        IEnumerable<TrafficIncident> incidents,
        int maxCorridors,
        DateTime? currentTimeUtc,
        TimeZoneInfo? timeZone,
        int corridorGroupingMaxGapMeters,
        CorridorSelectionOptions? selectionOptions = null)
    {
        var options = selectionOptions ?? new CorridorSelectionOptions();
        var incidentList = incidents.ToList();
        if (incidentList.Count == 0)
            return [];

        // 1. Filter unreportable incidents
        var reportable = FilterReportable(incidentList, options);

        // 2. Group by road+direction, then split each road group by spatial single-linkage.
        var corridors = reportable
            .Select(i => (Incident: i, RoadKey: ExtractRoadKey(i)))
            .Where(x => !string.IsNullOrEmpty(x.RoadKey.RoadName))
            .GroupBy(x => x.RoadKey, new RoadKeyComparer())
            .SelectMany(g => ClusterIncidents(g.Select(x => x.Incident).ToList(), corridorGroupingMaxGapMeters)
                .Select(cluster => CreateCorridor(g.Key, cluster)))
            .ToList();
        corridors = MergeSameRoadCorridors(corridors, corridorGroupingMaxGapMeters);

        // 3. Apply enhanced scoring
        if (options.EnableTimeWeighting && currentTimeUtc.HasValue && timeZone is not null)
            corridors = ApplyTimeWeighting(corridors, currentTimeUtc.Value, timeZone, options);

        if (options.EnableDeltaBoost)
            corridors = ApplyDeltaBoost(corridors, options);

        // 4. Sort by impact score
        var ranked = corridors.OrderByDescending(c => c.ImpactScore).ToList();

        // 5. Apply geographic diversity
        if (options.EnableGeographicDiversity)
            ranked = ApplyGeographicDiversity(ranked, maxCorridors, options.GeographicProximityThresholdKm);
        else
            ranked = ranked.Take(maxCorridors).ToList();

        return ranked;
    }

    /// <summary>
    ///     Extracts road name and direction from an incident.
    /// </summary>
    private static RoadKey ExtractRoadKey(TrafficIncident incident)
    {
        string text = $"{incident.Location} {incident.Description}";

        // Extract the road name from the geocoded location ONLY — never blend in the
        // description, whose road-type words would fabricate a name (location "99" +
        // description "Road construction" → "99 Road").
        string roadName = ExtractRoadFromText(incident.Location);

        // Fall back to the street portion (before the first comma) of the geocoded location.
        if (string.IsNullOrWhiteSpace(roadName) && !string.IsNullOrEmpty(incident.Location))
            roadName = NormalizeRoadName(incident.Location.Split(',')[0]);

        // HERE's geocoded location is sometimes unusable as a road name: the raw
        // "Unidentified Road"/"Unknown Location" placeholder, OR — live example, a Melbourne
        // congestion incident reverse-geocoded to "W5" — an internal freeway link code that
        // technically contains a letter but isn't a real street name (see IsSpeakableRoadName).
        // The incident's own description still anchors it to a real road ("At Todd Road /
        // Cook Street - ..."). Recover that road rather than voicing "an unnamed road". Gate on
        // "At"/"Ramp to" (the incident's OWN road) and an unusable location-derived name — never
        // "Between X and Y" (ambiguous cross-streets, which would re-introduce description bleed).
        if (IsPlaceholderRoadName(roadName) && AtAnchorRegex().IsMatch(incident.Description))
        {
            string fromDescription = ExtractRoadFromText(incident.Description);
            if (!IsPlaceholderRoadName(fromDescription))
                roadName = fromDescription;
        }

        // Unresolved, or non-speakable (a bare route number like "99") → keep as a distinct
        // corridor under a placeholder key; the suburb is voiced instead via ResolveDisplayRoadName.
        if (roadName is "Unidentified Road" or "Unknown Location" or "" || !roadName.Any(char.IsLetter))
            roadName = $"Unknown-{incident.Id}";

        // Direction: description keywords win (road-sign semantics), else geometry compass bearing.
        Option<string> direction = default;
        foreach ((string keyword, string normalized) in DirectionKeywords)
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                direction = Option<string>.Some(normalized);
                break;
            }

        return new RoadKey(roadName, direction.Or(incident.Direction));
    }

    /// <summary>
    ///     Runs the road-name patterns over a text fragment, returning the normalized road name
    ///     of the first match, or empty if none match.
    /// </summary>
    private static string ExtractRoadFromText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        foreach (var regexFactory in MajorRoadPatterns)
        {
            var match = regexFactory().Match(text);
            if (match.Success)
                return NormalizeRoadName(match.Value);
        }

        return string.Empty;
    }

    /// <summary>
    ///     Resolves the road name shown to the LLM (and operators). Unresolved incidents are keyed by
    ///     <c>Unknown-{id}</c> to keep them as separate corridors, but that placeholder must never be voiced
    ///     on air — render a speakable phrase instead, preferring the suburb when known.
    /// </summary>
    private static string ResolveDisplayRoadName(string roadName, Option<string> suburb) =>
        IsPlaceholderRoadName(roadName)
            ? suburb.Match(s => $"the {s} area", () => "an unnamed road")
            : roadName;

    private static bool IsPlaceholderRoadName(string roadName) =>
        string.IsNullOrWhiteSpace(roadName)
        || roadName.StartsWith("Unknown-", StringComparison.OrdinalIgnoreCase)
        || roadName is "Unidentified Road" or "Unknown Location"
        || !roadName.Any(char.IsLetter)
        || !IsSpeakableRoadName(roadName);

    /// <summary>
    ///     A lone alphanumeric route code (e.g. "M80", "W5") is only speakable on air for known
    ///     Australian route classes — M(otorway), A, B, C. HERE occasionally returns internal
    ///     class labels like "W5" that must never be voiced; those fall back to the suburb.
    /// </summary>
    private static bool IsSpeakableRoadName(string roadName)
    {
        string trimmed = roadName.Trim();
        if (trimmed.Length == 0)
            return false;
        if (!LoneRouteCodeRegex().IsMatch(trimmed))
            return true;
        return char.ToUpperInvariant(trimmed[0]) is 'M' or 'A' or 'B' or 'C';
    }

    [GeneratedRegex(@"^[A-Za-z]\d+$")]
    private static partial Regex LoneRouteCodeRegex();

    // ── Source-generated regex for road-name normalization (single-pass) ──

    [GeneratedRegex(@"\b(Mtwy|Mwy|Byp|Rd|St|Hwy|Fwy|Br|Ave|Tce|Pde|Dr|Pl|Cres|Ct|Ln|Blvd|Cl|Gr|Cct|Westgate|Tulla)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AbbreviationRegex();

    [GeneratedRegex(@"\s+((NSW|VIC|QLD|SA|WA|TAS|NT|ACT)(\s+\d{4})?|\d{4})\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SuburbStateSuffixRegex();

    [GeneratedRegex(@"^\s*(?:At|Ramp to)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AtAnchorRegex();
    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordDescriptionCharactersRegex();

    /// <summary>
    ///     Normalizes road name for consistent grouping using a single regex pass.
    ///     <para>
    ///     "St" is ambiguous — a trailing "Street" abbreviation ("Collins St" → "Collins Street")
    ///     vs a leading "Saint" prefix ("St Kilda Road", "St Georges Terrace" — both real,
    ///     prominent arterials, live repro: TDEMO-001/RISK-32 follow-up, 2026-07-02). HERE only
    ///     ever uses "St" as the Street suffix at the very END of a road name; a "St" earlier in
    ///     the string is always "Saint" and must pass through unexpanded, so the token is only
    ///     expanded when it's the last word.
    ///     </para>
    /// </summary>
    private static string NormalizeRoadName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string trimmed = name.Trim();
        string expanded = AbbreviationRegex().Replace(trimmed, m =>
        {
            bool isTrailingToken = m.Index + m.Length == trimmed.Length;
            if (!isTrailingToken && m.Value.Equals("St", StringComparison.OrdinalIgnoreCase))
                return m.Value;
            return AbbreviationMap.TryGetValue(m.Value, out string? full) ? full : m.Value;
        });
        // After expansion, gazetted motorway names collapse to their on-air designation
        // ("Western Motorway" → "M4") so text-extracted and map-resolved names group together.
        return RoadDesignationAliases.Canonicalize(expanded);
    }

    /// <summary>
    ///     The record a corridor leads on: the most newsworthy TYPE first, then severity, then extent.
    ///     A closure outranks everything when the corridor carries one, because that is what the
    ///     corridor is reported as.
    /// </summary>
    private static TrafficIncident LeadingIncident(List<TrafficIncident> incidents) =>
        OrderLeadingFirst(incidents).First();

    /// <summary>
    ///     Worst-first corridor order: closures, then type priority, then severity, then extent.
    ///     The corridor's stored incident list uses this order so every consumer — the narrative
    ///     the writer reads, the review rundown, the suburb/coordinate anchors — agrees on which
    ///     record the corridor leads on. Unordered lists let the writer anchor a corridor on an
    ///     arbitrary member ("chockers at Clyde" from the 130 m record, prod 2026-08-18).
    /// </summary>
    private static List<TrafficIncident> OrderLeadingFirst(List<TrafficIncident> incidents) =>
        incidents
            .OrderBy(i => i.RoadClosed ? 0 : 1)
            .ThenBy(i => TypePriority.GetValueOrDefault(i.Type, DefaultTypePriority))
            .ThenBy(i => i.Severity)
            .ThenByDescending(i => i.LengthMeters)
            .ToList();

    /// <summary>
    ///     The corridor's own type. A <c>RoadClosed</c> record makes it a closure regardless of the type
    ///     HERE reported (construction-CAUSED closures come back as type "construction" with
    ///     <c>roadClosed: true</c>, and rendered as roadworks the closure disappeared — Flinders St
    ///     "Closed due to road construction" aired as "moderate delays"). Otherwise the most newsworthy
    ///     type present wins, as before.
    ///     <para>
    ///         The ONE exception is an accident sharing a road with a standing footprint. Cairns, job
    ///         c9083927: one accident record among three roadworks records typed the whole road, and the
    ///         bullet's anchors came from the roadworks, so "a crash at Gillies Highway" aired for a
    ///         crash that was at Forest Gardens Boulevard. A crash is the claim a listener acts on hardest
    ///         and the only one measured to have been fabricated this way, so it may not become the
    ///         road's type while a worksite on the same road can supply a competing place. The corridor
    ///         is typed neutrally and each record still voices its own condition from its own
    ///         <see cref="IncidentFact" />. Congestion alongside a crash is the crash's own consequence,
    ///         not competing evidence, and still lets the crash type the road.
    ///     </para>
    /// </summary>
    private static string ResolvePrimaryType(List<TrafficIncident> incidents)
    {
        if (incidents.Any(i => i.RoadClosed))
            return "roadClosure";

        var distinct = incidents
            .Select(i => i.Type)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinct.Count == 0)
            return MixedIncidentType;

        string top = distinct
            .OrderBy(t => TypePriority.GetValueOrDefault(t, DefaultTypePriority))
            .First();
        if (distinct.Count == 1)
            return top;


        bool accidentOverWorksite = top.Equals("accident", StringComparison.OrdinalIgnoreCase)
                                    && distinct.Any(IncidentTypeClassifier.IsStandingFootprint);
        return accidentOverWorksite ? MixedIncidentType : top;
    }
    /// <summary>
    ///     Splits one road/key group into spatial clusters using single linkage: every pair of
    ///     observations within the configured gap joins the same connected component, so a queue
    ///     can span more than the gap end-to-end without being chopped at its centroid.
    ///     <para>
    ///         Downstream corridor identity remains road-level (<c>RoadName</c>): <c>CorridorCurator</c>,
    ///         <c>CuratedCorridorRoads</c>, <c>selectedRoads</c>, and the web curation/rundown keys
    ///         still share one road identity when two clusters coexist. This is intentional for
    ///         this data-fidelity fix; per-incident ids remain distinct and preserve precise edits.
    ///     </para>
    /// </summary>
    private static List<List<TrafficIncident>> ClusterIncidents(
        List<TrafficIncident> incidents,
        double maxGapMeters)
    {
        if (incidents.Count <= 1)
            return [incidents];

        var coordinateIndices = new List<(int Index, IReadOnlyList<CoveragePoint> Points)>();
        for (int i = 0; i < incidents.Count; i++)
        {
            var points = GetUsableIncidentPoints(incidents[i]);
            if (points.Count > 0)
                coordinateIndices.Add((i, points));
        }

        // Incidents without usable coordinates cannot participate in geometry. They are attached
        // to the road's primary cluster below instead of being dropped: an imprecise grouping is
        // safer than losing a real incident from the writer's evidence.
        if (coordinateIndices.Count == 0)
            return [incidents];

        int count = coordinateIndices.Count;
        int[] parent = Enumerable.Range(0, count).ToArray();

        int Find(int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }

            return value;
        }

        void Union(int left, int right)
        {
            left = Find(left);
            right = Find(right);
            if (left != right)
                parent[right] = left;
        }

        double threshold = Math.Max(0, maxGapMeters);
        for (int i = 0; i < count; i++)
            for (int j = i + 1; j < count; j++)
            {
                double distance = TrafficFeedComparison.MinDistanceMeters(
                    coordinateIndices[i].Points, coordinateIndices[j].Points);
                if (distance <= threshold)
                    Union(i, j);
            }

        var clusters = new List<List<TrafficIncident>>();
        var clusterByRoot = new Dictionary<int, int>();
        var incidentCluster = new Dictionary<int, int>();
        for (int i = 0; i < count; i++)
        {
            int root = Find(i);
            if (!clusterByRoot.TryGetValue(root, out int clusterIndex))
            {
                clusterIndex = clusters.Count;
                clusterByRoot[root] = clusterIndex;
                clusters.Add([]);
            }

            clusters[clusterIndex].Add(incidents[coordinateIndices[i].Index]);
            incidentCluster[coordinateIndices[i].Index] = clusterIndex;
        }

        // The primary cluster is the one that would lead if the road had stayed whole. Attach
        // coordinate-less incidents there, preserving the existing worst-first editorial anchor.
        int primaryIncidentIndex = Enumerable.Range(0, incidents.Count)
            .OrderBy(i => incidents[i].Severity)
            .ThenByDescending(i => incidents[i].LengthMeters)
            .First();
        int primaryClusterIndex = incidentCluster.GetValueOrDefault(primaryIncidentIndex, -1);
        if (primaryClusterIndex < 0)
        {
            primaryClusterIndex = Enumerable.Range(0, clusters.Count)
                .OrderBy(i => clusters[i].Min(x => x.Severity))
                .ThenByDescending(i => clusters[i].Max(x => x.LengthMeters))
                .First();
        }

        for (int i = 0; i < incidents.Count; i++)
            if (!incidentCluster.ContainsKey(i))
                clusters[primaryClusterIndex].Add(incidents[i]);

        return clusters;
    }

    private static List<CoveragePoint> GetUsableIncidentPoints(TrafficIncident incident)
    {
        var points = new List<CoveragePoint>();
        if (incident.Shape is { Count: > 0 })
            points.AddRange(incident.Shape.Where(IsUsablePoint));

        if (TryGetUsableCoordinate(incident, out var point)
            && !points.Contains(point))
            points.Add(point);

        return points;
    }

    private static bool TryGetUsableCoordinate(TrafficIncident incident, out CoveragePoint point)
    {
        point = default!;
        if (!incident.Latitude.HasValue || !incident.Longitude.HasValue)
            return false;

        point = new CoveragePoint(incident.Latitude.Value, incident.Longitude.Value);
        return IsUsablePoint(point);
    }

    private static bool IsUsablePoint(CoveragePoint point)
    {
        // HERE uses zero as an unknown sentinel. Negative Australian latitudes are valid, so do
        // not apply a generic "non-positive means unknown" test to latitude.
        return double.IsFinite(point.Lat) && double.IsFinite(point.Lng)
            && point.Lat is >= -90 and <= 90
            && point.Lng is >= -180 and <= 180
            && point.Lat != 0 && point.Lng != 0;
    }

    /// <summary>The type of a corridor whose records disagree — rankable, and claims nothing.</summary>
    internal const string MixedIncidentType = "incident";

    /// <summary>
    ///     Creates a corridor from grouped incidents.
    /// </summary>
    private static TrafficCorridor CreateCorridor(RoadKey key, List<TrafficIncident> unordered)
    {
        // Worst-first: the stored list order IS the corridor's editorial order (see
        // OrderLeadingFirst). The leading record is its head; its place locates the corridor.
        var incidents = OrderLeadingFirst(unordered);
        var leading = incidents[0];
        var primary = incidents.OrderBy(i => i.Severity).ThenByDescending(i => i.LengthMeters).First();

        // Calculate totals
        double totalLength = incidents.Sum(i => i.LengthMeters);
        int worstSeverity = incidents.Min(i => i.Severity);
        int bestFunctionalClass = incidents.Min(i => i.FunctionalClass);

        // Determine primary incident type (most severe type wins). A RoadClosed=true incident is a
        // closure regardless of its reported type: HERE reports construction-CAUSED closures with
        // type "construction" (priority 6) + RoadClosed=true, which would otherwise render as
        // roadworks/delays and drop the closure (observed live: Flinders St "Closed due to road
        // construction" aired as "moderate delays"). The RoadClosed flag is authoritative — mirror
        // FalseClosureDetector's own `Incidents.Any(i => i.RoadClosed)` closure test — so force
        // roadClosure and let FormatImpact/tiering voice and lead it as a closure.
        string primaryType = ResolvePrimaryType(incidents);

        // Calculate impact score
        // Higher = more impactful
        // Formula: prioritizes major roads (low FC) with severe (low severity) long incidents
        double impactScore = CalculateImpactScore(worstSeverity, bestFunctionalClass, totalLength, incidents.Count, primaryType);

        // Build consolidated description
        string description = BuildCorridorDescription(key, incidents, primaryType);

        // Get coordinates from primary incident
        var lat = primary.Latitude;
        var lng = primary.Longitude;

        // HERE's own locality for the LEADING incident. Taking it from an arbitrary member put the
        // Cairns Bruce Highway crash "near Gordonvale", the suburb of a construction record 20 km away,
        // when the crash itself was at Forest Gardens Boulevard in Woree.
        var suburb = leading.Suburb.HasValue ? leading.Suburb : ExtractSuburb(leading.Location);

        // Calculate start/end points for drive time (if we have multiple incidents)
        Option<(double Lat, double Lng)> startPoint = default;
        Option<(double Lat, double Lng)> endPoint = default;

        var incidentsWithCoords = incidents
            .Where(i => i.Latitude.HasValue && i.Longitude.HasValue)
            .ToList();

        if (incidentsWithCoords.Count >= 2)
        {
            double latRange = incidentsWithCoords.Max(i => i.Latitude.Value) -
                              incidentsWithCoords.Min(i => i.Latitude.Value);
            double lngRange = incidentsWithCoords.Max(i => i.Longitude.Value) -
                              incidentsWithCoords.Min(i => i.Longitude.Value);
            incidentsWithCoords = lngRange > latRange
                ? incidentsWithCoords.OrderBy(i => i.Longitude.Value).ToList()
                : incidentsWithCoords.OrderBy(i => i.Latitude.Value).ToList();
        }

        if (incidentsWithCoords.Count >= 2)
        {
            var first = incidentsWithCoords.First();
            var last = incidentsWithCoords.Last();
            startPoint = Option<(double Lat, double Lng)>.Some((first.Latitude.Value, first.Longitude.Value));
            endPoint = Option<(double Lat, double Lng)>.Some((last.Latitude.Value, last.Longitude.Value));
        }

        return new TrafficCorridor(
            ResolveDisplayRoadName(key.RoadName, suburb),
            worstSeverity,
            bestFunctionalClass,
            totalLength,
            impactScore,
            primaryType,
            description,
            incidents,
            suburb,
            key.Direction,
            Latitude: lat,
            Longitude: lng,
            StartPoint: startPoint,
            EndPoint: endPoint,
            // Provenance is DERIVED from the member records, never decorated on afterwards —
            // a corridor built from records of two feeds is genuinely multi-source.
            Sources: EvidenceSources.Union(incidents));
    }

    /// <summary>
    ///     Calculates impact score for prioritization.
    ///     Higher score = more impactful = should be reported first.
    ///     Balances road importance, severity, extent, and incident count
    ///     for meaningful differentiation even when severity is uniform.
    /// </summary>
    private static double CalculateImpactScore(
        int severity, int functionalClass, double lengthMeters, int incidentCount, string primaryType)
    {
        // Road importance: FC1=100, FC2=80, FC3=60, FC4=40, FC5=20
        double roadScore = (6 - functionalClass) * 20.0;

        // Severity: 1 (critical)=80, 2=60, 3=40, 4=20
        double severityScore = (5 - severity) * 20.0;

        // Length and incident count signal impact only insofar as they reflect real
        // disruption. A roadworks zone or lane restriction is a standing footprint, mostly
        // free-flowing — a 7 km works corridor is not a 7 km queue — so its extent and segment
        // count count for far less. This keeps major commuter roads leading over a long rural
        // works zone, while a genuine queue (congestion, an event, or a closure/accident) still
        // ranks on any road.
        double ambientFactor = IncidentTypeClassifier.IsStandingFootprint(primaryType) ? AmbientImpactFactor : 1.0;

        // Length: logarithmic scale, 100m=1, 1km=10, 10km=20, capped at 25
        double lengthScore = lengthMeters > 0
            ? Math.Min(Math.Log10(lengthMeters) * 10.0, 25.0) * ambientFactor
            : 0.0;

        // Incident density: multiple incidents on the same road = more impactful
        // 1 incident=0, 2=15, 3=22, 5=30, 10=40, 15=45, capped at 50
        double densityScore = incidentCount > 1
            ? Math.Min(Math.Log2(incidentCount) * 15.0, 50.0) * ambientFactor
            : 0.0;

        return roadScore + severityScore + lengthScore + densityScore;
    }

    /// <summary>
    ///     Comparable rank score for a flowing ("good run") road, on the same scale as a corridor's
    ///     <see cref="CalculateImpactScore"/> but deliberately damped into a LOWER band by
    ///     <see cref="FlowImpactFactor"/>: a clear road is less newsworthy than a problem, so flow
    ///     generally ranks below incidents and reads as slot-filler. Road importance + monitored
    ///     length (a clear road has no severity, density or delay). The market priority ×1.3 boost is
    ///     folded in by the caller — enough to lift a well-flowing priority road into contention, but
    ///     not past a genuinely disrupted corridor.
    /// </summary>
    public static double CalculateFlowScore(int functionalClass, double lengthMeters)
    {
        double roadScore = (6 - functionalClass) * 20.0;
        double lengthScore = lengthMeters > 0
            ? Math.Min(Math.Log10(lengthMeters) * 10.0, 25.0)
            : 0.0;
        return (roadScore + lengthScore) * FlowImpactFactor;
    }

    /// <summary>
    ///     Ranking bonus for a corridor's measured delay, applied at final corridor SELECTION —
    ///     after routing and flow enrichment attach drive times / flow delay — rather than baked
    ///     into <see cref="CalculateImpactScore"/>. Delay is unknown when a corridor is first
    ///     created, and ImpactScore is kept a pure structural signal (road importance, severity,
    ///     extent, density), mirroring how on-air magnitude is a separate axis. The bands match the
    ///     magnitude thresholds used by CorridorMagnitude / FormatImpact (minor ≤5, moderate ≤15,
    ///     heavy ≤30, severe >30) so a real jam out-ranks an idle major road of equal structural
    ///     impact instead of losing to it on road class alone.
    /// </summary>
    public static double CalculateDelayScore(Option<int> delayMinutes)
    {
        if (!delayMinutes.HasValue || delayMinutes.Value <= 0)
            return 0.0;

        return delayMinutes.Value switch
        {
            <= 5 => 15.0,
            <= 15 => 40.0,
            <= 30 => 70.0,
            _ => 100.0
        };
    }

    /// <summary>
    ///     Builds a human-readable description for the corridor. Never states a bare incident COUNT
    ///     the writer has no concrete type/location for — "3 incidents including construction" tells
    ///     the model there are 2 unaccounted-for incidents, and it fills that gap with vague filler
    ///     ("...and another incident") rather than naming or dropping them (observed live). Instead,
    ///     name up to 2 distinct types (matching the Incidents: line's own cap downstream) when the
    ///     incidents genuinely differ, or the single shared type when they don't — every word here
    ///     maps to a real type the writer can actually speak.
    /// </summary>
    private static string BuildCorridorDescription(RoadKey key, List<TrafficIncident> incidents, string primaryType)
    {
        string directionText = key.Direction.Match(d => $" {d}", () => "");

        var distinctTypes = incidents
            .Select(i => TrafficTypeMapper.MapType(i.Type).ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (distinctTypes.Count <= 1)
            return $"{TrafficTypeMapper.MapType(primaryType).ToLowerInvariant()}{directionText}";

        return $"{string.Join(" and ", distinctTypes.Take(2))}{directionText}";
    }

    // ── Enhanced scoring methods ──

    private static List<TrafficIncident> FilterReportable(
        List<TrafficIncident> incidents, CorridorSelectionOptions options)
    {
        // The congestion floor applies unconditionally, BEFORE the reportability OR-chain: a
        // 130 m congestion record on a motorway passes the FunctionalClass arm and airs as a
        // corridor anchor otherwise (see MinCongestionIncidentMeters).
        return incidents.Where(i =>
            IsAboveCongestionFloor(i) &&
            (i.Severity <= options.MinSeverity ||
             i.FunctionalClass <= options.MaxFunctionalClass - 1 ||
             i.LengthMeters >= options.MinLengthMeters)).ToList();
    }

    private static List<TrafficCorridor> ApplyTimeWeighting(
        List<TrafficCorridor> corridors, DateTime utcNow, TimeZoneInfo tz,
        CorridorSelectionOptions options)
    {
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
        int hour = localTime.Hour;
        bool isPeakMorning = hour is >= 6 and < 9;
        bool isPeakEvening = hour is >= 15 and < 19;
        bool isPeak = isPeakMorning || isPeakEvening;

        if (!isPeak) return corridors;

        return corridors.Select(c =>
        {
            double multiplier = options.PeakHourMultiplier;

            // Direction alignment: citybound in morning, outbound in evening
            if (c.Direction.HasValue)
            {
                string dir = c.Direction.Value.ToLowerInvariant();
                bool alignsWithPeak = (isPeakMorning && dir is "citybound" or "inbound")
                                      || (isPeakEvening && dir is "outbound");
                if (alignsWithPeak) multiplier *= options.DirectionAlignmentBoost;
            }

            return c with { ImpactScore = c.ImpactScore * multiplier };
        }).ToList();
    }

    private static List<TrafficCorridor> ApplyDeltaBoost(
        List<TrafficCorridor> corridors, CorridorSelectionOptions options)
    {
        return corridors.Select(c =>
        {
            int newCount = c.Incidents.Count(i => i.IsNew);
            if (newCount == 0) return c;

            double newRatio = (double)newCount / c.Incidents.Count;
            double boost = 1.0 + (options.NewIncidentBoost - 1.0) * newRatio;
            return c with { ImpactScore = c.ImpactScore * boost };
        }).ToList();
    }

    private static List<TrafficCorridor> ApplyGeographicDiversity(
        List<TrafficCorridor> ranked, int maxCorridors, double thresholdKm)
    {
        // Capacity is a HINT, never trust the cap: the good-run exclusion path deliberately passes
        // int.MaxValue for "no cap", and List.ctor(int.MaxValue) throws OutOfMemory ("Array
        // dimensions exceeded supported range") — observed live in prod 2026-08-17T14:58Z, where it
        // silently disabled the uncapped exclusion (a road with a reported incident could be voiced
        // "moving nicely" again, the exact defect the uncapped pool exists to prevent).
        var selected = new List<TrafficCorridor>(Math.Min(maxCorridors, ranked.Count));

        foreach (var corridor in ranked)
        {
            if (selected.Count >= maxCorridors) break;

            if (!corridor.Latitude.HasValue || !corridor.Longitude.HasValue)
            {
                selected.Add(corridor);
                continue;
            }

            bool tooClose = selected.Any(s =>
                s.Latitude.HasValue && s.Longitude.HasValue &&
                HaversineKm(corridor.Latitude.Value, corridor.Longitude.Value,
                    s.Latitude.Value, s.Longitude.Value) < thresholdKm);

            if (!tooClose || selected.Count < 2)
                selected.Add(corridor);
        }

        // Backfill if geographic filter was too aggressive
        if (selected.Count < maxCorridors)
            foreach (var corridor in ranked)
            {
                if (selected.Count >= maxCorridors) break;
                if (!selected.Contains(corridor))
                    selected.Add(corridor);
            }

        return selected;
    }
    private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * DegToRad;
        double dLon = (lon2 - lon1) * DegToRad;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                   + Math.Cos(lat1 * DegToRad) * Math.Cos(lat2 * DegToRad)
                                                    * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static Option<string> ExtractSuburb(string location)
    {
        if (string.IsNullOrWhiteSpace(location) || location is "Unidentified Road" or "Unknown Location")
            return default;

        // HERE labels are "Street, Suburb STATE POSTCODE, Country" — the suburb is the
        // segment right after the street, with any trailing state + postcode stripped.
        // A plain "Road Name, Suburb" still resolves correctly (two segments).
        var parts = location.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            string suburb = SuburbStateSuffixRegex().Replace(parts[1], string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(suburb))
                return Option<string>.Some(suburb);
        }

        return default;
    }

    // ── Same-road dedup: spatial clustering plus explicit same-event re-merge ──

    /// <summary>
    ///     First keeps directional or road-name-recovery variants separate when their incident
    ///     geometry exceeds the configured gap, preserving distinct anchors on a long arterial.
    ///     Coordinate-less corridors attach to the strongest ("primary") spatial cluster rather than
    ///     being discarded. A later identity pass re-merges only distant clusters that satisfy the
    ///     same-event predicate (road, type, delay state, and normalized descriptions).
    /// </summary>
    private static List<TrafficCorridor> MergeSameRoadCorridors(
        List<TrafficCorridor> corridors,
        double maxGapMeters)
    {
        if (corridors.Count <= 1)
            return corridors;

        var result = new List<TrafficCorridor>(corridors.Count);
        foreach (var roadGroup in corridors.GroupBy(c => c.RoadName, StringComparer.OrdinalIgnoreCase))
        {
            var items = roadGroup.ToList();
            if (items.Count == 1 || IsPlaceholderDisplayRoadName(roadGroup.Key))
            {
                result.AddRange(items);
                continue;
            }

            var withCoordinates = items
                .Select((corridor, index) => (corridor, index, points: CorridorPoints(corridor)))
                .Where(x => x.points.Count > 0)
                .ToList();
            var coordinateClusters = new List<List<TrafficCorridor>>();
            if (withCoordinates.Count > 0)
            {
                int[] parent = Enumerable.Range(0, withCoordinates.Count).ToArray();

                int Find(int value)
                {
                    while (parent[value] != value)
                    {
                        parent[value] = parent[parent[value]];
                        value = parent[value];
                    }

                    return value;
                }

                void Union(int left, int right)
                {
                    left = Find(left);
                    right = Find(right);
                    if (left != right)
                        parent[right] = left;
                }

                for (int i = 0; i < withCoordinates.Count; i++)
                    for (int j = i + 1; j < withCoordinates.Count; j++)
                    {
                        if (CorridorsWithinGap(withCoordinates[i].points, withCoordinates[j].points, maxGapMeters))
                            Union(i, j);
                    }

                var byRoot = new Dictionary<int, int>();
                for (int i = 0; i < withCoordinates.Count; i++)
                {
                    int root = Find(i);
                    if (!byRoot.TryGetValue(root, out int clusterIndex))
                    {
                        clusterIndex = coordinateClusters.Count;
                        byRoot[root] = clusterIndex;
                        coordinateClusters.Add([]);
                    }

                    coordinateClusters[clusterIndex].Add(withCoordinates[i].corridor);
                }
            }

            if (coordinateClusters.Count == 0)
            {
                result.Add(MergeCorridorGroup(items));
                continue;
            }

            // A corridor with no usable coordinates cannot be distance-tested. Keep it in the
            // strongest road cluster, mirroring ClusterIncidents' loss-averse grouping choice.
            var primaryCluster = coordinateClusters
                .OrderByDescending(cluster => cluster.Max(c => c.ImpactScore))
                .First();
            primaryCluster.AddRange(items.Where(c => CorridorPoints(c).Count == 0));

            foreach (var cluster in coordinateClusters)
                result.Add(cluster.Count == 1 ? cluster[0] : MergeCorridorGroup(cluster));
        }

        return MergeSameEventCorridors(result);
    }

    private static List<TrafficCorridor> MergeSameEventCorridors(List<TrafficCorridor> corridors)
    {
        if (corridors.Count <= 1)
            return corridors;

        // Spatial clustering remains the default identity rule. Only merge clusters when every
        // member agrees on the road, condition, delay state, and description: a shared road is not
        // enough to collapse two independent incidents.
        var groups = new List<List<TrafficCorridor>>();
        foreach (TrafficCorridor corridor in corridors)
        {
            List<TrafficCorridor>? target = groups.FirstOrDefault(group =>
                group.All(existing => DescribesSameEvent(existing, corridor)));
            if (target is null)
                groups.Add([corridor]);
            else
                target.Add(corridor);
        }

        return groups
            .Select(group => group.Count == 1 ? group[0] : MergeSameEventGroup(group))
            .ToList();
    }

    private static bool DescribesSameEvent(TrafficCorridor left, TrafficCorridor right)
    {
        if (IsPlaceholderDisplayRoadName(left.RoadName)
            || IsPlaceholderDisplayRoadName(right.RoadName)
            || !RoadNameFormatter.IsSameRoad(left.RoadName, right.RoadName)
            || !string.Equals(left.PrimaryType, right.PrimaryType, StringComparison.OrdinalIgnoreCase)
            || HasActionableDelay(left)
            || HasActionableDelay(right))
        {
            return false;
        }

        string leftDescription = NormalizeEventDescription(left.Description);
        string rightDescription = NormalizeEventDescription(right.Description);
        if (!DescriptionsOverlap(leftDescription, rightDescription))
            return false;

        // Corridor descriptions are intentionally concise ("crash"), so also compare HERE's
        // original descriptions. This keeps two distant generic incidents distinct when their
        // anchors differ, while allowing a specific works description to contain a shorter one.
        var leftIncidentDescriptions = left.Incidents
            .Select(i => NormalizeEventDescription(i.Description))
            .Where(d => d.Length > 0)
            .ToList();
        var rightIncidentDescriptions = right.Incidents
            .Select(i => NormalizeEventDescription(i.Description))
            .Where(d => d.Length > 0)
            .ToList();
        return leftIncidentDescriptions.Count == 0
            || rightIncidentDescriptions.Count == 0
            || leftIncidentDescriptions.Any(leftText =>
                rightIncidentDescriptions.Any(rightText => DescriptionsOverlap(leftText, rightText)));
    }

    private static bool DescriptionsOverlap(string left, string right) =>
        left.Length > 0
        && right.Length > 0
        && (left.Equals(right, StringComparison.Ordinal)
            || left.Contains(right, StringComparison.Ordinal)
            || right.Contains(left, StringComparison.Ordinal));

    private static bool HasActionableDelay(TrafficCorridor corridor) =>
        corridor.DelayMinutes.HasValue && corridor.DelayMinutes.Value > 0;

    private static string NormalizeEventDescription(string description)
    {
        string lower = (description ?? string.Empty).ToLowerInvariant();
        string normalized = NonWordDescriptionCharactersRegex().Replace(lower, " ");
        return string.Join(' ', normalized.Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static TrafficCorridor MergeSameEventGroup(List<TrafficCorridor> group)
    {
        TrafficCorridor merged = MergeCorridorGroup(group);
        string description = group
            .OrderByDescending(c => NormalizeEventDescription(c.Description).Length)
            .ThenByDescending(c => c.Description.Length)
            .First()
            .Description;

        Option<string> suburb = merged.Suburb;
        if (!suburb.HasValue)
            suburb = group.FirstOrDefault(c => c.Suburb.HasValue)?.Suburb ?? default;

        Option<string> extent = merged.Extent;
        if (!extent.HasValue)
            extent = group.FirstOrDefault(c => c.Extent.HasValue)?.Extent ?? default;

        return merged with
        {
            Description = description,
            Suburb = suburb,
            Extent = extent,
            Sources = UnionCorridorSources(group),
            OfficialEvidences = UnionOfficialEvidences(group)
        };
    }

    private static IReadOnlyList<string> UnionCorridorSources(IEnumerable<TrafficCorridor> corridors)
    {
        var sources = new List<string>();
        foreach (TrafficCorridor corridor in corridors)
            foreach (string source in EvidenceSources.Of(corridor.Sources))
            {
                if (!sources.Contains(source, StringComparer.OrdinalIgnoreCase))
                    sources.Add(source);
            }

        return sources.Count == 0 ? EvidenceSources.HereOnly : sources;
    }

    private static IReadOnlyList<OfficialEvidence>? UnionOfficialEvidences(
        IEnumerable<TrafficCorridor> corridors)
    {
        var evidences = new List<OfficialEvidence>();
        foreach (OfficialEvidence evidence in corridors.SelectMany(c => c.OfficialEvidences ?? []))
        {
            if (evidences.Any(existing => existing.Equals(evidence)))
                continue;

            evidences.Add(evidence);
        }

        return evidences.Count == 0 ? null : evidences.Take(4).ToList();
    }

    private static List<CoveragePoint> CorridorPoints(TrafficCorridor corridor) =>
        corridor.Incidents
            .SelectMany(GetUsableIncidentPoints)
            .Distinct()
            .ToList();

    private static bool CorridorsWithinGap(
        IReadOnlyList<CoveragePoint> left,
        IReadOnlyList<CoveragePoint> right,
        double maxGapMeters) =>
        TrafficFeedComparison.MinDistanceMeters(left, right) <= Math.Max(0, maxGapMeters);

    private static TrafficCorridor MergeCorridorGroup(List<TrafficCorridor> group)
    {
        var best = group.OrderByDescending(c => c.ImpactScore).First();
        var allIncidents = group.SelectMany(c => c.Incidents).ToList();
        int worstSeverity = allIncidents.Min(i => i.Severity);
        double totalLength = allIncidents.Sum(i => i.LengthMeters);
        int bestFunctionalClass = allIncidents.Min(i => i.FunctionalClass);

        string primaryType = ResolvePrimaryType(allIncidents);

        // Prefer a real road name over "the X area"
        string roadName = group
            .Select(c => c.RoadName)
            .OrderBy(r => r.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(r => r.Length)
            .First();

        // The suburb belongs to the record the merged corridor leads on, not to whichever member
        // happened to be first.
        var leading = LeadingIncident(allIncidents);
        Option<string> suburb = leading.Suburb.HasValue
            ? leading.Suburb
            : group.FirstOrDefault(c => c.Suburb.HasValue)?.Suburb ?? best.Suburb;

        // Direction after a same-road merge. `best with` silently inherited the HIGHEST-IMPACT
        // direction and stated it for the whole road, which is a claim the merged data does not
        // support: Cairns 2026-08-13 merged Kennedy Highway records heading citybound, outbound,
        // northbound AND southbound into one "northbound" corridor, and the bulletin placed the
        // (outbound/southbound) Myola Road works "northbound". Only one surviving direction may be
        // stated. Several closure directions collapse to "both directions" — the Mildura 2026-07-06
        // fix (Walker Road closed citybound AND outbound, aired as citybound only, inviting
        // listeners onto the closed side); that safety net lived in the summarizer, which this merge
        // pre-empts, so it is restored here. Anything else stays directionless: the per-incident
        // headings still carry each observation's own direction.
        var mergedDirections = group
            .Where(c => c.Direction.HasValue)
            .Select(c => c.Direction.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool everyIncidentClosed = allIncidents.Count > 0 && allIncidents.All(i => i.RoadClosed);
        Option<string> direction = mergedDirections.Count switch
        {
            1 => Option<string>.Some(mergedDirections[0]),
            > 1 when everyIncidentClosed => Option<string>.Some("both directions"),
            _ => default
        };

        double impactScore = CalculateImpactScore(
            worstSeverity, bestFunctionalClass, totalLength, allIncidents.Count, primaryType);

        return best with
        {
            RoadName = roadName,
            Severity = worstSeverity,
            FunctionalClass = bestFunctionalClass,
            TotalLengthMeters = totalLength,
            ImpactScore = impactScore,
            PrimaryType = primaryType,
            Suburb = suburb,
            Direction = direction,
            Incidents = OrderLeadingFirst(allIncidents),
            Sources = EvidenceSources.Union(allIncidents)
        };
    }

    private static bool IsPlaceholderDisplayRoadName(string roadName) =>
        roadName.Equals("an unnamed road", StringComparison.OrdinalIgnoreCase)
        || (roadName.StartsWith("the ", StringComparison.OrdinalIgnoreCase)
            && roadName.EndsWith(" area", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Key for grouping incidents by road and direction.
    /// </summary>
    private sealed record RoadKey(string RoadName, Option<string> Direction);

    /// <summary>
    ///     Comparer for RoadKey that handles null directions.
    /// </summary>
    private sealed class RoadKeyComparer : IEqualityComparer<RoadKey>
    {
        public bool Equals(RoadKey? x, RoadKey? y)
        {
            if (ReferenceEquals(x, y)) return true;

            if (x is null || y is null) return false;

            return string.Equals(x.RoadName, y.RoadName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(NormalizeDirection(x.Direction), NormalizeDirection(y.Direction),
                       StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(RoadKey obj)
        {
            return HashCode.Combine(
                obj.RoadName.ToLowerInvariant(),
                NormalizeDirection(obj.Direction).ToLowerInvariant());
        }

        private static string NormalizeDirection(Option<string> direction)
        {
            return direction.GetValueOr(string.Empty);
        }
    }
}

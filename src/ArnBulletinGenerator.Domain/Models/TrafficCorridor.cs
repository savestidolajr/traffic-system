using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Represents a grouped collection of traffic incidents along a road corridor.
///     Aggregates multiple incidents on the same road into a single reportable unit.
/// </summary>
/// <param name="RoadName">The primary road name for this corridor (e.g., "M4", "West Gate Bridge", "Punt Road").</param>
/// <param name="Severity">Combined severity score (1 = most severe). Based on worst incident.</param>
/// <param name="FunctionalClass">Road functional class (1-2 = motorways, 3 = arterials, 4-5 = local).</param>
/// <param name="TotalLengthMeters">Total length of congestion/incidents in meters across this corridor.</param>
/// <param name="ImpactScore">Combined impact score for prioritization. Higher = more impactful.</param>
/// <param name="PrimaryType">Primary incident type (e.g., "accident", "roadClosure", "construction").</param>
/// <param name="Description">Consolidated description for the corridor, combining incident details.</param>
/// <param name="Incidents">The individual incidents that were aggregated into this corridor.</param>
/// <param name="Suburb">The suburb/area where the most significant incident is located.</param>
/// <param name="Direction">Direction of travel affected (e.g., "citybound", "eastbound", "outbound").</param>
/// <param name="Extent">Human-readable extent (e.g., "from Church Street to Parramatta Road").</param>
/// <param name="DriveTimeMinutes">Estimated drive time across the corridor in minutes (if available).</param>
/// <param name="NormalDriveTimeMinutes">Normal (free-flow) drive time in minutes for comparison (if available).</param>
/// <param name="Latitude">Latitude of the most significant point on the corridor.</param>
/// <param name="Longitude">Longitude of the most significant point on the corridor.</param>
/// <param name="StartPoint">Start coordinates for drive time calculation.</param>
/// <param name="EndPoint">End coordinates for drive time calculation.</param>
public sealed record TrafficCorridor(
    string RoadName,
    int Severity,
    int FunctionalClass,
    double TotalLengthMeters,
    double ImpactScore,
    string PrimaryType,
    string Description,
    IReadOnlyList<TrafficIncident> Incidents,
    Option<string> Suburb = default,
    Option<string> Direction = default,
    Option<string> Extent = default,
    Option<int> DriveTimeMinutes = default,
    Option<int> NormalDriveTimeMinutes = default,
    Option<double> Latitude = default,
    Option<double> Longitude = default,
    Option<(double Lat, double Lng)> StartPoint = default,
    Option<(double Lat, double Lng)> EndPoint = default,
    /// <summary>Congestion trend from live flow data: -2 (rapidly easing) … +2 (rapidly building).</summary>
    Option<int> JamTendency = default,
    /// <summary>Delay derived from flow speeds, for corridors without routed drive times.</summary>
    Option<int> FlowDelayMinutes = default,
    /// <summary>True when a per-market priority road boosted this corridor's impact score (WS2-012).</summary>
    bool PriorityBoosted = false,
    double? BaseScore = null,
    double? BoostAmount = null,
    Option<DetourSuggestion> Detour = default,
    /// <summary>
    ///     The longest SINGLE live-flow observation behind an incident-free congestion corridor, so
    ///     <see cref="VoicedExtentMeters" /> has something honest to fall back on. Flow segments for
    ///     one road name are summed into <see cref="TotalLengthMeters" /> regardless of whether they
    ///     touch, and HERE returns them across a whole coverage area: Cairns, 2026-08-09, Port
    ///     Douglas Road's segments summed to 45.8 km and aired (published) as "the queue stretching
    ///     about forty-five kays", twice in one read. None when the corridor came from incidents.
    /// </summary>
    Option<double> MaxObservationMeters = default,
    /// <summary>
    ///     A congestion corridor whose worst reading is a standstill but whose road, taken as a whole,
    ///     is not: the length HERE reports at or above <see cref="CongestionSegment.StandstillJamFactor" />.
    ///     Measured live (Sydney, 2026-08-15): 52 roads carried a standstill reading somewhere and the
    ///     median one was stopped over 16% of its measured length — Canterbury Road was called "at a
    ///     standstill" on 363 m of 7,683 m. ATN's own definition of that band is "stopped, crawling or
    ///     bumper-to-bumper FOR AN EXTENDED STRETCH", so the road's pace and its worst spot are two
    ///     facts and must be voiced as two.
    /// </summary>
    Option<double> HotSpotMeters = default,
    /// <summary>
    ///     Provenance: every feed contributing anything to this corridor ("here",
    ///     "livetraffic-nsw", …). Null means HERE only. This is operator-facing provenance,
    ///     not a claim that every feed observed the current condition.
    /// </summary>
    IReadOnlyList<string>? Sources = null,
    Option<string> OfficialHeadline = default,
    Option<string> OfficialCrossStreet = default,
    /// <summary>
    ///     Every authority record matched to this corridor, bounded and verbatim-as-parsed, each with
    ///     its own match basis — the operator-facing receipts behind the provenance chip. Empty when
    ///     no official source contributed. At most four receipts are retained, ordered by the
    ///     authority's explicit priority signal (real priority before unknown, then rank), because
    ///     each receipt is deliberately bounded at roughly 600 bytes and the list is persisted with
    ///     every corridor.
    /// </summary>
    IReadOnlyList<OfficialEvidence>? OfficialEvidences = null,
    /// <summary>
    ///     True when <paramref name="Description" /> is OUR OWN inference rather than something a
    ///     provider observed.
    ///     <para>
    ///         Flow-derived corridors have no reported description, so the aggregator writes one from a
    ///         weighted jam factor. That word is a classifier output wearing the same clothes as
    ///         "breakdown Eastbound", and downstream cannot tell them apart — so a writer told to
    ///         voice the supplied facts voices ours back to us as though a road had been observed
    ///         stopped.
    ///     </para>
    ///     <para>
    ///         Provenance, deliberately, and not a list of words. The evidence layer used to name
    ///         the three synthesized strings and suppress those; the aggregator renamed one and the
    ///         list did not learn, so "crawling" aired on a five-minute delay (job 50b3ad83). A flag
    ///         set where the sentence is invented cannot go stale when the wording changes.
    ///     </para>
    /// </summary>
    bool DescriptionIsDerived = false,
    /// <summary>
    ///     Length-weighted live speed divided by free-flow speed. None when the corridor was built
    ///     from incidents without flow observations. Ranking consumes this dimensionless value so a
    ///     short severe crawl is not hidden behind a smaller absolute delay than a long mild queue.
    /// </summary>
    Option<double> PaceRatio = default)
{
    /// <summary>
    ///     Explicit count of distinct sources that observed the current road condition. Null means
    ///     derive the legacy value from <see cref="Sources" />; fusion sets this when provenance also
    ///     contains planned-work records, because a scheduling permit is a document, not an
    ///     observation.
    /// </summary>
    public int? CorroboratingSourceCount { get; init; }

    /// <summary>
    ///     Number of independent observations of the condition now on the road. This drives tiering
    ///     and stale-closure demotion; <see cref="Sources" /> remains the operator-facing list of
    ///     every contributing feed, including enrichment-only planned works.
    /// </summary>
    public int CorroborationCount => CorroboratingSourceCount ?? Sources?.Count ?? 1;
    /// <summary>
    ///     Delay in minutes compared to normal conditions. Routed drive times
    ///     win; live flow speeds fill in for corridors that weren't routed.
    /// </summary>
    public Option<int> DelayMinutes =>
        DriveTimeMinutes.Bind(d => NormalDriveTimeMinutes.Map(n => d - n))
            .Or(FlowDelayMinutes);

    /// <summary>
    ///     The extent (metres) it is honest to VOICE for this corridor: the longest SINGLE HERE
    ///     observation of the type actually being reported — never a sum across observations.
    ///     <para>
    ///         <see cref="TotalLengthMeters" /> sums every incident on the road, in both directions,
    ///         and HERE routinely reports the same physical works twice (once per carriageway) plus a
    ///         separate hazard record over the same segment. Voicing that sum invented a queue that
    ///         did not exist: Cairns, 2026-08-13, six Kennedy Highway records (5665 + 5523 + 268 +
    ///         233 + 247 + 233 m over four shared segments) summed to 12,169 m and aired as "queues
    ///         back about ten kays" while every routed and flow-derived delay on that corridor was 0.
    ///         The sum stays the ranking input (<see cref="ImpactScore" /> is unchanged); only what
    ///         reaches air and <see cref="Services.CorridorMagnitude" /> uses this.
    ///     </para>
    ///     Scoped to <see cref="PrimaryType" /> so the extent belongs to the condition being
    ///     reported — a 268 m hazard is not 5.6 km long because roadworks share its road. Falls back
    ///     to the longest incident when the primary type carries none (a <c>RoadClosed</c>-forced
    ///     closure whose incidents are typed "construction"), and to <see cref="TotalLengthMeters" />
    ///     for incident-free flow corridors, whose length is congested-segment extent, not a footprint.
    /// </summary>
    public double VoicedExtentMeters
    {
        get
        {
            if (Incidents.Count == 0)
                return MaxObservationMeters.HasValue ? MaxObservationMeters.Value : TotalLengthMeters;
            double typed = Incidents
                .Where(i => string.Equals(i.Type, PrimaryType, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.LengthMeters)
                .DefaultIfEmpty(0)
                .Max();
            if (typed > 0)
                return typed;
            // The voiced type carries no measured length (a RoadClosed-forced closure typed
            // "construction", or point incidents with no shape). Fall back to the longest single
            // observation, then to the corridor total — this property may only ever CAP an extent
            // at one real observation, never erase a length the corridor genuinely has.
            double longest = Incidents.Max(i => i.LengthMeters);
            return longest > 0 ? longest : TotalLengthMeters;
        }
    }
}

/// <summary>
///     A live flow segment — one road's current speed vs free-flow — used to derive standalone
///     congestion corridors (incident-free volume crawls). Speeds are metres/second, as HERE reports.
/// </summary>
/// <param name="Confidence">
///     HERE's own confidence in this reading: ≥0.7 is real-time probe data, lower is a historical or
///     speed-limit estimate. Measured live on Sydney (2026-08-13): 41 segments were historical and 33
///     carried no confidence at all, and every one was voiced as a current condition.
/// </param>
/// <param name="MaxSubSegmentJamFactor">
///     The worst <c>jamFactor</c> among this segment's <c>subSegments</c>, or 0 when HERE reported
///     none. A parent segment averages over its length, so a standstill inside it can read as
///     free-flowing: measured live on Sydney, 21 roads carried ~9.9 km of high-jam subsegments inside
///     parents we put in the good-run pool. This is an ELIGIBILITY signal — "is any of this road
///     bad?" — and never the voiced band; see <see cref="JamFactorMeters" />.
/// </param>
/// <param name="JamFactorMeters">
///     Σ(jamFactor × length) across this row at subSegment resolution, so a road's pace can be
///     weighted by how much of it each reading covers. The maximum alone says "somewhere on this road
///     is stopped"; only the weighted reading says whether the ROAD is.
/// </param>
/// <param name="StandstillMeters">
///     Length within this row reading at or above <see cref="CongestionSegment.StandstillJamFactor" />
///     — the size of the actual queue, which is what makes a hot spot worth airtime.
/// </param>
public readonly record struct CongestionSegment(
    string RoadName,
    double JamFactor,
    double LengthMeters,
    double SpeedMps,
    double FreeFlowMps,
    double Confidence = 1.0,
    double MaxSubSegmentJamFactor = 0.0,
    /// <summary>
    ///     The functional class HERE reported for this flow row's own links. Present in the flow
    ///     response we already request; today it is discarded and the class is guessed from the road
    ///     NAME with a default of 3, which is why 219 of 235 corridors aired as FC3/Severity1.
    /// </summary>
    int HereFunctionalClass = 0,
    /// <summary>
    ///     HERE's segment references for this flow row, full form. The only authoritative source of a
    ///     flow road's identity: <c>Location.Description</c> is a label and is sometimes a locality
    ///     ("Wiley Park") or a POI 14 km away ("Paddys Market Sydney").
    /// </summary>
    IReadOnlyList<string>? SegmentRefs = null,
    /// <summary>
    ///     The map's name for this row's own segments, or null when the map could not settle one. When
    ///     present this — never <see cref="RoadName" /> — is the road the row measures.
    /// </summary>
    string? ResolvedRoadName = null,
    /// <summary>How decisively the row's segments named the road.</summary>
    IdentityVerdict Verdict = IdentityVerdict.Unnamed,
    double JamFactorMeters = 0.0,
    double StandstillMeters = 0.0,
    /// <summary>
    ///     HERE's own <c>traversability</c> for the row. Carried and counted, never voiced: 46 of 28,673
    ///     Australian rows report <c>closed</c> and nothing independently corroborates what is shut.
    /// </summary>
    string? Traversability = null,
    /// <summary>
    ///     The two ends of this flow row's own shape. HERE is already asked for
    ///     <c>locationReferencing=shape</c> and returns the polyline, but the projection into this
    ///     type used to drop it, so a flow-derived corridor reached the writer as a bare road name:
    ///     no suburb, no coordinates, and no endpoints for the extent pass, which therefore skipped
    ///     every one of them. ARN reviewed a Sydney read on 2026-08-24 whose five voiced roads all
    ///     came from flow ("Haig Ave is a good one - I'm not sure which it is"). Carrying the ends
    ///     lets the same reverse-geocode enrichment incident corridors already get name the place.
    /// </summary>
    (double Lat, double Lng)? StartPoint = null,
    (double Lat, double Lng)? EndPoint = null)
{
    /// <summary>HERE's documented boundary: 0.7 and above is real-time probe data.</summary>
    public const double RealTimeConfidenceFloor = 0.7;

    /// <summary>
    ///     The jam factor at which ATN's top band ("at a standstill") is the honest word for a stretch.
    /// </summary>
    public const double StandstillJamFactor = 8.0;

    /// <summary>The jam factor at which a stretch reads as heavy rather than merely slow.</summary>
    public const double HeavyJamFactor = 6.5;

    /// <summary>
    ///     The shortest queue worth naming as a hot spot. Below this a high reading is a set of lights,
    ///     not news: measured live (Sydney, 2026-08-15), the median standstill stretch behind a road we
    ///     called "at a standstill" was 250 m, and a 400 m floor left 12 hot spots across a 25 km radius.
    /// </summary>
    public const double MinHotSpotMeters = 400;

    /// <summary>
    ///     The jam factor it is honest to act on — the worst of the parent reading and anything HERE
    ///     reported inside it. Congestion DETECTION and good-run eligibility use this; the voiced band
    ///     never does.
    /// </summary>
    public double EffectiveJamFactor => Math.Max(JamFactor, MaxSubSegmentJamFactor);

    /// <summary>
    ///     Σ(jamFactor × length) for this row, falling back to the parent reading across the whole row
    ///     when no subSegment weighting was supplied. The fallback matters: a row built without it —
    ///     an older caller, a replayed fixture — must weigh in at its own jam factor, never at zero,
    ///     which would silently talk every road down to the bottom band.
    /// </summary>
    public double WeightedJamMeters => JamFactorMeters > 0 ? JamFactorMeters : JamFactor * LengthMeters;

    /// <summary>
    ///     Length of this row at or above <see cref="StandstillJamFactor" />, falling back to the whole
    ///     row when its worst reading is a standstill and no finer weighting was supplied.
    /// </summary>
    public double StandstillLengthMeters => JamFactorMeters > 0
        ? StandstillMeters
        : EffectiveJamFactor >= StandstillJamFactor ? LengthMeters : 0.0;

    /// <summary>True when HERE says this reading is real-time rather than an estimate.</summary>
    public bool IsRealTime => Confidence >= RealTimeConfidenceFloor;
}

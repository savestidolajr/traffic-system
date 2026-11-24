using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts;

// =============================================================================
// Traffic Stage DTOs
// =============================================================================

/// <summary>
///     Traffic incident data from the HERE API or mock provider.
/// </summary>
public sealed record JobIncidentDto(
    string Key,
    int Priority,
    string Headline,
    string Detail,
    int FunctionalClass = 5,
    double LengthMeters = 0,
    double? Latitude = null,
    double? Longitude = null,
    bool IsNew = false,
    IReadOnlyList<CoveragePointDto>? Shape = null,
    // Direction/LaneDetail/Comment are optional refinements straight from HERE
    // (direction of travel, lane-level state, and the road authority's own
    // free-text reason) — absent for most incidents, so the UI renders them
    // only when present rather than reserving row space for every incident.
    string? Direction = null,
    string? LaneDetail = null,
    string? Comment = null,
    bool RoadClosed = false,
    DateTimeOffset? EntryTimeUtc = null,
    // Evidence-modal fields: the record's own window and provenance, verbatim-as-parsed.
    string? Type = null,
    DateTimeOffset? StartTimeUtc = null,
    DateTimeOffset? EndTimeUtc = null,
    IReadOnlyList<string>? Sources = null,
    int? LanesClosed = null,
    int? LanesTotal = null,
    IReadOnlyList<string>? OtherAssets = null,
    string? CanonicalClass = null,
    IReadOnlyList<string>? ProseFields = null,
    IReadOnlyList<SourceRecordIdentity>? SourceRecordIdentities = null);

/// <summary>
///     Traffic data fetched from provider with bounding box and incidents.
/// </summary>
public sealed record TrafficDataDto(
    string FetchedAt,
    double[] Bbox,
    IReadOnlyList<JobIncidentDto> Incidents,
    IReadOnlyList<TrafficCorridorDto> Corridors,
    IReadOnlyList<FlowRoadDto>? FlowRoads = null,
    IReadOnlyList<MonitoredRouteDto>? MonitoredRoutes = null);

/// <summary>
///     Aggregated traffic corridor data for API responses.
/// </summary>
public sealed record TrafficCorridorDto(
    string RoadName,
    int Severity,
    double ImpactScore,
    string PrimaryType,
    string Description,
    int IncidentCount,
    double TotalLengthKm,
    int Rank,
    bool IsSelected,
    int FunctionalClass = 5,
    string? Direction = null,
    string? Suburb = null,
    int? DriveTimeMinutes = null,
    int? NormalDriveTimeMinutes = null,
    int? DelayMinutes = null,
    double? Latitude = null,
    double? Longitude = null,
    IReadOnlyList<string>? IncidentKeys = null,
    bool PriorityBoosted = false,
    double? BaseScore = null,
    double? BoostAmount = null,
    string? Magnitude = null,
    /// <summary>
    ///     Provenance feeds ("here", "livetraffic-nsw", …), including enrichment-only planned works.
    ///     CorroborationCount is the number of distinct sources observing the current condition now,
    ///     not the number of contributing documents.
    /// </summary>
    IReadOnlyList<string>? Sources = null,
    /// <summary>Number of distinct sources independently observing the current road condition.</summary>
    int CorroborationCount = 1,
    string? OfficialHeadline = null,
    string? OfficialCrossStreet = null,
    /// <summary>Matched authority records + match basis — the fact-check receipts for the console.</summary>
    IReadOnlyList<OfficialEvidence>? OfficialEvidences = null);

/// <summary>
///     A road the live flow feed reports as flowing freely — the data-backed "good run" pool
///     surfaced in the review Flow tab. <see cref="IsWeighted" /> marks station/priority roads;
///     <see cref="IsSelected" /> marks roads that aired in this bulletin.
/// </summary>
public sealed record FlowRoadDto(
    string RoadName,
    double JamFactor,
    double LengthKm,
    bool IsWeighted,
    bool IsSelected,
    double Score = 0,
    bool PriorityBoosted = false,
    double? BaseScore = null,
    double? BoostAmount = null,
    int FunctionalClass = 3);

/// <summary>
///     A station's configured point-to-point commuter run, measured live against its typical
///     time. This is the bulletin's ONLY numeric on-air channel for journey times, and it is
///     evidence the corridor/incident rows cannot account for: a monitored route is a route, not
///     a road segment, so a sentence reading "the M4 from Parramatta to the city is a slower run,
///     taking 33 minutes" has no corridor backing it and used to reach the operator console with
///     nothing to check it against (job cb5eadd4, test, 2026-08-23).
///     <para>
///         Every measured route is reported, including ones the writer never saw: the suppression
///         rules (closure reconciliation, the on-time aside's hot-board gate, the headline delay
///         threshold) live in <c>TrafficNarrativeSummarizer</c> and are deliberately NOT duplicated
///         here. A second copy of that policy would drift from the first, and the operator's
///         question is "what was measured", which is always answerable.
///     </para>
/// </summary>
public sealed record MonitoredRouteDto(
    string Name,
    int DriveTimeMinutes,
    int NormalDriveTimeMinutes,
    /// <summary>Live minus typical. Zero is running to time; negative is running better than typical.</summary>
    int DelayMinutes,
    double DistanceKm);

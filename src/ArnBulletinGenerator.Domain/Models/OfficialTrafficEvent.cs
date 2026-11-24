using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One event from an authoritative road-authority feed (Live Traffic NSW, QLDTraffic, VicTraffic, etc.),
///     carrying geometry and metadata for spatial corroboration and bulletin enrichment.
/// </summary>
/// <param name="SourceId">The feed that reported it ("livetraffic-nsw", "qldtraffic", etc.).</param>
/// <param name="Headline">The authority's own one-line description, for logs, summaries, and TTS.</param>
/// <param name="IsIncidentClass">
///     True for the event classes a bulletin must not miss — crashes, breakdowns, hazards, police
///     operations. False for standing roadworks and planned events, which corroborate closures but are
///     not "missing incidents" when HERE lacks them.
/// </param>
/// <param name="Shape">The event's geometry, flattened to points.</param>
/// <param name="Id">Optional feed-specific event identifier.</param>
/// <param name="RoadName">Road name parsed from official feed if available.</param>
/// <param name="CrossStreet">Cross street or secondary location if available.</param>
/// <param name="Suburb">Suburb or locality if available.</param>
/// <param name="Description">The authority's advisory tag text, retained for compatibility.</param>
/// <param name="Direction">Affected travel direction.</param>
/// <param name="StartTime">Incident start time if reported.</param>
/// <param name="EndTime">Incident expected or actual end time if reported.</param>
/// <param name="PublishedTime">When the authority first published the record.</param>
/// <param name="AuthorityPriority">The authority's own priority label, when published.</param>
/// <param name="Category">The authority's primary event category.</param>
/// <param name="SubCategory">The authority's event subtype.</param>
/// <param name="IsStateRoad">Whether the authority classifies the road as state-owned.</param>
/// <param name="IsPlanned">Whether the authority classifies the event as planned.</param>
/// <param name="AdviceTags">Closed-vocabulary advice labels used only as severity signals.</param>
/// <param name="ImpactType">The authority's impact type.</param>
/// <param name="ImpactSubtype">The authority's impact subtype.</param>
/// <param name="QualitativeDelay">The authority's qualitative delay label.</param>
/// <param name="SpeedLimitKph">Reduced speed limit in kilometres per hour, when present.</param>
/// <param name="LanesClosed">Number of lanes closed, when the authority reports it.</param>
/// <param name="LastUpdated">The authority's latest update timestamp.</param>
/// <param name="Detail">Impact prose from the feed, with HTML markup removed.</param>
/// <param name="LanesTotal">Total lanes reported by the authority, when present or recovered from prose.</param>
/// <param name="OtherAssets">Secondary roads/assets recovered from the authority's prose.</param>
/// <param name="ExtentDescription">Verbatim extent wording recovered from the authority's prose.</param>
/// <param name="CanonicalClass">Deterministic crosswalk class, when the source vocabulary is recognised.</param>
public sealed record OfficialTrafficEvent(
    string SourceId,
    string Headline,
    bool IsIncidentClass,
    IReadOnlyList<CoveragePoint> Shape,
    string? Id = null,
    string? RoadName = null,
    string? CrossStreet = null,
    string? Suburb = null,
    string? Description = null,
    string? Direction = null,
    DateTimeOffset? StartTime = null,
    DateTimeOffset? EndTime = null,
    /// <summary>When the authority first PUBLISHED the record — distinct from the event's own
    ///  start: a future-scheduled worksite is created long before it starts. Fusion uses this as
    ///  the age lower-bound only when the record carries no start of its own.</summary>
    DateTimeOffset? PublishedTime = null,
    string? AuthorityPriority = null,
    string? Category = null,
    string? SubCategory = null,
    bool? IsStateRoad = null,
    bool? IsPlanned = null,
    IReadOnlyList<string>? AdviceTags = null,
    string? ImpactType = null,
    string? ImpactSubtype = null,
    string? QualitativeDelay = null,
    int? SpeedLimitKph = null,
    int? LanesClosed = null,
    DateTimeOffset? LastUpdated = null,
    string? Detail = null,
    int? LanesTotal = null,
    IReadOnlyList<string>? OtherAssets = null,
    string? ExtentDescription = null,
    IncidentClass? CanonicalClass = null,
    /// <summary>Fields filled by the prose extractor rather than the source's structured payload.</summary>
    IReadOnlyList<string>? ProseFields = null);

/// <summary>
///     The authority's record as the pipeline parsed it, persisted per corridor so an operator can
///     fact-check a provenance chip: every claim-bearing field verbatim-as-parsed, plus HOW the
///     match was made. Deliberately BOUNDED (~600 B per receipt); geometry is reduced to a point
///     count so a job's corridor list can never blow Table Storage limits. The four-receipt cap is
///     measured against a realistic 15-corridor fixture at 58,657 raw UTF-8 bytes (2,399
///     gzip+base64 characters), before the full entity's other columns are added.
///     The full shape belongs to a blob-backed evidence endpoint if the map overlay wave ever needs
///     it.
/// </summary>
/// <param name="MatchBasis">"road-name", "distance-212m", or "official-only" (unmatched injection).</param>
public sealed record OfficialEvidence(
    string SourceId,
    string Headline,
    bool IsIncidentClass,
    string? RoadName = null,
    string? CrossStreet = null,
    string? Suburb = null,
    string? Direction = null,
    string? Description = null,
    DateTimeOffset? StartTime = null,
    DateTimeOffset? EndTime = null,
    DateTimeOffset? PublishedTime = null,
    int ShapePointCount = 0,
    string? MatchBasis = null,
    string? AuthorityPriority = null,
    string? Category = null,
    string? SubCategory = null,
    bool? IsStateRoad = null,
    bool? IsPlanned = null,
    IReadOnlyList<string>? AdviceTags = null,
    string? ImpactType = null,
    string? ImpactSubtype = null,
    string? QualitativeDelay = null,
    int? SpeedLimitKph = null,
    DateTimeOffset? LastUpdated = null,
    /// <summary>The provider's own record identifier, when the authority exposes one.</summary>
    string? SourceRecordId = null,
    /// <summary>
    ///     Lanes the authority reports closed, and the road's total, when published. Restored
    ///     2026-08-20: this projection previously dropped both, which is the ONLY reason the
    ///     pipeline could not voice ATN's "two centre lanes out" — the parser recovers them
    ///     (including from the authority's prose) onto <see cref="OfficialTrafficEvent" /> and they
    ///     died here, before ever reaching a corridor. Not a provider limit; a projection bug.
    ///     Two nullable ints cost ~10 B per receipt, which the bounded budget above absorbs; the
    ///     authority's free-text Detail and ExtentDescription are deliberately NOT carried here,
    ///     because adding them put a realistic 15-corridor fixture at 65,557 B against Table
    ///     Storage's 65,536 B entity ceiling. Prose reaches the writer via the corridor's own
    ///     Description instead, which is not size-bounded by persistence.
    /// </summary>
    int? LanesClosed = null,
    int? LanesTotal = null)
{
    public static OfficialEvidence From(OfficialTrafficEvent e, string matchBasis) => new(
        e.SourceId, e.Headline, e.IsIncidentClass, e.RoadName, e.CrossStreet, e.Suburb,
        e.Direction, e.Description, e.StartTime, e.EndTime, e.PublishedTime,
        e.Shape.Count, matchBasis, e.AuthorityPriority, e.Category, e.SubCategory,
        e.IsStateRoad, e.IsPlanned, e.AdviceTags, e.ImpactType, e.ImpactSubtype,
        e.QualitativeDelay, e.SpeedLimitKph, e.LastUpdated, e.Id,
        e.LanesClosed, e.LanesTotal);
}

using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>What a record's condition applies to. A ramp closure is not a road closure.</summary>
public enum AffectedAsset
{
    /// <summary>Neither the description nor the map identified the asset. Nothing may be claimed shut.</summary>
    Unknown,

    /// <summary>The through carriageway.</summary>
    Carriageway,

    /// <summary>A ramp. A closure may name only the ramp, never the road it leaves.</summary>
    Ramp
}

/// <summary>Where a direction came from, so a claim can be traced instead of trusted.</summary>
public enum DirectionSource
{
    /// <summary>No direction. The record does not support one and none was invented.</summary>
    None,

    /// <summary>HERE stated it in the record's own description ("(Eastbound)").</summary>
    HereDescription,

    /// <summary>Derived from the shape, and only where every segment reference agreed on a direction sign.</summary>
    SegmentOriented,

    /// <summary>
    ///     The road authority published it as a structured field rather than prose — for NSW,
    ///     <c>roads[].impactedLanes[].affectedDirection</c> or <c>periods[].direction</c>. Kept
    ///     distinct from <see cref="HereDescription" /> because this enum exists so a direction can be
    ///     traced rather than trusted, and until 2026-08-24 an authority-structured direction was
    ///     stamped as HERE's. The mislabel was inert only while the official-feed path populated no
    ///     direction at all; parsing it made the provenance wrong rather than merely absent. Appended
    ///     last so the existing members keep their ordinals.
    /// </summary>
    AuthorityStructured
}

/// <summary>
///     One HERE record reduced to the facts it can support, each carrying its own source. This is the
///     atomic unit a bulletin may claim: a condition, the asset it applies to, and the place it sits
///     at, all belonging to the SAME record.
///     <para>
///         The defect this exists to make impossible: two Cairns drafts aired a crash at Gillies
///         Highway because the corridor took its claim label from one record's type while its anchors
///         were flattened from every record on the road, keeping no record identity at all. Nothing
///         downstream may recombine these fields across records.
///     </para>
/// </summary>
/// <param name="IncidentId">HERE's per-revision id, for the withholding log and the job record.</param>
/// <param name="OriginalId">HERE's stable id for the event across revisions.</param>
/// <param name="Condition">The condition from <c>codes[0]</c> — never from the English text.</param>
/// <param name="Asset">The asset the condition applies to.</param>
/// <param name="RoadName">The map's name for the record's own segments, or null when withheld.</param>
/// <param name="Identity">How decisively the segments named the road.</param>
/// <param name="Anchor">The record's own primary cross-street anchor.</param>
/// <param name="SecondaryAnchor">The far end of a "between A and B" span.</param>
/// <param name="RampTarget">The ramp's destination, when the affected asset is a ramp.</param>
/// <param name="Direction">Direction of travel, when one is supported.</param>
/// <param name="DirectionSource">Where <paramref name="Direction" /> came from.</param>
/// <param name="Suburb">The locality HERE resolved at the record's position — an anchor, never a road.</param>
/// <param name="RoadClosed">HERE's own <c>roadClosed</c> flag: the authority on closure.</param>
/// <param name="Window">How current the record is, from HERE's validity window.</param>
/// <param name="LaneDetail">HERE's own lane wording for this record, when it adds anything.</param>
/// <param name="SourceDetail">The road authority's normalised free-text for this record.</param>
/// <param name="AuthorityPriority">The source's raw priority label, retained for audit.</param>
/// <param name="AuthorityPriorityRank">The source-aware priority rank, where 1 is most severe.</param>
/// <param name="Category">The source's primary event category.</param>
/// <param name="SubCategory">The source's event subtype.</param>
/// <param name="ImpactType">The source's impact type.</param>
/// <param name="QualitativeDelay">The source's qualitative delay label.</param>
/// <param name="SpeedLimitKph">The source's reduced speed limit, when reported.</param>
/// <param name="LanesClosed">The number of closed lanes, when reported.</param>
/// <param name="LanesTotal">The total lanes affected, when reported.</param>
/// <param name="OtherAssets">Secondary roads/assets named in the source prose, when reported.</param>
/// <param name="CanonicalClass">The deterministic source-vocabulary incident class, when recognised.</param>
public sealed record IncidentFact(
    string IncidentId,
    string OriginalId,
    IncidentCondition Condition,
    AffectedAsset Asset,
    string? RoadName,
    IdentityVerdict Identity,
    Option<string> Anchor,
    Option<string> SecondaryAnchor,
    Option<string> RampTarget,
    Option<string> Direction,
    DirectionSource DirectionSource,
    Option<string> Suburb,
    bool RoadClosed,
    WindowClass Window,
    Option<string> LaneDetail,
    Option<string> SourceDetail,
    Option<string> AuthorityPriority = default,
    Option<int> AuthorityPriorityRank = default,
    Option<string> Category = default,
    Option<string> SubCategory = default,
    Option<string> ImpactType = default,
    Option<string> QualitativeDelay = default,
    Option<int> SpeedLimitKph = default,
    Option<int> LanesClosed = default,
    Option<int> LanesTotal = default,
    IReadOnlyList<string>? OtherAssets = null,
    Option<IncidentClass> CanonicalClass = default);

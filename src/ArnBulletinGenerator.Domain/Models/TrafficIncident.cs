namespace ArnBulletinGenerator.Domain.Models;

/// <summary>A single fresh HERE traffic incident as fetched for a coverage area, before it is reduced to the leaner JobIncident review shape.</summary>
public sealed record TrafficIncident(
    string Id,
    string Description,
    int Severity,
    string Location,
    string Type,
    double LengthMeters = 0,
    int FunctionalClass = 5,
    Option<double> Latitude = default,
    Option<double> Longitude = default,
    bool IsNew = false,
    DateTimeOffset? DataTimestampUtc = null,
    Option<string> Direction = default,
    Option<string> Summary = default,
    Option<string> JunctionTraversability = default,
    IReadOnlyList<string>? SegmentRefs = null,
    Option<string> Comment = default,
    IReadOnlyList<CoveragePoint>? Shape = null,
    bool RoadClosed = false,
    DateTimeOffset? EntryTimeUtc = null,
    /// <summary>
    ///     HERE's <c>originalId</c> — the identifier of the FIRST incident in an update chain, stable
    ///     for the life of the event. <see cref="Id" /> changes every time HERE revises the incident
    ///     (measured live: the same Kennedy Hwy hazard changed id between two fetches three hours
    ///     apart), so only this can tell an UPDATE from a NEW event, or collapse the same event
    ///     reported twice. Empty when HERE omitted it.
    /// </summary>
    string OriginalId = "",
    /// <summary>
    ///     The functional class HERE itself reported for this incident's road links, preserved before
    ///     any of our own adjustment. Without it a promotion is unauditable: the Sydney 2026-08-13
    ///     false M7 closure was FC5 in HERE's data and FC1 in ours, and the record kept no trace.
    /// </summary>
    int HereFunctionalClass = 5,
    /// <summary>
    ///     The locality HERE resolved at this incident's position, kept separate from
    ///     <see cref="Location" /> so it can anchor a report without ever becoming the road's name.
    /// </summary>
    Option<string> Suburb = default,
    /// <summary>
    ///     HERE's own segments for this record, full reference form with the tile and direction sign
    ///     intact, each with the length HERE reported. <see cref="SegmentRefs" /> is the Routing-API
    ///     form with the tile stripped, which cannot be resolved back to the map — the reason
    ///     historical incidents could not be checked against HERE's own topology.
    /// </summary>
    IReadOnlyList<SegmentSpan>? HereSegmentSpans = null,
    /// <summary>
    ///     True when the road name is the map's own name for this record's segments (verdict
    ///     <see cref="IdentityVerdict.Unambiguous" /> or <see cref="IdentityVerdict.Dominant" />).
    ///     An unverified name was never confirmed against HERE's topology and may never carry a
    ///     motorway-class or closure claim.
    /// </summary>
    bool RoadNameVerified = false,
    /// <summary>
    ///     HERE's <c>startTime</c> — when the incident becomes valid. Inert until the currency gate
    ///     reads it (RISK-38): every live Sydney closure carries one, and windows of 97–693 days are
    ///     long-term worksites rather than news.
    /// </summary>
    DateTimeOffset? StartTimeUtc = null,
    /// <summary>HERE's <c>endTime</c> — when the incident stops being valid. See <see cref="StartTimeUtc" />.</summary>
    DateTimeOffset? EndTimeUtc = null,
    /// <summary>
    ///     HERE's <c>codes</c> — the AlertC cause list (ISO 14819-2), most significant first. Present on
    ///     100% of live Sydney incidents and richer than <see cref="Type" />: 701, 500, 803 and 735 all
    ///     map to <c>construction</c>.
    /// </summary>
    IReadOnlyList<int>? Codes = null,
    /// <summary>
    ///     The asset HERE named as closed, when its description's subject is a thing rather than a
    ///     place — "Ramp to Alfred StreetNorth - Closed". A closure claim must name THIS, never the road
    ///     the ramp leaves (122 records aired as mainline closures).
    /// </summary>
    Option<string> ClosedAsset = default,
    /// <summary>
    ///     The map's <c>RAMP</c> flag for the length-weighted majority of this record's own segments.
    /// </summary>
    bool SegmentIsRamp = false,
    /// <summary>
    ///     Where <see cref="Direction" /> came from. <see cref="Models.DirectionSource.None" /> means the
    ///     record supports no direction and none was guessed: the compass-from-coverage-centre
    ///     derivation this replaces made direction a property of which station fetched the record.
    /// </summary>
    DirectionSource DirectionSource = DirectionSource.None,
    /// <summary>
    ///     The atomic, source-tagged reading of this record — the only shape a bulletin may make a
    ///     claim from. Null for incidents rebuilt outside the HERE pipeline (an old persisted job, a
    ///     regression fixture): such a record may still state its own condition, but never an anchor.
    /// </summary>
    IncidentFact? Fact = null,
    /// <summary>
    ///     Set by <see cref="Services.IncidentEligibility" /> when the record's claim was reduced
    ///     ("demoted:…") or the record was kept out of corridors entirely ("withheld:…"). None for a
    ///     record whose claims stood as fetched. Persisted to the job so the console can answer why a
    ///     record present in the evidence was never reported.
    /// </summary>
    Option<string> WithheldReason = default,
    /// <summary>
    ///     The authority's verbatim priority string. Its vocabulary is source-dependent; compare it
    ///     only through the source-aware <see cref="Services.AuthorityPriorityNormalizer" />, never
    ///     with string equality.
    /// </summary>
    Option<string> AuthorityPriority = default,
    /// <summary>
    ///     Provenance: the feeds this record came from ("here", "livetraffic-nsw", "qldtraffic", …).
    ///     Null means HERE — the overwhelmingly common case is left implicit rather than allocated
    ///     on every record. Multi-entry when fusion matched the same physical event across sources.
    /// </summary>
    IReadOnlyList<string>? Sources = null,
    /// <summary>Fields filled by the prose extractor rather than the source's structured payload.</summary>
    IReadOnlyList<string>? ProseFields = null,
    /// <summary>
    ///     Source-native current/stable identifiers retained for audit and cross-feed corroboration.
    ///     Null appears only on legacy rows or providers that supplied no identifier.
    /// </summary>
    IReadOnlyList<SourceRecordIdentity>? SourceRecordIdentities = null);

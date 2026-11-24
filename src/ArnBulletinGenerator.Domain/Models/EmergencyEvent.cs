namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Australian Warning System tier, as published by the state emergency services and normalised
///     by the aggregator. This is the ONLY escalation axis that carries an on-air meaning.
///     <para>
///         CAP-AU also supplies <c>severity</c>, <c>urgency</c> and <c>certainty</c>. They are
///         deliberately NOT modelled as the airing tier: measured against 500 live features on
///         2026-08-31, every <c>severity: Severe</c> record nationally was an ambulance pager case
///         ("Stab / Gun", "Cardiac"), because that feed derives CAP severity from case acuity. A
///         must-air rule bound to severity would have aired a stabbing callout. See
///     </para>
/// </summary>
public enum EmergencyWarningLevel
{
    /// <summary>No public warning attached — an incident the agency is simply working.</summary>
    None = 0,
    /// <summary>"Advice": an incident has started, no immediate action required.</summary>
    Advice = 1,
    /// <summary>"Watch and Act": conditions are changing, take action now to protect yourself.</summary>
    WatchAndAct = 2,
    /// <summary>"Emergency Warning": the highest tier — imminent threat to life.</summary>
    EmergencyWarning = 3
}

/// <summary>
///     One emergency-services incident inside a market's coverage area, normalised from the
///     aggregator's GeoJSON feature.
///     <para>
///         <b>This is situational awareness, never a road-network fact.</b> The upstream API carries
///         no closure, lane, delay or detour field of any kind, so a fire whose location string reads
///         "BRUXNER HWY, TABULAM" is a fire located on that highway — NOT a claim the highway is
///         shut. Road facts continue to come only from HERE and the road authorities. For this reason
///         emergency events are a separate evidence rail and are never fused into
///         <see cref="TrafficCorridor" />: attaching them to corridors would reproduce the 13 Aug
///         M7 false-closure defect.
///     </para>
/// </summary>
/// <param name="Id">
///     Revision-collapsed identity. The upstream ids of some feeds embed an update timestamp
///     (<c>nsw-rfs-cap-2026-08-31T12:45:00.0000000:673492</c>), so one live incident arrives 2-4
///     times; this holds the stable trailing record id and the newest revision wins.
/// </param>
/// <param name="SourceFeedId">Aggregator feed id, e.g. "nsw-rfs-cap", "qld-escad".</param>
/// <param name="Agency">Publishing agency, e.g. "RFS", "QFD".</param>
/// <param name="State">Lowercase jurisdiction code, e.g. "nsw".</param>
/// <param name="Title">The agency's own headline for the incident.</param>
/// <param name="WarningLevel">Australian Warning System tier.</param>
/// <param name="EventType">
///     Normalised type, e.g. "bushfire", "flood", "hazmat". Unreliable on some feeds: 100% of NSW RFS
///     CAP records classify as "other" even when the agency's own prose says "TYPE: Grass Fire", so
///     type must never be the sole filter.
/// </param>
/// <param name="EventCategory">Coarser grouping, e.g. "fire", "weather", "medical".</param>
/// <param name="Suburb">Locality, when the feed supplies one.</param>
/// <param name="Address">Street-level location string, when the feed supplies one.</param>
/// <param name="Latitude">Centroid latitude. Null when the feature carried no geometry.</param>
/// <param name="Longitude">Centroid longitude.</param>
/// <param name="DistanceKm">Great-circle distance from the coverage centre, for operator ordering.</param>
/// <param name="Status">Agency lifecycle state, e.g. "active", "contained", "controlled".</param>
/// <param name="ReportedUtc">When the agency first reported it.</param>
/// <param name="UpdatedUtc">When the agency last updated it — the revision-collapse tiebreak.</param>
/// <param name="SourceUrl">Deep link to the agency's own page, when published.</param>
/// <param name="Detail">The agency's own prose block, when published. Never rewritten here.</param>
/// <param name="Licence">
///     Source licence, e.g. "CC BY 4.0". Carried per record because it is not uniform: WA DFES is
///     CC BY-ND (no derivatives) and SA CFS is CC BY-NC-ND (non-commercial), neither of which a
///     commercial broadcast may use, while NSW RFS and QLD QFD are CC BY 4.0 with attribution.
/// </param>
/// <param name="AirEligible">
///     Whether this record cleared the licence allow-list AND the airable-category allow-list. False
///     records are shown to the operator (so suppression is visible rather than silent) but are never
///     candidates for air. Phase 1 airs nothing at all; this flag is what phase 2 will read.
/// </param>
public sealed record EmergencyEvent(
    string Id,
    string SourceFeedId,
    string Agency,
    string State,
    string Title,
    EmergencyWarningLevel WarningLevel,
    string EventType,
    string EventCategory,
    string? Suburb = null,
    string? Address = null,
    double? Latitude = null,
    double? Longitude = null,
    double? DistanceKm = null,
    string? Status = null,
    DateTimeOffset? ReportedUtc = null,
    DateTimeOffset? UpdatedUtc = null,
    string? SourceUrl = null,
    string? Detail = null,
    string Licence = "",
    bool AirEligible = false)
{
    /// <summary>
    ///     Operator-facing place, preferring the narrowest thing the feed gave us. Falls back to the
    ///     agency headline so a row is never blank.
    /// </summary>
    public string DisplayPlace =>
        !string.IsNullOrWhiteSpace(Address) ? Address!
        : !string.IsNullOrWhiteSpace(Suburb) ? Suburb!
        : Title;

    /// <summary>
    ///     Deterministic operator ordering: warning tier descending, then nearest first. Sorting here
    ///     rather than in the UI keeps the console, the persisted job and any later on-air selection
    ///     reading the same order.
    /// </summary>
    public static int CompareForOperator(EmergencyEvent a, EmergencyEvent b)
    {
        int byTier = ((int)b.WarningLevel).CompareTo((int)a.WarningLevel);
        if (byTier != 0) return byTier;

        int byEligibility = b.AirEligible.CompareTo(a.AirEligible);
        if (byEligibility != 0) return byEligibility;

        double da = a.DistanceKm ?? double.MaxValue;
        double db = b.DistanceKm ?? double.MaxValue;
        int byDistance = da.CompareTo(db);
        return byDistance != 0 ? byDistance : string.CompareOrdinal(a.Id, b.Id);
    }
}

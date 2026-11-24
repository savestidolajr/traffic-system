using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Normalizes an official road-authority event into a FIRST-CLASS <see cref="TrafficIncident" />
///     carrying its own <see cref="IncidentFact" /> — the same shape every HERE record takes — so
///     official events pass through the same claim gates, window rules, eligibility ledger and
///     impact formula as everything else.
///     <para>
///         This replaces the injection side-door (2026-08-18 design review): the previous
///         corroborator fabricated corridor shells with invented magnitudes (impact 85, 500 m
///         length, severity 1 "accident" for any event class) that bypassed the entire trust
///         boundary. Source-neutral rule: no source's events are second-class at the gate, and no
///         source's attributes are invented. Missing attributes stay missing — length 0 contributes
///         nothing to impact, road class is inferred from the authority's road-class flag or name —
///         so an official-only crash ranks from its real severity and type, never from a magic
///         constant.
///     </para>
/// </summary>
public static class OfficialIncidentNormalizer
{
    private static int rejectedRoadNameCount;

    /// <summary>
    ///     Number of official records whose placeholder or point-of-interest road name was removed.
    ///     The counter is process-wide so a fetch coordinator can snapshot it before and after one
    ///     fetch without making the normalizer depend on a logger.
    /// </summary>
    public static int RejectedRoadNameCount => Volatile.Read(ref rejectedRoadNameCount);

    /// <summary>Words in an official headline/description that evidence a genuine crash claim.</summary>
    private static readonly string[] CrashWords = ["crash", "collision", "accident", "rollover", "multi-vehicle"];

    /// <summary>Words that evidence the authority itself calls the road closed.</summary>
    private static readonly string[] ClosureWords = ["closed", "closure", "road blocked"];

    private static readonly string[] SignificantRoadTypes =
        ["motorway", "mwy", "freeway", "fwy", "highway", "hwy", "tunnel", "distributor", "parkway", "pkwy"];

    public static TrafficIncident ToIncident(OfficialTrafficEvent e, DateTimeOffset nowUtc)
    {
        string sourceRoad = e.RoadName ?? ExtractRoadNameFromHeadline(e.Headline);
        bool rejectedRoad = RoadNameFormatter.IsPlaceholderName(sourceRoad)
            || RoadNameFormatter.IsPointOfInterestName(sourceRoad);
        if (rejectedRoad)
            Interlocked.Increment(ref rejectedRoadNameCount);
        string road = rejectedRoad ? string.Empty : RoadDesignationAliases.Canonicalize(sourceRoad);


        // Once the fetch path has run the deterministic crosswalk, trust that authority-owned
        // class over prose words. Direct callers that bypass enrichment retain the historical
        // word fallback so they do not silently change semantics.
        bool isCrash = e.CanonicalClass.HasValue
            ? e.CanonicalClass.Value == IncidentClass.Crash
            : ContainsAny(e, CrashWords);
        bool claimsClosed = e.IsIncidentClass
            && (e.CanonicalClass == IncidentClass.Closure || ContainsAny(e, ClosureWords));
        string type = e.CanonicalClass.HasValue
            ? TypeForCanonicalClass(e.CanonicalClass.Value)
            : isCrash ? "accident" : e.IsIncidentClass ? "roadHazard" : "construction";

        // Condition from the event's own class, mapped through the SAME catalog HERE codes use, so
        // ClaimGate sees a real condition: crash → incident(214), hazard → hazardous(1001),
        // closure claim → closed(401), planned/roadwork → roadworks(701).
        int conditionCode = claimsClosed ? 401 : isCrash ? 214 : e.IsIncidentClass ? 1001 : 701;

        // The domain incident needs an internal key even when the authority omitted one, but that
        // fallback is deliberately NOT copied into OriginalId: it is not a source identity and must
        // never make two id-less authority records look like one event. Include the event's spatial
        // and temporal detail so same-road records with identical headlines remain distinct.
        string internalId = e.Id ?? Fnv1A(string.Join('|',
            e.Headline, road, e.CrossStreet, e.Suburb, e.Direction,
            e.StartTime?.ToString("O"), e.EndTime?.ToString("O"), e.Detail,
            string.Join(';', e.Shape.Select(p => $"{p.Lat:R},{p.Lng:R}"))));
        string stableId = $"official-{e.SourceId}-{internalId}";
        string originalId = e.Id ?? string.Empty;
        double? lat = e.Shape.Count > 0 ? e.Shape[0].Lat : null;
        double? lng = e.Shape.Count > 0 ? e.Shape[0].Lng : null;

        var fact = new IncidentFact(
            IncidentId: stableId,
            OriginalId: originalId,
            Condition: IncidentConditionCatalog.FromCodes([conditionCode]),
            Asset: AffectedAsset.Carriageway,
            // The authority names its own road: when present, that identity is as decisive as a
            // unanimous segment vote. Absent (headline-extracted or none), it stays unresolved and
            // the claim gates treat it exactly as an unnamed HERE record.
            RoadName: string.IsNullOrWhiteSpace(road) ? null : road,
            Identity: !rejectedRoad && e.RoadName is { Length: > 0 }
                ? IdentityVerdict.Unambiguous
                : IdentityVerdict.Unnamed,
            Anchor: Option.OfWhitespace(e.CrossStreet),
            SecondaryAnchor: default,
            RampTarget: default,
            Direction: Option.OfWhitespace(e.Direction),
            // The authority publishes this structurally; it is not HERE's prose. See
            // DirectionSource.AuthorityStructured for why the distinction is load-bearing.
            DirectionSource: string.IsNullOrWhiteSpace(e.Direction)
                ? DirectionSource.None
                : DirectionSource.AuthorityStructured,
            Suburb: Option.OfWhitespace(e.Suburb),
            RoadClosed: claimsClosed,
            Window: IncidentWindow.Classify(e.StartTime, e.EndTime, e.StartTime, nowUtc),
            LaneDetail: default,
            SourceDetail: Option.OfWhitespace(e.Detail),
            AuthorityPriority: Option.OfWhitespace(e.AuthorityPriority),
            AuthorityPriorityRank: AuthorityPriorityNormalizer.Normalize(e.SourceId, e.AuthorityPriority),
            Category: Option.OfWhitespace(e.Category),
            SubCategory: Option.OfWhitespace(e.SubCategory),
            ImpactType: Option.OfWhitespace(e.ImpactType),
            QualitativeDelay: Option.OfWhitespace(e.QualitativeDelay),
            SpeedLimitKph: e.SpeedLimitKph is > 0 ? Option<int>.Some(e.SpeedLimitKph.Value) : default,
            LanesClosed: e.LanesClosed is > 0 ? Option<int>.Some(e.LanesClosed.Value) : default,
            LanesTotal: e.LanesTotal is > 0 ? Option<int>.Some(e.LanesTotal.Value) : default,
            OtherAssets: e.OtherAssets is { Count: > 0 } ? e.OtherAssets : null,
            CanonicalClass: Option.OfNullable(e.CanonicalClass));

        return new TrafficIncident(
            Id: stableId,
            Description: e.Headline,
            Severity: isCrash ? 1 : 2,
            Location: road,
            Type: type,
            LengthMeters: 0, // no measured extent — contributes nothing to impact, never invented
            FunctionalClass: InferFunctionalClass(e, sourceRoad),
            Latitude: Option.OfNullable(lat),
            Longitude: Option.OfNullable(lng),
            Direction: Option.OfWhitespace(e.Direction),
            // Set here as well as on the Fact: IncidentFactBuilder rebuilds a fact FROM the incident,
            // so leaving this default silently reset an official direction's provenance to None the
            // moment anything re-derived the fact. The HERE path has always set both.
            DirectionSource: fact.DirectionSource,
            RoadClosed: claimsClosed,
            EntryTimeUtc: e.StartTime,
            OriginalId: originalId,
            Suburb: Option.OfWhitespace(e.Suburb),
            StartTimeUtc: e.StartTime,
            EndTimeUtc: e.EndTime,
            Fact: fact,
            Sources: [e.SourceId],
            ProseFields: e.ProseFields,
            SourceRecordIdentities: e.Id is { Length: > 0 } id
                ? [new SourceRecordIdentity(e.SourceId, id)]
                : null);
    }

    private static int InferFunctionalClass(OfficialTrafficEvent e, string road)
    {
        if (e.IsStateRoad is true)
            return 2;
        if (e.IsStateRoad is false)
            return 5;

        return road.Split([' ', '/', '-', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => SignificantRoadTypes.Contains(part, StringComparer.OrdinalIgnoreCase))
            ? 2
            : 5;
    }

    private static string TypeForCanonicalClass(IncidentClass incidentClass) =>
        incidentClass switch
        {
            IncidentClass.Crash => "accident",
            IncidentClass.Breakdown => "disabledVehicle",
            IncidentClass.Flooding => "roadHazard",
            IncidentClass.Roadworks => "construction",
            IncidentClass.Closure => "roadClosure",
            IncidentClass.Hazard => "roadHazard",
            IncidentClass.Congestion => "congestion",
            IncidentClass.SpecialEvent => "plannedEvent",
            IncidentClass.Ferry => "roadClosure",
            IncidentClass.Fire => "roadHazard",
            _ => "roadHazard"
        };

    private static bool ContainsAny(OfficialTrafficEvent e, string[] words) =>
        words.Any(w => e.Headline.Contains(w, StringComparison.OrdinalIgnoreCase)
                       || (e.Description?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false));

    /// <summary>Deterministic 32-bit FNV-1a over the invariant string, hex-encoded.</summary>
    internal static string Fnv1A(string text)
    {
        uint hash = 2166136261;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 16777619;
        }

        return hash.ToString("x8");
    }

    /// <summary>Best-effort road extraction from "Crash on M4 at Church St"-shaped headlines.</summary>
    internal static string ExtractRoadNameFromHeadline(string headline)
    {
        if (string.IsNullOrWhiteSpace(headline))
            return "";

        int onIdx = headline.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
        if (onIdx >= 0)
        {
            string rest = headline[(onIdx + 4)..].Trim();
            int atIdx = rest.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
            if (atIdx > 0)
                return rest[..atIdx].Trim();
            int nearIdx = rest.IndexOf(" near ", StringComparison.OrdinalIgnoreCase);
            if (nearIdx > 0)
                return rest[..nearIdx].Trim();
            int dashIdx = rest.IndexOf(" - ", StringComparison.OrdinalIgnoreCase);
            if (dashIdx > 0)
                return rest[..dashIdx].Trim();
            return rest;
        }

        int colonIdx = headline.IndexOf(':');
        if (colonIdx > 0 && colonIdx < headline.Length - 1)
        {
            string afterColon = headline[(colonIdx + 1)..].Trim();
            int atIdx = afterColon.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
            if (atIdx > 0)
                return afterColon[..atIdx].Trim();
            return afterColon;
        }

        return "";
    }
}

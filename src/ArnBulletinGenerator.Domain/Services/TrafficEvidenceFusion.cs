using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Configuration options governing evidence fusion across official road-authority feeds.
/// </summary>
/// <param name="CorroborateIncidents">
///     Demote stale closure wording/rank only when the feed supplies positive coverage evidence for
///     this road; a non-empty result alone is not coverage proof, so absence remains unknown.
/// </param>
/// <param name="InjectMissingOfficialIncidents">Official-only incident-class events become corridors.</param>
/// <param name="CorroborationGraceMinutes">
///     Boundary grace used both for fresh HERE closure protection and for official event windows:
///     a present start more than this far in the future or end more than this far in the past is
///     inactive. Missing times are unknown and therefore allowed.
/// </param>
/// <param name="FreshSourceIds">
///     Sources whose payload was fetched live for this bulletin. A null set preserves the
///     historical all-fresh behavior for callers that do not use cache metadata; when present,
///     only these sources can prove authority road coverage for closure demotion.
/// </param>
/// <param name="Exclusions">
///     Records the operator ruled out. Applied to the authority events on the way in, so an omitted
///     authority record neither becomes an official-only corridor nor rides along as a corroborating
///     receipt on someone else's. Null means nothing is excluded.
/// </param>
public sealed record CorroborationOptions(
    bool Enabled = true,
    bool CorroborateIncidents = true,
    bool InjectMissingOfficialIncidents = true,
    double MaxCorroborationDistanceMeters = 500.0,
    int CorroborationGraceMinutes = 30,
    IReadOnlySet<string>? FreshSourceIds = null,
    IncidentExclusions? Exclusions = null);

/// <summary>
///     Source-neutral evidence fusion (2026-08-18 design review): every source's events are equal
///     citizens. HERE remains the attribute-richest feed (geometry, flow, extents) but holds no
///     privileged position at the gate or in the ranking.
///     <list type="bullet">
///         <item><b>Match</b> — an official event matching a HERE corridor (canonical road name or
///         shape distance) merges as provenance: the corridor's <see cref="TrafficCorridor.Sources" />
///         lists both feeds, and per-attribute precedence applies — the authority wins on the
///         event's AGE (its start date re-windows a rolled HERE record), HERE wins on geometry and
///         measured extent.</item>
///         <item><b>Official-only</b> — an unmatched incident-class event is normalized into a
///         first-class incident (<see cref="OfficialIncidentNormalizer" />), passes the SAME
///         eligibility boundary as HERE records, and aggregates through the SAME corridor machinery
///         and impact formula. No invented magnitudes, no side door.</item>
///         <item><b>Uncorroborated</b> — a HERE closure with no road-level coverage proof is retained:
///         absence of an authority record is unknown, never evidence that the closure is false.</item>
///     </list>
/// </summary>
public static class TrafficEvidenceFusion
{
    public static IReadOnlyList<TrafficCorridor> Fuse(
        IReadOnlyList<TrafficCorridor> hereCorridors,
        IReadOnlyList<OfficialTrafficEvent> officialEvents,
        CorroborationOptions options,
        DateTimeOffset nowUtc)
    {
        // Filter the authority events once, at the boundary, rather than at each of the places
        // below that normalizes one: an excluded record must be invisible to matching AND to the
        // official-only path. Normalizing here costs what the match loop would have spent anyway.
        if (options.Exclusions is { IsEmpty: false })
            officialEvents = officialEvents
                .Where(e => !options.Exclusions.Excludes(
                    OfficialIncidentNormalizer.ToIncident(e, nowUtc)))
                .ToList();

        if (!options.Enabled || hereCorridors.Count == 0 && officialEvents.Count == 0)
            return hereCorridors;

        var result = new List<TrafficCorridor>();
        var matchedOfficial = new HashSet<int>();

        foreach (var corridor in hereCorridors)
        {
            var matches = new List<(int Index, double Distance, bool ByName)>();
            for (int j = 0; j < officialEvents.Count; j++)
            {
                if (!IsActiveNow(officialEvents[j], nowUtc, options.CorroborationGraceMinutes))
                    continue;

                var (dist, byName) = MatchToCorridor(corridor, officialEvents[j]);
                if (dist <= options.MaxCorroborationDistanceMeters)
                    matches.Add((j, dist, byName));
            }

            if (matches.Count > 0)
            {
                foreach (var match in matches)
                    matchedOfficial.Add(match.Index);

                // Planned works enrich provenance and receipts but are not a second observation of the
                // current condition: Sources/Incidents retain every contributing feed for operators,
                // while corroboratingSourceIds counts only non-planned observations for tiering and
                // stale-closure demotion. A scheduling permit is a document, not an observation.
                var ordered = matches
                    .OrderBy(m => officialEvents[m.Index].IsPlanned == true ? 1 : 0)
                    .ThenBy(m => m.Distance)
                    .ThenByDescending(m => officialEvents[m.Index].PublishedTime ?? DateTimeOffset.MinValue)
                    .ThenBy(m => officialEvents[m.Index].SourceId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(m => officialEvents[m.Index].Id ?? "", StringComparer.Ordinal)
                    .ThenBy(m => officialEvents[m.Index].Headline, StringComparer.Ordinal)
                    .ThenBy(m => officialEvents[m.Index].RoadName ?? "", StringComparer.Ordinal)
                    .ThenBy(m => officialEvents[m.Index].CrossStreet ?? "", StringComparer.Ordinal)
                    .ThenBy(m => officialEvents[m.Index].StartTime ?? DateTimeOffset.MinValue)
                    .ThenBy(m => officialEvents[m.Index].EndTime ?? DateTimeOffset.MaxValue)
                    .ToList();
                var primary = ordered[0];
                var receipts = ordered
                    .GroupBy(m => officialEvents[m.Index].SourceId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .Select(m => OfficialEvidence.From(
                        officialEvents[m.Index],
                        m.ByName ? "road-name" : $"distance-{m.Distance:F0}m"))
                    .ToList();
                var corroboratingSourceIds = ordered
                    .Where(m => officialEvents[m.Index].IsPlanned != true)
                    .Select(m => officialEvents[m.Index].SourceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var contributingSourceIds = ordered
                    .Select(m => officialEvents[m.Index].SourceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var incidentAssignments = BuildIncidentAssignments(corridor, ordered, officialEvents);
                result.Add(MergeOfficialIntoCorridor(
                    corridor, officialEvents[primary.Index], nowUtc, receipts,
                    contributingSourceIds, corroboratingSourceIds, incidentAssignments,
                    IsSourceFresh(officialEvents[primary.Index], options)));
                continue;
            }
            result.Add(DemoteIfUncorroboratedStaleClosure(
                corridor, officialEvents, options, nowUtc));
        }

        if (options.InjectMissingOfficialIncidents)
            result.AddRange(BuildOfficialOnlyCorridors(officialEvents, matchedOfficial, result, nowUtc,
                options.CorroborationGraceMinutes));

        return result;
    }
    /// <summary>
    ///     A matched corridor gains official detail, provenance, and receipts. Planned works remain
    ///     enrichment (including their receipt and source attribution) but do not add a corroborating
    ///     source; a non-planned event is preferred as the authority anchor. Per-attribute precedence:
    ///     the authority wins on the event's AGE. A worksite HERE re-issues daily with a rolled start
    ///     time, but the authority carries the true start and can demote a stale closure claim to
    ///     standing roadworks.
    /// </summary>
    private static TrafficCorridor MergeOfficialIntoCorridor(
        TrafficCorridor corridor, OfficialTrafficEvent official, DateTimeOffset nowUtc,
        IReadOnlyList<OfficialEvidence> receipts, IReadOnlyList<string> contributingSourceIds,
        IReadOnlyList<string> corroboratingSourceIds,
        IncidentEvidenceAssignments incidentAssignments, bool officialFresh)
    {
        // Sources is operator-facing provenance: retain every feed that contributed anything,
        // including planned works. CorroborationCount is separate current-condition evidence and
        // therefore counts HERE's observation plus only distinct non-planned authority sources.
        var currentObservationSources = new HashSet<string>(
            [EvidenceSources.Here, .. corroboratingSourceIds],
            StringComparer.OrdinalIgnoreCase);
        var merged = corridor with
        {
            Sources = contributingSourceIds.Aggregate(
                corridor.Sources,
                (sources, sourceId) => AppendSource(sources, sourceId)),
            CorroboratingSourceCount = Math.Max(1, currentObservationSources.Count),
            Incidents = corridor.Incidents
                .Select((i, incidentIndex) =>
                {
                    var assignedSources = incidentAssignments.Sources.TryGetValue(
                        incidentIndex, out var sourcesForIncident)
                        ? sourcesForIncident
                        : [];
                    var sources = assignedSources.Aggregate(
                        i.Sources,
                        (existing, sourceId) => AppendSource(existing, sourceId));
                    var identities = incidentAssignments.Identities.TryGetValue(
                        incidentIndex, out var identitiesForIncident)
                        ? identitiesForIncident
                        : null;
                    return i with
                    {
                        Sources = sources,
                        SourceRecordIdentities = identities is { Count: > 0 }
                            ? AppendIdentities(i.SourceRecordIdentities, identities)
                            : i.SourceRecordIdentities
                    };
                })
                .ToList(),
            OfficialHeadline = Option.OfWhitespace(official.Headline),
            OfficialCrossStreet = Option.OfWhitespace(official.CrossStreet),
            OfficialEvidences = BoundOfficialEvidences(
                (corridor.OfficialEvidences ?? []).Concat(receipts))
        };

        // The age signal, chosen explicitly: the event's own start when the authority gave one,
        // else its publication time — a record the authority has carried for more than a day is
        // at least that old, while a future-scheduled start correctly reads as not-yet-started.
        DateTimeOffset? officialAge = official.StartTime ?? official.PublishedTime;
        bool officialSaysOldPlanned = officialFresh
                                      && official.IsPlanned == true
                                      && officialAge is { } age
                                      && nowUtc - age > TimeSpan.FromHours(24);
        bool claimsClosure = merged.Incidents.Any(i => i.RoadClosed);
        if (!officialSaysOldPlanned || !claimsClosure)
            return merged;

        return DemoteClosureWording(merged, "demoted:official-start-scheduled", capImpact: false);
    }
    private sealed record IncidentEvidenceAssignments(
        IReadOnlyDictionary<int, IReadOnlyList<string>> Sources,
        IReadOnlyDictionary<int, IReadOnlyList<SourceRecordIdentity>> Identities);

    private static IncidentEvidenceAssignments BuildIncidentAssignments(
        TrafficCorridor corridor,
        IReadOnlyList<(int Index, double Distance, bool ByName)> matches,
        IReadOnlyList<OfficialTrafficEvent> officialEvents)
    {
        var sourceAssignments = new Dictionary<int, List<string>>();
        var identityAssignments = new Dictionary<int, List<SourceRecordIdentity>>();
        foreach (var match in matches)
        {
            var official = officialEvents[match.Index];
            // Planned authority records still contribute per-incident provenance and identities;
            // only the corridor-level corroboration count excludes them.
            if (string.IsNullOrWhiteSpace(official.SourceId))
                continue;

            int incidentIndex = NearestIncidentIndex(corridor.Incidents, official);
            if (incidentIndex < 0)
                continue;

            if (!sourceAssignments.TryGetValue(incidentIndex, out var sources))
            {
                sources = [];
                sourceAssignments[incidentIndex] = sources;
            }
            if (!sources.Contains(official.SourceId, StringComparer.OrdinalIgnoreCase))
                sources.Add(official.SourceId);

            if (string.IsNullOrWhiteSpace(official.Id))
                continue;

            if (!identityAssignments.TryGetValue(incidentIndex, out var identities))
            {
                identities = [];
                identityAssignments[incidentIndex] = identities;
            }

            var identity = new SourceRecordIdentity(official.SourceId, official.Id);
            if (!identities.Any(existing =>
                    string.Equals(existing.SourceId, identity.SourceId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.RecordId, identity.RecordId, StringComparison.Ordinal)))
                identities.Add(identity);
        }

        return new IncidentEvidenceAssignments(
            sourceAssignments.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value),
            identityAssignments.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<SourceRecordIdentity>)pair.Value));
    }

    private static int NearestIncidentIndex(
        IReadOnlyList<TrafficIncident> incidents,
        OfficialTrafficEvent official)
    {
        if (incidents.Count == 0)
            return -1;
        if (incidents.Count == 1)
            return 0;
        if (official.Shape.Count == 0)
            return -1;

        int bestIndex = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < incidents.Count; i++)
        {
            var shape = IncidentPoints(incidents[i]);
            if (shape.Count == 0)
                continue;

            double distance = TrafficFeedComparison.MinDistanceMeters(shape, official.Shape);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static IReadOnlyList<CoveragePoint> IncidentPoints(TrafficIncident incident)
    {
        if (incident.Shape is { Count: > 0 })
            return incident.Shape;
        if (incident.Latitude.HasValue && incident.Longitude.HasValue)
            return [new CoveragePoint(incident.Latitude.Value, incident.Longitude.Value)];
        return [];
    }



    /// <summary>
    ///     Demote only when the authority has supplied a CURRENT event naming this exact road and
    ///     therefore demonstrates that it is actively reporting on that road without reporting a
    ///     closure. This preserves the 2026-08-18 Warringah pilot fix (a stale HERE closure really
    ///     aired as "still closed"; 72f0c9d4 added wording demotion after that defect), while
    ///     avoiding the unsafe old rule that treated any non-empty market feed as coverage.
    ///     Same-road name coverage is only a positive jurisdiction signal, not proof that a
    ///     20-km arterial's far end is clear; the <1 km corridor clustering makes that signal
    ///     materially stronger. Without it, preserve HERE's closure claim.
    /// </summary>
    private static bool IsSourceFresh(OfficialTrafficEvent official, CorroborationOptions options)
        => options.FreshSourceIds is null
            || options.FreshSourceIds.Contains(official.SourceId);

    private static TrafficCorridor DemoteIfUncorroboratedStaleClosure(
        TrafficCorridor corridor, IReadOnlyList<OfficialTrafficEvent> officialEvents,
        CorroborationOptions options, DateTimeOffset nowUtc)
    {
        bool isClosureOrWorks = corridor.PrimaryType.Equals("roadClosure", StringComparison.OrdinalIgnoreCase)
                                || corridor.PrimaryType.Equals("construction", StringComparison.OrdinalIgnoreCase)
                                || corridor.Incidents.Any(inc => inc.RoadClosed);
        bool pastGrace = !HasFreshClosureEvidence(corridor, nowUtc,
            TimeSpan.FromMinutes(options.CorroborationGraceMinutes));
        bool authorityCoversRoad = officialEvents.Any(off =>
            IsSourceFresh(off, options)
            && IsActiveNow(off, nowUtc, options.CorroborationGraceMinutes)
            && OfficialRoadNames(off.RoadName).Any(name => RoadNameFormatter.IsSameRoad(
                RoadDesignationAliases.Canonicalize(corridor.RoadName),
                RoadDesignationAliases.Canonicalize(name))));

        return options.CorroborateIncidents && isClosureOrWorks && pastGrace && authorityCoversRoad
            ? DemoteClosureWording(corridor, "demoted:uncorroborated-closure", capImpact: true)
            : corridor;
    }

    /// <summary>
    ///     True when any closure-claiming record on the corridor carries HERE evidence (entry or
    ///     start) newer than <paramref name="grace" /> — such a corridor is breaking news and is
    ///     exempt from uncorroborated demotion until the official feed has had time to catch up.
    /// </summary>
    private static bool HasFreshClosureEvidence(TrafficCorridor corridor, DateTimeOffset nowUtc, TimeSpan grace)
    {
        foreach (var inc in corridor.Incidents)
        {
            if (!inc.RoadClosed && !inc.Type.Equals("construction", StringComparison.OrdinalIgnoreCase))
                continue;
            if (inc.EntryTimeUtc is { } entry && nowUtc - entry < grace)
                return true;
            if (inc.StartTimeUtc is { } start && nowUtc - start < grace)
                return true;
        }

        return false;
    }

    private static TrafficCorridor DemoteClosureWording(
        TrafficCorridor corridor, string reason, bool capImpact)
    {
        var demotedIncidents = corridor.Incidents
            .Select(inc => inc.RoadClosed
                ? inc with
                {
                    RoadClosed = false,
                    Type = "construction",
                    WithheldReason = Option<string>.Some(reason)
                }
                : inc)
            .ToList();

        return corridor with
        {
            PrimaryType = corridor.PrimaryType.Equals("roadClosure", StringComparison.OrdinalIgnoreCase)
                ? "construction"
                : corridor.PrimaryType,
            Incidents = demotedIncidents,
            ImpactScore = capImpact ? Math.Min(corridor.ImpactScore, 10.0) : corridor.ImpactScore
        };
    }

    /// <summary>
    ///     Unmatched official incident-class events become corridors through the SAME machinery as
    ///     every other record: normalize → eligibility boundary → corridor aggregation → the shared
    ///     impact formula on their real attributes. An event whose claims cannot survive the gates
    ///     (no resolvable road, unsupported closure) is withheld exactly as a HERE record would be.
    /// </summary>
    private static IEnumerable<TrafficCorridor> BuildOfficialOnlyCorridors(
        IReadOnlyList<OfficialTrafficEvent> officialEvents, HashSet<int> matchedOfficial,
        List<TrafficCorridor> existing, DateTimeOffset nowUtc, int graceMinutes)
    {
        var normalized = new List<TrafficIncident>();
        for (int j = 0; j < officialEvents.Count; j++)
        {
            var off = officialEvents[j];
            if (matchedOfficial.Contains(j)
                || !off.IsIncidentClass
                || off.IsPlanned == true
                || !IsActiveNow(off, nowUtc, graceMinutes))
                continue;

            // No geometry, no injection: an authority record that cannot be placed inside
            // the coverage area must not become a corridor on the strength of its words
            // alone. Geometry-less feeds (TasALERT RSS) still corroborate by road name.
            if (off.Shape.Count == 0)
                continue;

            var incident = OfficialIncidentNormalizer.ToIncident(off, nowUtc);
            if (string.IsNullOrWhiteSpace(incident.Location))
                continue;

            // Same-road dedupe against everything already reported, alias-canonicalized on both
            // sides so "M4" and "Western Motorway" cannot duplicate a road.
            string road = RoadDesignationAliases.Canonicalize(incident.Location);
            if (existing.Any(r => string.Equals(
                    RoadDesignationAliases.Canonicalize(r.RoadName), road, StringComparison.OrdinalIgnoreCase)))
                continue;

            normalized.Add(incident);
        }

        if (normalized.Count == 0)
            return [];

        var eligible = IncidentEligibility.Transform(normalized).Incidents
            .Where(i => !IncidentEligibility.IsWithheld(i))
            .ToList();

        // The aggregator derives Sources from the member records itself; the authority's own
        // record rides along as the operator-facing receipt.
        var eventByIncidentId = new Dictionary<string, OfficialTrafficEvent>(StringComparer.Ordinal);
        for (int j = 0; j < officialEvents.Count; j++)
        {
            if (!matchedOfficial.Contains(j)
                && officialEvents[j].IsIncidentClass
                && officialEvents[j].IsPlanned != true
                && IsActiveNow(officialEvents[j], nowUtc, graceMinutes))
                eventByIncidentId[OfficialIncidentNormalizer.ToIncident(officialEvents[j], nowUtc).Id] =
                    officialEvents[j];
        }

        return CorridorAggregator.AggregateByRoad(eligible, int.MaxValue)
            .Select(c => c with
            {
                OfficialHeadline = Option.OfWhitespace(c.Incidents[0].Description),
                OfficialEvidences = BoundOfficialEvidences(c.Incidents
                    .Select(i => eventByIncidentId.TryGetValue(i.Id, out var src)
                        ? OfficialEvidence.From(src, "official-only")
                        : null)
                    .OfType<OfficialEvidence>())
            });
    }

    private const int MaxOfficialEvidenceReceipts = 4;

    /// <summary>
    ///     Keeps one receipt per authority, preferring records with an explicit priority signal,
    ///     then the most severe source-aware rank, then the original feed order. Four receipts is
    ///     the measured bound: a realistic 15-corridor × 4-receipt fixture serialized to 58,657
    ///     UTF-8 bytes raw and 2,399 gzip+base64 characters (61,056 bytes including both forms),
    ///     below Table Storage's 64 KB property and 1 MB entity limits.
    /// </summary>
    private static IReadOnlyList<OfficialEvidence> BoundOfficialEvidences(
        IEnumerable<OfficialEvidence> evidences)
    {
        var uniqueBySource = evidences
            .GroupBy(e => e.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select((evidence, index) => new
            {
                Evidence = evidence,
                Index = index,
                HasPrioritySignal = !string.IsNullOrWhiteSpace(evidence.AuthorityPriority),
                Priority = AuthorityPriorityNormalizer.Normalize(
                    evidence.SourceId, evidence.AuthorityPriority)
            })
            .OrderBy(x => x.HasPrioritySignal ? 0 : 1)
            .ThenBy(x => x.Priority.HasValue ? x.Priority.Value : int.MaxValue)
            .ThenBy(x => x.Index)
            .Take(MaxOfficialEvidenceReceipts)
            .Select(x => x.Evidence)
            .ToList();

        return uniqueBySource;
    }

    private static IReadOnlyList<string> AppendSource(IReadOnlyList<string>? sources, string sourceId)
    {
        var list = new List<string>(EvidenceSources.Of(sources));
        if (!list.Contains(sourceId, StringComparer.OrdinalIgnoreCase))
            list.Add(sourceId);
        return list;
    }

    private static IReadOnlyList<SourceRecordIdentity> AppendIdentities(
        IReadOnlyList<SourceRecordIdentity>? identities,
        IReadOnlyList<SourceRecordIdentity> additions)
    {
        var list = (identities ?? []).ToList();
        foreach (var addition in additions)
        {
            if (!list.Any(existing => string.Equals(existing.SourceId, addition.SourceId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.RecordId, addition.RecordId, StringComparison.Ordinal)))
                list.Add(addition);
        }

        return list;
    }

    /// <summary>
    ///     A present official time is allowed only while it plausibly overlaps now. The existing
    ///     corroboration grace is deliberately used as the boundary: feeds can lag by a few
    ///     minutes, but a clearly future or expired record cannot corroborate or be injected.
    ///     Missing times are unknown, not failures.
    /// </summary>
    private static bool IsActiveNow(
        OfficialTrafficEvent official, DateTimeOffset nowUtc, int graceMinutes)
    {
        var grace = TimeSpan.FromMinutes(Math.Max(0, graceMinutes));
        if (official.StartTime is { } start && start > nowUtc + grace)
            return false;
        if (official.EndTime is { } end && end < nowUtc - grace)
            return false;
        return true;
    }

    /// <summary>
    ///     How close an official event is to a corridor, with the branch that decided it.
    ///     <list type="bullet">
    ///         <item>Any of the authority's named roads (records name compounds: "Pitt St,
    ///         Macquarie St &amp; Phillip St", "Gore Hill Freeway and Warringah Freeway",
    ///         "Cairns Western Arterial Road (Pease Street)") is the SAME road as the corridor —
    ///         abbreviation-aware and designation-canonicalized — → authoritative match.</item>
    ///         <item>The authority names roads and NONE is the corridor's road → NO match at any
    ///         distance: a worksite 300 m away on a DIFFERENT named road is a different claim
    ///         (observed live 2026-08-18: Luddenham Rd paired with the Mamre Road upgrade at
    ///         McIntyre Avenue, 297 m; Darling Drive with Western Distributor bridge work, 311 m).</item>
    ///         <item>The record names no specific road ("Various Roads", empty) → geometry
    ///         proximity decides, as before.</item>
    ///     </list>
    ///     The branch is returned — never inferred from the distance — because a genuine geometry
    ///     match can also be exactly 0 m.
    /// </summary>
    private static (double Dist, bool ByName) MatchToCorridor(
        TrafficCorridor corridor, OfficialTrafficEvent official)
    {
        if (official.Shape.Count == 0)
            return (double.MaxValue, false);

        var corridorPoints = new List<CoveragePoint>();
        if (corridor.Latitude.HasValue && corridor.Longitude.HasValue)
            corridorPoints.Add(new CoveragePoint(corridor.Latitude.Value, corridor.Longitude.Value));

        foreach (var inc in corridor.Incidents)
        {
            if (inc.Latitude.HasValue && inc.Longitude.HasValue)
                corridorPoints.Add(new CoveragePoint(inc.Latitude.Value, inc.Longitude.Value));
            if (inc.Shape is { Count: > 0 })
                corridorPoints.AddRange(inc.Shape);
        }

        var namedRoads = OfficialRoadNames(official.RoadName);
        if (namedRoads.Count > 0)
        {
            string corridorRoad = RoadDesignationAliases.Canonicalize(corridor.RoadName);
            bool sameRoad = namedRoads.Any(n => RoadNameFormatter.IsSameRoad(
                corridorRoad, RoadDesignationAliases.Canonicalize(n)));
            // A specifically-named different road can never corroborate this one, however close.
            if (!sameRoad)
                return (double.MaxValue, false);

            // A same-road event must pass the distance threshold whenever HERE supplied usable
            // geometry. If HERE has no points, identity is the only available evidence and remains
            // sufficient; rejecting it would turn a missing measurement into a false mismatch.
            return corridorPoints.Count > 0
                ? (TrafficFeedComparison.MinDistanceMeters(corridorPoints, official.Shape), true)
                : (0.0, true);
        }

        if (corridorPoints.Count == 0)
            return (double.MaxValue, false);

        return (TrafficFeedComparison.MinDistanceMeters(corridorPoints, official.Shape), false);
    }

    /// <summary>Placeholder "names" that identify no road and must not gate proximity matching.</summary>
    private static readonly string[] NonSpecificRoadNames = ["various roads", "various", "multiple roads"];

    /// <summary>
    ///     The individual roads an authority record names. Records use compound forms — comma/
    ///     ampersand/"and" lists and parenthesised aliases — and each named part is a road claim
    ///     in its own right.
    /// </summary>
    internal static IReadOnlyList<string> OfficialRoadNames(string? roadName)
    {
        if (string.IsNullOrWhiteSpace(roadName))
            return [];

        var parts = roadName
            .Replace("(", ",").Replace(")", ",")
            .Replace(" & ", ",").Replace("/", ",").Replace(" and ", ",", StringComparison.OrdinalIgnoreCase)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length > 1 && !NonSpecificRoadNames.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return parts;
    }
}

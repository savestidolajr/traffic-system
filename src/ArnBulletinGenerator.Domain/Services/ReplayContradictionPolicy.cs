using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Decides whether a script from an earlier bulletin may be re-aired against the traffic data
///     fetched for THIS bulletin.
///     <para>
///         RISK-37b/38. Fallback replays a previous script and audio so a slot is never dead air, and
///         nothing compares the replayed claims against the fresh fetch sitting in the same job.
///         Measured on the dev corpus: <b>53 of 54 replays</b> voice a road absent from their own
///         evidence. Melbourne 13 Aug is the worked case — job 5ff01455 at 04:55 legitimately aired a
///         Greensborough Bypass closure (HERE: <c>roadClosure</c>, <c>roadClosed: true</c>, "Ramp to M80
///         Metropolitan Ring Road (Southbound) - Closed"); the same script then replayed <b>15 times,
///         all Published, through 07:45</b>, while each of those jobs had fetched HERE data showing
///         <c>construction</c>, <c>roadClosed: false</c>, "Between Lower Plenty Road and Watsonia Road -
///         Lane blocked", and later no Greensborough record at all.
///     </para>
///     A replay is refused only on a CLOSURE claim: it is the claim that routes listeners into a road
///     that may be open, and it is the one claim the fresh fetch can corroborate outright through
///     HERE's own <c>roadClosed</c> flag. Slower claims ("busy", "delays") are not adjudicated here.
/// </summary>
public static class ReplayContradictionPolicy
{
    /// <summary>
    ///     True when <paramref name="script" /> makes a claim the freshly fetched corridors contradict —
    ///     a closure that has cleared or downgraded, or a road that is no longer reported at all.
    /// </summary>
    /// <param name="script">The script proposed for re-air.</param>
    /// <param name="freshCorridors">Corridors fetched for the bulletin now being produced.</param>
    public static bool Contradicts(string script, IReadOnlyList<TrafficCorridor> freshCorridors) =>
        Contradictions(script, freshCorridors).Count > 0;

    /// <summary>
    ///     The specific contradicted claims, for the job log and the operator console. Empty when the
    ///     replay is safe.
    /// </summary>
    public static IReadOnlyList<string> Contradictions(
        string script, IReadOnlyList<TrafficCorridor> freshCorridors)
    {
        if (string.IsNullOrWhiteSpace(script))
            return [];

        var contradictions = new List<string>();
        foreach (string road in ClosureClaimExtractor.ClaimedClosedRoads(script))
        {
            if (FreshFetchConfirmsClosure(road, freshCorridors))
                continue;

            contradictions.Add(
                $"the script says {road} is closed, but this bulletin's own HERE fetch does not report " +
                $"{road} as closed");
        }

        return contradictions;
    }

    /// <summary>
    ///     The same adjudication over a job's PERSISTED evidence — the shapes a covered slot actually
    ///     holds when the fallback chain runs. <paramref name="corridors" /> and
    ///     <paramref name="incidents" /> are this bulletin's own fresh fetch; the caller must not pass
    ///     a job that never fetched (an absent fetch cannot corroborate or contradict anything).
    /// </summary>
    public static IReadOnlyList<string> Contradictions(
        string script, IReadOnlyList<JobCorridor> corridors, IReadOnlyList<JobIncident> incidents)
    {
        if (string.IsNullOrWhiteSpace(script))
            return [];

        var contradictions = new List<string>();
        foreach (string road in ClosureClaimExtractor.ClaimedClosedRoads(script))
        {
            bool confirmed =
                corridors.Any(c =>
                    (RoadNameFormatter.IsSameRoad(c.RoadName, road) || RoadNameFormatter.ContainsRoad(c.RoadName, road))
                    && IncidentTypeClassifier.IsClosure(c.PrimaryType))
                || incidents.Any(i =>
                    (RoadNameFormatter.IsSameRoad(i.Location, road) || RoadNameFormatter.ContainsRoad(i.Location, road))
                    && i.RoadClosed);
            if (confirmed)
                continue;

            contradictions.Add(
                $"the script says {road} is closed, but this bulletin's own HERE fetch does not report " +
                $"{road} as closed");
        }

        return contradictions;
    }

    /// <summary>
    ///     True when a corridor on the same road carries a live closure in THIS bulletin's data. The
    ///     corridor's road name and its incidents' own locations are both accepted, because a corridor
    ///     can be named from the map while the closure record's location still reads as HERE's
    ///     description ("Ramp to M80 Metropolitan Ring Road (Southbound)").
    /// </summary>
    private static bool FreshFetchConfirmsClosure(string road, IReadOnlyList<TrafficCorridor> freshCorridors) =>
        freshCorridors.Any(corridor =>
            NamesSameRoad(corridor, road)
            && (corridor.Incidents.Any(i => i.RoadClosed) || IncidentTypeClassifier.IsClosure(corridor.PrimaryType)));

    private static bool NamesSameRoad(TrafficCorridor corridor, string road) =>
        RoadNameFormatter.IsSameRoad(corridor.RoadName, road)
        || RoadNameFormatter.ContainsRoad(corridor.RoadName, road)
        || corridor.Incidents.Any(i =>
            RoadNameFormatter.IsSameRoad(i.Location, road) || RoadNameFormatter.ContainsRoad(i.Location, road));
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Decides which flowing roads air as "good runs" and in what role, per the confirmed editorial
///     rule (2026-06-23):
///     <list type="number">
///         <item>no problem corridors → the bulletin MUST fill with the top flowing roads;</item>
///         <item>
///             problem corridors present → only STATION-WEIGHTED roads air, UNLESS the incidents
///             don't fill the slot, in which case generic flowing roads are added as last-resort
///             fill so the bulletin never ships dead air (the hybrid resolution);
///         </item>
///         <item>
///             an operator's explicit Flow-tab pick overrides the rule — still data-backed, since
///             only roads present in the live flow pool are honoured.
///         </item>
///     </list>
///     Selection drives the script; the full ranked pool is persisted separately for the Flow tab.
/// </summary>
public static partial class GoodRunPolicy
{
    /// <summary>On-air role for the selected good runs, used to steer the draft prompt's tone.</summary>
    public enum Mode
    {
        /// <summary>No incidents — the flowing roads ARE the bulletin body.</summary>
        Fill,

        /// <summary>Incidents lead — flowing roads are a brief secondary mention.</summary>
        Secondary
    }

    public sealed record Selection(IReadOnlyList<string> Roads, Mode Mode);

    /// <summary>
    ///     Removes every flowing road that ANY incident HERE returned names — taking the raw incident
    ///     road names rather than the corridor set, which the caller has already capped at
    ///     <c>BroadcastOptions.MaxCorridorsForStorage</c>. An incident on a road that lost the cap
    ///     therefore never suppressed its road from the flow pool, so HERE could report a crash on a
    ///     road the same bulletin called "moving nicely". Placeholder locations HERE gave no usable
    ///     name for are ignored, so an unnamed incident cannot silently empty the pool.
    /// </summary>
    public static IReadOnlyList<FlowRoad> ExcludeIncidentRoads(
        IReadOnlyList<FlowRoad> flowPool,
        IReadOnlyList<string> incidentRoadNames)
    {
        if (flowPool.Count == 0 || incidentRoadNames.Count == 0)
            return flowPool;

        var named = incidentRoadNames
            .Where(n => !string.IsNullOrWhiteSpace(n)
                && n.Trim() is not ("Unidentified Road" or "Unknown Location"))
            .ToList();

        // Abbreviation-aware, not substring: HERE writes "Hume Hwy" where the flow feed says "Hume
        // Highway", and a substring test both misses that and false-matches "Bay St" inside
        // "Botany Bay St".
        return named.Count == 0
            ? flowPool
            : flowPool.Where(f => !named.Any(road => RoadNameFormatter.IsSameRoad(f.RoadName, road))).ToList();
    }

    /// <param name="flowPool">Ranked, data-backed flowing roads (weighted flag already applied).</param>
    /// <param name="problemCorridorCount">Selected incident/congestion corridors in the bulletin.</param>
    /// <param name="slotCorridorCapacity">Corridor slots the target duration can hold (fill threshold).</param>
    /// <param name="maxGoodRuns">Hard cap on the number of good-run roads fed to the script.</param>
    /// <param name="operatorSelection">Explicit Flow-tab pick (road names), when the operator curated.</param>
    public static Selection SelectForGeneration(
        IReadOnlyList<FlowRoad> flowPool,
        int problemCorridorCount,
        int slotCorridorCapacity,
        int maxGoodRuns,
        Option<IReadOnlyList<string>> operatorSelection = default)
    {
        Mode mode = problemCorridorCount == 0 ? Mode.Fill : Mode.Secondary;

        // (3) Operator override — honour the explicit pick, but only roads the flow data confirms
        // (data-backed, no fabrication), preserving the operator's chosen order.
        if (operatorSelection.HasValue)
        {
            var picked = operatorSelection.Value
                .Where(road => flowPool.Any(f => RoadMatch(f.RoadName, road)))
                .Take(maxGoodRuns)
                .ToList();
            return new Selection(picked, mode);
        }

        if (flowPool.Count == 0 || maxGoodRuns <= 0)
            return new Selection([], mode);

        // (1) No incidents → fill with the top flowing roads.
        if (problemCorridorCount == 0)
            return new Selection(Names(flowPool, maxGoodRuns), mode);

        // (2) Incidents present. Weighted (watched) roads always qualify. Generic roads only when
        // the problem corridors don't fill the slot — otherwise a busy bulletin isn't diluted.
        var weighted = flowPool.Where(f => f.IsWeighted).ToList();
        bool slotFull = problemCorridorCount >= slotCorridorCapacity;

        IEnumerable<FlowRoad> eligible = slotFull
            ? weighted
            : weighted.Concat(flowPool.Where(f => !f.IsWeighted));

        return new Selection(Names(eligible, maxGoodRuns), mode);
    }

    /// <summary>
    ///     Removes flow roads that name the same canonical road as an active incident corridor, so a
    ///     road is never voiced as both blocked and "running freely" in the same bulletin. HERE can
    ///     spell the same road with direction residue in either field (<c>PACIFIC MOTORWAY
    ///     (NORTHBOUND)</c> vs <c>Pacific Motorway citybound</c>), so this uses
    ///     <see cref="RoadNameFormatter.IsSameRoad" /> rather than exact string equality while still
    ///     preserving genuinely distinct roads ("Smith Street" vs "Smith Street Motorway").
    /// </summary>
    public static IReadOnlyList<FlowRoad> ExcludeCorridorRoads(
        IReadOnlyList<FlowRoad> flowPool,
        IReadOnlyList<TrafficCorridor> corridors)
    {
        if (flowPool.Count == 0 || corridors.Count == 0)
            return flowPool;

        var incidentRoads = corridors
            .Select(c => c.RoadName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

        return incidentRoads.Count == 0
            ? flowPool
            : flowPool.Where(f => !incidentRoads.Any(road =>
                RoadNameFormatter.IsSameRoad(f.RoadName, road))).ToList();
    }

    private static List<string> Names(IEnumerable<FlowRoad> roads, int max) =>
        roads.Select(f => f.RoadName).Take(max).ToList();

    private static bool RoadMatch(string corridorRoadName, string selected)
    {
        string a = corridorRoadName.Trim();
        string b = selected.Trim();
        if (a.Length == 0 || b.Length == 0)
            return false;

        return a.Contains(b, StringComparison.OrdinalIgnoreCase) ||
               b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }
}

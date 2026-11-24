using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Builds the data-backed flowing-road pool used to fill good-run slots: the live free-flow
///     fetch, with station/market "weighted" roads flagged and watched roads with no active
///     corridor recovered, scored on the same scale as incidents (damped low). Extracted from
///     <see cref="TrafficEvidenceBuilder.BuildFlowPoolAsync" />, which composes this with
///     <see cref="GoodRunPolicy.ExcludeCorridorRoads" /> before selection so good-runs can be
///     ranked against incidents for the slot (see <c>BulletinContentSelector</c>).
/// </summary>
internal sealed class FlowPoolBuilder(
    ITrafficProvider trafficProvider,
    ILogger<BroadcastOrchestrator> logger)
{
    // Cap on the flow pool persisted for the review Flow tab (Table-property size guard).
    internal const int FlowPoolCap = 20;

    /// <summary>
    ///     Bounded review/matching pool requested from the provider. The provider cheaply pre-ranks
    ///     the full flow snapshot and map-resolves 20-40-row waves only until the pool has enough
    ///     authoritative road names. Twenty survive into the review record and at most six reach
    ///     the writer.
    /// </summary>
    private const int FlowMatchPoolSize = 40;

    internal async Task<IReadOnlyList<FlowRoad>> FetchFreeFlowingRoadsSafeAsync(
        CoverageArea coverage,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? excludedRoadNames = null)
    {

        try
        {
            var result = await trafficProvider.GetFreeFlowingRoadsAsync(
                coverage,
                FlowMatchPoolSize,
                cancellationToken,
                excludedRoadNames);
            return result.IsSuccess ? result.Value : [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Free-flowing roads fetch failed; proceeding without good-run fill.");
            return [];
        }
    }

    /// <summary>
    ///     Flags flow-pool roads that are per-market priority roads and applies each priority
    ///     road's operator-chosen <see cref="FlowPriorityTier" /> as an additive score bonus (a
    ///     flowing priority road competes for the slot by score — never a hard guarantee). Orders
    ///     weighted-first then by score and caps the persisted pool size.
    /// </summary>
    /// <remarks>
    ///     A watched road reaches the pool ONLY from the provider's own flow data. This used to
    ///     also synthesise a <see cref="FlowRoad" /> for any watched road with no incident
    ///     corridor, named from the operator's configured string — two defects in one (dev,
    ///     2026-08-12). It aired the operator's TYPO: a misspelling can never match a corridor, so
    ///     it was always "flowing" and was read out verbatim. And "no incident" is not evidence of
    ///     flowing: a watched road standing still at jamFactor 8 with no reported incident was
    ///     aired as a good run. Both disappear when the name and the claim both come from data.
    /// </remarks>
    internal static IReadOnlyList<FlowRoad> MarkWeightedFlowRoads(
        IReadOnlyList<FlowRoad> pool,
        IReadOnlyList<PriorityRoad> priorityRoads)
    {
        // `Enabled` is the row's on/off switch in the priority-roads manager; `FlowTier` is what
        // the row does for good runs. Both must hold — a road switched off was still driving
        // good-run selection because only the tier was checked (dev, 2026-07-28).
        static bool Watches(PriorityRoad p) => p.Enabled && p.FlowTier != FlowPriorityTier.None;

        FlowPriorityTier TierFor(string roadName)
        {
            var match = priorityRoads.FirstOrDefault(p =>
                Watches(p) && PriorityRoads.IsMatch(roadName, p.Name));
            return match?.FlowTier ?? FlowPriorityTier.None;
        }

        bool IsPriority(string roadName) => priorityRoads.Any(p =>
            Watches(p) && PriorityRoads.IsMatch(roadName, p.Name));

        var watchedRoads = priorityRoads.Where(Watches).ToList();
        if (watchedRoads.Count == 0)
            return pool.Take(FlowPoolCap).ToList();

        var marked = pool
            .Select(f =>
            {
                var tier = TierFor(f.RoadName);
                if (tier == FlowPriorityTier.None) return f;
                double bonus = PriorityRoads.FlowTierBonus(tier);
                return f with
                {
                    IsWeighted = true,
                    PriorityBoosted = IsPriority(f.RoadName),
                    BaseScore = f.Score,
                    BoostAmount = bonus,
                    Score = f.Score + bonus
                };
            })
            .ToList();

        return marked
            .OrderByDescending(f => f.IsWeighted)
            .ThenByDescending(f => f.Score)
            .ThenByDescending(f => f.LengthMeters)
            .Take(FlowPoolCap)
            .ToList();
    }
}

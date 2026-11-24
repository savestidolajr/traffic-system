using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Unified rundown selection (2026-06-25): incident corridors and flowing "good-run" roads
///     compete for the SAME slot, ranked together by score. Flow scores are damped well below
///     incidents (<see cref="CorridorAggregator.CalculateFlowScore" /> × the flow factor) with the
///     market priority boost folded in — so a good run only makes the cut when it out-ranks an
///     incident (a quiet bulletin of minor incidents) or a priority boost lifts it. This replaces the
///     count-based good-run gating for the AUTOMATIC case; an operator's explicit drag pick still
///     overrides on regeneration (handled by the orchestrator via curation).
/// </summary>
public static partial class BulletinContentSelector
{
    public sealed record Selection(
        IReadOnlyList<TrafficCorridor> Corridors,
        IReadOnlyList<FlowRoad> GoodRuns);

    /// <param name="corridors">Incident corridors, impact-scored (priority boost already applied).</param>
    /// <param name="flowPool">Flowing roads, flow-scored + weighted/boosted.</param>
    /// <param name="slotCapacity">Total rundown slots the target duration can hold.</param>
    /// <param name="maxGoodRuns">Hard cap on good-run roads fed to the script.</param>
    public static Selection Select(
        IReadOnlyList<TrafficCorridor> corridors,
        IReadOnlyList<FlowRoad> flowPool,
        int slotCapacity,
        int maxGoodRuns)
    {
        if (slotCapacity <= 0)
            return new Selection([], []);

        // Rank incidents (impact score) and good runs (damped flow score) together; on a tie an
        // incident outranks a good run — a problem is always at least as newsworthy as a clear road.
        var picks = corridors
            .Select(c => (
                Score: c.ImpactScore,
                Corridor: (TrafficCorridor?)c,
                Flow: (FlowRoad?)null))
            .Concat(flowPool.Select(f => (
                Score: f.Score,
                Corridor: (TrafficCorridor?)null,
                Flow: (FlowRoad?)f)))
            .OrderByDescending(p => p.Score)
            .ThenByDescending(p => p.Corridor is not null)
            .Take(slotCapacity)
            .ToList();

        var selectedCorridors = picks
            .Where(p => p.Corridor is not null)
            .Select(p => p.Corridor!)
            .ToList();
        var selectedGoodRuns = picks
            .Where(p => p.Flow is not null)
            .Select(p => p.Flow!)
            .Take(maxGoodRuns)
            .ToList();

        // AIRTIME correction: selected corridors fill the operator's rundown, but only the
        // VOICEABLE subset (tier caps + major-headline minor-suppression) reaches the writer —
        // suppressed minors consume rundown slots yet produce zero spoken seconds. When that
        // happens, backfill the freed airtime with the next-ranked flowing roads so a nominally
        // "busy" market whose content collapses to one closure still fills with real,
        // market-specific roads before generic filler (Sydney fresh-eval: 12 raw corridors, 2
        // voiceable, zero good runs offered, 18s of dead air went to 3 generic road-safety facts).
        int voiceableCount = CorridorTiering.CountVoiceable(selectedCorridors);
        int freedSlots = selectedCorridors.Count - voiceableCount;
        if (freedSlots > 0 && selectedGoodRuns.Count < maxGoodRuns)
        {
            var backfill = flowPool
                .Where(f => !selectedGoodRuns.Contains(f))
                .Take(Math.Min(freedSlots, maxGoodRuns - selectedGoodRuns.Count));
            selectedGoodRuns = selectedGoodRuns.Concat(backfill).ToList();
        }

        return new Selection(selectedCorridors, selectedGoodRuns);
    }
}

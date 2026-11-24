using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Suppresses a "has cleared" narrative for a road that also carries a currently active,
///     selected incident — e.g. one contraflow on A1 clears while a DIFFERENT HERE incident
///     starts on A1 moments later. Both facts are individually true (different incident ids),
///     but voicing them from the same generic road label reads as a flat on-air self-
///     contradiction: "A1 is still under construction ... construction on A1 has cleared"
///     (live feedback, Port Augusta, 2026-07-06). Mirrors the existing closure vs.
///     monitored-route reconciliation in <c>TrafficNarrativeSummarizer.AppendMonitoredRouteBullets</c>.
///     <para>
///     Applied ONCE, upstream of both narrative consumers (<c>TrafficNarrativeSummarizer</c>'s
///     "Cleared:" bullets and <c>IncidentChangeFormatter</c>'s "changes since last bulletin"
///     continuity block) so neither can independently re-introduce the contradiction. The
///     underlying incident-state bookkeeping (id-based cleared/active classification, persisted
///     per market) is untouched — this only trims what gets VOICED.
///     </para>
/// </summary>
public static partial class IncidentDeltaReconciler
{
    public static IncidentDeltaContext SuppressClearedOnActiveRoads(
        IncidentDeltaContext delta, IReadOnlyList<TrafficCorridor> activeCorridors)
    {
        if (delta.ClearedIncidents.Count == 0 || activeCorridors.Count == 0)
            return delta;

        var reconciled = delta.ClearedIncidents
            .Where(item => !item.RoadName.HasValue ||
                           !activeCorridors.Any(c => RoadNameFormatter.IsSameRoad(c.RoadName, item.RoadName.Value)))
            .ToList();

        return reconciled.Count == delta.ClearedIncidents.Count
            ? delta
            : delta with { ClearedIncidents = reconciled };
    }
}

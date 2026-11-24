using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Model;
namespace ArnBulletinGenerator.Workflow.Stages.Conversational;

/// <summary>
///     Splits the normalized events between the writers, and puts their briefs back together.
///     <para>
///         Pure and deterministic: no model, no clock, no I/O. Everything here is a decision that
///         has one right answer, and asking an agent to re-derive it would be slower and less
///         reliable than computing it.
///     </para>
/// </summary>
internal static class BriefDivision
{
    /// <summary>Which writer a slice of the evidence belongs to.</summary>
    internal enum Desk
    {
        /// <summary>What has happened on the network: closures, crashes, hazards.</summary>
        Incidents,

        /// <summary>How the network is moving: delays, drive times, and the roads that are clear.</summary>
        Flow,

        /// <summary>Always voiced, never written by a model. Weather and fuel.</summary>
        Fixture
    }

    /// <summary>
    ///     Routes every event to a desk.
    ///     <para>
    ///         Clear runs sit with flow rather than on their own, because "the M5's running clean"
    ///         is only worth airtime in proportion to how much of the read congestion did not fill.
    ///         A desk that saw only good runs could not make that trade, and the trade is the whole
    ///         judgement: the ClearRun kind exists because good runs were invisible to planning
    ///         while reads ran under their slot.
    ///     </para>
    ///     <para>
    ///         Fixtures are separated but not written. Weather and fuel are always voiced, in a
    ///         fixed position, one sentence each, with no selection to make. Handing them to a model
    ///         adds a chance to get them wrong and buys nothing.
    ///     </para>
    /// </summary>
    internal static Desk DeskFor(BulletinEvent evt, IReadOnlySet<string> incidentCorridors) =>
        evt.Kind switch
        {
            BulletinEventKind.Weather or BulletinEventKind.Fuel => Desk.Fixture,

            // A queue caused by a crash belongs with the crash. Split them and the read describes
            // the same mess twice, once as an incident and once as congestion, from two briefs
            // neither of which knew about the other.
            BulletinEventKind.Corridor when incidentCorridors.Contains(evt.EventId)
                                            && !HasDominantDelay(evt) => Desk.Incidents,

            BulletinEventKind.Corridor or BulletinEventKind.DriveTime or BulletinEventKind.ClearRun => Desk.Flow,

            _ => Desk.Flow
        };

    /// <summary>
    ///     A big measured delay is FLOW news even when works metadata rides on the same corridor.
    ///     Routed to the incidents desk, a 38-minute queue with night-works attached was briefed as
    ///     roadworks colour, the composer dropped the works story for budget, and the board's worst
    ///     figure never aired while the read called two lighter roads "the biggest queues" (dev
    ///     fixture 571555b1, 2026-08-27). Facts travel with the event whichever desk briefs it, so
    ///     cause and effect still arrive together; genuine closures stay on the incidents desk
    ///     whatever their delay, because a closure is a routing decision, not a pace.
    /// </summary>
    private static bool HasDominantDelay(BulletinEvent evt) =>
        !evt.Facts.Any(fact =>
            fact.Value.Contains("closed", StringComparison.OrdinalIgnoreCase)
            || fact.Value.Contains("closure", StringComparison.OrdinalIgnoreCase))
        && evt.Facts.Any(fact =>
            fact.Field.Equals("delayMinutes", StringComparison.OrdinalIgnoreCase)
            && SpokenNumber.TryParse(fact.Value) is >= 15);

    /// <summary>
    ///     Corridors carrying an incident, so the routing rule above can keep cause and effect on
    ///     one desk. An event counts when it is an incident itself or its facts name one.
    /// </summary>
    internal static IReadOnlySet<string> IncidentCorridors(IReadOnlyList<BulletinEvent> events) =>
        events
            .Where(HasIncident)
            .Select(evt => evt.EventId)
            .ToHashSet(StringComparer.Ordinal);

    private static bool HasIncident(BulletinEvent evt) =>
        evt.Facts.Any(fact =>
            fact.Field.Equals("incident", StringComparison.OrdinalIgnoreCase)
            || fact.Field.Equals("detail", StringComparison.OrdinalIgnoreCase)
            || fact.Field.Equals("lanesClosed", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///     Briefs the fixtures without a model.
    ///     <para>
    ///         Weather and fuel are always voiced, in a fixed position, one sentence each, with no
    ///         selection and no ranking to do. Every word is a supplied value copied verbatim, which
    ///         is precisely what a model is worst at and a projection is perfect at. Asking for
    ///         these briefs bought nothing and cost a chance to invent a temperature.
    ///     </para>
    /// </summary>
    internal static IReadOnlyList<StoryBrief> FixtureBriefs(IReadOnlyList<BulletinEvent> events) =>
    [
        .. events
            .Where(evt => evt.Kind is BulletinEventKind.Weather or BulletinEventKind.Fuel)
            .Select((evt, index) => new StoryBrief(
                Order: index + 1,
                StoryId: evt.Kind.ToString().ToLowerInvariant(),
                Subject: evt.Kind.ToString(),
                EventIds: [evt.EventId],
                Priority: 0,
                WhyItMatters: $"the station always carries {evt.Kind.ToString().ToLowerInvariant()}",
                Required: true))
    ];

    /// <summary>
    ///     Merges each desk's briefs into the single ordered set the composer expects.
    ///     <para>
    ///         Ordered by <see cref="BulletinEvent.Rank" /> — the console's own ordering, computed
    ///         once across every kind — rather than by anything a writer returned. Two independent
    ///         agents cannot produce comparable priorities, so they are not asked to: the ordering
    ///         was never theirs to invent, which is exactly what makes sharding them safe.
    ///     </para>
    ///     <para>
    ///         Fixtures go last, together, because they are the tail of the read by editorial
    ///         convention and not because they rank lowest.
    ///     </para>
    /// </summary>
    internal static BulletinBriefs Stitch(
        IReadOnlyList<(Desk Desk, StoryBrief Brief)> briefs,
        IReadOnlyDictionary<string, int> rankByEventId,
        IReadOnlyList<string>? segmentOrder = null,
        TrafficRunningOrder trafficOrder = TrafficRunningOrder.WorstFirst)
    {
        int RankOf(StoryBrief brief) => brief.EventIds
            .Select(id => rankByEventId.TryGetValue(id, out int r) ? r : 0)
            .DefaultIfEmpty(0)
            .Max();
        var segmentRanks = (segmentOrder ?? ["Traffic", "Weather", "Fuel"])
            .Select((name, index) => (Name: name, Index: index))
            .ToDictionary(item => item.Name, item => item.Index, StringComparer.OrdinalIgnoreCase);

        int SegmentRank((Desk Desk, StoryBrief Brief) item)
        {
            if (item.Desk != Desk.Fixture)
                return segmentRanks.GetValueOrDefault("Traffic", 0);

            string segment = item.Brief.StoryId.Equals("fuel", StringComparison.OrdinalIgnoreCase)
                ? "Fuel"
                : "Weather";
            return segmentRanks.GetValueOrDefault(segment, int.MaxValue);
        }

        var ordered = briefs
            .OrderBy(SegmentRank)
            .ThenBy(item => item.Desk == Desk.Fixture
                ? 0
                : trafficOrder == TrafficRunningOrder.WorstLast ? RankOf(item.Brief) : -RankOf(item.Brief))
            .ThenBy(item => item.Brief.Order)
            .Select(item => item.Brief with
            {
                Priority = item.Desk == Desk.Fixture ? 0 : RankOf(item.Brief)
            })
            .ToList();

        // Order is renumbered so it stays a stable tiebreak downstream. Leaving each desk's own
        // numbering in place would give several briefs the same Order and make ByPriority's
        // secondary sort arbitrary.
        return new BulletinBriefs([.. ordered.Select((brief, index) => brief with { Order = index + 1 })]);
    }
}

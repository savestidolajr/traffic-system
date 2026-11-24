using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Classifies traffic incident types by how their physical extent relates to commuter delay.
///     A "standing footprint" (roadworks, a lane restriction) occupies a stretch of road but is
///     mostly free-flowing — its kilometres are a work-zone extent, not a queue. Both impact
///     scoring and on-air phrasing must treat that length differently from a live jam (congestion,
///     an event backing traffic up, or a crash/closure queue), which it never inflates.
/// </summary>
public static partial class IncidentTypeClassifier
{
    public static bool IsStandingFootprint(string type) =>
        type.Equals("construction", StringComparison.OrdinalIgnoreCase)
        || type.Equals("laneRestriction", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     A road (or ramp) closure — the road being shut is its own magnitude. Its on-air framing
    ///     leads on the closure ("X is shut"), never on a residual routed-delay figure for the
    ///     through-lanes, which would bury the one thing a listener must hear.
    /// </summary>
    public static bool IsClosure(string type) =>
        type.Equals("roadClosure", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     A discrete collision event. The vocabulary is spread across the codebase — HERE returns
    ///     <c>accident</c>, while descriptions and internal labels use "crash" and "collision"
    ///     (see <c>TrafficTypeMapper</c>, <c>PolishFacts</c>, <c>CorridorMagnitude</c>) — so the
    ///     definition lives here beside <see cref="IsClosure" /> and <see cref="IsCongestion" />
    ///     rather than being re-derived per caller. Added 2026-08-23 when the cleared-incident
    ///     worthiness gate needed "closure or crash" and only closure existed.
    /// </summary>
    public static bool IsCrash(string type) =>
        type.Equals("accident", StringComparison.OrdinalIgnoreCase)
        || type.Equals("crash", StringComparison.OrdinalIgnoreCase)
        || type.Equals("collision", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Whether the DATA marks this corridor as closed — the single definition every consumer
    ///     must share. A corridor's free-text <see cref="TrafficCorridor.Description" /> can assert
    ///     a closure the typed fields deny: HERE returns <c>roadClosed=false</c>, <c>type</c>
    ///     <c>construction</c> and a description reading "Closed" for the same works zone.
    ///     Observed live 2026-08-20 on Heathcote Road: the writer read "road closure and roadworks"
    ///     out of the description and voiced a closure, while
    ///     <see cref="Detectors.FalseClosureDetector" /> judged the same read against the type and
    ///     returned FALSE-CLOSURE. Both were right about their own input. The typed fields win,
    ///     because they are what the authority asserts rather than what its prose happens to say.
    /// </summary>
    public static bool DataMarksClosure(TrafficCorridor corridor) =>
        IsClosure(corridor.PrimaryType) || corridor.Incidents.Any(incident => incident.RoadClosed);

    /// <summary>
    ///     Live congestion / queue — physical lost time or reduced speeds, distinct from discrete
    ///     events (crash, breakdown) or planned roadworks.
    /// </summary>
    public static bool IsCongestion(string type) =>
        type.Equals("congestion", StringComparison.OrdinalIgnoreCase)
        || type.Equals("queuingTraffic", StringComparison.OrdinalIgnoreCase)
        || type.Equals("trafficCongestion", StringComparison.OrdinalIgnoreCase);
}

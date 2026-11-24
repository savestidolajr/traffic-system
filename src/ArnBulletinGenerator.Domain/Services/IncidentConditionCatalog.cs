using System.Collections.Frozen;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     What a HERE incident actually IS, keyed to the AlertC cause code HERE publishes rather than to
///     the English sentence it renders.
/// </summary>
public enum IncidentConditionKind
{
    Unknown,
    RoadWorks,
    Closed,
    ClosedForRoadWorks,
    LaneBlocked,
    ConstructionWork,
    MaintenanceWork,
    BrokenDownVehicle,
    MajorEvent,
    TrafficCongestion,
    RoadWorksContraflow,
    PoorRoadSurface,
    QueuingTraffic,
    ClosedForFlooding,
    HazardousConditions,
    Restrictions,
    ResurfacingWork,
    Incidents
}

/// <summary>
///     The condition a record reports, with the phrase it may be voiced as and the two facts a gate
///     needs: whether a closure is expected, and whether the condition is a standing footprint
///     (a worksite that is there every day) rather than an event.
/// </summary>
/// <param name="Kind">The condition, from <c>codes[0]</c>.</param>
/// <param name="Phrase">The Australian-register phrase for this condition; empty for
///     <see cref="IncidentConditionKind.Unknown" />, which is never voiced.</param>
/// <param name="ExpectsClosure">
///     True when this condition's vocabulary implies the road is shut. This is an EXPECTATION only:
///     the authority for closure is always the record's own <c>roadClosed</c> flag, which agreed with
///     this column on all 793 measured Australian records. A disagreement is a data anomaly.
/// </param>
/// <param name="IsStandingFootprint">True when the condition is a worksite/permanent restriction
///     rather than a live event — it outranks nothing and carries no continuity.</param>
public readonly record struct IncidentCondition(
    IncidentConditionKind Kind, string Phrase, bool ExpectsClosure, bool IsStandingFootprint);

/// <summary>
///     Resolves HERE's <c>codes</c> (AlertC, ISO 14819-2) to a condition.
///     <para>
///         <c>codes</c> is present on 100% of measured incidents (5,197 records, 10 cities, 4
///         continents) and <c>codes[0]</c> alone determines the condition: 17 primary codes cover the
///         793 Australian records with exactly one duplicated phrasing. The English
///         <c>description</c> is a rendering of the same fact in whatever register HERE's <c>lang</c>
///         resolves to — parsing it made the pipeline's behaviour a function of HERE's wording, and
///         it is not the source of truth for anything here.
///     </para>
/// </summary>
public static class IncidentConditionCatalog
{
    /// <summary>Never voiced: an unmapped code tells us nothing we may put to air.</summary>
    private static readonly IncidentCondition UnknownCondition =
        new(IncidentConditionKind.Unknown, string.Empty, false, false);

    /// <summary>
    ///     The complete observed Australian primary-code set. Any code not listed resolves to
    ///     <see cref="IncidentConditionKind.Unknown" /> and is withheld by <c>ClaimGate</c> rather
    ///     than guessed at.
    /// </summary>
    private static readonly FrozenDictionary<int, IncidentCondition> ByPrimaryCode =
        new Dictionary<int, IncidentCondition>
        {
            [701] = new(IncidentConditionKind.RoadWorks, "roadworks", false, true),
            [401] = new(IncidentConditionKind.Closed, "closed", true, false),
            [735] = new(IncidentConditionKind.ClosedForRoadWorks, "closed for roadworks", true, true),
            [520] = new(IncidentConditionKind.LaneBlocked, "a lane blocked", false, false),
            [803] = new(IncidentConditionKind.ConstructionWork, "construction work", false, true),
            [703] = new(IncidentConditionKind.MaintenanceWork, "maintenance work", false, true),
            [211] = new(IncidentConditionKind.BrokenDownVehicle, "a broken down vehicle", false, false),
            [1501] = new(IncidentConditionKind.MajorEvent, "a major event", false, false),
            [136] = new(IncidentConditionKind.TrafficCongestion, "heavy traffic", false, false),
            [746] = new(IncidentConditionKind.RoadWorksContraflow, "roadworks with contraflow", false, true),
            [916] = new(IncidentConditionKind.PoorRoadSurface, "a poor road surface", false, true),
            [138] = new(IncidentConditionKind.QueuingTraffic, "queuing traffic", false, false),
            [938] = new(IncidentConditionKind.ClosedForFlooding, "closed for flooding", true, false),
            [1001] = new(IncidentConditionKind.HazardousConditions, "hazardous driving conditions", false, false),
            [493] = new(IncidentConditionKind.Restrictions, "restrictions", false, true),
            [704] = new(IncidentConditionKind.ResurfacingWork, "resurfacing work", false, true),
            [214] = new(IncidentConditionKind.Incidents, "an incident", false, false)
        }.ToFrozenDictionary();

    /// <summary>
    ///     The condition <paramref name="codes" /> reports, or <see cref="IncidentConditionKind.Unknown" />
    ///     when the list is empty or its primary code is unmapped.
    ///     <para>
    ///         Only <c>codes[0]</c> is read, and that is the point: AlertC orders by primary cause, so
    ///         <c>(520, 701)</c> is a lane blocked at a worksite while <c>(701, 520)</c> is a worksite
    ///         that happens to block a lane. Folding the list together loses which one HERE reported.
    ///     </para>
    /// </summary>
    public static IncidentCondition FromCodes(IReadOnlyList<int>? codes) =>
        codes is { Count: > 0 } && ByPrimaryCode.TryGetValue(codes[0], out var condition)
            ? condition
            : UnknownCondition;
}

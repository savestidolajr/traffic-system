using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     How strongly a flowing priority road competes for a bulletin slot. The bonus is ADDITIVE on
///     the incident-impact scale (see <see cref="Services.PriorityRoads.FlowTierBonus" />) rather than
///     a multiplier, so the operator's choice lands predictably against the incident bands regardless
///     of the road's length/class: <see cref="High" /> reaches minor-incident contention,
///     <see cref="Critical" /> reaches moderate. It is never a guarantee — a stack of bigger incidents
///     still buries it (the unified <see cref="Services.BulletinContentSelector" /> ranks both by score).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<FlowPriorityTier>))]
public enum FlowPriorityTier
{
    /// <summary>Not a flowing good-run booster — only competes if it lands in the generic flow pool.</summary>
    None = 0,

    /// <summary>Watched good-run; slight lift above generic filler. Airs in a quiet bulletin.</summary>
    Normal = 1,

    /// <summary>Lifts into minor-incident contention — beats minor incidents, loses on a busy day.</summary>
    High = 2,

    /// <summary>Lifts into moderate-incident contention — airs unless serious incidents stack up.</summary>
    Critical = 3
}

/// <summary>
///     A per-market priority road (Workshop 2 / SDD §4.2): an arterial that matters to listeners.
///     <see cref="BoostIncidents" /> lifts a matching incident corridor's impact score so it survives
///     selection capping. <see cref="FlowTier" /> sets how strongly the road competes for a slot when
///     it is flowing freely — an operator-tuned additive lift on the incident scale (None disables the
///     flowing boost). The operator chooses the incident axis and the flow tier independently when
///     creating the road; legacy string-only entries migrate to boosting incidents + flow tier High.
/// </summary>
/// <param name="Name">Speakable road name (e.g. "M1 Pacific Motorway").</param>
/// <param name="BoostIncidents">Boost a matching incident/congestion corridor's impact score (×1.3).</param>
/// <param name="FlowTier">How strongly this road competes for a slot when flowing (additive, by tier).</param>
/// <param name="Enabled">Whether this priority road is active. Disabled roads are persisted but skipped at generation time.</param>
public sealed record PriorityRoad(
    string Name,
    bool BoostIncidents = true,
    FlowPriorityTier FlowTier = FlowPriorityTier.High,
    bool Enabled = true);

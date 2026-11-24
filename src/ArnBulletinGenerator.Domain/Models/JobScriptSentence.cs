namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One sentence of a bulletin script, tagged with the corridor keys and monitored drive-time
///     routes it names for review highlighting, plus the evidence ids committed by the structured
///     Draft.
/// </summary>
/// <param name="RouteKeys">
///     Exact <see cref="MonitoredRouteStatus.Name" /> values this sentence names. A drive-time run
///     is not a corridor, so before this existed a grounded route claim reached the operator
///     console with no lineage at all and was flagged "No source found" (job cb5eadd4, test).
///     Null on jobs persisted before the field existed.
/// </param>
public sealed record JobScriptSentence(
    string Text,
    IReadOnlyList<string> CorridorKeys,
    IReadOnlyList<string> EvidenceIds = null!,
    IReadOnlyList<string> RouteKeys = null!);

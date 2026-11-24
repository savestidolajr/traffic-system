using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Workflow.Model;

/// <summary>
///     One real-world thing the bulletin can talk about — a corridor, the weather block, the fuel
///     block — with every fact known about it hanging off it.
///     <para>
///         This is the semantic unit the writer and editor both reason over. The flat evidence list
///         stays the unit of CITATION, but it is a poor unit of MEANING: given
///         <c>[r9:detail]</c> and <c>[r9:location:0]</c> as unrelated lines, a judge has to infer
///         whether they may be joined. As properties of one event the answer is structural, and
///         that inference — the source of the 2026-08-26 WRONG_LOCATION false positive — disappears.
///     </para>
/// </summary>
/// <param name="EventId">Stable per-request id, derived from the evidence <c>SourceKey</c>.</param>
/// <param name="Kind">What sort of thing this is.</param>
/// <param name="Label">Human name for the trace and prompts, e.g. <c>South Road, Torrensville</c>.</param>
/// <param name="Facts">Every fact, keyed by field. Order is the order the evidence supplied.</param>
public sealed record BulletinEvent(
    string EventId,
    BulletinEventKind Kind,
    string Label,
    IReadOnlyList<EventFact> Facts)
{
    /// <summary>
    ///     Where this event ranks against every other one, on one scale shared by every kind.
    ///     <para>
    ///         Computed by <c>EventRank</c> from the facts, not supplied by a feed. That is what
    ///         makes it safe to shard the writers: two independent agents cannot produce comparable
    ///         priorities, but they do not have to when the ordering was never theirs to invent.
    ///     </para>
    ///     <para>
    ///         DECISION METADATA, NEVER VOICEABLE. It is deliberately not an <see cref="EventFact" />
    ///         and carries no evidence id, so no claim can cite it and no read can say it out loud.
    ///     </para>
    /// </summary>
    public int Rank { get; init; }

    /// <summary>Every citable id this event owns. A claim about this event may cite any of these.</summary>
    public IReadOnlyList<string> EvidenceIds => Facts.Select(f => f.EvidenceId).ToList();

    public EventFact? Fact(string field) =>
        Facts.FirstOrDefault(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<EventFact> AllFacts(string field) =>
        Facts.Where(f => string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase)).ToList();

    public bool Has(string field) => Fact(field) is not null;
}

public enum BulletinEventKind
{
    Corridor,
    Weather,
    Fuel,

    /// <summary>
    ///     A clear-run report ("good run on X"). Live on 2026-08-26 these normalised to Unknown,
    ///     so two valid facts (goodrun:South Eastern Freeway, goodrun:North-South Motorway) were
    ///     invisible to planning while the read ran under its slot.
    /// </summary>
    ClearRun,

    /// <summary>A monitored route's end-to-end drive time, e.g. "25 min run (typical 20 min)".</summary>
    DriveTime,

    Unknown
}

/// <param name="EvidenceId">The citable id, e.g. <c>r9:suburb</c>.</param>
/// <param name="Field">Fact kind, e.g. <c>suburb</c>, <c>delayMinutes</c>, <c>location</c>.</param>
/// <param name="Value">The value, in the form it is meant to be spoken.</param>
/// <param name="DeskOnly">
///     True when the fact is material for the desk to REASON from rather than content to voice. The
///     desk sees it; <see cref="BulletinBriefs.Render" /> withholds it from the composer's AVAILABLE
///     FACTS. See <see cref="Domain.Models.EvidenceUnit.DeskOnly" /> for the read that forced it.
/// </param>
public sealed record EventFact(string EvidenceId, string Field, string Value, bool DeskOnly = false);

/// <summary>The normalized view handed to the planner: events plus the fixtures the station demands.</summary>
/// <param name="Events">Every event, in the order the evidence supplied them.</param>
/// <param name="RequiredEventIds">Events that MUST appear in the plan (station fixtures).</param>
public sealed record NormalizedInput(
    IReadOnlyList<BulletinEvent> Events,
    IReadOnlyList<string> RequiredEventIds)
{
    public BulletinEvent? Event(string eventId) =>
        Events.FirstOrDefault(e => string.Equals(e.EventId, eventId, StringComparison.Ordinal));
}

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Which part of the read a defect can be repaired in.
/// </summary>
public enum RefineScope
{
    /// <summary>
    ///     Repairable by replacing ONE sentence. The defect lives inside that sentence's own wording
    ///     — a wrong incident type, a dropped suburb, internal jargon, an unsupported condition.
    /// </summary>
    Sentence = 0,

    /// <summary>
    ///     Only repairable with the whole read in view: a missing required section (the bulletin as a
    ///     whole never voices fuel), section ordering, or a grouping complaint that spans sentences
    ///     ("these two roads share one condition, say it once"). No single sentence is wrong on its own.
    /// </summary>
    Script = 1
}

/// <summary>
///     One reviewable defect, addressed to the smallest thing that can fix it.
///     <para>
///         Replaces the free-prose <c>issues: string[]</c> the reviewer used to return. That shape
///         forced every repair through a whole-script refine, and the cost was measured on dev
///         2026-08-20: refine was handed a 13.9 KB prompt (41% evidence units, 31% station policy)
///         and asked to re-emit every sentence with three style variants each. Across seven live
///         refine calls it rewrote 3/4, 4/5, 4/5, 4/4, 5/5 and 5/7 sentences to address at most one
///         or two defects, emitted 105 variants in total, and inflated 68→97, 35→48 and 68→100 words
///         — producing 36.9s and 39.3s of copy for a 22.0s slot.
///     </para>
///     <para>
///         The reviewer already had what it needed to be precise: its prompt carries the structured
///         draft with each sentence's <c>order</c> and <c>evidenceIds</c>. It simply never returned
///         them. Deterministic detectors are better still — they know exactly which road and sentence
///         tripped them, so their <see cref="Order" /> is computed rather than inferred.
///     </para>
/// </summary>
/// <param name="Order">
///     The 1-based sentence this defect belongs to, when it belongs to one. <c>null</c> for a
///     <see cref="RefineScope.Script" /> defect, and also for a sentence-scoped defect whose sentence
///     could not be resolved. An unresolved order defers to the next review attempt (2026-08-21: the
///     whole-script refine fallback is gone), because patching the wrong sentence silently corrupts a
///     correct fact.
/// </param>
/// <param name="Defect">Short stable label for the defect class, e.g. <c>WRONG_INCIDENT_TYPE</c>.</param>
/// <param name="Reason">The instruction handed to the refiner: what is wrong and what to do about it.</param>
/// <param name="Scope">Whether this is repairable inside one sentence or needs the whole read.</param>
public sealed record ReviewIssue(
    int? Order,
    string Defect,
    string Reason,
    RefineScope Scope)
{
    /// <summary>
    ///     True when this defect can be handed to a sentence-scoped refine: it is sentence-scoped AND
    ///     its sentence is known. Both halves are required.
    /// </summary>
    public bool IsPatchable => Scope == RefineScope.Sentence && Order is > 0;

    /// <summary>A script-scoped defect, with no owning sentence.</summary>
    public static ReviewIssue ForScript(string defect, string reason) =>
        new(null, defect, reason, RefineScope.Script);

    /// <summary>A sentence-scoped defect whose sentence is known.</summary>
    public static ReviewIssue ForSentence(int order, string defect, string reason) =>
        new(order, defect, reason, RefineScope.Sentence);

    /// <summary>
    ///     A sentence-scoped defect whose sentence could NOT be resolved. Deliberately carries a null
    ///     order so <see cref="IsPatchable" /> is false and the caller widens to a script refine.
    /// </summary>
    public static ReviewIssue ForUnresolvedSentence(string defect, string reason) =>
        new(null, defect, reason, RefineScope.Sentence);
}

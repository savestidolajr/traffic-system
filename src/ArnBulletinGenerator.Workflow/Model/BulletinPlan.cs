namespace ArnBulletinGenerator.Workflow.Model;

/// <summary>
///     What the read will be ABOUT, in what order, with what grouped — and not one word of prose.
///     <para>
///         The planner writes this. Because it carries no English, the fitter can add and remove
///         content without any of the damage that dropping finished sentences causes: there is no
///         connective to strand, no ordering to invalidate, no phrasing to re-pick.
///     </para>
/// </summary>
/// <param name="Stories">Ordered. Position 0 leads the read.</param>
public sealed record BulletinPlan(IReadOnlyList<PlannedStory> Stories)
{
    public static BulletinPlan Empty { get; } = new([]);
}

/// <summary>
///     One thing the read will say, possibly spanning several events when they genuinely belong
///     together (same cause, same road-feel, same stretch).
/// </summary>
/// <param name="StoryId">Stable id, referenced by the fitter and the trace.</param>
/// <param name="EventIds">The events this story voices. More than one means a deliberate grouping.</param>
/// <param name="Role">Editorial role in the read.</param>
/// <param name="Priority">0-100. Higher survives the budget. The planner's editorial judgement.</param>
/// <param name="RequiredFacts">Evidence ids without which the story is not worth voicing at all.</param>
/// <param name="OptionalFacts">Detail the fitter may buy if the budget allows, richest first.</param>
/// <param name="Relationship">Why grouped events belong together, for the composer. Null when single-event.</param>
public sealed record PlannedStory(
    string StoryId,
    IReadOnlyList<string> EventIds,
    StoryRole Role,
    int Priority,
    IReadOnlyList<string> RequiredFacts,
    IReadOnlyList<OptionalFact> OptionalFacts,
    string? Relationship = null,
    IReadOnlyList<StoryMember>? Members = null)
{
    /// <summary>
    ///     A group the fitter may take a SUBSET of. Planning identifies which events could be voiced
    ///     together; fitting decides how many of them the slot affords; composition decides how they
    ///     are worded. Collapsing those three decisions into one planner choice is what made a
    ///     seven-event Adelaide sweep cost 71 words and get dropped whole while the read ran 15
    ///     seconds under its slot (measured 2026-08-26).
    /// </summary>
    public bool IsSplittableGroup => Members is { Count: > 1 };
}

/// <summary>One event inside a group candidate, independently affordable.</summary>
public sealed record StoryMember(
    string MemberId,
    string EventId,
    IReadOnlyList<string> RequiredFacts,
    IReadOnlyList<OptionalFact> OptionalFacts,
    int Priority);

public enum StoryRole
{
    Lead,
    Traffic,
    Group,
    Weather,
    Fuel
}

/// <param name="EvidenceIds">The ids this detail would license.</param>
/// <param name="EstimatedWords">What voicing it is expected to cost. Fed by the calibrated estimator.</param>
/// <param name="Priority">0-100 within its story. Higher is bought first.</param>
public sealed record OptionalFact(
    IReadOnlyList<string> EvidenceIds,
    int EstimatedWords,
    int Priority);

/// <summary>
///     The plan after the budget has been spent: exactly what the composer will write, and nothing
///     it may not. Produced by code, never by a model.
/// </summary>
/// <param name="Stories">Ordered, already filtered to what fits.</param>
/// <param name="TargetWords">The word count the composer is aiming at.</param>
/// <param name="Decisions">Why each story or fact was kept or dropped. Trace material.</param>
public sealed record FittedPlan(
    IReadOnlyList<FittedStory> Stories,
    int TargetWords,
    IReadOnlyList<FitDecision> Decisions)
{
    public static FittedPlan Empty { get; } = new([], 0, []);
}

/// <param name="MustExpressEvidenceIds">
///     Facts the fitter SPENT BUDGET ON. Every one must reach the read: the fitter did not grant
///     permission to mention them, it decided the slot is paying for them.
///     <para>
///         Measured 2026-08-26: the composer was handed "evidence this story may cite" and wrote 54
///         words against an 85-word budget. The duration controller then read the short output as a
///         signal to change the budget, the fitter re-selected the same material, and the composer
///         compressed it again - a treadmill in which nothing converged and the read shrank on every
///         pass. Permission and obligation are different contracts and only the second one closes
///         that loop.
///     </para>
/// </param>
/// <param name="AuthorizedEvidenceIds">
///     Everything the story may legally cite, a superset of <paramref name="MustExpressEvidenceIds" />.
///     Citing outside this set is a defect; citing inside it but beyond must-express is free.
/// </param>
/// <param name="EstimatedWords">Budgeted cost, for the trace and the next re-fit.</param>
/// <param name="Members">Group members actually selected, when this story realises a group candidate.</param>
public sealed record FittedStory(
    string StoryId,
    IReadOnlyList<string> EventIds,
    StoryRole Role,
    IReadOnlyList<string> MustExpressEvidenceIds,
    IReadOnlyList<string> AuthorizedEvidenceIds,
    int EstimatedWords,
    string? Relationship = null,
    IReadOnlyList<string>? Members = null)
{
    /// <summary>Every id this story may cite. Convenience over <see cref="AuthorizedEvidenceIds" />.</summary>
    public IReadOnlyList<string> EvidenceIds => AuthorizedEvidenceIds;
}

/// <param name="Subject">Story id, or story id plus fact.</param>
/// <param name="Kept">True when it survived the budget.</param>
/// <param name="Reason">Plain English, shown in the viewer.</param>
public sealed record FitDecision(string Subject, bool Kept, int Words, string Reason);

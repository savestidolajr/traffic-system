namespace ArnBulletinGenerator.Workflow.Contracts;

/// <summary>
///     What ARN may configure, split by the stage it governs.
///     <para>
///         The split is the point. The predecessor pipeline had one undifferentiated "house style"
///         string that reached the writer, the reviewer and the refiner alike, so an editorial
///         instruction about which roads matter arrived at an agent judging sentence grammar, and a
///         wording preference arrived at an agent deciding coverage. Each agent then reasoned about
///         territory it did not own. Configuration that crosses a stage boundary recreates exactly
///         the overlapping-authority problem this architecture exists to remove.
///     </para>
///     <para>
///         Grounding and safety rules are deliberately absent from both halves: they are
///         system-owned. A station cannot widen what counts as supported evidence, because the whole
///         value of the grounding stage is that it answers to the data rather than to the customer.
///     </para>
/// </summary>
/// <param name="Editorial">Reaches the planner. Governs MEANING: what is worth saying, in what order.</param>
/// <param name="Voice">Reaches the composer. Governs LANGUAGE: how the decided content is said.</param>
public sealed record StationPolicy(EditorialPolicy Editorial, VoicePolicy Voice)
{
    public static StationPolicy Empty { get; } = new(EditorialPolicy.Empty, VoicePolicy.Empty);

    /// <summary>
    ///     The station's edits to a role's instructions, keyed by role (<c>compose2</c>, <c>write</c>,
    ///     <c>edit</c>).
    ///     <para>
    ///         <see cref="Editorial" /> and <see cref="Voice" /> are structured settings: they exist
    ///         because a value ARN sets repeatedly deserves a field, a validator and a UI control.
    ///         This is the escape hatch beneath them, for a station that wants to say something we
    ///         never anticipated. Stored as a diff against the shipped default, so a block a station
    ///         never touched keeps improving underneath them.
    ///     </para>
    /// </summary>
    public IReadOnlyDictionary<string, Domain.Models.PlaybookOverlay> Playbooks { get; init; } =
        new Dictionary<string, Domain.Models.PlaybookOverlay>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The station's edits for one role, or none.</summary>
    public Domain.Models.PlaybookOverlay PlaybookFor(string role) =>
        Playbooks.TryGetValue(role, out Domain.Models.PlaybookOverlay? overlay)
            ? overlay
            : Domain.Models.PlaybookOverlay.None;
}

/// <summary>
///     Editorial configuration. Reaches the PLANNER and nothing else.
///     <para>
///         Everything here is a judgement about content: what leads, what belongs together, what is
///         worth airtime. None of it describes wording, because the planner writes no prose.
///     </para>
/// </summary>
public sealed record EditorialPolicy
{
    public static EditorialPolicy Empty { get; } = new();

    /// <summary>Roads the market treats as headline assets. Present in the data means lead with it.</summary>
    public IReadOnlyList<string> PriorityRoads { get; init; } = [];

    /// <summary>When and how widely the planner may offer grouped events to the fitter.</summary>
    public GroupingPolicy Grouping { get; init; } = GroupingPolicy.Default;

    /// <summary>Whether clear runs earn airtime in this market, or only disruptions do.</summary>
    public bool IncludeClearRuns { get; init; } = true;

    /// <summary>Cap on planned stories. Zero means the evidence decides.</summary>
    public int MaxStories { get; init; }

    /// <summary>
    ///     Roughly how many incidents this market wants in one read. Zero means no guidance.
    ///     <para>
    ///         Deliberately approximate. A hard cap would make the read worse in exactly the case it
    ///         is meant to improve: if six items genuinely flow as three sentences, forcing four is
    ///         a downgrade. This is rendered to the planner as a target to aim at, not a rule, so a
    ///         read carrying more because it reads well is a success rather than a violation.
    ///     </para>
    /// </summary>
    public int ApproximateIncidentCount { get; init; }

    /// <summary>On-air section order, e.g. <c>traffic</c>, <c>weather</c>, <c>fuel</c>. Empty keeps the planner's order.</summary>
    public IReadOnlyList<string> SectionOrder { get; init; } = [];

    /// <summary>
    ///     How recent an event must be to earn a place. Null lets the planner judge from the data.
    ///     Relevant once bulletins run every fifteen minutes and yesterday's roadworks must not lead.
    /// </summary>
    public TimeSpan? FreshnessWindow { get; init; }

    /// <summary>Free-text editorial direction from ARN, rendered into the planner prompt verbatim.</summary>
    public string Notes { get; init; } = string.Empty;

    public bool IsEmpty =>
        PriorityRoads.Count == 0 && SectionOrder.Count == 0 && MaxStories == 0 && ApproximateIncidentCount == 0
        && FreshnessWindow is null && Notes.Length == 0 && Grouping == GroupingPolicy.Default
        && IncludeClearRuns;
}

/// <summary>
///     How freely the planner may offer grouped events.
///     <para>
///         <see cref="MaxMembers" /> exists because an unbounded group defeats the fitter: measured
///         2026-08-26, a seven-event group priced at 71 words was dropped whole while the read ran
///         fifteen seconds under its slot. A group is a candidate the fitter samples, so its size
///         has to stay within what a slot could plausibly afford.
///     </para>
/// </summary>
public sealed record GroupingPolicy
{
    public static GroupingPolicy Default { get; } = new();

    public bool Enabled { get; init; } = true;
    public int MinMembers { get; init; } = 2;
    public int MaxMembers { get; init; } = 4;
}

/// <summary>
///     Voice configuration. Reaches the COMPOSER and nothing else.
///     <para>
///         Everything here is about language. None of it can change WHAT is voiced: the fitted plan
///         has already decided that, and a voice setting that could drop a fact would hand editorial
///         authority to a wording preference.
///     </para>
/// </summary>
public sealed record VoicePolicy
{
    public static VoicePolicy Empty { get; } = new();

    /// <summary>Station read tone, e.g. <c>brisk and factual</c>, <c>warm breakfast</c>.</summary>
    public string Tone { get; init; } = string.Empty;

    /// <summary>The market's delay vocabulary, banded by magnitude. Authoritative over the model's own words.</summary>
    public string RoadFeelPalette { get; init; } = string.Empty;

    /// <summary>
    ///     Spoken-sentence ceiling, or null for none. A driver at 80km/h cannot parse a run-on.
    ///     <para>
    ///         Nullable because a default that binds silently is worse than no setting. Measured
    ///         2026-08-26: this defaulted to 24 and rendered whenever ANY other voice field was set,
    ///         so switching on a house style that asked for varied sentence lengths simultaneously
    ///         capped them. Every sentence over 24 words disappeared across five markets and the
    ///         longest fell 29 to 20, which is the opposite of what the same policy had just asked
    ///         for. A station now gets a ceiling only by choosing one.
    ///     </para>
    /// </summary>
    public int? MaxSentenceWords { get; init; }

    /// <summary>
    ///     How many delay figures the station wants spoken in one read. Zero means no limit.
    ///     <para>
    ///         ARN's deployed writer prompt says "voice a delay figure at most once per bulletin, on
    ///         the lead item only", so one is the house position. It belongs here rather than in
    ///         code because it is an editorial preference about how a read SOUNDS, not a fact about
    ///         the data, and different markets may want it differently.
    ///     </para>
    /// </summary>
    public int MaxDelayFigures { get; init; }

    /// <summary>How connected the read should sound.</summary>
    public TransitionDensity Transitions { get; init; } = TransitionDensity.Natural;

    /// <summary>Wording ARN wants used where it fits.</summary>
    public IReadOnlyList<string> PreferredPhrases { get; init; } = [];

    /// <summary>Wording ARN has ruled out. Never a grounding rule, only a style one.</summary>
    public IReadOnlyList<string> AvoidPhrases { get; init; } = [];

    /// <summary>Reference reads showing the shape ARN wants, never wording to copy verbatim.</summary>
    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>Free-text voice direction from ARN, rendered into the compose prompt verbatim.</summary>
    public string Notes { get; init; } = string.Empty;

    public bool IsEmpty =>
        Tone.Length == 0 && RoadFeelPalette.Length == 0 && PreferredPhrases.Count == 0
        && AvoidPhrases.Count == 0 && Examples.Count == 0 && Notes.Length == 0
        && Transitions == TransitionDensity.Natural && MaxSentenceWords is null && MaxDelayFigures == 0;
}

/// <summary>How much the read should join its thoughts together.</summary>
public enum TransitionDensity
{
    /// <summary>Each item stands alone. Suits a very short slot.</summary>
    Minimal,

    /// <summary>Join where the data genuinely relates, otherwise stand alone.</summary>
    Natural,

    /// <summary>Prefer a continuous read, joining wherever a real relationship exists.</summary>
    Connected
}

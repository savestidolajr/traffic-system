namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Geographic scope of a standing-guidance directive. The numeric order encodes
///     precedence: a narrower scope wins (Station &gt; Market &gt; Global).
/// </summary>
public enum GuidanceScope
{
    Global = 0,
    Market = 1,
    Station = 2
}

/// <summary>
///     Editorial type of a directive. Every value composes into the prompt as editorial
///     text inside the &lt;guidance&gt; block, except <see cref="HouseStyle" /> which renders
///     into its own &lt;house-style&gt; preamble slot. (Road emphasis and pronunciation are
///     deliberately NOT modelled here — they route to the existing PriorityRoads and
///     ElevenLabs-pronunciation mechanisms; modelling them as guidance text would create
///     dead enum values.)
/// </summary>
public enum GuidanceType
{
    /// <summary>Tone, slang level, phrasing preferences.</summary>
    Style,

    /// <summary>Content ordering, lead criteria, item-selection rules.</summary>
    Editorial,

    /// <summary>Naming conventions ("the Monash", "the M1", "the CBD").</summary>
    Lexicon,

    /// <summary>Words or framings to avoid ("never say chaos").</summary>
    Prohibition,

    /// <summary>Free-text house-style preamble. Renders into the &lt;house-style&gt; slot.</summary>
    HouseStyle
}

/// <summary>
///     One standing-guidance directive (Layer 2 of the self-service guidance model):
///     scoped, typed, enable/disable, provenance-tracked. Composed into the draft prompt
///     by <see cref="Services.GuidanceComposer" />; it may steer STYLE DEFAULTS but never
///     crosses the engineered prompt's BOUNDARIES.
/// </summary>
public sealed record GuidanceEntry(
    string Id,
    GuidanceScope Scope,
    string ScopeId,
    GuidanceType Type,
    string Text,
    bool Enabled,
    string CreatedBy,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Option<string> Source = default,
    Option<DateTime> ReviewAfterUtc = default)
{
    /// <summary>The manual-authoring provenance marker.</summary>
    public const string ManualSource = "manual";

    /// <summary>Provenance marker prefix for a directive promoted from operator feedback.</summary>
    private const string PromotedPrefix = "promoted:";

    /// <summary>Provenance marker for a directive promoted from operator feedback.</summary>
    public static string PromotedSource(string jobId) => $"{PromotedPrefix}{jobId}";

    /// <summary>
    ///     Extracts the job id from a promoted-source marker, or null when the source is not a
    ///     promotion (manual or absent). Lets the candidates queue and the promote route recognise
    ///     an already-promoted candidate by its stored marker.
    /// </summary>
    public static string? PromotedJobIdOf(Option<string> source)
        => source.Match(
            s => s.StartsWith(PromotedPrefix, StringComparison.Ordinal) ? s[PromotedPrefix.Length..] : null,
            () => null);

    /// <summary>
    ///     Mints a new directive with a fresh id and matching created/updated timestamps.
    ///     <paramref name="scopeId" /> is normalised to empty for Global scope.
    /// </summary>
    public static GuidanceEntry Create(
        GuidanceScope scope,
        string scopeId,
        GuidanceType type,
        string text,
        bool enabled,
        string createdBy,
        DateTime nowUtc,
        Option<string> source = default,
        Option<DateTime> reviewAfterUtc = default)
    {
        return new GuidanceEntry(
            Guid.NewGuid().ToString("N"),
            scope,
            NormalizeScopeId(scope, scopeId),
            type,
            (text ?? string.Empty).Trim(),
            enabled,
            createdBy,
            nowUtc,
            nowUtc,
            source.HasValue ? source : Option<string>.Some(ManualSource),
            reviewAfterUtc);
    }

    /// <summary>Global directives carry no scope id; market/station keep theirs (trimmed).</summary>
    public static string NormalizeScopeId(GuidanceScope scope, string? scopeId)
        => scope == GuidanceScope.Global ? string.Empty : (scopeId ?? string.Empty).Trim();
}

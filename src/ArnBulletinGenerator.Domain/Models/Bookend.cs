using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The versioned value of a bookend: the opening and closing lines ARN authored, as ONE unit.
///     Either may be empty (an intro with no closer, or vice versa) but not both. Both may contain
///     <c>{token}</c> placeholders resolved per station (see
///     <see cref="Services.BookendTokenResolver" />).
///     <para>
///         Intro and outro live together because they are written together — an opener and its
///         matching sign-off are one editorial decision, and a station assigns the pair, not two
///         halves it has to keep in step.
///     </para>
///     The text is ARN's own editorial content and is never rewritten by the pipeline: the writer is
///     shown it so the body reads into it, but no LLM stage may reword, critique, or cut it.
/// </summary>
public sealed partial record BookendTemplate(string IntroText, string OutroText)
{
    /// <summary>
    ///     Storage/abuse guard on each authored line. Deliberately NOT a slot budget: a bookend's
    ///     effect on the airtime available for traffic content is absorbed by the word target, not
    ///     capped here.
    /// </summary>
    public const int MaxTextLength = 500;

    /// <summary>True when this pair opens the bulletin.</summary>
    public bool HasIntro => !string.IsNullOrWhiteSpace(IntroText);

    /// <summary>True when this pair closes the bulletin.</summary>
    public bool HasOutro => !string.IsNullOrWhiteSpace(OutroText);

    /// <summary>Rejects an empty pair, an oversized line, or an incomplete outro.</summary>
    public Result Validate()
    {
        if (!HasIntro && !HasOutro)
            return Result.Failure("A bookend needs an intro, an outro, or both.");
        if ((IntroText ?? "").Length > MaxTextLength)
            return Result.Failure($"Bookend intro must be at most {MaxTextLength} characters.");
        if ((OutroText ?? "").Length > MaxTextLength)
            return Result.Failure($"Bookend outro must be at most {MaxTextLength} characters.");
        return ValidateOutro(OutroText);
    }

    /// <summary>
    ///     Rejects an authored outro that ends in a connector or handoff with no following
    ///     sponsor/content, or in a dangling pause marker. Empty outros are valid because a pair
    ///     may intentionally carry only an intro.
    /// </summary>
    public static Result ValidateOutro(string? outroText)
    {
        if (string.IsNullOrWhiteSpace(outroText) || !DanglingOutroPattern().IsMatch(outroText))
            return Result.Success();

        return Result.Failure(
            "Bookend outro must end with a complete closing phrase; dangling connectors, handoffs, dashes, and ellipses are not allowed.");
    }

    /// <summary>
    ///     Value identity used to detect a real change between versions — a whitespace-only edit
    ///     must not mint a version (mirrors <see cref="DeliveryStylePolicy.ConfigurationKey" />).
    /// </summary>
    public string ConfigurationKey => $"{(IntroText ?? "").Trim()}|{(OutroText ?? "").Trim()}";

    /// <summary>Stores both lines trimmed so a trailing newline never reaches the synthesiser.</summary>
    public BookendTemplate Normalized() =>
        this with { IntroText = (IntroText ?? "").Trim(), OutroText = (OutroText ?? "").Trim() };

    [GeneratedRegex(
        @"(?:\bpowered\s+by\b|\bbrought\s+to\s+you\s+by\b|\b(?:and|or|but|with)\b)[\p{P}\s]*$|(?:\.{3,}|…|[-\u2010-\u2015])[\p{P}\s]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DanglingOutroPattern();

}

/// <summary>
///     One immutable iteration of a bookend: a template snapshot. Append-only — editing either line
///     mints a new version rather than mutating an existing one, so the wording history of an
///     on-air phrase is never lost (mirrors <see cref="DeliveryStyleVersion" />).
/// </summary>
public sealed record BookendVersion(
    int Version,
    BookendTemplate Template,
    string? ChangeNote,
    DateTime CreatedAt,
    string? CreatedBy);

/// <summary>
///     A bookend pair as it will actually air: both token-substituted lines plus the library id and
///     version they came from, snapshotted at dispatch so what the poller resolved is what airs even
///     across a queue redelivery (same philosophy as the resolved delivery style and TTS model).
///     Carried on the broadcast context and persisted as job provenance. An empty line means that
///     end is simply not read.
/// </summary>
public sealed record ResolvedBookends(string IntroText, string OutroText, string Id, int Version)
{
    public bool HasIntro => !string.IsNullOrWhiteSpace(IntroText);
    public bool HasOutro => !string.IsNullOrWhiteSpace(OutroText);
}

/// <summary>
///     A named, versioned opening-and-closing pair stations read around every bulletin — the unit of
///     the shared bookend library. The current version is always the latest iteration; editing either
///     line appends a new one. A station selects one pair (optionally rotating a set via
///     <see cref="BookendRotationPolicy" />); an unassigned station reads exactly as it does today,
///     with no bookend at all.
/// </summary>
public sealed class Bookend
{
    private readonly List<BookendVersion> _versions;

    private Bookend(
        string id,
        string name,
        string? description,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<BookendVersion> versions)
    {
        Id = id;
        Name = name;
        Description = description;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CreatedBy = createdBy;
        AssignedStations = assignedStations;
        _versions = versions;
    }

    public string Id { get; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string Status { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; private set; }
    public string? CreatedBy { get; }
    public IReadOnlyList<string> AssignedStations { get; private set; }
    public IReadOnlyList<BookendVersion> Versions => _versions;

    /// <summary>The current iteration is always the most recently appended version.</summary>
    public int CurrentVersion => _versions.Count == 0 ? 0 : _versions[^1].Version;

    /// <summary>The template of the current (latest) version.</summary>
    public BookendTemplate CurrentTemplate =>
        _versions.Count == 0 ? new BookendTemplate("", "") : _versions[^1].Template;

    /// <summary>
    ///     Creates a new bookend and mints version 1 from the supplied template. An explicit
    ///     <paramref name="id" /> preserves identity for config imports (round-trip idempotency);
    ///     omitted, a fresh "bk-…" id is generated.
    /// </summary>
    public static Result<Bookend> Create(
        string name,
        string? description,
        BookendTemplate template,
        string? changeNote,
        string? createdBy,
        DateTime nowUtc,
        string? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<Bookend>.Failure("Bookend name is required.");
        if (template is null)
            return Result<Bookend>.Failure("Bookend template is required.");
        var templateValidation = template.Validate();
        if (templateValidation.IsFailure)
            return Result<Bookend>.Failure(templateValidation.Error);

        id = string.IsNullOrWhiteSpace(id) ? $"bk-{Guid.NewGuid():N}" : id.Trim();
        var version1 = new BookendVersion(1, template.Normalized(), Normalize(changeNote), nowUtc, createdBy);
        return new Bookend(id, name.Trim(), Normalize(description), "active",
            nowUtc, nowUtc, createdBy, [], [version1]);
    }

    /// <summary>
    ///     Applies a name/description edit and, when either line differs from the current version,
    ///     mints a new version. Callers validate the template before invoking this mutation.
    /// </summary>
    public bool ApplyEdit(
        string name,
        string? description,
        BookendTemplate template,
        string? changeNote,
        string? editedBy,
        DateTime nowUtc)
    {
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        Description = Normalize(description);

        var latest = _versions[^1];
        var textChanged = latest.Template.ConfigurationKey != template.ConfigurationKey;
        if (textChanged)
            _versions.Add(new BookendVersion(
                latest.Version + 1, template.Normalized(), Normalize(changeNote), nowUtc, editedBy));

        UpdatedAt = nowUtc;
        return textChanged;
    }

    /// <summary>Mints a new version that copies a prior version's template (immutable restore).</summary>
    public Result RestoreVersion(int version, string? restoredBy, DateTime nowUtc)
    {
        var source = _versions.FirstOrDefault(v => v.Version == version);
        if (source is null)
            return Result.Failure($"Bookend version {version} not found.");

        _versions.Add(new BookendVersion(_versions[^1].Version + 1, source.Template,
            $"Restored from version {version}.", nowUtc, restoredBy));
        UpdatedAt = nowUtc;
        return Result.Success();
    }

    /// <summary>Archives this bookend — hides it from the active library. Does NOT mint a new version.</summary>
    public void Archive(DateTime nowUtc)
    {
        Status = "archived";
        UpdatedAt = nowUtc;
    }

    /// <summary>Unarchives this bookend — restores it to the active library. Does NOT mint a new version.</summary>
    public void Unarchive(DateTime nowUtc)
    {
        Status = "active";
        UpdatedAt = nowUtc;
    }

    /// <summary>Rehydrates a bookend from persisted data without re-running creation invariants.</summary>
    public static Bookend Reconstitute(
        string id,
        string name,
        string? description,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<BookendVersion> versions)
    {
        return new Bookend(id, name, description, status, createdAt, updatedAt, createdBy,
            assignedStations, versions);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A delivery policy: the plain-language tone plus the curated ElevenLabs v3 audio-tag
///     palette and intensity the script director may use. Words are NEVER changed by the
///     director — a policy only steers HOW the bulletin is read (emphasis, pacing, emotion).
///     The neutral policy carries no tags, which makes the director a guaranteed no-op
///     (an unassigned station reads exactly as it does today).
/// </summary>
public sealed record DeliveryStylePolicy(
    string ToneDescription,
    IReadOnlyList<string> AllowedTags,
    int Intensity)
{
    /// <summary>No tagging — the director returns the script unchanged.</summary>
    public static DeliveryStylePolicy Neutral { get; } =
        new("Neutral, even, professional Australian radio read.", [], 0);

    /// <summary>
    ///     Permissive fallback used when an operator gives an explicit plain-language re-direction
    ///     ("read it calmer") on a bulletin whose station style is neutral — gives the director a
    ///     broad standard radio palette to honour the intent.
    /// </summary>
    public static DeliveryStylePolicy OperatorDirected { get; } = new(
        "Apply the operator's specific delivery direction for this read.",
        [],
        2);

    /// <summary>
    ///     Neutral ONLY at zero intensity. An empty palette is NOT neutral: it means
    ///     punctuation-only direction - ellipses and pacing, no audio tags at all.
    ///
    ///     That is the default an operator wants. Every tag that measurably works on our voices
    ///     inserts a non-speech artefact (a sigh, a breath, a throat clear, a mechanical pause),
    ///     and none of those belong in a traffic bulletin unless the operator asks for one
    ///     deliberately. Punctuation carries the read without putting anything on air that a
    ///     newsreader would not do (decided 2026-08-03).
    /// </summary>
    public bool IsNeutral => Intensity <= 0;

    /// <summary>Valid range for <see cref="Intensity" /> — the ElevenLabs v3 audio-tag intensity scale.</summary>
    public const int MinIntensity = 0;

    /// <summary>Valid range for <see cref="Intensity" /> — the ElevenLabs v3 audio-tag intensity scale.</summary>
    public const int MaxIntensity = 3;

    /// <summary>Rejects an intensity outside the supported audio-tag scale (e.g. a malformed import).</summary>
    public Result Validate() =>
        Intensity < MinIntensity || Intensity > MaxIntensity
            ? Result.Failure($"Invalid delivery policy intensity {Intensity}; must be between {MinIntensity} and {MaxIntensity}.")
            : Result.Success();

    /// <summary>
    ///     Value identity used to detect a real change between versions (record equality
    ///     compares the <see cref="AllowedTags" /> list by reference, which is not what we want).
    /// </summary>
    public string ConfigurationKey =>
        $"{ToneDescription.Trim()}|{Intensity}|{string.Join(",", AllowedTags)}";
}

/// <summary>
///     One immutable iteration of a delivery style: a policy snapshot. Append-only —
///     editing the policy mints a new version rather than mutating an existing one, so the
///     tuning history is never lost (mirrors <see cref="VoiceProfileVersion" />).
/// </summary>
public sealed record DeliveryStyleVersion(
    int Version,
    DeliveryStylePolicy Policy,
    string? ChangeNote,
    DateTime CreatedAt,
    string? CreatedBy);

/// <summary>
///     A named, versioned delivery tone stations read with — the unit of the shared delivery
///     library. The current version is always the latest iteration; editing the policy appends
///     a new one. A station selects a style as its default; an unassigned station falls back to
///     the neutral policy.
/// </summary>
public sealed class DeliveryStyle
{
    private readonly List<DeliveryStyleVersion> _versions;

    private DeliveryStyle(
        string id,
        string name,
        string? description,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<DeliveryStyleVersion> versions)
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
    public IReadOnlyList<DeliveryStyleVersion> Versions => _versions;

    /// <summary>The current iteration is always the most recently appended version.</summary>
    public int CurrentVersion => _versions.Count == 0 ? 0 : _versions[^1].Version;

    /// <summary>The policy of the current (latest) version.</summary>
    public DeliveryStylePolicy CurrentPolicy =>
        _versions.Count == 0 ? DeliveryStylePolicy.Neutral : _versions[^1].Policy;

    /// <summary>
    ///     Creates a new style and mints version 1 from the supplied policy. An explicit
    ///     <paramref name="id" /> preserves identity for config imports (round-trip idempotency);
    ///     omitted, a fresh "ds-…" id is generated.
    /// </summary>
    public static Result<DeliveryStyle> Create(
        string name,
        string? description,
        DeliveryStylePolicy policy,
        string? changeNote,
        string? createdBy,
        DateTime nowUtc,
        string? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<DeliveryStyle>.Failure("Delivery style name is required.");
        if (policy is null)
            return Result<DeliveryStyle>.Failure("Delivery policy is required.");
        var policyValidation = policy.Validate();
        if (policyValidation.IsFailure)
            return Result<DeliveryStyle>.Failure(policyValidation.Error);

        id = string.IsNullOrWhiteSpace(id) ? $"ds-{Guid.NewGuid():N}" : id.Trim();
        var version1 = new DeliveryStyleVersion(1, policy, Normalize(changeNote), nowUtc, createdBy);
        return new DeliveryStyle(id, name.Trim(), Normalize(description), "active",
            nowUtc, nowUtc, createdBy, [], [version1]);
    }

    /// <summary>
    ///     Applies a name/description edit and, when the policy differs from the current version,
    ///     mints a new version. Returns true when a new version was minted.
    /// </summary>
    public bool ApplyEdit(
        string name,
        string? description,
        DeliveryStylePolicy policy,
        string? changeNote,
        string? editedBy,
        DateTime nowUtc)
    {
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        Description = Normalize(description);

        var latest = _versions[^1];
        var policyChanged = latest.Policy.ConfigurationKey != policy.ConfigurationKey;
        if (policyChanged)
            _versions.Add(new DeliveryStyleVersion(latest.Version + 1, policy, Normalize(changeNote), nowUtc, editedBy));

        UpdatedAt = nowUtc;
        return policyChanged;
    }

    /// <summary>Mints a new version that copies a prior version's policy (immutable restore).</summary>
    public Result RestoreVersion(int version, string? restoredBy, DateTime nowUtc)
    {
        var source = _versions.FirstOrDefault(v => v.Version == version);
        if (source is null)
            return Result.Failure($"Delivery style version {version} not found.");

        _versions.Add(new DeliveryStyleVersion(_versions[^1].Version + 1, source.Policy,
            $"Restored from version {version}.", nowUtc, restoredBy));
        UpdatedAt = nowUtc;
        return Result.Success();
    }

    /// <summary>Archives this style — hides it from the active library. Does NOT mint a new version.</summary>
    public void Archive(DateTime nowUtc)
    {
        Status = "archived";
        UpdatedAt = nowUtc;
    }

    /// <summary>Unarchives this style — restores it to the active library. Does NOT mint a new version.</summary>
    public void Unarchive(DateTime nowUtc)
    {
        Status = "active";
        UpdatedAt = nowUtc;
    }

    /// <summary>Rehydrates a style from persisted data without re-running creation invariants.</summary>
    public static DeliveryStyle Reconstitute(
        string id,
        string name,
        string? description,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<DeliveryStyleVersion> versions)
    {
        return new DeliveryStyle(id, name, description, status, createdAt, updatedAt, createdBy,
            assignedStations, versions);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

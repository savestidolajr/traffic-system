using System.Text.Json;
using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Errors;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Locates a specific version of an ElevenLabs pronunciation dictionary. Attached to a
///     synthesis request so ElevenLabs applies the dictionary's rules (alias on every model,
///     phoneme on the v2-family only). The Domain-level twin of the Infrastructure locator that
///     hits the wire — carried on the broadcast context so the synth path stays storage-free.
/// </summary>
public sealed record PronunciationLocator(string DictionaryId, string VersionId);

/// <summary>
///     Which bulletins a dictionary reaches. The numeric order encodes precedence: a narrower
///     scope wins a word both scopes define (Station &gt; Global), mirroring
///     <see cref="GuidanceScope" /> so scope precedence reads the same way platform-wide.
/// </summary>
public enum PronunciationScope
{
    /// <summary>Applies to every station, under whatever dictionary the station itself carries.</summary>
    Global = 0,

    /// <summary>Assignable to a station via <see cref="MarketSchedule.PronunciationDictionaryId" />.</summary>
    Station = 1
}

/// <summary>
///     The ordered locator set attached to ONE synthesis call.
///     <para>
///         <b>Order is precedence.</b> ElevenLabs applies locators sequentially and the FIRST
///         one to match a word wins it — verified live 2026-07-31 with two dictionaries
///         disagreeing on one word: <c>[station, global]</c> aired the station's respelling,
///         <c>[global, station]</c> the global's. Non-colliding rules from every layer still
///         apply (the layers union), so the station layer is emitted FIRST to override the
///         global per word while inheriting the rest.
///     </para>
///     <para>
///         Emitting these the other way round would leave every station-local correction
///         silently dead — the same failure mode as appending a second rule for a word instead
///         of replacing it (see <see cref="PronunciationRuleSetEditor" />), which is why the
///         ordering lives here and not at each call site.
///     </para>
/// </summary>
public sealed record PronunciationStack
{
    /// <summary>ElevenLabs rejects a request carrying more locators than this.</summary>
    public const int MaxLocators = 3;

    private PronunciationStack(
        IReadOnlyList<PronunciationLocator> locators,
        Option<PronunciationLocator> station,
        Option<PronunciationLocator> global)
    {
        Locators = locators;
        Station = station;
        Global = global;
    }

    /// <summary>No dictionary applies — the synthesis request omits the locator key entirely.</summary>
    public static readonly PronunciationStack Empty =
        new([], Option<PronunciationLocator>.None, Option<PronunciationLocator>.None);

    /// <summary>The locators in the exact order they must reach the provider.</summary>
    public IReadOnlyList<PronunciationLocator> Locators { get; }

    /// <summary>
    ///     The station layer, or None. This is what an operator's one-word fix targets: a
    ///     correction made while listening to one market's bulletin must not change what every
    ///     other station says.
    /// </summary>
    public Option<PronunciationLocator> Station { get; }

    /// <summary>The platform-wide layer, or None.</summary>
    public Option<PronunciationLocator> Global { get; }

    public bool IsEmpty => Locators.Count == 0;

    /// <summary>Station first, global second. Either layer may be absent.</summary>
    public static PronunciationStack Of(
        Option<PronunciationLocator> station, Option<PronunciationLocator> global)
    {
        var locators = new List<PronunciationLocator>(2);
        if (station.HasValue)
            locators.Add(station.Value);
        if (global.HasValue)
            locators.Add(global.Value);
        return new PronunciationStack(locators, station, global);
    }

    /// <summary>
    ///     Builds a stack from the two explicitly-identified layers a queue message or job row
    ///     carries. Layer identity is never inferred from position: a station with no dictionary
    ///     of its own still gets the global layer, and mistaking that for a station layer would
    ///     aim an operator's word fix at the platform-wide dictionary.
    /// </summary>
    public static PronunciationStack Of(
        PronunciationLocator? station, PronunciationLocator? global) =>
        Of(Option.OfNullable(station), Option.OfNullable(global));

    /// <summary>
    ///     One persisted dictionary/version pair as a locator, or None when either half is
    ///     missing. ElevenLabs rejects a locator without both, so a half-recorded pair must never
    ///     reach the wire.
    /// </summary>
    public static Option<PronunciationLocator> Locator(
        Option<string> dictionaryId, Option<string> versionId) =>
        dictionaryId.Match(
            id => versionId.Match(
                version => Option<PronunciationLocator>.Some(new PronunciationLocator(id, version)),
                () => Option<PronunciationLocator>.None),
            () => Option<PronunciationLocator>.None);
}

/// <summary>
///     One immutable iteration of a pronunciation dictionary: the rule set plus the ElevenLabs
///     version id it was uploaded as. Append-only — editing the rules pushes them to ElevenLabs
///     (which mints a new version id) and appends a new iteration here, so the rule history and
///     every version id are preserved (mirrors <see cref="DeliveryStyleVersion" />).
///     <para>
///         <see cref="AliasOnlyVersionId" /> is the version id of the SAME upstream dictionary
///         holding only the alias rules of this version — the safe attach target for a model
///         (eleven_multilingual_v2) that would delete phoneme-matched words. Null when this
///         version has no phoneme rules (the full version is already safe) or the alias-only
///         line has not been minted.
///     </para>
/// </summary>
public sealed record PronunciationDictionaryVersion(
    int Version,
    string ElevenLabsVersionId,
    IReadOnlyList<PronunciationRule> Rules,
    string? ChangeNote,
    DateTime CreatedAt,
    string? CreatedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AliasOnlyVersionId = null)
{
    /// <summary>Value identity of the rule set, used to detect a real change between versions.</summary>
    [JsonIgnore]
    public string RuleSetKey =>
        string.Join(";", Rules.Select(r => r.Signature).OrderBy(s => s, StringComparer.Ordinal));
}

/// <summary>
///     A named, versioned ElevenLabs pronunciation dictionary that stations read with — the unit
///     of the shared pronunciation library, used so different regions can carry their own
///     suburb/road pronunciations and ARN can evolve them over time. A schedule selects a
///     dictionary by id (<see cref="MarketSchedule.PronunciationDictionaryId" />); an unassigned
///     station falls back to the configured default (or none). The current version is always the
///     latest iteration; editing the rules appends a new one. Mirrors
///     <see cref="VoiceProfile" /> / <see cref="DeliveryStyle" />, but additionally owns the
///     external ElevenLabs dictionary resource (<see cref="DictionaryId" />).
/// </summary>
public sealed class PronunciationDictionary
{
    private readonly List<PronunciationDictionaryVersion> _versions;

    private PronunciationDictionary(
        string id,
        string dictionaryId,
        string name,
        string? description,
        string status,
        PronunciationScope scope,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<PronunciationDictionaryVersion> versions,
        string? eTag)
    {
        Id = id;
        DictionaryId = dictionaryId;
        Name = name;
        Description = description;
        Status = status;
        Scope = scope;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CreatedBy = createdBy;
        AssignedStations = assignedStations;
        _versions = versions;
        ETag = eTag;
    }

    /// <summary>Internal library id (e.g. "pd-…").</summary>
    public string Id { get; }

    /// <summary>The ElevenLabs pronunciation dictionary id this library item owns.</summary>
    public string DictionaryId { get; }

    public string Name { get; private set; }
    public string? Description { get; private set; }
    public string Status { get; private set; }

    /// <summary>
    ///     Whether this dictionary is assigned per station or applies platform-wide. Rows written
    ///     before the global layer existed read back as <see cref="PronunciationScope.Station" />,
    ///     which is what they were.
    /// </summary>
    public PronunciationScope Scope { get; private set; }

    /// <summary>True when this dictionary applies to every station, under their own.</summary>
    public bool IsGlobal => Scope == PronunciationScope.Global;

    /// <summary>True when this dictionary is in the active (non-archived) library.</summary>
    public bool IsActive => string.Equals(Status, "active", StringComparison.OrdinalIgnoreCase);
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; private set; }
    public string? CreatedBy { get; }
    public IReadOnlyList<string> AssignedStations { get; private set; }
    public IReadOnlyList<PronunciationDictionaryVersion> Versions => _versions;

    /// <summary>Opaque Azure Table ETag for optimistic concurrency. Null means new entity (creation path).</summary>
    public string? ETag { get; set; }

    /// <summary>The current iteration is always the most recently appended version.</summary>
    public int CurrentVersion => _versions.Count == 0 ? 0 : _versions[^1].Version;

    /// <summary>The rule set of the current (latest) version.</summary>
    public IReadOnlyList<PronunciationRule> CurrentRules =>
        _versions.Count == 0 ? [] : _versions[^1].Rules;

    /// <summary>The ElevenLabs version id of the current (latest) version.</summary>
    public string CurrentElevenLabsVersionId =>
        _versions.Count == 0 ? string.Empty : _versions[^1].ElevenLabsVersionId;

    /// <summary>
    ///     The locator for the current version, or None when there is no usable version id
    ///     (e.g. a dictionary that never finished uploading). Attached to synthesis requests.
    /// </summary>
    public Option<PronunciationLocator> CurrentLocator =>
        string.IsNullOrWhiteSpace(DictionaryId) || string.IsNullOrWhiteSpace(CurrentElevenLabsVersionId)
            ? Option<PronunciationLocator>.None
            : Option<PronunciationLocator>.Some(new PronunciationLocator(DictionaryId, CurrentElevenLabsVersionId));

    /// <summary>
    ///     The locator for the current version's alias-only line — the same upstream dictionary
    ///     at the version holding only its alias rules. Null when the current version carries no
    ///     phoneme rules (the full locator is safe on every model) or the line was never minted.
    /// </summary>
    public Option<PronunciationLocator> CurrentAliasOnlyLocator
    {
        get
        {
            if (_versions.Count == 0 || string.IsNullOrWhiteSpace(DictionaryId))
                return Option<PronunciationLocator>.None;
            string? aliasOnlyVersionId = _versions[^1].AliasOnlyVersionId;
            return string.IsNullOrWhiteSpace(aliasOnlyVersionId)
                ? Option<PronunciationLocator>.None
                : Option<PronunciationLocator>.Some(new PronunciationLocator(DictionaryId, aliasOnlyVersionId));
        }
    }

    /// <summary>
    ///     The single decision both the airtime resolver and the preview path use when attaching
    ///     this dictionary to a synthesis on <paramref name="voiceModel" />, so a phoneme rule
    ///     can never air (or be previewed) on a model that would delete its matched words:
    ///     <list type="bullet">
    ///         <item>No phoneme rules, or the model renders/ignores them benignly — the full current locator.</item>
    ///         <item>Phoneme rules on eleven_multilingual_v2 — the alias-only locator when one exists.</item>
    ///         <item>Otherwise — None (no safe attachable version; callers drop the layer).</item>
    ///     </list>
    /// </summary>
    public Option<PronunciationLocator> LocatorFor(string voiceModel)
    {
        if (!CurrentRules.Any(r => r.Type == PronunciationRuleType.Phoneme)
            || PronunciationRule.PhonemeRulesSafeFor(voiceModel))
            return CurrentLocator;
        return CurrentAliasOnlyLocator;
    }

    /// <summary>
    ///     The words whose phoneme rules would be deleted (or otherwise not applied) on
    ///     <paramref name="voiceModel" /> — empty when the whole dictionary is attachable.
    ///     Callers log these so an operator sees exactly which rules were dropped and why.
    /// </summary>
    public IReadOnlyList<string> UnsupportedRuleWordsFor(string voiceModel) =>
        !CurrentRules.Any(r => r.Type == PronunciationRuleType.Phoneme)
        || PronunciationRule.PhonemeRulesSafeFor(voiceModel)
            ? []
            : CurrentRules
                .Where(r => r.Type == PronunciationRuleType.Phoneme)
                .Select(r => r.StringToReplace)
                .ToList();

    /// <summary>
    ///     Azure Table's hard per-property ceiling (32,768 UTF-16 chars) — the wall every
    ///     versioned library's serialised history must fit. Single source of truth shared with
    ///     <c>VersionHistoryTrimmer.PropertyCeiling</c>; the service's pre-provider size check
    ///     serialises a candidate version with exactly this bound.
    /// </summary>
    public const int StoragePropertyCeiling = 32_768;

    /// <summary>
    ///     Serialization options that mirror <c>Infrastructure/Serialization/JsonDefaults.Storage</c>
    ///     for this type (the Option converter it also carries does not affect a version's
    ///     properties), so a pre-flight size computed here agrees with the storage wall to the byte.
    /// </summary>
    private static readonly JsonSerializerOptions StorageMirrorOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    ///     The deterministic, local half of the storage wall: serialises the candidate version the
    ///     same way the repository's trimmer will and fails with a typed <c>InvalidInput</c> when it
    ///     ALONE exceeds <see cref="StoragePropertyCeiling" />. Trimming cannot help a single
    ///     oversized version, so the eventual table write would fail — the service runs this BEFORE
    ///     any TTS-provider call so an oversized payload is rejected with HTTP 400 and never reaches
    ///     ElevenLabs (the residual window where a failure still follows a successful upstream call
    ///     is covered by the service's archive rollback).
    /// </summary>
    public static Result EnsureVersionFitsStorage(PronunciationDictionaryVersion candidate)
    {
        string json = JsonSerializer.Serialize(new[] { candidate }, StorageMirrorOptions);
        if (json.Length <= StoragePropertyCeiling)
            return Result.Success();

        return Result.Failure(OperationalFailure.InvalidInput(
            "versionhistory.entry_too_large",
            $"This pronunciation dictionary is too large to save — a single version is " +
            $"{json.Length:N0} characters against a {StoragePropertyCeiling:N0} limit. Remove or shorten " +
            "some entries and save again."));
    }

    /// <summary>
    ///     Creates a new dictionary and mints version 1 from the supplied rules. The ElevenLabs
    ///     dictionary + first version must already have been created (the service uploads the
    ///     rules first, then constructs this with the returned ids). An explicit
    ///     <paramref name="id" /> preserves identity for config imports (round-trip idempotency);
    ///     omitted, a fresh "pd-…" id is generated. <paramref name="aliasOnlyVersionId" /> pins
    ///     the same dictionary's alias-only version (see
    ///     <see cref="PronunciationDictionaryVersion.AliasOnlyVersionId" />).
    /// </summary>
    public static Result<PronunciationDictionary> Create(
        string name,
        string? description,
        string dictionaryId,
        string elevenLabsVersionId,
        IReadOnlyList<PronunciationRule> rules,
        string? changeNote,
        string? createdBy,
        DateTime nowUtc,
        string? id = null,
        PronunciationScope scope = PronunciationScope.Station,
        string? aliasOnlyVersionId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<PronunciationDictionary>.Failure("Pronunciation dictionary name is required.");
        if (string.IsNullOrWhiteSpace(dictionaryId))
            return Result<PronunciationDictionary>.Failure("An ElevenLabs dictionary id is required.");
        if (string.IsNullOrWhiteSpace(elevenLabsVersionId))
            return Result<PronunciationDictionary>.Failure("An ElevenLabs version id is required.");

        id = string.IsNullOrWhiteSpace(id) ? $"pd-{Guid.NewGuid():N}" : id.Trim();
        var version1 = new PronunciationDictionaryVersion(
            1, elevenLabsVersionId, rules ?? [], Normalize(changeNote), nowUtc, createdBy, aliasOnlyVersionId);
        return new PronunciationDictionary(id, dictionaryId, name.Trim(), Normalize(description), "active",
            scope, nowUtc, nowUtc, createdBy, [], [version1], null);
    }

    /// <summary>
    ///     Applies a name/description edit and, when the rule set differs from the current
    ///     version, appends a new version carrying the supplied (already-uploaded) ElevenLabs
    ///     version id and rule snapshot. Returns true when a new version was minted. The service
    ///     uploads the rule diff to ElevenLabs first and passes the resulting version id here.
    ///     <paramref name="aliasOnlyVersionId" /> is the same dictionary's alias-only version for
    ///     the new rule set (see <see cref="PronunciationDictionaryVersion.AliasOnlyVersionId" />).
    /// </summary>
    public bool ApplyEdit(
        string name,
        string? description,
        string elevenLabsVersionId,
        IReadOnlyList<PronunciationRule> rules,
        string? changeNote,
        string? editedBy,
        DateTime nowUtc,
        string? aliasOnlyVersionId = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        Description = Normalize(description);

        var latest = _versions[^1];
        var next = new PronunciationDictionaryVersion(
            latest.Version + 1, elevenLabsVersionId, rules ?? [], Normalize(changeNote), nowUtc, editedBy,
            aliasOnlyVersionId);
        var rulesChanged = latest.RuleSetKey != next.RuleSetKey;
        if (rulesChanged)
            _versions.Add(next);

        UpdatedAt = nowUtc;
        return rulesChanged;
    }

    /// <summary>True when the supplied rule set differs from the current version's rules.</summary>
    public bool RulesDifferFrom(IReadOnlyList<PronunciationRule> rules)
    {
        var candidate = new PronunciationDictionaryVersion(0, string.Empty, rules ?? [], null, default, null);
        return _versions.Count == 0 || _versions[^1].RuleSetKey != candidate.RuleSetKey;
    }

    /// <summary>Archives this dictionary — hides it from the active library. No new version.</summary>
    public void Archive(DateTime nowUtc)
    {
        Status = "archived";
        UpdatedAt = nowUtc;
    }

    /// <summary>Unarchives this dictionary — restores it to the active library. No new version.</summary>
    public void Unarchive(DateTime nowUtc)
    {
        Status = "active";
        UpdatedAt = nowUtc;
    }

    /// <summary>
    ///     Changes which bulletins this dictionary reaches. Deliberately mints NO version: reach
    ///     is not rule content, and a no-op version row would pollute the audit trail every
    ///     station reads.
    /// </summary>
    public void Retarget(PronunciationScope scope, DateTime nowUtc)
    {
        Scope = scope;
        UpdatedAt = nowUtc;
    }

    /// <summary>Rehydrates a dictionary from persisted data without re-running creation invariants.</summary>
    public static PronunciationDictionary Reconstitute(
        string id,
        string dictionaryId,
        string name,
        string? description,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<PronunciationDictionaryVersion> versions,
        string? eTag = null,
        PronunciationScope scope = PronunciationScope.Station)
    {
        return new PronunciationDictionary(id, dictionaryId, name, description, status, scope, createdAt,
            updatedAt, createdBy, assignedStations, versions, eTag);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

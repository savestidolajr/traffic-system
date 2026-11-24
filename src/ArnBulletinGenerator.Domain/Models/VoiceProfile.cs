using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One immutable iteration of a voice profile: a settings snapshot + model, plus the
///     configuration key that locates its measured calibration (WPM/syllable memory).
///     Append-only — changing a profile's settings mints a new version rather than mutating
///     an existing one, so the tuning history is never lost.
/// </summary>
/// <param name="PaceBoostWpm">
///     Additive WPM boost over the voice's calibrated speaking rate, applied ONLY to
///     pre-synthesis script sizing (never sent to the TTS provider — separate from, and
///     independent of, ElevenLabs' 0.7–1.2 <see cref="VoiceSettings.Speed" />, which v3 voices
///     ignore entirely). A script sized for calibratedWpm + PaceBoostWpm reads naturally, then
///     the audio pipeline's post-synthesis time-stretch compresses it to the slot — the net
///     effect is a punchier, faster-sounding read without pitching up. 0 = no-op (default).
/// </param>
public sealed record VoiceProfileVersion(
    int Version,
    VoiceSettings Settings,
    string Model,
    string? ChangeNote,
    DateTime CreatedAt,
    string? CreatedBy,
    string? SampleUrl,
    string ConfigurationKey,
    double PaceBoostWpm = 0);

/// <summary>
///     A named, versioned tuning of a base ElevenLabs voice that stations read with — the unit
///     of the shared voice library. The current version is always the latest iteration; editing
///     the settings appends a new one. Calibration and on-air run samples are surfaced at read
///     time (from the calibration store / job history) and are not persisted on the version.
/// </summary>
public sealed class VoiceProfile
{
    private readonly List<VoiceProfileVersion> _versions;

    private VoiceProfile(
        string id,
        string name,
        string? description,
        string baseVoiceId,
        string baseVoiceName,
        string provider,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<VoiceProfileVersion> versions)
    {
        Id = id;
        Name = name;
        Description = description;
        BaseVoiceId = baseVoiceId;
        BaseVoiceName = baseVoiceName;
        Provider = provider;
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
    public string BaseVoiceId { get; private set; }
    public string BaseVoiceName { get; private set; }
    public string Provider { get; }
    public string Status { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; private set; }
    public string? CreatedBy { get; }
    public IReadOnlyList<string> AssignedStations { get; private set; }
    public IReadOnlyList<VoiceProfileVersion> Versions => _versions;

    /// <summary>The current iteration is always the most recently appended version.</summary>
    public int CurrentVersion => _versions.Count == 0 ? 0 : _versions[^1].Version;

    /// <summary>
    ///     Additive WPM boost accepted for a voice's pre-synthesis script sizing (see
    ///     <see cref="VoiceProfileVersion" />). Bounded so an operator cannot configure a
    ///     pathological over-compression request.
    ///     <para>
    ///         60 is set just past where the boost stops doing anything at all: the realisable
    ///         boost saturates at <c>calibratedWpm x (AbsolutePaceBoostSpeedUpCeiling - 1)</c>
    ///         (see <see cref="Services.WordTargetCalculator.SaturatingPaceBoostWpm" />), which
    ///         across every voice calibrated on dev falls between +42 and +58 wpm. The previous
    ///         cap of 40 sat BELOW every one of those, so it — not the compression ceiling — was
    ///         the binding constraint, and no voice could reach the pace the ceiling already
    ///         allowed. Raising the ceiling itself was considered and rejected: at 1.35 every
    ///         calibrated voice already reaches 183-222 wpm, past the 180-200 ATN band.
    ///     </para>
    /// </summary>
    public const double MaxPaceBoostWpm = 60;

    /// <summary>
    ///     Creates a new profile and mints version 1 from the supplied settings. An explicit
    ///     <paramref name="id" /> preserves identity for config imports (round-trip idempotency);
    ///     omitted, a fresh "vp-…" id is generated.
    /// </summary>
    public static Result<VoiceProfile> Create(
        string name,
        string? description,
        string baseVoiceId,
        string baseVoiceName,
        string model,
        VoiceSettings settings,
        string? changeNote,
        string? createdBy,
        DateTime nowUtc,
        double paceBoostWpm = 0,
        string? id = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<VoiceProfile>.Failure("Voice profile name is required.");
        if (string.IsNullOrWhiteSpace(baseVoiceId))
            return Result<VoiceProfile>.Failure("Base voice id is required.");
        if (string.IsNullOrWhiteSpace(model))
            return Result<VoiceProfile>.Failure("Model is required.");
        if (paceBoostWpm < 0 || paceBoostWpm > MaxPaceBoostWpm)
            return Result<VoiceProfile>.Failure(Errors.ErrorMessages.VoiceProfile.PaceBoostWpmOutOfRange);

        id = string.IsNullOrWhiteSpace(id) ? $"vp-{Guid.NewGuid():N}" : id.Trim();
        var version1 = NewVersion(1, baseVoiceId, settings, model, changeNote, createdBy, nowUtc, paceBoostWpm);
        return new VoiceProfile(id, name.Trim(), Normalize(description), baseVoiceId,
            string.IsNullOrWhiteSpace(baseVoiceName) ? baseVoiceId : baseVoiceName.Trim(),
            "elevenlabs", "active", nowUtc, nowUtc, createdBy, [], [version1]);
    }

    /// <summary>
    ///     Applies a name/description edit and, when the model, settings, or pace boost differ from
    ///     the current version (compared at calibration-key granularity, so sub-0.01 slider jitter
    ///     is ignored), mints a new version. Returns true when a new version was minted.
    /// </summary>
    public Result<bool> ApplyEdit(
        string name,
        string? description,
        string model,
        VoiceSettings settings,
        string? changeNote,
        string? editedBy,
        DateTime nowUtc,
        double paceBoostWpm = 0)
    {
        if (paceBoostWpm < 0 || paceBoostWpm > MaxPaceBoostWpm)
            return Result<bool>.Failure(Errors.ErrorMessages.VoiceProfile.PaceBoostWpmOutOfRange);

        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        Description = Normalize(description);

        var latest = _versions[^1];
        var configurationChanged =
            latest.ConfigurationKey != settings.GetConfigurationKey(BaseVoiceId) || latest.Model != model
            || Math.Abs(latest.PaceBoostWpm - paceBoostWpm) > 0.01;
        if (configurationChanged)
            _versions.Add(NewVersion(latest.Version + 1, BaseVoiceId, settings, model, changeNote, editedBy,
                nowUtc, paceBoostWpm));

        UpdatedAt = nowUtc;
        return Result<bool>.Success(configurationChanged);
    }

    /// <summary>
    ///     Replaces the base ElevenLabs voice this profile reads with — used when an operator
    ///     adopts a text-remixed voice. The swap is realized as a new version by the subsequent
    ///     <see cref="ApplyEdit" />, because the configuration key embeds the base voice id.
    /// </summary>
    public void AdoptBaseVoice(string baseVoiceId, string? baseVoiceName)
    {
        if (string.IsNullOrWhiteSpace(baseVoiceId) || baseVoiceId == BaseVoiceId)
            return;
        BaseVoiceId = baseVoiceId;
        BaseVoiceName = string.IsNullOrWhiteSpace(baseVoiceName) ? baseVoiceId : baseVoiceName.Trim();
    }

    /// <summary>Mints a new version that copies a prior version's settings/model (immutable restore).</summary>
    public Result RestoreVersion(int version, string? restoredBy, DateTime nowUtc)
    {
        var source = _versions.FirstOrDefault(v => v.Version == version);
        if (source is null)
            return Result.Failure($"Voice profile version {version} not found.");

        _versions.Add(NewVersion(_versions[^1].Version + 1, BaseVoiceId, source.Settings, source.Model,
            $"Restored from version {version}.", restoredBy, nowUtc, source.PaceBoostWpm));
        UpdatedAt = nowUtc;
        return Result.Success();
    }

    /// <summary>Archives this profile — hides it from the active library and stops NEW schedule
    /// assignment. Does NOT mint a new version (mirrors <see cref="DeliveryStyle.Archive" />).
    /// In-use profiles cannot be deleted, so this is the retire-in-place path for them: existing
    /// schedules keep airing until reassigned.</summary>
    public void Archive(DateTime nowUtc)
    {
        Status = "archived";
        UpdatedAt = nowUtc;
    }

    /// <summary>Unarchives this profile — restores it to the active library. Does NOT mint a new
    /// version (mirrors <see cref="DeliveryStyle.Unarchive" />).</summary>
    public void Unarchive(DateTime nowUtc)
    {
        Status = "active";
        UpdatedAt = nowUtc;
    }

    /// <summary>Rehydrates a profile from persisted data without re-running creation invariants.</summary>
    public static VoiceProfile Reconstitute(
        string id,
        string name,
        string? description,
        string baseVoiceId,
        string baseVoiceName,
        string provider,
        string status,
        DateTime createdAt,
        DateTime updatedAt,
        string? createdBy,
        IReadOnlyList<string> assignedStations,
        List<VoiceProfileVersion> versions)
    {
        return new VoiceProfile(id, name, description, baseVoiceId, baseVoiceName, provider, status,
            createdAt, updatedAt, createdBy, assignedStations, versions);
    }

    private static VoiceProfileVersion NewVersion(
        int version,
        string voiceId,
        VoiceSettings settings,
        string model,
        string? changeNote,
        string? createdBy,
        DateTime nowUtc,
        double paceBoostWpm)
    {
        return new VoiceProfileVersion(version, settings, model, Normalize(changeNote), nowUtc, createdBy,
            null, settings.GetConfigurationKey(voiceId), paceBoostWpm);
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    ///     Azure Table's hard per-property ceiling (32,768 UTF-16 chars) — the wall every
    ///     versioned library's serialised history must fit. Single source of truth shared with
    ///     <c>VersionHistoryTrimmer.PropertyCeiling</c>; the service's pre-provider size check
    ///     serialises a candidate version with exactly this bound.
    /// </summary>
    public const int StoragePropertyCeiling = PronunciationDictionary.StoragePropertyCeiling;

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
    ///     is covered by the service's delete rollback).
    /// </summary>
    public static Result EnsureVersionFitsStorage(VoiceProfileVersion candidate)
    {
        string json = JsonSerializer.Serialize(new[] { candidate }, StorageMirrorOptions);
        if (json.Length <= StoragePropertyCeiling)
            return Result.Success();

        return Result.Failure(OperationalFailure.InvalidInput(
            "versionhistory.entry_too_large",
            $"This voice profile is too large to save — a single version is " +
            $"{json.Length:N0} characters against a {StoragePropertyCeiling:N0} limit. Remove or shorten " +
            "some entries and save again."));
    }
}

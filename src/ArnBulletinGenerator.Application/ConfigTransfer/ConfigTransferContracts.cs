using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.ConfigTransfer;

// =============================================================================
// Config bundle — the single JSON document ARN exports, edits (by hand or with
// an AI assistant), and imports back. PascalCase on the wire like every other
// endpoint; import parsing is case-insensitive. The bundle carries CURRENT
// state only — version history stays server-side (import mints new versions
// through the ordinary service paths, so every import is restorable).
//
// Import semantics are UPSERT-ONLY: entities absent from the bundle are never
// deleted. Deletion stays a deliberate UI action.
// =============================================================================

/// <summary>Section names, in dependency order (libraries before the schedules that reference them).</summary>
public static class ConfigSections
{
    /// <summary>The only <see cref="ConfigBundleDto.SchemaVersion" /> the import accepts.</summary>
    public const int CurrentSchemaVersion = 1;

    public const string VoiceProfiles = "VoiceProfiles";
    public const string DeliveryStyles = "DeliveryStyles";
    public const string PronunciationDictionaries = "PronunciationDictionaries";
    public const string SafetyTerms = "SafetyTerms";
    public const string Guidance = "Guidance";
    public const string PriorityRoads = "PriorityRoads";
    public const string VerifiedPronunciations = "VerifiedPronunciations";
    public const string OperatorAssignments = "OperatorAssignments";
    public const string VoiceCalibrations = "VoiceCalibrations";
    public const string FillerStatements = "FillerStatements";
    public const string Bookends = "Bookends";
    public const string Schedules = "Schedules";

    public static readonly IReadOnlyList<string> All =
    [
        VoiceProfiles, DeliveryStyles, PronunciationDictionaries,
        SafetyTerms, Guidance, PriorityRoads, VerifiedPronunciations,
        OperatorAssignments, VoiceCalibrations, FillerStatements, Bookends, Schedules
    ];

    /// <summary>Parses a comma-separated sections filter; null/empty means all. Unknown names fail.</summary>
    public static Result<IReadOnlySet<string>> ParseFilter(string? sections)
    {
        if (string.IsNullOrWhiteSpace(sections))
            return Result<IReadOnlySet<string>>.Success(new HashSet<string>(All, StringComparer.OrdinalIgnoreCase));

        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in sections.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = All.FirstOrDefault(s => s.Equals(raw, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                return Result<IReadOnlySet<string>>.Failure(
                    $"Unknown section '{raw}'. Valid sections: {string.Join(", ", All)}.");
            requested.Add(match);
        }

        return Result<IReadOnlySet<string>>.Success(requested);
    }
}

/// <summary>Current-version content of a voice profile (the shared voice library).</summary>
public sealed record ConfigVoiceProfileDto(
    string? Id = null,
    string? Name = null,
    string? Description = null,
    string? BaseVoiceId = null,
    string? BaseVoiceName = null,
    string? Model = null,
    VoiceSettingsDto? Settings = null,
    double PaceBoostWpm = 0,
    string? ChangeNote = null);

/// <summary>Current-version content of a delivery style.</summary>
public sealed record ConfigDeliveryStyleDto(
    string? Id = null,
    string? Name = null,
    string? Description = null,
    DeliveryStylePolicyDto? Policy = null,
    string? ChangeNote = null);

/// <summary>
///     Current-version content of a bookend pair: both authored lines in one unit. Either line
///     may be empty (an intro with no closer, or vice versa) but not both.
/// </summary>
public sealed record ConfigBookendDto(
    string? Id = null,
    string? Name = null,
    string? Description = null,
    string? IntroText = null,
    string? OutroText = null,
    string? ChangeNote = null);

/// <summary>Current-version content of a pronunciation dictionary.</summary>
public sealed record ConfigPronunciationDictionaryDto(
    string? Id = null,
    string? Name = null,
    string? Description = null,
    List<PronunciationRuleDto>? Rules = null,
    string? ChangeNote = null,
    // Station|Global. Absent means Station, so every bundle written before the platform-wide
    // layer existed imports unchanged.
    string? Scope = null);

/// <summary>A banned word or allowed exception. Kind: Banned|Allowed; Scope: Global|Market.</summary>
public sealed record ConfigSafetyTermDto(
    string? Id = null,
    string? Kind = null,
    string? Scope = null,
    string? ScopeId = null,
    string? Term = null,
    string? Note = null,
    bool Enabled = true);

/// <summary>A standing-guidance directive. Scope: Global|Market|Station.</summary>
public sealed record ConfigGuidanceDto(
    string? Id = null,
    string? Scope = null,
    string? ScopeId = null,
    string? Type = null,
    string? Text = null,
    bool Enabled = true,
    DateTime? ReviewAfterUtc = null);

/// <summary>One market's operator-verified pronunciation verdicts.</summary>
public sealed record ConfigVerifiedPronunciationsDto(
    string? Market = null,
    List<VerifiedPronunciationDto>? Words = null);

/// <summary>One operator's global market-access assignment.</summary>
public sealed record ConfigOperatorAssignmentDto(
    string? ObjectId = null,
    string? DisplayName = null,
    List<string>? Markets = null,
    DateTime? UpdatedAtUtc = null,
    string? UpdatedBy = null);

/// <summary>One persisted voice/settings speech-rate calibration.</summary>
public sealed record ConfigVoiceCalibrationDto(
    string? ConfigurationKey = null,
    string? VoiceId = null,
    VoiceSettingsDto? Settings = null,
    string? Provider = null,
    double SyllablesPerSecond = 0,
    double SyllablesPerWord = 0,
    double WordsPerMinute = 0,
    double SyllablesPerMinute = 0,
    int RunCount = 0,
    int ReferenceSyllableCount = 0,
    double ReferenceDurationSeconds = 0,
    DateTime? CalibratedAt = null,
    double ValidForDays = 30);

/// <summary>
///     A station schedule (bulletin rules) keyed by its market id — the natural upsert key.
///     Field shape mirrors <see cref="UpsertScheduleRequest" /> so an exported schedule is
///     exactly what the import (and the ordinary PUT endpoint) accepts back.
/// </summary>
public sealed record ConfigScheduleDto(
    string? MarketId = null,
    string? Name = null,
    string? Station = null,
    string? Timezone = null,
    string? CronExpression = null,
    bool? Enabled = null,
    double? TargetDurationSeconds = null,
    int? NotifyBeforePlayoutMinutes = null,
    int? EscalationBeforePlayoutMinutes = null,
    string? VoiceProfileId = null,
    string? OutputPath = null,
    CoverageDto? Coverage = null,
    ContentSelectionDto? Content = null,
    string? DeliveryStyleId = null,
    string? PlaybookId = null,
    string? PronunciationDictionaryId = null,
    VoiceRotationDto? VoiceRotation = null,
    IReadOnlyList<string>? FillOrder = null,
    string? BookendId = null,
    BookendRotationDto? BookendRotation = null,
    IReadOnlyDictionary<string, string>? BookendTokens = null,
    /// <summary>
    ///     The station's year calendar. Optional and additive, so a bundle exported before
    ///     calendars existed omits it and still imports: absent means "leave the calendar
    ///     alone", which is why the SchemaVersion is NOT bumped for this field. Bumping it
    ///     would reject every bundle already in circulation.
    /// </summary>
    ScheduleCalendarDto? Calendar = null,
    bool? CurationEnabled = null,
    /// <summary>
    ///     Whether the station is armed for playout in the importing environment. Optional and
    ///     additive like <see cref="Calendar" />: absent means "leave the arming alone", so a
    ///     bundle exported before this field existed cannot silently re-arm or disarm a station
    ///     when it is promoted between environments — which matters precisely because arming is
    ///     the one setting deliberately allowed to DIFFER between test and production.
    /// </summary>
    bool? PlayoutEnabled = null)
{
    public UpsertScheduleRequest ToUpsertRequest() => new(
        Name, Station, Timezone, CronExpression, Enabled, TargetDurationSeconds,
        NotifyBeforePlayoutMinutes, EscalationBeforePlayoutMinutes, VoiceProfileId, OutputPath,
        Coverage, Content, DeliveryStyleId, PlaybookId, PronunciationDictionaryId, VoiceRotation, FillOrder,
        BookendId, BookendRotation, BookendTokens, MarketId: null, Calendar: Calendar,
        CurationEnabled: CurationEnabled, PlayoutEnabled: PlayoutEnabled);
}

/// <summary>The full export/import document. Sections are optional; only present sections import.</summary>
/// <remarks>
///     <see cref="SchemaVersion" /> must equal <see cref="ConfigSections.CurrentSchemaVersion" />;
///     anything else is rejected at plan time with an error naming the supported version. The
///     default value 1 makes a bundle that omits the field (pre-versioning exports) import cleanly.
/// </remarks>
public sealed record ConfigBundleDto(
    string? Schema = null,
    int SchemaVersion = 1,
    string? ExportedAtUtc = null,
    string? Source = null,
    List<ConfigVoiceProfileDto>? VoiceProfiles = null,
    List<ConfigDeliveryStyleDto>? DeliveryStyles = null,
    List<ConfigPronunciationDictionaryDto>? PronunciationDictionaries = null,
    List<ConfigSafetyTermDto>? SafetyTerms = null,
    List<ConfigGuidanceDto>? Guidance = null,
    List<PriorityRoadsDto>? PriorityRoads = null,
    List<ConfigVerifiedPronunciationsDto>? VerifiedPronunciations = null,
    List<ConfigOperatorAssignmentDto>? OperatorAssignments = null,
    List<ConfigVoiceCalibrationDto>? VoiceCalibrations = null,
    List<FillerStatementsDto>? FillerStatements = null,
    List<ConfigBookendDto>? Bookends = null,
    List<ConfigScheduleDto>? Schedules = null);

// =============================================================================
// Import report — the dry-run diff an admin (or the second approver) reviews.
// =============================================================================

/// <summary>Planned or applied action for one entity in the bundle.</summary>
public static class ConfigImportActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Unchanged = "Unchanged";
    public const string Error = "Error";
}

/// <summary>Apply outcome for one item (populated only on a non-dry-run apply).</summary>
public static class ConfigImportOutcomes
{
    public const string Applied = "Applied";
    public const string Failed = "Failed";
}

public sealed record ConfigImportItemDto(
    string Section,
    string Key,
    string Name,
    string Action,
    string? Detail = null,
    string? Outcome = null);

public sealed record ConfigImportReportDto(
    bool DryRun,
    bool RequiresApproval,
    bool Applied,
    int Creates,
    int Updates,
    int Unchanged,
    int Errors,
    IReadOnlyList<ConfigImportItemDto> Items)
{
    /// <summary>
    ///     True when two reports describe the same plan: identical counters AND the same
    ///     per-item (section, key, action) multiset. The approve gate compares the freshly
    ///     re-planned report against the stored one an approver reviewed — a pending import
    ///     whose re-plan merely changes shape (Create → Update, a different diff) must be
    ///     refused, not applied with semantics nobody approved.
    /// </summary>
    public bool IsEquivalentTo(ConfigImportReportDto other)
    {
        return Creates == other.Creates
            && Updates == other.Updates
            && Unchanged == other.Unchanged
            && Errors == other.Errors
            && SameItemActions(other);
    }

    private bool SameItemActions(ConfigImportReportDto other)
    {
        static Dictionary<(string Section, string Key, string Action), int> Multiset(
            IReadOnlyList<ConfigImportItemDto> items)
        {
            var counts = new Dictionary<(string, string, string), int>();
            foreach (var item in items)
            {
                var key = (item.Section, item.Key, item.Action);
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            return counts;
        }

        var a = Multiset(Items);
        var b = Multiset(other.Items);
        return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var n) && n == kv.Value);
    }

    /// <summary>
    ///     Human-readable description of how <paramref name="other" /> drifted from this report,
    ///     for the approve gate's 409 refusal — e.g. <c>VoiceProfiles vp-1 was Create, now Update</c>.
    ///     Empty when the reports are equivalent.
    /// </summary>
    public string DescribeDrift(ConfigImportReportDto other)
    {
        if (IsEquivalentTo(other))
            return string.Empty;

        var freshByKey = other.Items
            .GroupBy(i => (i.Section, i.Key))
            .ToDictionary(g => g.Key, g => g.First());
        var storedByKey = Items
            .GroupBy(i => (i.Section, i.Key))
            .ToDictionary(g => g.Key, g => g.First());

        var diffs = new List<string>();
        foreach (var (key, stored) in storedByKey)
        {
            if (!freshByKey.TryGetValue(key, out var fresh))
            {
                diffs.Add($"{key.Section} {key.Key} was {stored.Action}, now absent");
            }
            else if (!string.Equals(stored.Action, fresh.Action, StringComparison.Ordinal))
            {
                diffs.Add($"{key.Section} {key.Key} was {stored.Action}, now {fresh.Action}");
            }
        }
        foreach (var (key, fresh) in freshByKey)
        {
            if (!storedByKey.ContainsKey(key))
                diffs.Add($"{key.Section} {key.Key} was absent, now {fresh.Action}");
        }

        // Counter drift is implied by the item multiset above (counts are just tallies of item
        // actions), but the task's equivalence contract is counts PLUS items, so report it too
        // for reports assembled from different sources.
        if (Creates != other.Creates) diffs.Add($"creates {Creates} → {other.Creates}");
        if (Updates != other.Updates) diffs.Add($"updates {Updates} → {other.Updates}");
        if (Unchanged != other.Unchanged) diffs.Add($"unchanged {Unchanged} → {other.Unchanged}");
        if (Errors != other.Errors) diffs.Add($"errors {Errors} → {other.Errors}");

        return string.Join("; ", diffs);
    }
}

// =============================================================================
// Wire DTOs shared between the config bundle and the ordinary per-library HTTP
// endpoints (arrive PascalCase — the SPA pascalizes bodies before sending).
// Pure data contracts, not HTTP-layer: kept alongside the bundle they compose.
// =============================================================================

public sealed record DeliveryStylePolicyDto(
    string? ToneDescription = null,
    List<string>? AllowedTags = null,
    int Intensity = 0)
{
    public DeliveryStylePolicy ToDomain() =>
        new(ToneDescription?.Trim() ?? string.Empty, AllowedTags ?? [],
            Math.Clamp(Intensity, DeliveryStylePolicy.MinIntensity, DeliveryStylePolicy.MaxIntensity));
}

public sealed record PronunciationRuleDto(
    string? Type = null,
    string? StringToReplace = null,
    string? Replacement = null,
    string? Alphabet = null,
    bool CaseSensitive = false,
    bool WordBoundaries = true,
    bool AppliesOnAllModels = false);

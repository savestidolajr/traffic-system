using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Domain.Contracts;

/// <summary>Wire DTO for storing the numeric editorial settings at one scope.</summary>
public sealed record UpsertEditorialSettingsRequest(
    EditorialScope Scope,
    string? ScopeId,
    int? DriveTimeHeadlineThresholdMinutes = null,
    int? MaxCorridors = null,
    int? MajorTierCap = null,
    int? ModerateTierCap = null,
    int? MinorTierCap = null,
    int? ContinuityNewCap = null,
    int? ContinuityOngoingCap = null,
    int? ContinuityClearedCap = null,
    int? ContinuityUpdatedCap = null,
    int? GuidanceMaxDirectives = null,
    // Slot-fit envelope; trailing so existing positional callers are unaffected.
    double? MaxSlowDownFactor = null,
    double? MaxSpeedUpFactor = null,
    double? MaxSilencePadSeconds = null,
    double? MaxSafeOverrunTrimSeconds = null,
    // Denylist of road-authority feeds this scope must not consult; null = inherit the wider scope.
    IReadOnlyList<string>? DisabledTrafficSources = null)
{
    public EditorialSettings ToSettings(string scopeId, DateTime nowUtc, string updatedBy) => new(
        Scope, scopeId, DriveTimeHeadlineThresholdMinutes, MaxCorridors, MajorTierCap, ModerateTierCap,
        MinorTierCap, ContinuityNewCap, ContinuityOngoingCap, ContinuityClearedCap, ContinuityUpdatedCap,
        GuidanceMaxDirectives, nowUtc, nowUtc, updatedBy,
        MaxSlowDownFactor, MaxSpeedUpFactor, MaxSilencePadSeconds, MaxSafeOverrunTrimSeconds,
        DisabledTrafficSources);
}

/// <summary>The settings row stored at one scope; null fields mean "inherit".</summary>
public sealed record EditorialSettingsDto(
    string Scope,
    string ScopeId,
    int? DriveTimeHeadlineThresholdMinutes,
    int? MaxCorridors,
    int? MajorTierCap,
    int? ModerateTierCap,
    int? MinorTierCap,
    int? ContinuityNewCap,
    int? ContinuityOngoingCap,
    int? ContinuityClearedCap,
    int? ContinuityUpdatedCap,
    int? GuidanceMaxDirectives,
    DateTime UpdatedAtUtc,
    string? UpdatedBy,
    // Slot-fit envelope; see EditorialSettings for why these are ARN-tunable rather than compiled in.
    double? MaxSlowDownFactor = null,
    double? MaxSpeedUpFactor = null,
    double? MaxSilencePadSeconds = null,
    double? MaxSafeOverrunTrimSeconds = null,
    IReadOnlyList<string>? DisabledTrafficSources = null)
{
    public static EditorialSettingsDto From(EditorialSettings s) => new(
        s.Scope.ToString(), s.ScopeId, s.DriveTimeHeadlineThresholdMinutes, s.MaxCorridors, s.MajorTierCap,
        s.ModerateTierCap, s.MinorTierCap, s.ContinuityNewCap, s.ContinuityOngoingCap, s.ContinuityClearedCap,
        s.ContinuityUpdatedCap, s.GuidanceMaxDirectives, s.UpdatedAtUtc, s.UpdatedBy,
        s.MaxSlowDownFactor, s.MaxSpeedUpFactor, s.MaxSilencePadSeconds, s.MaxSafeOverrunTrimSeconds,
        s.DisabledTrafficSources);
}

/// <summary>
///     What a bulletin for this market/station would actually use: the overlay result, with the shipped
///     default substituted wherever ARN has set nothing, so the console never has to show a bare null.
/// </summary>
public sealed record EffectiveEditorialSettingsDto(
    string Market,
    string? Station,
    int DriveTimeHeadlineThresholdMinutes,
    int MaxCorridors,
    int MajorTierCap,
    int ModerateTierCap,
    int MinorTierCap,
    int ContinuityNewCap,
    int ContinuityOngoingCap,
    int ContinuityClearedCap,
    int ContinuityUpdatedCap,
    int GuidanceMaxDirectives,
    double MaxSlowDownFactor,
    double MaxSpeedUpFactor,
    double MaxSilencePadSeconds,
    double MaxSafeOverrunTrimSeconds,
    IReadOnlyList<string> ConfiguredByArn,
    // The POSITIVE resolved set: which authority feeds a bulletin here would actually consult.
    // Stored config is a denylist, but "effective" must answer the operator's real question —
    // what will this station use? — so the console never has to invert a denylist itself.
    IReadOnlyList<string> EnabledTrafficSources)
{
    /// <summary>Shipped defaults, mirroring the constants each consumer falls back to.</summary>
    private const int DefaultDriveTimeThreshold = 5;
    private const int DefaultMaxCorridors = 5;

    /// <summary>
    ///     <paramref name="durationDefaults" /> is the environment's DEPLOYED
    ///     <see cref="Configuration.SpeechDurationOptions" />, not the code defaults, because the two
    ///     genuinely differ: `appsettings.json` pins MaxSlowDownFactor to 1.0 while the code default is
    ///     1.10. "Effective" has to mean what a bulletin would really use, so an unwired caller falling
    ///     back to code defaults would show ARN a slow-fill allowance the environment does not have.
    /// </summary>
    public static EffectiveEditorialSettingsDto From(
        string market, string? station, EditorialSettings s,
        Configuration.SpeechDurationOptions? durationDefaults = null)
    {
        var defaults = durationDefaults ?? new Configuration.SpeechDurationOptions();
        var configured = new List<string>();
        void Note(string name, object? value)
        {
            if (value is not null)
                configured.Add(name);
        }

        Note(nameof(s.DriveTimeHeadlineThresholdMinutes), s.DriveTimeHeadlineThresholdMinutes);
        Note(nameof(s.MaxCorridors), s.MaxCorridors);
        Note(nameof(s.MajorTierCap), s.MajorTierCap);
        Note(nameof(s.ModerateTierCap), s.ModerateTierCap);
        Note(nameof(s.MinorTierCap), s.MinorTierCap);
        Note(nameof(s.ContinuityNewCap), s.ContinuityNewCap);
        Note(nameof(s.ContinuityOngoingCap), s.ContinuityOngoingCap);
        Note(nameof(s.ContinuityClearedCap), s.ContinuityClearedCap);
        Note(nameof(s.ContinuityUpdatedCap), s.ContinuityUpdatedCap);
        Note(nameof(s.GuidanceMaxDirectives), s.GuidanceMaxDirectives);
        Note(nameof(s.MaxSlowDownFactor), s.MaxSlowDownFactor);
        Note(nameof(s.MaxSpeedUpFactor), s.MaxSpeedUpFactor);
        Note(nameof(s.MaxSilencePadSeconds), s.MaxSilencePadSeconds);
        Note(nameof(s.MaxSafeOverrunTrimSeconds), s.MaxSafeOverrunTrimSeconds);
        Note(nameof(s.DisabledTrafficSources), s.DisabledTrafficSources);

        return new EffectiveEditorialSettingsDto(
            market,
            station,
            s.DriveTimeHeadlineThresholdMinutes ?? DefaultDriveTimeThreshold,
            s.MaxCorridors ?? DefaultMaxCorridors,
            s.MajorTierCap ?? CorridorTiering.MajorTierCap,
            s.ModerateTierCap ?? CorridorTiering.ModerateTierCap,
            s.MinorTierCap ?? CorridorTiering.MinorTierCap,
            s.ContinuityNewCap ?? 3,
            s.ContinuityOngoingCap ?? 2,
            s.ContinuityClearedCap ?? 2,
            s.ContinuityUpdatedCap ?? 1,
            s.GuidanceMaxDirectives ?? GuidanceComposer.DefaultMaxDirectives,
            s.MaxSlowDownFactor ?? defaults.MaxSlowDownFactor,
            s.MaxSpeedUpFactor ?? defaults.MaxSpeedUpFactor,
            s.MaxSilencePadSeconds ?? defaults.MaxSilencePadSeconds,
            s.MaxSafeOverrunTrimSeconds ?? defaults.MaxSafeOverrunTrimSeconds,
            configured,
            Services.OfficialTrafficSources.All.Where(s.IsTrafficSourceEnabled).ToArray());
    }
}

using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.ValueObjects;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Where a settings overlay applies. Same ladder as <see cref="GuidanceScope" />
///     (Station &gt; Market &gt; Global); resolution is most-specific-wins per field.
/// </summary>
public enum EditorialScope
{
    Global = 0,
    Market = 1,
    Station = 2
}

/// <summary>
///     The numeric half of ARN's editorial control: the caps and thresholds that decide how much of
///     the board reaches the writer and when a drive-time figure is worth airing. These were
///     compile-time constants, which made questions like "should a busy market be allowed more than
///     five corridors?" (RISK-33) or "is five minutes the right bar for a drive-time headline?"
///     product decisions blocked on a deploy.
///     <para>
///         Every value is NULLABLE and falls back to the shipped default, so an unset scope behaves
///         exactly as the constants did. Resolution layers Station &gt; Market &gt; Global,
///         most-specific-wins PER FIELD — a market that only wants a different corridor cap inherits
///         every other value rather than having to restate them.
///     </para>
/// </summary>
public sealed record EditorialSettings(
    EditorialScope Scope,
    string ScopeId,
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
    DateTime CreatedAtUtc = default,
    DateTime UpdatedAtUtc = default,
    string? UpdatedBy = null,
    // The slot-fit envelope. Appended LAST so every existing positional caller is unaffected.
    // These four decide whether a read that misses its slot is stretched, padded, compressed or
    // failed to the filler cart, which makes them the most consequential numbers ARN cannot
    // currently change without a deploy. MaxSlowDownFactor especially: `appsettings.json` pins it
    // to 1.0 to honour the 2026-06-25 "never slow the read" decision, and the measured cost of
    // that policy is that the 05:55 Sydney slot fell to the standby cart on 2 of 6 mornings
    // (2026-08-24 analysis). Whether an audible slow-fill beats sponsor credits is an editorial
    // judgement, not an engineering one, so it belongs here beside the caps.
    double? MaxSlowDownFactor = null,
    double? MaxSpeedUpFactor = null,
    double? MaxSilencePadSeconds = null,
    double? MaxSafeOverrunTrimSeconds = null,
    // Which road-authority feeds this scope does NOT consult. A DENYLIST, not an allowlist, for two
    // reasons: unset (null) must mean "every feed on", which is the behaviour before this field
    // existed; and a NEW feed added later must light up everywhere rather than staying silently off
    // until someone edits all fifteen stations. Null = inherit the wider scope; an EMPTY list is a
    // deliberate "disable nothing" that stops a market's denylist reaching one station.
    IReadOnlyList<string>? DisabledTrafficSources = null,
    // Optional ranking weights. Appended after the established persistence fields so every existing
    // positional constructor remains source-compatible; null keeps the shipped 50/30 defaults.
    int? FlowPriorityBand = null,
    int? PriorityRoadBandBoost = null)
{
    /// <summary>Nothing configured — every consumer sees its own shipped default.</summary>
    public static EditorialSettings Empty { get; } = new(EditorialScope.Global, string.Empty);

    /// <summary>Global settings carry no scope id; market/station keep theirs, normalised.</summary>
    public static string NormalizeScopeId(EditorialScope scope, string? scopeId)
        => scope == EditorialScope.Global ? string.Empty : MarketKey.Normalize(scopeId ?? string.Empty);

    /// <summary>
    ///     Bounds every supplied value. Deliberately strict: these caps shape what the writer is even
    ///     shown, so a zero or negative would silently produce an empty board rather than an error, and
    ///     an absurd upper value would hand the model a roll-call it cannot voice in a 30-second slot.
    /// </summary>
    public Result Validate()
    {
        foreach ((string name, int? value, int min, int max) in Bounds())
        {
            if (value is null)
                continue;
            if (value < min || value > max)
                return Result.Failure($"{name} must be between {min} and {max}.");
        }

        foreach ((string name, double? value, double min, double max) in FactorBounds())
        {
            if (value is null)
                continue;
            if (value < min || value > max || double.IsNaN(value.Value))
                return Result.Failure($"{name} must be between {min} and {max}.");
        }

        // Referential integrity at SAVE time, not at broadcast time. A mistyped id ("traffic-sa")
        // would otherwise persist cleanly, read back as configured in the console, and silently
        // disable nothing — the same trap OperatorsHttpTrigger documents for market ids, where an
        // unknown value "saves cleanly but then 403s the operator on every route with no signal at
        // save time".
        foreach (string source in DisabledTrafficSources ?? [])
        {
            if (!OfficialTrafficSources.IsKnown(source))
                return Result.Failure(
                    $"'{source}' is not a known traffic source. Valid sources: " +
                    $"{string.Join(", ", OfficialTrafficSources.All)}.");
        }

        return Result.Success();
    }

    /// <summary>
    ///     True when a bulletin at this scope should consult <paramref name="sourceId" />. Lives on
    ///     the settings record so the fan-out gate stays a single readable predicate and the rule is
    ///     unit-testable without a broadcast. Comparison is ordinal because
    ///     <see cref="OfficialTrafficSources.Normalize" /> has already canonicalised the stored form.
    /// </summary>
    public bool IsTrafficSourceEnabled(string sourceId)
    {
        if (DisabledTrafficSources is null || DisabledTrafficSources.Count == 0)
            return true;

        for (int i = 0; i < DisabledTrafficSources.Count; i++)
        {
            if (string.Equals(DisabledTrafficSources[i], sourceId, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     The denylist in canonical storage form, deduplicated and ordered by
    ///     <see cref="OfficialTrafficSources.All" /> so a stored row is stable regardless of the
    ///     order the console sent its checkboxes in. Null stays null: "inherit" is not "empty".
    /// </summary>
    public IReadOnlyList<string>? NormalizeDisabledTrafficSources()
    {
        if (DisabledTrafficSources is null)
            return null;

        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (string source in DisabledTrafficSources)
            normalized.Add(OfficialTrafficSources.Normalize(source));

        return OfficialTrafficSources.All.Where(normalized.Contains).ToArray();
    }

    private IEnumerable<(string Name, int? Value, int Min, int Max)> Bounds()
    {
        yield return (nameof(DriveTimeHeadlineThresholdMinutes), DriveTimeHeadlineThresholdMinutes, 1, 60);
        yield return (nameof(MaxCorridors), MaxCorridors, 1, 25);
        yield return (nameof(MajorTierCap), MajorTierCap, 1, 12);
        yield return (nameof(ModerateTierCap), ModerateTierCap, 1, 12);
        yield return (nameof(MinorTierCap), MinorTierCap, 1, 12);
        yield return (nameof(ContinuityNewCap), ContinuityNewCap, 0, 10);
        yield return (nameof(ContinuityOngoingCap), ContinuityOngoingCap, 0, 10);
        yield return (nameof(ContinuityClearedCap), ContinuityClearedCap, 0, 10);
        yield return (nameof(ContinuityUpdatedCap), ContinuityUpdatedCap, 0, 10);
        yield return (nameof(GuidanceMaxDirectives), GuidanceMaxDirectives, 1, 20);
        yield return (nameof(FlowPriorityBand), FlowPriorityBand, 0, 500);
        yield return (nameof(PriorityRoadBandBoost), PriorityRoadBandBoost, 0, 500);
    }

    /// <summary>
    ///     The slot-fit envelope, bounded tighter than the caps because every value here is audible.
    ///     A stretch factor below 1.0 would SPEED the read while claiming to slow it, so 1.0 (the
    ///     mechanism off) is the floor rather than zero. The upper bounds are the points past which
    ///     the effect stops being transparent on a punchy traffic read - the shipped code comments
    ///     put a ~10% slow and ~15% speed-up at the edge of acceptable, so these allow a little
    ///     headroom for ARN to trial and no more.
    /// </summary>
    private IEnumerable<(string Name, double? Value, double Min, double Max)> FactorBounds()
    {
        yield return (nameof(MaxSlowDownFactor), MaxSlowDownFactor, 1.0, 1.25);
        yield return (nameof(MaxSpeedUpFactor), MaxSpeedUpFactor, 1.0, 1.30);
        yield return (nameof(MaxSilencePadSeconds), MaxSilencePadSeconds, 0.0, 2.0);
        yield return (nameof(MaxSafeOverrunTrimSeconds), MaxSafeOverrunTrimSeconds, 0.0, 10.0);
    }

    /// <summary>
    ///     Layers <paramref name="narrower" /> over this instance field by field: a value the narrower
    ///     scope leaves unset inherits from the wider one. Per-field rather than whole-record so a
    ///     station tuning one cap does not silently discard its market's other choices.
    /// </summary>
    public EditorialSettings OverlayWith(EditorialSettings narrower) => new(
        narrower.Scope,
        narrower.ScopeId,
        narrower.DriveTimeHeadlineThresholdMinutes ?? DriveTimeHeadlineThresholdMinutes,
        narrower.MaxCorridors ?? MaxCorridors,
        narrower.MajorTierCap ?? MajorTierCap,
        narrower.ModerateTierCap ?? ModerateTierCap,
        narrower.MinorTierCap ?? MinorTierCap,
        narrower.ContinuityNewCap ?? ContinuityNewCap,
        narrower.ContinuityOngoingCap ?? ContinuityOngoingCap,
        narrower.ContinuityClearedCap ?? ContinuityClearedCap,
        narrower.ContinuityUpdatedCap ?? ContinuityUpdatedCap,
        narrower.GuidanceMaxDirectives ?? GuidanceMaxDirectives,
        narrower.CreatedAtUtc,
        narrower.UpdatedAtUtc,
        narrower.UpdatedBy,
        narrower.MaxSlowDownFactor ?? MaxSlowDownFactor,
        narrower.MaxSpeedUpFactor ?? MaxSpeedUpFactor,
        narrower.MaxSilencePadSeconds ?? MaxSilencePadSeconds,
        narrower.MaxSafeOverrunTrimSeconds ?? MaxSafeOverrunTrimSeconds,
        narrower.DisabledTrafficSources ?? DisabledTrafficSources);

    /// <summary>
    ///     Resolves the effective settings for a bulletin from every applicable scope. Widest first so
    ///     each narrower scope overlays it; a scope with no stored row contributes nothing.
    /// </summary>
    public static EditorialSettings Resolve(
        IReadOnlyList<EditorialSettings> stored, string market, Option<string> station)
    {
        var effective = Empty;
        foreach (var scope in new[] { EditorialScope.Global, EditorialScope.Market, EditorialScope.Station })
        {
            var match = stored.FirstOrDefault(s => s.Scope == scope && Matches(s, market, station));
            if (match is not null)
                effective = effective.OverlayWith(match);
        }

        return effective;
    }

    private static bool Matches(EditorialSettings settings, string market, Option<string> station)
    {
        return settings.Scope switch
        {
            EditorialScope.Global => true,
            EditorialScope.Market => string.Equals(settings.ScopeId, market, StringComparison.OrdinalIgnoreCase),
            EditorialScope.Station => station.HasValue &&
                                      string.Equals(settings.ScopeId, station.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}

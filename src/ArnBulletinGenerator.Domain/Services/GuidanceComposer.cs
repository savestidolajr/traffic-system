using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The result of composing standing guidance for one bulletin: the ordered, sanitised,
///     capped directives plus how many were dropped by the cap (surfaced to the operator UI
///     as "N rules in effect, M not shown").
/// </summary>
public sealed record ComposedGuidance(IReadOnlyList<GuidanceEntry> Entries, int DroppedCount)
{
    public static readonly ComposedGuidance Empty = new([], 0);
    public int Count => Entries.Count;
    public bool HasEntries => Entries.Count > 0;
}

/// <summary>
///     Pure composition of standing guidance for a bulletin. The heart of the self-service
///     guidance safety model — it filters to the applicable scopes, ranks by precedence
///     (Station &gt; Market &gt; Global), sanitises every directive against delimiter
///     injection, caps the count, and reports the dropped tail. No I/O, so it is fully
///     unit-testable against the adversarial scenarios.
/// </summary>
public static partial class GuidanceComposer
{
    public const int DefaultMaxDirectives = 8;

    /// <summary>Per-directive character cap — keeps any single entry from dominating the prompt.</summary>
    public const int MaxEntryLength = 400;

    /// <summary>
    ///     Composes the directives applicable to <paramref name="market" /> (+ optional
    ///     <paramref name="station" />). Disabled, non-matching, and (after sanitisation)
    ///     empty entries are excluded. Ordered Station→Market→Global, newest-first within a
    ///     scope; the lowest-precedence tail beyond <paramref name="maxDirectives" /> is dropped.
    /// </summary>
    public static ComposedGuidance Compose(
        IReadOnlyList<GuidanceEntry> entries,
        string market,
        Option<string> station,
        int maxDirectives = DefaultMaxDirectives)
    {
        if (entries.Count == 0 || maxDirectives <= 0)
            return ComposedGuidance.Empty;

        var matched = new List<GuidanceEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (!entry.Enabled)
                continue;
            if (!Matches(entry, market, station))
                continue;

            string clean = PromptSafe.Sanitize(entry.Text);
            if (clean.Length == 0)
                continue;
            if (clean.Length > MaxEntryLength)
                clean = clean[..MaxEntryLength];

            matched.Add(entry with { Text = clean });
        }

        matched.Sort(static (a, b) =>
        {
            int byScope = ((int)b.Scope).CompareTo((int)a.Scope); // Station(2) first
            return byScope != 0 ? byScope : b.UpdatedAtUtc.CompareTo(a.UpdatedAtUtc); // newest first
        });

        if (matched.Count <= maxDirectives)
            return new ComposedGuidance(matched, 0);

        var kept = matched.GetRange(0, maxDirectives);
        return new ComposedGuidance(kept, matched.Count - maxDirectives);
    }

    /// <summary>The single broadest scope present, for the <c>&lt;guidance scope=""&gt;</c> attribute.</summary>
    public static string BroadestScopeLabel(ComposedGuidance guidance)
    {
        var widest = GuidanceScope.Global;
        foreach (var e in guidance.Entries)
            if ((int)e.Scope > (int)widest)
                widest = e.Scope;
        return widest.ToString().ToLowerInvariant();
    }

    private static bool Matches(GuidanceEntry entry, string market, Option<string> station)
    {
        return entry.Scope switch
        {
            GuidanceScope.Global => true,
            GuidanceScope.Market => string.Equals(entry.ScopeId, market, StringComparison.OrdinalIgnoreCase),
            GuidanceScope.Station => station.HasValue &&
                                     string.Equals(entry.ScopeId, station.Value, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }
}

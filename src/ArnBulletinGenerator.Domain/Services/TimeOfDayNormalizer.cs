using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministically grounds time-of-day wording in a script. The draft model habitually opens
///     all-clear reads with "No major delays across X this morning" regardless of the market's
///     clock — observed live (Darwin/Port Lincoln/Queenstown, 2026-07-06: "this morning" aired at
///     15:30 local), and prompt bans alone don't break the habit. Flagging it through the LLM
///     reviewer instead caused a refine death-spiral (Port Lincoln replay: every review cycle spent
///     on the same phrase, script gutted from ~80 to 21 words). A mechanical rewrite fixes the
///     phrase without costing a single other word:
///     <list type="bullet">
///         <item>Known daypart → mismatched phrases are rewritten to it ("this morning" → "this afternoon").</item>
///         <item>Unknown daypart (ad-hoc jobs without a timezone) → phrases become the always-true "right now".</item>
///     </list>
/// </summary>
public static partial class TimeOfDayNormalizer
{
    [GeneratedRegex(@"\bthis\s+(morning|midday|afternoon|evening|arvo|overnight)\b|\btonight\b|\bovernight\s+tonight\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DaypartPhrase();

    /// <summary>
    ///     Rewrites daypart wording to match <paramref name="actualDaypart" /> ("morning"/"midday"/
    ///     "afternoon"/"evening"/"overnight", as resolved from the schedule timezone), or to a
    ///     neutral "right now" when null. Text without daypart wording is returned unchanged.
    /// </summary>
    public static string Normalize(string script, string? actualDaypart)
    {
        if (string.IsNullOrEmpty(script))
            return script;

        return DaypartPhrase().Replace(script, match =>
        {
            if (actualDaypart is null)
                return "right now";

            string spoken = actualDaypart switch
            {
                "overnight" => "overnight",
                "evening" => "this evening",
                "midday" => "at midday",
                _ => $"this {actualDaypart}"
            };

            // Already the right daypart? Keep the writer's phrasing ONLY when the natural spoken form
            // still uses the "this …" frame (morning/afternoon/evening). "overnight"/"midday" drop
            // that frame, so "this overnight"/"this midday" must be rewritten to the spoken form.
            return match.Value.Contains(actualDaypart, StringComparison.OrdinalIgnoreCase)
                   && spoken.StartsWith("this ", StringComparison.OrdinalIgnoreCase)
                ? match.Value
                : spoken;
        });
    }
}

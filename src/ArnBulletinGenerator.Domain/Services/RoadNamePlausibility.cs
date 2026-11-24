using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Performs a conservative editorial plausibility check on road names before they enter a
///     discretionary clear-road list. This deliberately does not replace <see cref="RoadNameFormatter" />:
///     the formatter makes source names speakable, while this gate decides whether a discretionary
///     source name looks mangled enough to omit (2026-08-23, Adjungbillt-Threeway Road replay).
///     Incident roads are never passed through this gate for removal; callers may use
///     <see cref="SuspiciousIncidentRoads" /> to surface a name for telemetry while retaining its story.
/// </summary>
public static partial class RoadNamePlausibility
{

    /// <summary>Returns the reasons a name is suspicious, or an empty result when it is plausible.</summary>
    public sealed record Assessment(bool IsPlausible, IReadOnlyList<string> Reasons)
    {
        public static Assessment Plausible { get; } = new(true, []);
    }

    /// <summary>
    ///     Checks a source road name without attempting to correct it. Empty names and names with
    ///     non-name punctuation are suspicious; route numbers, apostrophes, and tight hyphens are
    ///     valid Australian naming forms.
    /// </summary>
    public static Assessment Check(string? roadName)
    {
        if (string.IsNullOrWhiteSpace(roadName))
            return Suspicious("empty road name");

        string name = roadName.Trim();
        var reasons = new List<string>();

        if (name.Length > 120)
            reasons.Add("road name is implausibly long");

        if (name.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c is not ('\'' or '’' or '-' or '.' or '/' or '(' or ')')))
            reasons.Add("contains non-name characters");

        if (!name.Any(char.IsLetter))
            reasons.Add("contains no alphabetic road name");

        if (name.Contains("--", StringComparison.Ordinal) || name.Contains("''", StringComparison.Ordinal) ||
            name.Contains("’’", StringComparison.Ordinal) || name.Contains("  ", StringComparison.Ordinal))
            reasons.Add("contains malformed separators");

        if (name.Count(c => c == '(') != name.Count(c => c == ')'))
            reasons.Add("contains unbalanced parentheses");

        string[] tokens = TokenRegex().Matches(name)
            .Select(m => m.Value)
            .Where(token => token.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
            reasons.Add("contains no name words");

        if (tokens.Length == 1
            && tokens[0].Any(char.IsDigit)
            && char.IsDigit(tokens[0][0]))
            reasons.Add("contains a malformed route code");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string token in tokens)
        {
            if (token.Length > 1 && char.IsLower(token[0]) && token.Skip(1).Any(char.IsUpper))
                reasons.Add("contains a missing-apostrophe word boundary");

            if (!seen.Add(token))
                reasons.Add("repeats a name token");

            if (DoubledConsonantSuffixRegex().IsMatch(token))
                reasons.Add("contains a doubled-consonant suffix artefact");

            if (GluedFragmentRegex().IsMatch(token))
                reasons.Add("contains a glued name fragment");
        }

        // A tight hyphen is valid (for example Nerang-Broadbeach Road). Only reject a hyphenated
        // name when one side independently has a mangling signal above; ordinary compound names pass.
        if (name.Contains('-', StringComparison.Ordinal) && tokens.Length < 2)
            reasons.Add("contains an empty hyphenated fragment");

        return reasons.Count == 0 ? Assessment.Plausible : new Assessment(false, reasons.Distinct().ToArray());
    }

    /// <summary>Convenience predicate for callers that only need the editorial decision.</summary>
    public static bool IsPlausible(string? roadName) => Check(roadName).IsPlausible;

    /// <summary>
    ///     Removes suspicious names from the discretionary flow pool. The pool is the single common
    ///     finalisation point used by both first-run and refreshed/curated generation, so its selected
    ///     good-run subset and persisted Flow tab inherit the same decision without a second filter.
    /// </summary>
    public static IReadOnlyList<FlowRoad> FilterDiscretionaryFlowPool(IReadOnlyList<FlowRoad> flowPool) =>
        flowPool.Where(flow => IsPlausible(flow.RoadName)).ToList();

    /// <summary>
    ///     Returns suspicious incident corridor names for warning/telemetry. It intentionally returns
    ///     names only; the caller retains every corridor and incident so a bad source label cannot
    ///     erase a real closure or delay.
    /// </summary>
    public static IReadOnlyList<string> SuspiciousIncidentRoads(
        IEnumerable<TrafficCorridor> corridors) =>
        corridors
            .Select(c => c.RoadName)
            .Where(name => !IsPlausible(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static Assessment Suspicious(string reason) => new(false, [reason]);

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    // A terminal doubled consonant plus t/d/s/n is not a normal English/Australian road suffix and
    // catches the observed "Adjungbillt" shape without rejecting ordinary names such as Bennett.
    [GeneratedRegex(@"(?i)(?<consonant>[b-df-hj-np-tv-z])\k<consonant>(?:t|d|s|n)$", RegexOptions.CultureInvariant)]
    private static partial Regex DoubledConsonantSuffixRegex();

    // Number-word + way(s) is a common upstream word-gluing failure ("Three Ways" → "Threeway").
    // Keep this intentionally tiny: legitimate compound names, including Nerang-Broadbeach, pass.
    [GeneratedRegex(@"(?i)^(?:two|three|four|five|six|seven|eight|nine)(?:way|ways)$", RegexOptions.CultureInvariant)]
    private static partial Regex GluedFragmentRegex();
}

using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Flags unspeakable junk the model should never voice on air: placeholder/ID road names that leak
///     from unresolved traffic data (e.g. "Unknown-4386185267451476265") and bare long digit runs. This
///     is a hard gate — a bulletin that reads an ID aloud is broken regardless of its other scores.
/// </summary>
internal static partial class ScriptSanityScorer
{
    // "Unknown-<id>" placeholders, or any run of 5+ digits (IDs; real bulletins say "700 metres", not 5-digit numbers).
    [GeneratedRegex(@"(?i:unknown-\d+)|\b\d{5,}\b")]
    private static partial Regex JunkTokenRegex();

    public static SanityResult Evaluate(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new SanityResult([]);

        var tokens = JunkTokenRegex().Matches(script)
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SanityResult(tokens);
    }
}

internal sealed record SanityResult(IReadOnlyList<string> JunkTokens)
{
    public bool Passed => JunkTokens.Count == 0;
}

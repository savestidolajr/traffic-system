using System.Globalization;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>Maps each authority's source-specific priority vocabulary to a common severity rank.</summary>
public static partial class AuthorityPriorityNormalizer
{
    /// <summary>
    ///     Returns 1 for the most severe rank. Unknown sources and values intentionally return None:
    ///     a new authority vocabulary must not become a silently invented comparison.
    /// </summary>
    public static Option<int> Normalize(string? sourceId, string? rawPriority)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(rawPriority))
            return default;

        string source = sourceId.Trim().ToLowerInvariant();
        string value = rawPriority.Trim();

        return source switch
        {
            "here" => value.ToLowerInvariant() switch
            {
                "critical" => Option<int>.Some(1),
                "major" => Option<int>.Some(2),
                "minor" => Option<int>.Some(3),
                "lowimpact" => Option<int>.Some(4),
                _ => default
            },
            "qldtraffic" => value.ToLowerInvariant() switch
            {
                "high" => Option<int>.Some(1),
                "medium" => Option<int>.Some(2),
                "low" => Option<int>.Some(3),
                _ => default
            },
            "mainroads-wa" => ParseWaLevel(value),
            // NSW's isMajor flag fired on only 1 of 414 live records. Treat its deliberate Major
            // marker as a rare high-precision rank 1, not as HERE's routine four-level band.
            "livetraffic-nsw" => value.Equals("major", StringComparison.OrdinalIgnoreCase)
                ? Option<int>.Some(1)
                : default,
            "trafficsa" => default,
            _ => default
        };
    }

    private static Option<int> ParseWaLevel(string value)
    {
        var match = WaLevelPattern().Match(value);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out int rank) && rank is >= 1 and <= 3
            ? Option<int>.Some(rank)
            : default;
    }

    [GeneratedRegex(@"^level\s+([0-9]+)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WaLevelPattern();
}

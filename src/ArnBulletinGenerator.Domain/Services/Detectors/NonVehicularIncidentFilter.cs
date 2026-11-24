using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic filter that removes non-vehicular infrastructure incidents
///     (pedestrian shared paths, cycleways, footpaths) from the traffic set.
///
///     HERE's incident feed classifies PSP/cycleway works as "road incidents"
///     even though they are irrelevant to drivers — they consume bulletin airtime
///     and dilute genuine traffic. This filter catches them by text-matching
///     against Description, Summary, and Comment fields.
///
///     Token list is a static domain constant: these are Australian road-authority
///     terms for non-vehicular infrastructure that won't change per-deployment.
///     "PSP" uses a word-boundary regex to avoid false positives inside words
///     (e.g. "suspspension" or road names containing the substring).
/// </summary>
public static partial class NonVehicularIncidentFilter
{
    /// <summary>
    ///     Multi-word tokens matched case-insensitively via simple string contains.
    ///     These are long enough that substring false positives are negligible.
    /// </summary>
    private static readonly string[] ContainsTokens =
    [
        "principal shared path",
        "shared path",
        "shared-path",
        "footpath",
        "foot path",
        "cycle path",
        "cycleway",
        "bike path",
        "pedestrian path",
        "footbridge closed"
    ];

    /// <summary>
    ///     Short token matched with word-boundary regex to avoid false positives
    ///     inside unrelated words (e.g. "suspspension", "Gypsum Rd").
    /// </summary>
    [GeneratedRegex(@"\bPSP\b", RegexOptions.IgnoreCase)]
    private static partial Regex PspWordBoundary();

    /// <summary>Result of filtering, with the kept incidents and any removed ones.</summary>
    public readonly record struct FilterResult(
        IReadOnlyList<TrafficIncident> Kept,
        IReadOnlyList<(TrafficIncident Incident, string Token)> Removed);

    /// <summary>
    ///     Filters out non-vehicular incidents from the given set.
    ///     Returns both the kept incidents and the removed ones (with their matched token).
    /// </summary>
    public static FilterResult Filter(IReadOnlyList<TrafficIncident> incidents)
    {
        var kept = new List<TrafficIncident>(incidents.Count);
        var removed = new List<(TrafficIncident, string)>();

        foreach (var incident in incidents)
        {
            string? matchToken = FindNonVehicularToken(incident);
            if (matchToken is not null)
            {
                removed.Add((incident, matchToken));
                continue;
            }

            kept.Add(incident);
        }

        return new FilterResult(kept, removed);
    }

    /// <summary>
    ///     Checks Description, Summary, and Comment for non-vehicular tokens.
    ///     Returns the first matched token, or null if the incident is vehicular.
    /// </summary>
    internal static string? FindNonVehicularToken(TrafficIncident incident)
    {
        string? hit = MatchText(incident.Description);
        if (hit is not null) return hit;

        if (incident.Summary.HasValue)
        {
            hit = MatchText(incident.Summary.Value);
            if (hit is not null) return hit;
        }

        if (incident.Comment.HasValue)
        {
            hit = MatchText(incident.Comment.Value);
            if (hit is not null) return hit;
        }

        return null;
    }

    private static string? MatchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (PspWordBoundary().IsMatch(text))
            return "PSP";

        foreach (string token in ContainsTokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                return token;
        }

        return null;
    }
}

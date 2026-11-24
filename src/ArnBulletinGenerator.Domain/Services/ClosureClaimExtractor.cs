using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Reads the roads a finished script claims are CLOSED, so a claim can be checked against the data
///     that is supposed to support it.
///     <para>
///         This parses our own prose, never HERE's: the script is the only artefact a replay carries
///         forward, so the claims it makes have to be recovered from its words. Attribution is
///         positional and deliberately conservative — a closure verb is credited to the nearest road
///         name before it in the same sentence, so "the Greensborough Bypass is still closed southbound
///         at the M80 Metropolitan Ring Road ramp" claims a closure of the Bypass and says nothing
///         about the M80.
///     </para>
/// </summary>
internal static partial class ClosureClaimExtractor
{
    /// <summary>Every road the script claims is closed, de-duplicated, in first-mention order.</summary>
    public static IReadOnlyList<string> ClaimedClosedRoads(string script)
    {
        var claimed = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string sentence in SentenceSplitRegex().Split(script))
        {
            if (string.IsNullOrWhiteSpace(sentence))
                continue;

            var roads = RoadNameRegex().Matches(sentence);
            if (roads.Count == 0)
                continue;

            foreach (var closure in ClosureVerbRegex().Matches(sentence).Cast<Match>())
            {
                var owner = roads.LastOrDefault(r => r.Index + r.Length <= closure.Index)
                            ?? roads.FirstOrDefault(r => r.Index >= closure.Index + closure.Length);
                if (owner is null)
                    continue;

                string road = TrimArticle(owner.Value);
                if (road.Length > 0 && seen.Add(road))
                    claimed.Add(road);
            }
        }

        return claimed;
    }

    /// <summary>Drops a leading article the road-name pattern swept up ("The Greensborough Bypass").</summary>
    private static string TrimArticle(string road)
    {
        string trimmed = road.Trim();
        return LeadingArticleRegex().Replace(trimmed, string.Empty).Trim();
    }

    [GeneratedRegex(@"[.!?;]+")]
    private static partial Regex SentenceSplitRegex();

    /// <summary>A capitalised run ending in an Australian road-type word, or a bare route code.</summary>
    [GeneratedRegex(
        @"\b(?:(?:[A-Z][A-Za-z'\-]*|[A-Z]\d{1,3})\s+){0,4}(?:Road|Rd|Street|St|Highway|Hwy|Freeway|Fwy|Motorway|Mwy|Mtwy|Bypass|Byp|Bridge|Tunnel|Parade|Pde|Drive|Dr|Avenue|Ave|Boulevard|Blvd|Terrace|Tce|Expressway|Distributor|Connector|Link|Circuit|Cct|Crescent|Cres|Court|Ct|Place|Pl|Lane|Ln)\b")]
    private static partial Regex RoadNameRegex();

    [GeneratedRegex(@"\b(?:closed|shut|closure)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ClosureVerbRegex();

    [GeneratedRegex(@"^(?:The|A|An)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingArticleRegex();
}

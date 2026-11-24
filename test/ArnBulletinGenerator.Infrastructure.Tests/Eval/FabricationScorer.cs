using System.Text;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Deterministic detector for the single most damaging on-air failure: a script naming a road
///     that is not present in the traffic data it was generated from. Builds the allowed road
///     vocabulary from the corridors/incidents the model was given, extracts road mentions from the
///     script, and flags any that are unsupported.
///     <para>
///         Scope (v1): road names and motorway codes — the dominant, highest-impact fabrication and the
///         one detectable with high precision. Invented suburbs/causes are a documented follow-up; the
///         existing prompt + LLM review gate already constrain incident causes.
///     </para>
/// </summary>
internal static partial class FabricationScorer
{
    // A capitalized name (1–4 words, allowing lowercase connectors) followed by a road designator.
    [GeneratedRegex(
        @"\b[A-Z][A-Za-z']+(?:\s+(?:[A-Z][A-Za-z']+|of|the|and|de|la)){0,3}\s+(?:Freeway|Motorway|Highway|Tollway|Expressway|Distributor|Bridge|Tunnel|Road|Street|Drive|Parade|Avenue|Boulevard|Esplanade|Terrace|Crescent|Way)\b")]
    private static partial Regex NamedRoadRegex();

    // Australian motorway/route codes: M1, M7, A38, B23.
    [GeneratedRegex(@"\b[ABM]\d{1,3}\b")]
    private static partial Regex RoadCodeRegex();

    [GeneratedRegex(@"^(?i:the)\s+")]
    private static partial Regex LeadingArticleRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    ///     Extracts road mentions (named roads and motorway codes) from free text. Shared with the
    ///     pronunciation scorer so both agree on what counts as a road term.
    /// </summary>
    internal static IReadOnlyList<string> ExtractRoadTerms(string text)
    {
        var terms = new List<string>();
        foreach (Match match in NamedRoadRegex().Matches(text))
            terms.Add(Normalize(match.Value));
        foreach (Match match in RoadCodeRegex().Matches(text))
            terms.Add(match.Value.ToUpperInvariant());
        return terms;
    }

    public static FabricationResult Evaluate(
        string script,
        IReadOnlyList<TrafficCorridor> corridors,
        IReadOnlyList<MonitoredRouteStatus>? monitoredRoutes = null,
        IReadOnlyList<string>? goodRunRoads = null)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new FabricationResult([]);

        string allowedRaw = BuildAllowedText(corridors, monitoredRoutes ?? [], goodRunRoads ?? []);
        string allowed = ExpandAbbreviations(allowedRaw.ToLowerInvariant());
        var allowedCodes = RoadCodeRegex().Matches(allowedRaw)
            .Select(m => m.Value.ToUpperInvariant())
            .ToHashSet();

        var unsupported = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in NamedRoadRegex().Matches(script))
        {
            string mention = Normalize(match.Value);
            if (!seen.Add("NAME:" + mention))
                continue;
            if (!IsSupported(mention, allowed))
                unsupported.Add(mention);
        }

        foreach (Match match in RoadCodeRegex().Matches(script))
        {
            string code = match.Value.ToUpperInvariant();
            if (!seen.Add("CODE:" + code))
                continue;
            if (!allowedCodes.Contains(code))
                unsupported.Add(code);
        }

        return new FabricationResult(unsupported);
    }

    private static string BuildAllowedText(
        IReadOnlyList<TrafficCorridor> corridors,
        IReadOnlyList<MonitoredRouteStatus> monitoredRoutes,
        IReadOnlyList<string> goodRunRoads)
    {
        var builder = new StringBuilder();
        foreach (var route in monitoredRoutes)
            builder.Append(' ').Append(route.Name);
        foreach (var road in goodRunRoads)
            builder.Append(' ').Append(road);
        foreach (var corridor in corridors)
        {
            builder.Append(' ').Append(corridor.RoadName);
            builder.Append(' ').Append(corridor.Description);
            builder.Append(' ').Append(corridor.PrimaryType);
            builder.Append(' ').Append(corridor.Suburb.GetValueOr(string.Empty));
            builder.Append(' ').Append(corridor.Extent.GetValueOr(string.Empty));
            foreach (var incident in corridor.Incidents)
            {
                builder.Append(' ').Append(incident.Description);
                builder.Append(' ').Append(incident.Location);
                builder.Append(' ').Append(incident.Type);
                // "Source detail" free-text IS rendered to the writer (AppendCorridorBullet) —
                // an anchor pulled from it ("Drumpellier Dr", "Vincent St") is grounded, not
                // fabricated. Omitting it here false-positived every such anchor (iter3/iter5 Perth).
                if (incident.Comment.HasValue)
                    builder.Append(' ').Append(incident.Comment.Value);

                // The writer is handed the FACT's places, not HERE's raw description, and the two are
                // not the same string: the fact carries the parsed, expanded anchor. Scoring against
                // the raw text alone flagged four roads on 2026-08-15 (Beecroft Road; Currumbin Creek
                // Road, Tallebudgera Connection Road, Stewart Road) that are all present in HERE's live
                // feed. A fabrication detector that cries wolf is worse than none — it buries the one
                // real hit ("Beechmont Road", which HERE has nowhere within 40 km).
                if (incident.Fact is not { } fact)
                    continue;
                builder.Append(' ').Append(fact.Anchor.GetValueOr(string.Empty));
                builder.Append(' ').Append(fact.SecondaryAnchor.GetValueOr(string.Empty));
                builder.Append(' ').Append(fact.RampTarget.GetValueOr(string.Empty));
                builder.Append(' ').Append(fact.Suburb.GetValueOr(string.Empty));
                builder.Append(' ').Append(fact.RoadName);
            }
        }

        return builder.ToString();
    }

    private static readonly (string Abbreviation, string Full)[] RoadAbbreviations =
    [
        ("mtwy", "motorway"), ("mwy", "motorway"), ("fwy", "freeway"),
        ("hwy", "highway"), ("expwy", "expressway"), ("pde", "parade"),
        ("cwy", "causeway"), ("bvd", "boulevard"), ("espl", "esplanade"),
        ("tce", "terrace"), ("cres", "crescent"),
        // Short generic types: RoadNameFormatter.ToSpeakable expands these in the WRITER's view
        // ("Drumpellier Dr" → "Drumpellier Drive"), so the scorer's allowed text must expand the
        // same way or every voiced expansion false-positives (frozen-eval iter3, Perth).
        ("rd", "road"), ("st", "street"), ("dr", "drive"), ("ave", "avenue"),
        ("ln", "lane"), ("ct", "court"), ("pl", "place"), ("blvd", "boulevard")
    ];

    /// <summary>
    ///     Expands road-type abbreviations in the allowed vocabulary so a script that spells out a road
    ///     ("Pacific Motorway") still matches data that uses HERE's abbreviation ("Pacific Mtwy").
    /// </summary>
    private static string ExpandAbbreviations(string lower)
    {
        foreach (var (abbreviation, full) in RoadAbbreviations)
            lower = Regex.Replace(lower, $@"\b{abbreviation}\b", full);
        return lower;
    }

    /// <summary>
    ///     A road mention is supported if any of its trailing windows of length ≥ 2 tokens appears in the
    ///     allowed text. This tolerates leading sentence filler ("Meanwhile Pacific Motorway") while never
    ///     matching on a bare road-type word ("Motorway") alone.
    /// </summary>
    private static bool IsSupported(string mention, string allowedLower)
    {
        // Expand the mention with the SAME table as the allowed text, so both sides land on the
        // full form regardless of which one HERE abbreviated ("St Kilda Rd" vs "St Kilda Road").
        string[] tokens = ExpandAbbreviations(mention.ToLowerInvariant())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int start = 0; start <= tokens.Length - 2; start++)
        {
            string window = string.Join(' ', tokens[start..]);
            if (allowedLower.Contains(window, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string Normalize(string mention)
    {
        mention = LeadingArticleRegex().Replace(mention.Trim(), string.Empty);
        return WhitespaceRegex().Replace(mention, " ");
    }
}

/// <summary>Result of the fabrication check. <see cref="Passed" /> is true when no unsupported roads were found.</summary>
internal sealed record FabricationResult(IReadOnlyList<string> UnsupportedMentions)
{
    public bool Passed => UnsupportedMentions.Count == 0;
}

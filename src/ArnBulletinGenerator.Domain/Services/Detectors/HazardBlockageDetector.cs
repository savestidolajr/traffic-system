using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic "hazard-blockage" gate. The prompt tells the writer never to invent a lane
///     blockage that isn't in the data, but the model hallucinates it anyway (observed live:
///     debris on the M4, data says lanes open, script says "centre lanes blocked"). The trigger
///     is a live HAZARD (debris / object on the road / lost load) whose incident data does NOT
///     report any lane block, shut, or closure — meaning lanes are open despite the hazard — yet
///     the script says a lane is blocked for that road. This counts the evidence in CODE so a
///     re-draft is forced rather than shipping the fabricated blockage.
/// </summary>
public static partial class HazardBlockageDetector
{
    // ── Incident-side keyword sets (matched against TrafficIncident.Description) ────────

    /// <summary>Hazard keywords: the incident is a live hazard but NOT a lane closure.</summary>
    [GeneratedRegex(@"debris|hazard|object on the road|lost load", RegexOptions.IgnoreCase)]
    private static partial Regex HazardKeywordRegex();

    /// <summary>
    ///     Lane-block keywords in the incident data: when present, the data ITSELF reports a
    ///     lane block/shut/close — so the script is faithfully reflecting a real closure, not
    ///     fabricating one.
    /// </summary>
    [GeneratedRegex(@"lane[s]?\s*(?:\(s\))?\s*(?:block|shut|clos)", RegexOptions.IgnoreCase)]
    private static partial Regex DataLaneBlockRegex();

    // ── Script-side keyword sets (matched against voiced sentences) ────────────────────

    /// <summary>
    ///     Script says a lane is blocked — "blocking a lane", "lane is blocked", "a blocked
    ///     lane", "lanes blocked".
    /// </summary>
    [GeneratedRegex(@"block(?:ing|ed|s)?\b[^.]*\blane", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptBlockLaneRegex();

    /// <summary>
    ///     Script says a lane is blocked (lane-first ordering) — "lanes are blocked", "a lane
    ///     is blocked".
    /// </summary>
    [GeneratedRegex(@"lane[s]?\b[^.]*\bblock", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptLaneBlockRegex();

    /// <summary>
    ///     Centre-lane claim: "centre lane(s)" — the writer attributes a blockage to the
    ///     centre lanes without data backing.
    /// </summary>
    [GeneratedRegex(@"centre lanes?", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptCentreLanesRegex();

    public readonly record struct HazardBlockageResult(
        bool HasHazardBlockage,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public HazardBlockageResult(bool hasHazardBlockage, string feedback)
            : this(hasHazardBlockage, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static HazardBlockageResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script) || context.Corridors.Count == 0)
            return new HazardBlockageResult(false, string.Empty, []);

        var sentences = ScriptSentenceSplitter.Split(script);

        foreach (var corridor in context.Corridors)
        {
            // ── 1. Does ANY incident on this corridor carry a live hazard? ────────────
            bool hasHazard = corridor.Incidents
                .Any(i => !string.IsNullOrWhiteSpace(i.Description) &&
                          HazardKeywordRegex().IsMatch(i.Description));

            if (!hasHazard)
                continue;

            // ── 2. Does the DATA show a lane block for this corridor? ────────────────
            //    Concatenate all incident descriptions as a single blob (mirrors Python's
            //    " ".join(fact_texts)).
            string incidentBlob = string.Join(" ",
                corridor.Incidents
                    .Select(i => i.Description)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));

            bool dataLaneBlock = DataLaneBlockRegex().IsMatch(incidentBlob);

            if (dataLaneBlock)
                continue; // Data itself reports a lane closure — no fabrication.

            // ── 3. Does the SCRIPT say a lane is blocked for this road? ──────────────
            string roadName = corridor.RoadName;
            if (string.IsNullOrWhiteSpace(roadName))
                continue;

            int? order = null;

            for (int s = 0; s < sentences.Count; s++)
            {
                var sentence = sentences[s];
                if (!Mentions(sentence, roadName))
                    continue;

                // Check if this road's sentence claims a lane block.
                if (ScriptBlockLaneRegex().IsMatch(sentence) ||
                    ScriptLaneBlockRegex().IsMatch(sentence) ||
                    ScriptCentreLanesRegex().IsMatch(sentence))
                {
                    order = s + 1;
                    break;
                }
            }

            if (order is null)
                continue;

            // ── 4. HAZARD in data, NO lane-block in data, but script claims blocked ───
            string feedback =
                $"HAZARD-BLOCKAGE: {roadName} data shows lanes open (a hazard, not a lane closure) " +
                "but the script says a lane is blocked — never invent a blockage or move " +
                "another road's onto it";
            var issue = ReviewIssue.ForSentence(order.Value, "HAZARD_BLOCKAGE", feedback);
            return new HazardBlockageResult(true, feedback, [issue]);
        }

        return new HazardBlockageResult(false, string.Empty, []);
    }

    /// <summary>Whole-phrase, case-insensitive match so a road name only counts when actually voiced.</summary>
    private static bool Mentions(string text, string roadName)
    {
        return text.IndexOf(roadName, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

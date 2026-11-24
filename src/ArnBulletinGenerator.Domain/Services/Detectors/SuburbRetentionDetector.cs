using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic suburb-retention gate. Every corridor with a resolved <see cref="TrafficCorridor.Suburb"/>
///     already carries that suburb in the source data handed to the writer (TrafficNarrativeSummarizer's
///     unconditional " near {suburb}" bullet suffix) — the risk this guards against is the model DROPPING an
///     already-given suburb while condensing prose, which is what motivated the since-removed per-market
///     curated "ambiguous roads" list (WALK3-002 — superseded, see docs/canonical/decisions.md). Enforcing
///     retention in code, for every road with resolved suburb data, replaces that list: no admin has to
///     identify which roads are "ambiguous" up front, and the guarantee is market-wide and permanent.
/// </summary>
public static partial class SuburbRetentionDetector
{
    public readonly record struct SuburbRetentionResult(
        bool HasMissingSuburb,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public SuburbRetentionResult(bool hasMissingSuburb, string feedback)
            : this(hasMissingSuburb, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }
    // Matches a road name already immediately followed by a place qualifier the writer supplied —
    // a locative preposition + a Capitalised place (or "the <Place>"). Distinguishes "at Rozelle"
    // (a place, guard the insert) from "at a standstill" (a condition, allow the insert).

    [GeneratedRegex(
        @"^\s*(?i:near|at|between|past|approaching|around|heading|along|off|opposite|outside|by)\b\s+(?:(?i:the)\s+)?\p{Lu}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex AlreadyPlacedRegex();

    public static SuburbRetentionResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new SuburbRetentionResult(false, string.Empty, []);

        // Grouped by ROAD, not per corridor. A road with several spots supplies several suburbs,
        // but the read voices the spots the slot holds — the writer is told to group a road's spots
        // into one item, and the item cap trims the rest. Charging the read for every suburb the
        // road carries anywhere in the data demands content the bulletin deliberately dropped:
        // cairns names the Bruce Highway once, placed at Edmonton, and Gordonvale flagged as
        // dropped though its spot never aired (dev f553c893 / 7d4664af, 2026-08-24). Neither refine
        // nor Repair could clear it — Repair's already-placed guard rightly refuses to stack a
        // second qualifier — so both bulletins shipped an unresolved style gate. The guarantee that
        // matters is that a listener can PLACE the road, and one of its suburbs does that.
        var missing = new List<(string RoadName, string Suburb)>();
        foreach (var group in context.Corridors
                     .Where(c => !string.IsNullOrWhiteSpace(c.RoadName) && c.Suburb.HasValue)
                     .GroupBy(c => c.RoadName, StringComparer.OrdinalIgnoreCase))
        {
            string roadName = group.Key;
            if (!Mentions(script, roadName))
                continue;

            var suburbs = group
                .Select(c => c.Suburb.Value)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                // Suburb already embedded in the road name itself ("Neranwood") — nothing to add.
                .Where(s => !roadName.Contains(s, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (suburbs.Count == 0 || suburbs.Any(s => Mentions(script, s)))
                continue;

            missing.Add((roadName, suburbs[0]));
        }

        if (missing.Count == 0)
            return new SuburbRetentionResult(false, string.Empty, []);

        string list = string.Join("; ", missing.Select(m => $"\"{m.RoadName}\" (near {m.Suburb})"));
        string feedback =
            $"SUBURB DROPPED: the script names a road but leaves out the suburb the data gave it, which a " +
            $"listener needs to place it: {list}. Add the suburb back exactly where each road is named — never " +
            "drop a location qualifier the data provided.";

        var sentences = ScriptSentenceSplitter.Split(script);
        var issues = new List<ReviewIssue>(missing.Count);

        foreach (var (roadName, suburb) in missing)
        {
            int? order = null;
            for (int i = 0; i < sentences.Count; i++)
            {
                if (Mentions(sentences[i], roadName))
                {
                    order = i + 1;
                    break;
                }
            }

            string itemReason =
                $"SUBURB DROPPED: \"{roadName}\" (near {suburb}) is missing its suburb qualifier. " +
                $"Add \"near {suburb}\" back where {roadName} is named.";

            issues.Add(order is > 0
                ? ReviewIssue.ForSentence(order.Value, "SUBURB_RETENTION", itemReason)
                : ReviewIssue.ForUnresolvedSentence("SUBURB_RETENTION", itemReason));
        }

        return new SuburbRetentionResult(true, feedback, issues);
    }

    /// <summary>
    ///     Mechanical last-resort fix: inserts " near {Suburb}" immediately after the FIRST whole-word
    ///     mention of each still-missing road name — the exact suffix shape TrafficNarrativeSummarizer
    ///     already uses when building the source bullet, so the inserted text matches the read's own voice.
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        string result = script;
        foreach (var corridor in context.Corridors)
        {
            if (string.IsNullOrWhiteSpace(corridor.RoadName) || !corridor.Suburb.HasValue)
                continue;
            string suburb = corridor.Suburb.Value;
            if (string.IsNullOrWhiteSpace(suburb))
                continue;
            if (corridor.RoadName.Contains(suburb, StringComparison.OrdinalIgnoreCase))
                continue;
            // Placed at ANY of the road's spots is placed, matching Detect. Without this the two
            // disagree on a multi-spot road: Detect is satisfied, Repair still wants to insert, and
            // only the already-placed guard below stops a contradictory second qualifier.
            if (!Mentions(result, corridor.RoadName) || RoadIsPlaced(result, context, corridor.RoadName))
                continue;

            int idx = IndexOfWholeWord(result, corridor.RoadName);
            int insertAt = idx + corridor.RoadName.Length;

            // Don't stack a locative. If the road name is already immediately followed by a place
            // qualifier the writer supplied ("... near Rozelle", "... at Eight Mile Plains"), inserting
            // the data's suburb here would produce a contradictory double qualifier
            // ("Victoria Road near Drummoyne near Rozelle"). The road is already placed — leave it.
            if (AlreadyPlacedRegex().IsMatch(result.AsSpan(insertAt)))
                continue;

            result = result[..insertAt] + $" near {suburb}" + result[insertAt..];
        }
        return result;
    }

    /// <summary>
    ///     True when the script already names any suburb the data gives for this road, so a listener
    ///     can place it. Shared by <see cref="Detect" /> and <see cref="Repair" /> so the gate and
    ///     its mechanical fix can never disagree about what "placed" means.
    /// </summary>
    private static bool RoadIsPlaced(string script, BroadcastWithTraffic context, string roadName) =>
        context.Corridors
            .Where(c => string.Equals(c.RoadName, roadName, StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Suburb.HasValue && !string.IsNullOrWhiteSpace(c.Suburb.Value))
            .Select(c => c.Suburb.Value)
            .Any(s => Mentions(script, s));

    private static int IndexOfWholeWord(string text, string word)
    {
        int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool leftOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            int end = idx + word.Length;
            bool rightOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (leftOk && rightOk)
                return idx;
            idx = text.IndexOf(word, idx + 1, StringComparison.OrdinalIgnoreCase);
        }
        return -1;
    }

    private static bool Mentions(string text, string phrase) => IndexOfWholeWord(text, phrase) >= 0;
}

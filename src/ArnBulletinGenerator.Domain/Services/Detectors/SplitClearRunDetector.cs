using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic "split clear-run" gate + repair. Same-state clear roads should be voiced in ONE
///     grouped clause ("A, B and C are all moving nicely"). The draft prompt already asks for this,
///     but the model intermittently splits the group across adjacent sentences that REPEAT the same
///     clear idiom — observed live (Sydney, 2026-07-13): "The Sydney Harbour Bridge, M4 Western
///     Motorway and M5 South West Motorway are moving nicely. Anzac Bridge is moving nicely." Two
///     occurrences sit below <see cref="RepeatedStatePhraseDetector" />'s 3x threshold, so this
///     targeted gate catches the split and mechanically merges the clauses into one grouped clear-run.
///
///     Conservative by design: only ADJACENT clear-road sentences carrying the IDENTICAL idiom are
///     merged. Different idioms ("moving nicely" then "looking good") are legitimate variation and are
///     left alone; incident/congestion sentences never match the clear-only idiom set.
/// </summary>
public static partial class SplitClearRunDetector
{
    // An optional "both"/"all" is tolerated so an already-merged clause re-matches and a run of three
    // or more same-idiom sentences collapses fully into one.
    [GeneratedRegex(
        @"^\s*(?<subj>.+?)\s+(?:is|are)\s+(?:both\s+|all\s+)?(?<idiom>moving nicely|looking good|no dramas|a clear run|clear run|running well|running cleanly|moving well|running smoothly)\s*\.?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClearSentenceRegex();

    public readonly record struct SplitClearRunResult(
        bool HasSplitClearRun,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public SplitClearRunResult(bool hasSplitClearRun, string feedback)
            : this(hasSplitClearRun, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static SplitClearRunResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new SplitClearRunResult(false, string.Empty, []);

        var sentences = ScriptSentenceSplitter.Split(script);
        for (int i = 0; i + 1 < sentences.Count; i++)
        {
            var a = ClearSentenceRegex().Match(sentences[i]);
            var b = ClearSentenceRegex().Match(sentences[i + 1]);
            if (a.Success && b.Success &&
                string.Equals(a.Groups["idiom"].Value, b.Groups["idiom"].Value, StringComparison.OrdinalIgnoreCase))
            {
                int order = i + 2; // Flag the duplicate trailing sentence
                string feedback =
                    $"SPLIT-CLEAR-RUN: adjacent clear-road sentences both end \"{a.Groups["idiom"].Value.ToLowerInvariant()}\" — " +
                    "group every road sharing that state into ONE clause (\"A, B and C are all moving nicely\"), " +
                    "never a trailing sentence that restates the same clear idiom.";
                var issue = ReviewIssue.ForSentence(order, "SPLIT_CLEAR_RUN", feedback);
                return new SplitClearRunResult(true, feedback, [issue]);
            }
        }

        return new SplitClearRunResult(false, string.Empty, []);
    }

    /// <summary>
    ///     Merges adjacent clear-road sentences that repeat the same idiom into a single grouped
    ///     clause. Idempotent; leaves everything else untouched.
    /// </summary>
    public static string Repair(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        var sentences = ScriptSentenceSplitter.Split(script).ToList();
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i + 1 < sentences.Count; i++)
            {
                var a = ClearSentenceRegex().Match(sentences[i]);
                var b = ClearSentenceRegex().Match(sentences[i + 1]);
                if (!a.Success || !b.Success ||
                    !string.Equals(a.Groups["idiom"].Value, b.Groups["idiom"].Value, StringComparison.OrdinalIgnoreCase))
                    continue;

                var roads = ExtractRoads(a.Groups["subj"].Value)
                    .Concat(ExtractRoads(b.Groups["subj"].Value))
                    .ToList();
                string idiom = a.Groups["idiom"].Value;
                string quantifier = roads.Count > 2 ? "all " : "both ";
                sentences[i] = $"{JoinRoads(roads)} are {quantifier}{idiom}.";
                sentences.RemoveAt(i + 1);
                merged = true;
                break;
            }
        }

        return string.Join(" ", sentences);
    }

    /// <summary>Splits a subject phrase ("The A, B and C") into its road-name items.</summary>
    private static List<string> ExtractRoads(string subject) =>
        Regex.Split(subject.Trim(), @",\s*|\s+and\s+", RegexOptions.IgnoreCase)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

    private static string JoinRoads(IReadOnlyList<string> roads) =>
        roads.Count == 1
            ? roads[0]
            : string.Join(", ", roads.Take(roads.Count - 1)) + " and " + roads[^1];
}

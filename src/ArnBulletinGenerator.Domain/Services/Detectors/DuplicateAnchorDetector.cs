using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic "duplicate anchor" gate (BFN-006/BFN-020). ATN names a road once per item and
///     refers back to it implicitly ("the closure", "the same stretch") — voicing the identical
///     road/location name twice within one sentence reads as a data-dump, not a presenter's copy.
///     Prompt-only instructions for this proved unreliable (BFN-006 was verified clean on live data
///     2026-06-30, then regressed on fresh live generations 2026-07-01 — see decisions.md), so this
///     counts anchor repeats in CODE and forces a targeted re-draft, the same gate shape as
///     <see cref="RollCallDetector" />.
///     <para>
///         Trigger: any known road/corridor name, or the market's monitored-route names, mentioned
///         TWICE OR MORE within a single sentence. A corridor's own extent/location sub-phrases are
///         not separately tracked here (that is <see cref="RollCallDetector" />'s job) — this only
///         catches literal repetition of the SAME name within one sentence.
///     </para>
/// </summary>
public static partial class DuplicateAnchorDetector
{
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    public readonly record struct DuplicateAnchorResult(
        bool HasDuplicateAnchor,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public DuplicateAnchorResult(bool hasDuplicateAnchor, string feedback)
            : this(hasDuplicateAnchor, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static DuplicateAnchorResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new DuplicateAnchorResult(false, string.Empty, []);

        var anchors = context.Corridors
            .Select(c => c.RoadName)
            .Concat(context.MonitoredRoutes.Select(r => r.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length) // longest-first so "Smith Street Motorway" counts
            .ToList(); // separately from "Smith Street" rather than double-counting a substring.

        var sentences = SentenceSplit().Split(script);
        for (int i = 0; i < sentences.Length; i++)
        {
            string sentence = sentences[i];
            string working = sentence;
            foreach (string anchor in anchors)
            {
                var positions = FindWholeWordMatches(working, anchor);
                if (positions.Count >= 2)
                {
                    int order = i + 1;
                    string feedback =
                        $"DUPLICATE ANCHOR: \"{anchor}\" is named {positions.Count} times in one sentence " +
                        $"(\"{sentence.Trim()}\"). Name it ONCE per sentence and refer back implicitly " +
                        "(\"the same stretch\", drop the repeat, or fold both facts into the first mention) " +
                        "— never voice the identical road or location name twice in a row.";
                    var issue = ReviewIssue.ForSentence(order, "DUPLICATE_ANCHOR", feedback);
                    return new DuplicateAnchorResult(true, feedback, [issue]);
                }

                // Mask out matched span(s) so a SHORTER anchor that is a textual prefix of this one
                // (e.g. "Smith Street" inside "Smith Street Motorway" — two genuinely different known
                // roads) is never re-counted against the longer anchor's already-claimed text.
                foreach (int pos in positions)
                    working = Mask(working, pos, anchor.Length);
            }
        }

        return new DuplicateAnchorResult(false, string.Empty, []);
    }

    // A bare trailing "[the] at/on/near/along ANCHOR" phrase that reaches straight to a sentence
    // boundary (comma or terminal punctuation) restates a place the same sentence already named —
    // pure self-reference, zero new information. Any OTHER preposition ("with", "to", "between", a
    // genuine extent/second-fact join) is left alone; that shape may carry real information this
    // mechanical pass cannot safely judge.

    [GeneratedRegex(
        @"\s+(at|on|near|along)\s+(the\s+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TrailingSelfReferencePreposition();

    /// <summary>
    ///     Mechanical last-resort fix for a duplicate-anchor violation that survives the retry
    ///     budget (mirrors <see cref="RollCallDetector.Repair" />/<see cref="IncidentTypeStackDetector.Repair" />).
    ///     Only acts on the safe, observed-live shape: exactly ONE anchor repeats in a sentence, and
    ///     its LAST occurrence is a bare trailing "at/on/near/along [the] ANCHOR" phrase with nothing
    ///     but a sentence boundary after it — that phrase is deleted whole, restoring the sentence to
    ///     naming the anchor once. Any other shape (a second mention carrying new information, or
    ///     more than one anchor duplicated in the same sentence) is left completely untouched.
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        var anchors = context.Corridors
            .Select(c => c.RoadName)
            .Concat(context.MonitoredRoutes.Select(r => r.Name))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(n => n.Length)
            .ToList();

        var sentences = SentenceSplit().Split(script);
        bool changed = false;

        for (int s = 0; s < sentences.Length; s++)
        {
            string? repaired = TryRepairSentence(sentences[s], anchors);
            if (repaired is null)
                continue;
            sentences[s] = repaired;
            changed = true;
        }

        return changed ? string.Join(" ", sentences) : script;
    }

    private static string? TryRepairSentence(string sentence, IReadOnlyList<string> anchors)
    {
        // Only when exactly ONE known anchor repeats in this sentence — two independently
        // duplicated anchors in the same sentence is an interaction this mechanical pass does
        // not attempt to resolve.
        string? targetAnchor = null;
        List<int>? positions = null;
        foreach (string anchor in anchors)
        {
            var found = FindWholeWordMatches(sentence, anchor);
            if (found.Count < 2)
                continue;
            if (targetAnchor is not null)
                return null;
            targetAnchor = anchor;
            positions = found;
        }
        if (targetAnchor is null || positions is null)
            return null;

        int lastStart = positions[^1];
        int lastEnd = lastStart + targetAnchor.Length;

        string before = sentence[..lastStart];
        var prepMatch = TrailingSelfReferencePreposition().Match(before);
        if (!prepMatch.Success)
            return null;

        string after = sentence[lastEnd..];
        string afterTrimmed = after.TrimStart();
        if (afterTrimmed.Length > 0 && afterTrimmed[0] is not (',' or '.' or '!' or '?' or ';'))
            return null;

        return sentence[..prepMatch.Index] + after;
    }

    /// <summary>
    ///     Whole-phrase, case-insensitive match positions so a name only counts when actually voiced.
    ///     A match immediately preceded by an attached capitalized word is REJECTED: it is the tail
    ///     of a LONGER proper name, not a mention of the anchor ("Coast Highway" inside "Gold Coast
    ///     Highway" — observed live, atn2 Gold Coast: the corridor's own extent text named the longer
    ///     road, the writer voiced the data faithfully, and the gate false-flagged a duplicate).
    ///     Longest-first masking cannot cover this case because the longer name comes from extent
    ///     free text, not the known-anchor list.
    /// </summary>
    private static List<int> FindWholeWordMatches(string text, string name)
    {
        var positions = new List<int>();
        int idx = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool leftOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            int end = idx + name.Length;
            bool rightOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (leftOk && rightOk && !PrecededByAttachedCapitalizedWord(text, idx))
                positions.Add(idx);
            idx = text.IndexOf(name, idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return positions;
    }

    /// <summary>True when the token directly before <paramref name="matchStart" /> (one space away)
    /// is a capitalized word — the match is then part of a longer compound proper name.</summary>
    private static bool PrecededByAttachedCapitalizedWord(string text, int matchStart)
    {
        int i = matchStart - 1;
        if (i < 0 || text[i] != ' ')
            return false;
        int wordEnd = i; // exclusive
        while (i > 0 && char.IsLetter(text[i - 1]))
            i--;
        return wordEnd - i > 0 && char.IsUpper(text[i]);
    }

    /// <summary>Replaces a matched span with a control-character sentinel that cannot match any road name
    /// (breaks word-boundary adjacency too), so subsequent shorter-anchor searches skip it entirely.</summary>
    private static string Mask(string text, int start, int length) =>
        string.Concat(text[..start], new string('\u0001', length), text[(start + length)..]);
}

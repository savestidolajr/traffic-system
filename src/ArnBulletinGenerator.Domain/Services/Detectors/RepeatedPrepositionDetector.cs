using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic "repeated-preposition split closure" gate. A corridor bullet carrying TWO
///     Location lines always represents TWO SEPARATE INCIDENTS on the same road (each Location
///     line comes from a different incident — a single incident's own "between A and B" span is
///     already rendered as ONE line, never two; see <c>CorridorAnchors</c> in
///     <c>TrafficNarrativeSummarizer</c>). Voicing both with the IDENTICAL preposition repeated
///     ("shut at Brook Street, at the ramp to Alfred Street North") is the database-read-out
///     AI-tell — a presenter never lists locations that way. Prompt-only guidance for this proved
///     unreliable for the sibling classes (BFN-006/BFN-019 both regressed live after a prose-only
///     fix — see decisions.md 2026-07-01), so this counts the repeated preposition in CODE, the
///     same gate shape as <see cref="RollCallDetector" />/<see cref="DuplicateAnchorDetector" />/
///     <see cref="IncidentTypeStackDetector" />.
///     <para>
///         Deliberately narrow: only the unambiguous shape — the SAME preposition word introducing
///         two different anchors, separated by nothing but a comma — is flagged. A conjoined "at X
///         and Y" or a naturally-varied "at X, with Y also..." is NOT flagged; those are legitimate
///         connected prose even when they carry the same underlying two-incident ambiguity risk.
///     </para>
/// </summary>
public static partial class RepeatedPrepositionDetector
{
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    // Captures "<prep1> <anchor>, <prep2> <anchor>" where prep1 == prep2 (backreference \2) and
    // nothing but the comma/space separates the two prepositional phrases.
    [GeneratedRegex(
        @"\b(at|on|near)\b[^,.!?]+,\s*\b(\1)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RepeatedPrepositionPattern();

    public readonly record struct RepeatedPrepositionResult(
        bool HasRepeatedPreposition,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public RepeatedPrepositionResult(bool hasRepeatedPreposition, string feedback)
            : this(hasRepeatedPreposition, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static RepeatedPrepositionResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new RepeatedPrepositionResult(false, string.Empty, []);

        var sentences = ScriptSentenceSplitter.Split(script);
        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            var match = RepeatedPrepositionPattern().Match(sentence);
            if (!match.Success)
                continue;

            string prep = match.Groups[1].Value.ToLowerInvariant();
            string feedback =
                $"REPEATED PREPOSITION: one sentence stacks two \"{prep} ...\" phrases back to back " +
                $"(\"{sentence.Trim()}\"). Two Location lines on a corridor are two SEPARATE incidents, " +
                "not endpoints of one closure — never fabricate a bounded span between them. Connect them " +
                "naturally instead: vary the preposition or verb for the second (\"...shut at Brook Street, " +
                "with the ramp to Alfred Street North also closed\"), or give the second its own short clause. " +
                "Never repeat the identical preposition to introduce a second location in the same sentence.";
            int order = i + 1;
            var issue = ReviewIssue.ForSentence(order, "REPEATED_PREPOSITION", feedback);
            return new RepeatedPrepositionResult(true, feedback, [issue]);
        }

        // Fallback: If pattern matched full script but wasn't isolated by splitter
        var scriptMatch = RepeatedPrepositionPattern().Match(script);
        if (scriptMatch.Success)
        {
            string prep = scriptMatch.Groups[1].Value.ToLowerInvariant();
            string feedback =
                $"REPEATED PREPOSITION: script stacks two \"{prep} ...\" phrases back to back. " +
                "Two Location lines on a corridor are two SEPARATE incidents, " +
                "not endpoints of one closure — never fabricate a bounded span between them. Connect them " +
                "naturally instead: vary the preposition or verb for the second (\"...shut at Brook Street, " +
                "with the ramp to Alfred Street North also closed\"), or give the second its own short clause. " +
                "Never repeat the identical preposition to introduce a second location in the same sentence.";
            var issue = ReviewIssue.ForUnresolvedSentence("REPEATED_PREPOSITION", feedback);
            return new RepeatedPrepositionResult(true, feedback, [issue]);
        }

        return new RepeatedPrepositionResult(false, string.Empty, []);
    }

    /// <summary>
    ///     Mechanical last-resort fix: when the LLM has exhausted its retry budget and still ships
    ///     a repeated preposition, swap the SECOND occurrence for "and" — the cheapest grammatically
    ///     safe connective that removes the mechanical repetition without inventing a bounded span
    ///     or new content. Returns the input unchanged when no offending pattern is present.
    /// </summary>
    public static string Repair(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        var sentences = SentenceSplit().Split(script);
        bool changed = false;

        for (int s = 0; s < sentences.Length; s++)
        {
            string sentence = sentences[s];
            var match = RepeatedPrepositionPattern().Match(sentence);
            if (!match.Success)
                continue;

            var secondPrep = match.Groups[2];
            sentences[s] = sentence[..secondPrep.Index] + "and" + sentence[(secondPrep.Index + secondPrep.Length)..];
            changed = true;
        }

        return changed ? string.Join(" ", sentences) : script;
    }
}

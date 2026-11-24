using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic "trailing incident-type stack" gate (BFN-019). The data's raw incident-type
///     words ("closure", "construction", "roadworks", "speed reduction", "detour"...) are NOTES for
///     the writer, not copy — reading two or more of them as a trailing summary clause ("...for
///     upgrades, including closures and speed reductions", "...with road closure and construction
///     there") is a data recitation, not a sentence. A prose-only ban on this proved unreliable
///     (observed regressing live in two separate markets the SAME day the rule was strengthened —
///     see decisions.md 2026-07-01), so this counts type-word density in the sentence TAIL in CODE,
///     the same gate shape as <see cref="RollCallDetector" />/<see cref="DuplicateAnchorDetector" />.
/// </summary>
public static partial class IncidentTypeStackDetector
{
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    // The raw type-noun vocabulary a writer might recite verbatim from an "Incidents:" data line
    // or an incident-type floor — deliberately nouns/gerunds, not the driving-state verbs
    // ("crawling", "at a standstill") the writer SHOULD reach for instead.
    private static readonly string[] TypeNouns =
    [
        "closure", "closures", "construction", "roadworks", "congestion", "accident", "accidents",
        "breakdown", "breakdowns", "hazard", "hazards", "restriction", "restrictions",
        "reduction", "reductions", "detour", "detours", "upgrade", "upgrades"
    ];

    [GeneratedRegex(@"(?:\bwith\b|\bincluding\b)\s+([a-z ,]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingClause();

    public readonly record struct IncidentTypeStackResult(
        bool HasTypeStack,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public IncidentTypeStackResult(bool hasTypeStack, string feedback)
            : this(hasTypeStack, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static IncidentTypeStackResult Detect(string script)
    {
        var (hasStack, feedback, offendingSentence) = DetectInternal(script);
        if (!hasStack)
            return new IncidentTypeStackResult(false, string.Empty, []);

        int? order = null;
        if (offendingSentence is not null)
        {
            var sentences = ScriptSentenceSplitter.Split(script);
            for (int i = 0; i < sentences.Count; i++)
            {
                if (sentences[i].Contains(offendingSentence.Trim(), StringComparison.Ordinal)
                    || offendingSentence.Contains(sentences[i].Trim(), StringComparison.Ordinal))
                {
                    order = i + 1;
                    break;
                }
            }
        }

        var issue = order is > 0
            ? ReviewIssue.ForSentence(order.Value, "INCIDENT_TYPE_STACK", feedback)
            : ReviewIssue.ForUnresolvedSentence("INCIDENT_TYPE_STACK", feedback);

        return new IncidentTypeStackResult(true, feedback, [issue]);
    }

    /// <summary>
    ///     Mechanical last-resort fix: when the LLM has exhausted its retry budget and still ships
    ///     a trailing type-stack clause, strip that clause verbatim rather than accept it. Safe
    ///     because the clause is an APPENDED afterthought — the sentence already stated the closure/
    ///     delay fact before it, so removing "with closures, speed reductions and detours in place"
    ///     loses no fact the listener needed, only the raw data recitation. Returns the input
    ///     unchanged when no offending clause is present.
    /// </summary>
    public static string Repair(string script)
    {
        var (hasStack, _, offendingSentence) = DetectInternal(script);
        if (!hasStack || offendingSentence is null)
            return script;

        int matchPos = script.IndexOf(offendingSentence, StringComparison.Ordinal);
        if (matchPos < 0)
            return script; // Defensive: sentence text didn't round-trip verbatim, don't guess.

        // Re-run the match against the TRIMMED sentence — DetectInternal's regex needs terminal
        // punctuation stripped for its $ anchor to fire. The trimmed form is always a PREFIX of
        // offendingSentence, so the match's start index is valid against either string.
        string trimmed = offendingSentence.TrimEnd('.', '!', '?', ' ');
        var trailing = TrailingClause().Match(trimmed);
        string cleanedSentence = offendingSentence[..trailing.Index].TrimEnd(' ', ',') + ".";

        string before = script[..matchPos];
        string after = script[(matchPos + offendingSentence.Length)..];
        return before + cleanedSentence + after;
    }

    private static (bool HasStack, string Feedback, string? OffendingSentence) DetectInternal(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return (false, string.Empty, null);

        foreach (string rawSentence in SentenceSplit().Split(script))
        {
            string sentence = rawSentence.TrimEnd('.', '!', '?', ' ');
            var trailing = TrailingClause().Match(sentence);
            if (!trailing.Success)
                continue;

            string tail = trailing.Groups[1].Value;
            int distinctTypesInTail = TypeNouns.Count(t => ContainsWholeWord(tail, t));
            if (distinctTypesInTail >= 2)
                return (true,
                    $"TYPE-STACK: the sentence (\"{sentence.Trim()}\") ends with a trailing clause that " +
                    "recites two or more raw incident-type words. That is a data recitation, not a sentence — " +
                    "pick the ONE type that matters (usually whichever the closure/delay is actually about) " +
                    "and drop the rest, or fold the single most relevant detail into the main clause instead.",
                    rawSentence);
        }

        return (false, string.Empty, null);
    }

    private static bool ContainsWholeWord(string text, string word)
    {
        int idx = text.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool leftOk = idx == 0 || !char.IsLetter(text[idx - 1]);
            int end = idx + word.Length;
            bool rightOk = end >= text.Length || !char.IsLetter(text[end]);
            if (leftOk && rightOk)
                return true;
            idx = text.IndexOf(word, idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}

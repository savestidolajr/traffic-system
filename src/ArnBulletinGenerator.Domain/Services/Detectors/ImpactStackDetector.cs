using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic "trailing impact-word stack" gate — the sibling of
///     <see cref="IncidentTypeStackDetector" /> for SEVERITY words rather than incident-TYPE words.
///     The data layer already computes exactly ONE honest magnitude per corridor
///     (<c>FormatImpact</c> in <c>TrafficNarrativeSummarizer</c>: "minor delays", "heavy congestion",
///     etc.) — reading two or more severity synonyms as a trailing clause ("...is congested at X,
///     with heavy traffic and moderate delays" — observed live, Perth) restates the SAME fact three
///     times (congested / heavy traffic / moderate delays all mean "this road is bad") rather than
///     adding new information. Draft.md line 34 already bans stacking "the type, the impact word and
///     the detail in one breath" in the MAIN clause; this catches the equivalent failure mode when
///     the stack lands in a trailing "with X and Y" clause instead — the same shape
///     IncidentTypeStackDetector exists to catch for type words, now for severity words.
/// </summary>
public static partial class ImpactStackDetector
{
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    // Severity/congestion synonyms a writer might restate redundantly in a trailing clause after
    // the sentence already voiced the road's condition. Deliberately the qualified delay phrases
    // ("minor delays", not bare "delays") to avoid false positives on ordinary prose, plus the
    // common congestion-state descriptors FormatImpact's callers translate the data into.
    private static readonly string[] ImpactWords =
    [
        "congested", "congestion", "heavy traffic", "bumper to bumper", "stop-start", "crawling",
        "backed up", "banked up", "at a standstill", "gridlocked",
        "minor delays", "moderate delays", "heavy delays", "severe delays"
    ];

    [GeneratedRegex(@"(?:\bwith\b|\bincluding\b)\s+([a-z ,-]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingClause();

    public readonly record struct ImpactStackResult(
        bool HasImpactStack,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public ImpactStackResult(bool hasImpactStack, string feedback)
            : this(hasImpactStack, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static ImpactStackResult Detect(string script)
    {
        var (hasStack, feedback, offendingSentence) = DetectInternal(script);
        if (!hasStack)
            return new ImpactStackResult(false, string.Empty, []);

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
            ? ReviewIssue.ForSentence(order.Value, "IMPACT_STACK", feedback)
            : ReviewIssue.ForUnresolvedSentence("IMPACT_STACK", feedback);

        return new ImpactStackResult(true, feedback, [issue]);
    }

    /// <summary>
    ///     Mechanical last-resort fix: strips the trailing impact-stack clause verbatim, mirroring
    ///     <see cref="IncidentTypeStackDetector.Repair" />. Safe because the clause is an APPENDED
    ///     afterthought restating a condition the sentence already conveyed — removing "with heavy
    ///     traffic and moderate delays" loses no fact the listener needed, only the repetition.
    /// </summary>
    public static string Repair(string script)
    {
        var (hasStack, _, offendingSentence) = DetectInternal(script);
        if (!hasStack || offendingSentence is null)
            return script;

        int matchPos = script.IndexOf(offendingSentence, StringComparison.Ordinal);
        if (matchPos < 0)
            return script; // Defensive: sentence text didn't round-trip verbatim, don't guess.

        string trimmed = offendingSentence.TrimEnd('.', '!', '?', ' ');
        var trailing = TrailingClause().Match(trimmed);
        string cleanedSentence = offendingSentence[..trailing.Index].TrimEnd(' ', ',') + ".";

        string before = script[..matchPos];
        string after = script[(matchPos + offendingSentence.Length)..];
        return before + cleanedSentence + after;
    }

    // A linking verb inside the "trailing clause" span signals it actually contains a SECOND
    // independent clause (a different subject — "...and Point Road is congested") rather than a
    // genuine list of stacked severity phrases about the SAME item.

    [GeneratedRegex(@"\b(is|are|was|were|has|have|had)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LinkingVerb();

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
            if (LinkingVerb().IsMatch(tail))
                continue; // a second independent clause about a DIFFERENT item, not a stacked list

            int distinctImpactWordsInTail = ImpactWords.Count(w => ContainsWholeWord(tail, w));
            if (distinctImpactWordsInTail >= 2)
                return (true,
                    $"IMPACT-STACK: the sentence (\"{sentence.Trim()}\") ends with a trailing clause that " +
                    "restates two or more severity synonyms (congested/heavy traffic/moderate delays all mean " +
                    "the same thing). Pick the SINGLE most telling descriptor and drop the rest — never stack " +
                    "severity words in one breath.",
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

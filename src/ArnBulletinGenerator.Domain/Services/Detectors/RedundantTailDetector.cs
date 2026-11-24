using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic gate for the summarising/back-referencing TAIL — a closing sentence that adds
///     no new road or fact, only restates the clear-road status already voiced or pads with a
///     wrap-up. Observed live (Mildura, refine/polish output): "...with traffic moving nicely along
///     those key routes", "no other incidents in the current update", "For the latest local
///     traffic, keep it with us." The draft rarely produces these; the refine loop and polish stage
///     add them under length pressure. A listener already heard the roads — the tail is dead air.
///     The gate fires only on a genuine tail: the LAST sentence must carry a clear-run word AND a
///     back-reference to already-mentioned roads ("those roads/routes/run", "key routes"), OR match
///     a known filler-close. A normal clear-road close that names roads is never flagged.
/// </summary>
public static partial class RedundantTailDetector
{
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    // A clear-run state word (the tail is a clear-road restatement, not a fresh incident).
    [GeneratedRegex(
        @"\b(moving nicely|moving well|looking good|flowing well|clear run|no dramas|running well|running to time|all clear)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex ClearWord();

    // A back-reference to roads already named ("those routes", "these roads", "that run", "key
    // routes"). The determiner may be separated from the noun by a count or qualifier — "those FOUR
    // roads", "all four routes" — which the original adjacent-only pattern missed, letting a textbook
    // padding tail through (job fa754637, Gold Coast: "So, for the latest traffic picture, those four
    // roads are all moving nicely, giving drivers a clear run across the Gold Coast."). The inserted
    // token is a CLOSED set so this can never swallow arbitrary prose.
    [GeneratedRegex(
        @"\b((those|these|the|all)( (two|three|four|five|six|other|main|key|named))? (roads?|routes?|run)|key routes?|named roads?)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex BackReference();

    // Known filler-closes that carry no road and no fact. "for the latest ... traffic" only matched
    // when "traffic" came LAST ("for the latest local traffic"), so the equally common noun-trailing
    // form ("for the latest traffic picture/update") slipped through — same job fa754637.
    [GeneratedRegex(
        @"\b(no other (incidents|dramas|hold-ups)|in the (current update|latest (traffic )?check|update)|keep it with us|for the latest (?:[\w-]+ ){0,3}traffic|for the latest traffic (picture|update|check|wrap)|that'?s the (latest|traffic))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex FillerClose();

    public readonly record struct RedundantTailResult(
        bool HasRedundantTail,
        string Feedback,
        string Tail,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public RedundantTailResult(bool hasRedundantTail, string feedback, string tail)
            : this(hasRedundantTail, feedback, tail, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static RedundantTailResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new RedundantTailResult(false, string.Empty, string.Empty, []);

        var sentences = SentenceSplit().Split(script.Trim())
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (sentences.Count < 2)
            return new RedundantTailResult(false, string.Empty, string.Empty, []);

        string tail = sentences[^1];
        bool isRestatement = ClearWord().IsMatch(tail) && BackReference().IsMatch(tail);
        bool isFiller = FillerClose().IsMatch(tail);
        if (!isRestatement && !isFiller)
            return new RedundantTailResult(false, string.Empty, string.Empty, []);

        string feedback =
            $"REDUNDANT_TAIL: the closing sentence (\"{Truncate(tail)}\") restates the clear-road status " +
            "already voiced or pads with a wrap-up — a listener heard it. End on the last real traffic fact; " +
            "drop the summarising tail.";

        var scriptSentences = ScriptSentenceSplitter.Split(script);
        int? order = scriptSentences.Count > 0 ? scriptSentences.Count : null;
        var issue = order is > 0
            ? ReviewIssue.ForSentence(order.Value, "REDUNDANT_TAIL", feedback)
            : ReviewIssue.ForUnresolvedSentence("REDUNDANT_TAIL", feedback);

        return new RedundantTailResult(true, feedback, tail, [issue]);
    }

    /// <summary>Drops the flagged tail sentence, returning the read ending on its prior fact.</summary>
    public static string Repair(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        var result = Detect(script);
        if (!result.HasRedundantTail)
            return script;

        int idx = script.LastIndexOf(result.Tail, StringComparison.Ordinal);
        return idx < 0 ? script : script[..idx].TrimEnd();
    }

    private static string Truncate(string s) => s.Length > 60 ? s[..60] + "…" : s;
}

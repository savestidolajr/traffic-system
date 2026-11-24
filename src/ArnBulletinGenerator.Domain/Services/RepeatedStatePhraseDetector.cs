using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic anti-repetition gate: flags a bulletin that leans on the SAME content word
///     three or more times (e.g. "roadworks ... roadworks ... roadworks", or "minor delays" voiced
///     three ways). General by design — it counts whichever word is over-repeated, so a new
///     repetition pattern needs no code change (unlike a per-phrase blocklist). Road-type nouns,
///     directions, and function words recur legitimately across items and are ignored. Prompt
///     guidance alone proved insufficient — the same listy repetition kept resurfacing on whichever
///     word the model defaulted to — so the count lives in code, the same gate shape as the sibling
///     detectors, and it drives a refine (never a hard fail) when it fires.
/// </summary>
public static partial class RepeatedStatePhraseDetector
{
    private const int RepeatThreshold = 3;
    // Lowercase-INITIAL words only: condition/state words are lowercase mid-sentence, while road and
    // place-name fragments ("South", "Harbour", "Gold") are capitalised — matching lowercase-initial
    // excludes every proper-noun fragment by case, so a repeated road-name word can never false-fire.
    [GeneratedRegex(@"\b[a-z][a-z]{3,}\b")]
    private static partial Regex Word();

    // Words that recur legitimately and must NOT count as repetition: function words, directions,
    // road-type nouns (three different freeways are not a repeated word), and the recurring nouns of
    // the weather/fuel tail.
    private static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "with", "that", "this", "also", "there", "from", "then", "while", "have", "has",
        "its", "are", "about", "both", "your", "their", "into", "over", "only", "just", "been", "they",
        "were", "will", "onto", "some", "more",
        "citybound", "outbound", "inbound", "northbound", "southbound", "eastbound", "westbound",
        "between", "near", "approaching",
        "freeway", "highway", "road", "roads", "street", "drive", "motorway", "avenue", "bridge",
        "tunnel", "parade", "boulevard", "court", "terrace", "place", "crescent", "bypass", "expressway",
        "circuit", "close", "parkway", "lane", "lanes",
        "degrees", "cheapest", "unleaded", "diesel", "litre", "dollar", "today", "minutes", "minute"
    };
    /// <summary>
    ///     Shared lexical policy for phrase-level repetition checks. Sentence-shape detection uses the
    ///     same function-word, direction and road-type vocabulary so the two repetition gates cannot
    ///     drift into contradictory false positives.
    /// </summary>
    internal static bool IsIgnoredWord(string word) => Ignore.Contains(word);


    public readonly record struct RepeatedStatePhraseResult(
        bool HasExcessRepetition,
        string Feedback,
        string Word,
        int Count,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public RepeatedStatePhraseResult(bool hasExcessRepetition, string feedback, string word, int count)
            : this(hasExcessRepetition, feedback, word, count, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static RepeatedStatePhraseResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new RepeatedStatePhraseResult(false, string.Empty, string.Empty, 0, []);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Word().Matches(script))
        {
            string w = m.Value;
            if (Ignore.Contains(w))
                continue;
            counts[w] = counts.TryGetValue(w, out int v) ? v + 1 : 1;
        }

        var worst = counts
            .Where(kv => kv.Value >= RepeatThreshold)
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (Word: kv.Key, Count: kv.Value))
            .FirstOrDefault();

        if (worst.Count < RepeatThreshold)
            return new RepeatedStatePhraseResult(false, string.Empty, string.Empty, 0, []);

        string feedback =
            $"REPETITION: the word \"{worst.Word}\" is used {worst.Count} times — the listy, machine tell. " +
            "Vary it: say the condition a different way for each item (reach for the road-feel palette), or " +
            "if several roads genuinely share that state, GROUP them into one sentence instead of repeating the word.";
        var issue = ReviewIssue.ForScript("REPEATED_STATE_PHRASE", feedback);

        return new RepeatedStatePhraseResult(
            true,
            feedback,
            worst.Word,
            worst.Count,
            [issue]);
    }
}

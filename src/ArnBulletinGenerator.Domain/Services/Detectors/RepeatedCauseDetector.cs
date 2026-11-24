using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services.Detectors;

/// <summary>
///     Deterministic gate for repeated explicit cause/event clauses across a read. A sentence such as
///     "Gold Coast Highway is closed for the Gold Coast Marathon" is individually grounded, but four
///     such sentences restate one shared cause and sound like a database read-out. Prompt guidance
///     alone proved insufficient on the Gold Coast fixture (2026-08-23), so the cross-sentence shape
///     is made enforceable here. This is a script-scoped issue: no single sentence is wrong, and the
///     repair must group the affected roads while naming the cause once.
/// </summary>
public static partial class RepeatedCauseDetector
{
    [GeneratedRegex(
        @"\b(?:for|because of|due to|caused by)\s+(?:the\s+)?(?<cause>[a-z0-9][a-z0-9'&/ -]*?[a-z0-9])(?=\s*[.!?;,]|$)",
        RegexOptions.IgnoreCase)]
    private static partial Regex CauseClause();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public readonly record struct RepeatedCauseResult(
        bool HasRepeatedCause,
        string Feedback,
        string Cause,
        int Count,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public RepeatedCauseResult(bool hasRepeatedCause, string feedback, string cause, int count)
            : this(hasRepeatedCause, feedback, cause, count, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static RepeatedCauseResult Detect(string script, BroadcastWithTraffic context)
    {
        _ = context;
        if (string.IsNullOrWhiteSpace(script))
            return new RepeatedCauseResult(false, string.Empty, string.Empty, 0, []);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string sentence in ContentSourceSignals.SplitSentences(script))
        {
            // Count a clause once per sentence. A sentence that repeats its own cause is a different
            // shape; this gate is specifically for the cross-item restatement that grouping fixes.
            var causesInSentence = CauseClause().Matches(sentence)
                .Select(match => Normalise(match.Groups["cause"].Value))
                .Where(cause => cause.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string cause in causesInSentence)
                counts[cause] = counts.TryGetValue(cause, out int count) ? count + 1 : 1;
        }

        var repeated = counts
            .Where(pair => pair.Value >= 2)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (repeated.Value < 2)
            return new RepeatedCauseResult(false, string.Empty, string.Empty, 0, []);

        string feedback =
            $"SHARED CAUSE/EVENT: \"{repeated.Key}\" is repeated {repeated.Value} times across sentences. "
            + "Group the affected roads or items into one sentence and state the supplied cause or event once; "
            + "do not infer a cause for items that do not share one.";
        var issue = ReviewIssue.ForScript("REPEATED_SHARED_CAUSE", feedback);
        return new RepeatedCauseResult(true, feedback, repeated.Key, repeated.Value, [issue]);
    }

    private static string Normalise(string phrase) =>
        Whitespace().Replace(phrase, " ").Trim().Trim(',', ';', ':').ToLowerInvariant();
}

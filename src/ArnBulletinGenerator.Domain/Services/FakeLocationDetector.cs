using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic "incident type recited as a location" gate. When a corridor's condensed
///     bullet carries no Location line at all (observed live: Newcastle's "Mitchell Street
///     outbound — road closure, road closed" — no anchor in the data), the writer is still under
///     pressure from the "pin every item to its spot" style rule and, with nothing real to pin to,
///     invents a pseudo-location out of the incident TYPE itself — "closed at a road closure",
///     "shut near the road closure point" (reproduced 3/3 live generations for the identical
///     no-anchor item). "Road closure" is WHAT happened, never WHERE — this is a fabricated
///     location, not a paraphrase. Prompt-only bans proved unreliable for every sibling class
///     this session (BFN-006/BFN-019 both regressed live the day the prose rule was strengthened —
///     see decisions.md 2026-07-01), so this counts the pattern in CODE, the same gate shape as
///     <see cref="RollCallDetector" />/<see cref="DuplicateAnchorDetector" />/
///     <see cref="IncidentTypeStackDetector" />/<see cref="RepeatedPrepositionDetector" />/
///     <see cref="ImpactStackDetector" />.
/// </summary>
public static partial class FakeLocationDetector
{
    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    // Incident-type nouns a writer might recite as if they were a place — the same vocabulary
    // class IncidentTypeStackDetector guards against being stacked, now guarded against being
    // used as a fake spatial anchor instead.
    private static readonly string[] TypeNouns =
    [
        "closure", "closures", "road closure", "construction", "roadworks", "congestion",
        "accident", "accidents", "crash", "breakdown", "breakdowns", "hazard", "hazards",
        "restriction", "restrictions", "reduction", "reductions", "detour", "detours",
        "upgrade", "upgrades", "planned event"
    ];

    // "at/near/on/in [a/the] TYPE-NOUN [point/location/spot]" — a prepositional phrase whose
    // "location" is actually the incident type restated, optionally with a generic trailing noun
    // that adds no real place information ("...road closure point").
    // Second alternation: possessive pseudo-anchors — "near its listed location", "at its main
    // run" (observed live, fresh-eval Sydney ×2: the writer, pressured to pin a no-anchor item,
    // invents a self-referential "place" that locates nothing). "its" only — a road is "it";
    // "their" would false-positive on people ("drivers on their way into the city").
    // Dynamic pattern (interpolates TypeNouns) — cannot be [GeneratedRegex]; built once per process.
    private static readonly Regex FakeLocationPatternInstance = new(
        $@"\b(at|near|on|in)\s+(a\s+|the\s+)?({string.Join("|", TypeNouns.Select(Regex.Escape))})\b" +
        @"(\s+(point|location|spot))?" +
        @"|\b(at|near|on|in)\s+its\s+\w+(\s+\w+)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static Regex FakeLocationPattern() => FakeLocationPatternInstance;

    public readonly record struct FakeLocationResult(
        bool HasFakeLocation,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public FakeLocationResult(bool hasFakeLocation, string feedback)
            : this(hasFakeLocation, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static FakeLocationResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new FakeLocationResult(false, string.Empty, []);

        var sentences = ScriptSentenceSplitter.Split(script);
        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            var match = FakeLocationPattern().Match(sentence);
            if (!match.Success)
                continue;

            string feedback =
                $"FAKE LOCATION: the sentence (\"{sentence.Trim()}\") recites the incident TYPE as if it " +
                $"were a place (\"{match.Value.Trim()}\") — that is WHAT happened, never WHERE. When an item " +
                "has no real Location anchor in the data, state the road, direction, and condition plainly " +
                "and move on; never invent a spot out of the incident type itself.";
            int order = i + 1;
            var issue = ReviewIssue.ForSentence(order, "FAKE_LOCATION", feedback);
            return new FakeLocationResult(true, feedback, [issue]);
        }

        // Fallback: If pattern matched full script but wasn't isolated by splitter
        var scriptMatch = FakeLocationPattern().Match(script);
        if (scriptMatch.Success)
        {
            string feedback =
                $"FAKE LOCATION: the script recites the incident TYPE as if it " +
                $"were a place (\"{scriptMatch.Value.Trim()}\") — that is WHAT happened, never WHERE. When an item " +
                "has no real Location anchor in the data, state the road, direction, and condition plainly " +
                "and move on; never invent a spot out of the incident type itself.";
            var issue = ReviewIssue.ForUnresolvedSentence("FAKE_LOCATION", feedback);
            return new FakeLocationResult(true, feedback, [issue]);
        }

        return new FakeLocationResult(false, string.Empty, []);
    }

    /// <summary>
    ///     Mechanical last-resort fix: strips the fake-location phrase verbatim, leaving the
    ///     sentence's real facts intact. Safe because the phrase carries zero location information
    ///     — it only restates the incident type, which the sentence already states elsewhere.
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
            var match = FakeLocationPattern().Match(sentence);
            if (!match.Success)
                continue;

            string before = sentence[..match.Index].TrimEnd();
            string after = sentence[(match.Index + match.Length)..];
            sentences[s] = (before + after).Replace("  ", " ");
            changed = true;
        }

        return changed ? string.Join(" ", sentences) : script;
    }
}

using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic gate for vague, unsourced filler that names no specific road — the padding a
///     writer (or an expansion refine) reaches for under length pressure: "a couple more spots have
///     minor delays", "a few hold-ups around town", "the rest is clear". The reviewer's VAGUENESS
///     check catches these, but only via an LLM round-trip; counting the known vague frames in CODE
///     lets the polish gate revert a polish that introduces one and lets the refine ratchet penalise
///     a candidate that pads with one. Deliberately a TIGHT blocklist of vague-quantifier / no-road
///     frames, so a legitimate grouped list of NAMED roads ("X, Y and Z are slow") never trips it.
/// </summary>
public static partial class VaguenessDetector
{
    private static readonly Regex[] Patterns =
    [
        // Inherently vague / unnamed nouns: "a couple more spots", "a few places", "a handful of pockets"
        VaguenessPattern1(),
        // ADDITIONAL unnamed items only ("a few MORE hold-ups", "some OTHER delays"); a bare
        // "a few delays"/"a few hold-ups" attached to NAMED roads is fine and NOT flagged.
        VaguenessPattern2(),
        // "hold-ups around town", "delays elsewhere", "slow going in the suburbs"
        VaguenessPattern3(),
        // "the rest is clear", "everywhere else is moving"
        VaguenessPattern4(),
        // "a few more roads are slow" (unnamed additional roads)
        VaguenessPattern5(),
    ];

    [GeneratedRegex(@"\b(a (couple|few|handful)|some|several)( of)?( more| other)? (spots|places|areas|pockets|others)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex VaguenessPattern1();

    [GeneratedRegex(@"\b(a (couple|few|handful)|some|several)( of)? (more|other) (hold-?ups|delays|roads?|closures|works|jams)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex VaguenessPattern2();

    [GeneratedRegex(@"\b(hold-?ups|delays|congestion|slow going|slow-?downs|traffic) (around town|about town|elsewhere|in the suburbs|across town|here and there)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex VaguenessPattern3();

    [GeneratedRegex(@"\b(the rest|everywhere else|the rest of (them|it|the roads?)) (is|are|'?s) (clear|fine|moving|flowing|good|ok|okay)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex VaguenessPattern4();

    [GeneratedRegex(@"\b(a few|some|several|a couple of) (more )?(other )?roads? (are|is|'?s)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex VaguenessPattern5();

    public readonly record struct VaguenessResult(
        bool HasVagueness,
        string Feedback,
        string Match,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public VaguenessResult(bool hasVagueness, string feedback, string match)
            : this(hasVagueness, feedback, match, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static VaguenessResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new VaguenessResult(false, string.Empty, string.Empty, []);

        var sentences = ScriptSentenceSplitter.Split(script);
        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            foreach (var p in Patterns)
            {
                var m = p.Match(sentence);
                if (m.Success)
                {
                    string feedback =
                        $"VAGUENESS: \"{m.Value}\" names no specific road — a listener can't use it, and it isn't a sourced " +
                        "fact. Either name the actual road(s) and their condition, or drop the phrase; a shorter read is fine.";
                    int order = i + 1;
                    var issue = ReviewIssue.ForSentence(order, "VAGUENESS", feedback);
                    return new VaguenessResult(true, feedback, m.Value, [issue]);
                }
            }
        }

        // Fallback: If matched whole script but wasn't isolated by splitter
        foreach (var p in Patterns)
        {
            var m = p.Match(script);
            if (m.Success)
            {
                string feedback =
                    $"VAGUENESS: \"{m.Value}\" names no specific road — a listener can't use it, and it isn't a sourced " +
                    "fact. Either name the actual road(s) and their condition, or drop the phrase; a shorter read is fine.";
                var issue = ReviewIssue.ForUnresolvedSentence("VAGUENESS", feedback);
                return new VaguenessResult(true, feedback, m.Value, [issue]);
            }
        }

        return new VaguenessResult(false, string.Empty, string.Empty, []);
    }
}

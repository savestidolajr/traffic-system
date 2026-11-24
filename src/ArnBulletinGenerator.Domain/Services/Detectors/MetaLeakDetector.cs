using ArnBulletinGenerator.Domain.Models;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;
/// <summary>
///     Deterministic "meta-reference leak" gate. The prompt tells the writer never to say the
///     word "data" (a presenter never references their own source material on air) but a prompt-
///     only ban proved unreliable for the same reason <see cref="RollCallDetector" /> and
///     <see cref="DuplicateAnchorDetector" /> exist — soft instructions get missed under model
///     sampling variance (observed live: "...across the other clear roads in the data.", and later
///     "no other problems are logged on the listed roads" — database-structure language describing
///     the SOURCE, not the road, even without the literal word "data"). This counts the vocabulary
///     in CODE so a re-draft is forced rather than shipping the leak.
/// </summary>
public static partial class MetaLeakDetector
{
    [GeneratedRegex(@"\bdata\b", RegexOptions.IgnoreCase)]
    private static partial Regex DataWord();

    // Multi-word database-structure phrases — deliberately NOT single words like "logged" or
    // "listed" alone, which are ordinary English outside this context ("logged on", "listed for
    // sale"); the phrase as a whole is what reads as a presenter referencing their own source.
    // "listed location" and "cut/drop that line" are refine-instruction echoes observed on the
    // fresh-live eval (Sydney: "at a standstill near its listed location"; Brisbane: "is at a
    // standstill, so cut that line" — the writer voicing the reviewer's editing instruction).
    // "the source" / "not stated/provided/given" are absence-narration echoes of the anchoring
    // rule itself (observed: "at the standstill point not stated in the source" — the writer
    // voicing that it HAS no anchor instead of simply omitting one).
    //
    // The PASSIVE "reported" is the same leak in its politest form: the presenter narrating that a
    // fact was supplied to them rather than simply saying it. Measured across the 2026-08-25
    // end-to-end reads in four grammatical dresses, only one of which an auxiliary-anchored pattern
    // caught — "traffic is reported clear", "the previously reported road closure", the bare
    // participle "South Road is a touch slower, with roadworks reported", and the active
    // "Cleveland Street are all reporting clear roads", which additionally has the ROAD doing the
    // reporting. So the verb is matched in every form.
    //
    // The NOUN survives, and must: "there are reports of a crash" is how a presenter honestly
    // attributes something genuinely unconfirmed, and it is the only honest way to say it.
    [GeneratedRegex(
    @"\b(are|is|were|was)\s+logged\b|\bthe\s+listed\s+roads?\b|\bon\s+record\b|\bin\s+the\s+system\b" +
    @"|\blisted\s+location\b|\b(cut|drop)\s+(that|this|the)\s+line\b" +
    @"|\bthe\s+source\b|\bnot\s+(stated|provided|given|specified)\b" +
    @"|\breport(ed|ing)\b",
    RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MetaPhrase();

    // Internal system jargon a presenter never voices — our own labels for the pipeline's data
    // structures (observed live, Newcastle: "the rest of the run through Newcastle's monitored
    // routes is clear ... an easy commute on those corridors"). "corridor" and "monitored route"
    // are never natural on-air words, unlike the allowed natural "a good run" / "running well".
    [GeneratedRegex(
        @"\bcorridors?\b|\bmonitored\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InternalTerm();

    public readonly record struct MetaLeakResult(
        bool HasMetaLeak,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public MetaLeakResult(bool hasMetaLeak, string feedback)
            : this(hasMetaLeak, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    // ", with no delays reported" / ", with no further issues reported" — a trailing clause whose
    // only content is the absence of a fact. Anchored to a comma so it can never eat real content.
    [GeneratedRegex(
        @",\s*(?:and\s+)?with\s+no\s+(?:further\s+)?[\w\s]{0,24}?reported\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EmptyProvenanceClause();

    // "the [previously] reported road closure" -> "the road closure".
    [GeneratedRegex(@"(?<=\b(?:the|a|an)\s)(?:previously\s+)?reported\s+", RegexOptions.IgnoreCase)]
    private static partial Regex ReportedAdjective();

    // "traffic is reported clear" -> "traffic is clear".
    [GeneratedRegex(@"\b(is|are|was|were)\s+reported\s+(clear|closed|open)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReportedCopula();

    public static MetaLeakResult Detect(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new MetaLeakResult(false, string.Empty, []);

        var sentences = ScriptSentenceSplitter.Split(script);

        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            int order = i + 1;

            if (DataWord().IsMatch(sentence))
            {
                string feedback =
                    "META-LEAK: the script uses the word \"data\" — a presenter never references their own source " +
                    "material on air. Rewrite that sentence to state the fact plainly without naming its source " +
                    "(\"the rest of the run's clear\", not \"the rest of the clear roads in the data\").";
                var issue = ReviewIssue.ForSentence(order, "META_LEAK", feedback);
                return new MetaLeakResult(true, feedback, [issue]);
            }

            var metaMatch = MetaPhrase().Match(sentence);
            if (metaMatch.Success)
            {
                string feedback =
                    $"META-LEAK: the script uses database-structure language (\"{metaMatch.Value.Trim()}\") — a " +
                    "presenter never references their own source material on air, even without the word \"data\" " +
                    "itself. Rewrite to state the plain fact (\"the rest of Newcastle's roads are clear\", not " +
                    "\"no other problems are logged on the listed roads\").";
                var issue = ReviewIssue.ForSentence(order, "META_LEAK", feedback);
                return new MetaLeakResult(true, feedback, [issue]);
            }

            var internalMatch = InternalTerm().Match(sentence);
            if (internalMatch.Success)
            {
                string feedback =
                    $"META-LEAK: the script uses internal jargon (\"{internalMatch.Value.Trim()}\") — \"corridor\" " +
                    "and \"monitored route\" are our own labels for the data, never words a presenter says on air. " +
                    "Name the roads plainly instead (\"the rest of the run's clear\", \"the main approaches into the " +
                    "city are moving well\").";
                var issue = ReviewIssue.ForSentence(order, "META_LEAK", feedback);
                return new MetaLeakResult(true, feedback, [issue]);
            }
        }

        // Fallback: If for any reason full script matched but sentence split didn't isolate (e.g. across cue boundary),
        // report unresolved order (which widens to script refine by design).
        if (DataWord().IsMatch(script))
        {
            string feedback =
                "META-LEAK: the script uses the word \"data\" — a presenter never references their own source " +
                "material on air. Rewrite that sentence to state the fact plainly without naming its source " +
                "(\"the rest of the run's clear\", not \"the rest of the clear roads in the data\").";
            var issue = ReviewIssue.ForUnresolvedSentence("META_LEAK", feedback);
            return new MetaLeakResult(true, feedback, [issue]);
        }

        var scriptMetaMatch = MetaPhrase().Match(script);
        if (scriptMetaMatch.Success)
        {
            string feedback =
                $"META-LEAK: the script uses database-structure language (\"{scriptMetaMatch.Value.Trim()}\") — a " +
                "presenter never references their own source material on air, even without the word \"data\" " +
                "itself. Rewrite to state the plain fact (\"the rest of Newcastle's roads are clear\", not " +
                "\"no other problems are logged on the listed roads\").";
            var issue = ReviewIssue.ForUnresolvedSentence("META_LEAK", feedback);
            return new MetaLeakResult(true, feedback, [issue]);
        }

        var scriptInternalMatch = InternalTerm().Match(script);
        if (scriptInternalMatch.Success)
        {
            string feedback =
                $"META-LEAK: the script uses internal jargon (\"{scriptInternalMatch.Value.Trim()}\") — \"corridor\" " +
                "and \"monitored route\" are our own labels for the data, never words a presenter says on air. " +
                "Name the roads plainly instead (\"the rest of the run's clear\", \"the main approaches into the " +
                "city are moving well\").";
            var issue = ReviewIssue.ForUnresolvedSentence("META_LEAK", feedback);
            return new MetaLeakResult(true, feedback, [issue]);
        }

        return new MetaLeakResult(false, string.Empty, []);
    }

    /// <summary>
    ///     Mechanically repairs the classes with a SAFE fix, and leaves the rest to the re-draft the
    ///     gate already forces.
    ///     <para>
    ///         INTERNAL TERMS — a lexical substitution swapping our jargon for the natural on-air
    ///         word ("corridors" → "roads", "monitored route" → "route"), preserving the sentence.
    ///         This is the class the forced word-target expansion pass reintroduces while padding a
    ///         short read (observed live, Newcastle), after the main review loop already cleared it.
    ///     </para>
    ///     <para>
    ///         PROVENANCE FRAMING — added 2026-08-25, when "reported" reached air on 3 of 10 station
    ///         panel reads despite this gate blocking on it. Blocking only buys a re-draft, and the
    ///         read ships anyway once attempts run out, so a blocking-only gate on a fixable defect
    ///         is the same "detect, log, ship" path that lets a staccato run air. Three shapes can
    ///         be swept without breaking a sentence or dropping a fact: an empty trailing clause
    ///         ("...flowing freely on Sydney Gateway, with no delays reported."), the adjective
    ///         ("the reported road closure has cleared"), and the copula ("traffic is reported
    ///         clear"). "A clear run is being reported on Hallet Place" is NOT repaired — fixing it
    ///         needs the sentence rebuilt, so it stays blocking.
    ///     </para>
    ///     <para>
    ///         The "data" / database-phrase classes have NO safe substitution (deleting them
    ///         collapses the sentence) and are left to the LLM loop, which catches them reliably;
    ///         <see cref="Detect" /> still flags them for the shipped-warning path.
    ///     </para>
    /// </summary>
    public static string Repair(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        // "monitored" is internal framing in every form ("monitored routes/run/roads") — drop the
        // word, keeping the noun and its sentence-position case ("Monitored routes" → "Routes").
        script = Regex.Replace(script, @"\bmonitored\s+(\p{L}+)", m =>
            char.IsUpper(m.Value[0])
                ? char.ToUpperInvariant(m.Groups[1].Value[0]) + m.Groups[1].Value[1..]
                : m.Groups[1].Value,
            RegexOptions.IgnoreCase);
        script = ReplacePreservingCase(script, @"\bcorridors\b", "roads");
        script = ReplacePreservingCase(script, @"\bcorridor\b", "road");

        script = EmptyProvenanceClause().Replace(script, "");
        script = ReportedAdjective().Replace(script, "");
        script = ReportedCopula().Replace(script, "$1 $2");
        return script;
    }

    /// <summary>Regex replace that keeps the first letter's case (sentence-start "Corridors" → "Roads").</summary>
    private static string ReplacePreservingCase(string input, string pattern, string replacement) =>
        Regex.Replace(input, pattern, m =>
            char.IsUpper(m.Value[0])
                ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
                : replacement,
            RegexOptions.IgnoreCase);
}

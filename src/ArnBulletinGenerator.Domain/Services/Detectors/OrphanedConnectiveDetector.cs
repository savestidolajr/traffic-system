using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic orphaned-back-reference gate: an ADDITIVE connective ("also", "as well",
///     a leading "Meanwhile,"/"Elsewhere,") in the bulletin's FIRST traffic claim, where there is
///     nothing prior for it to add to.
///     <para>
///         Observed live on dev job 54befeec (2026-08-24, Cairns): the read opened "Checking out
///         the roads... Roadworks are ALSO slowing the Bruce Highway at Edmonton and Gordonvale."
///         The draft was correct — Captain Cook Highway roadworks were sentence one and the Bruce
///         Highway sentence genuinely added to them. Review then proved the Captain Cook claim
///         unsupported ("major delays" against supplied moderate), its sentence-scoped refine cited
///         an unsupplied evidence id, and the loop DROPPED the sentence rather than ship a defect.
///         Correct call — but the drop left the survivor pointing back at a sentence that no longer
///         existed. ARN heard a bulletin whose first words referred to something never said.
///     </para>
///     <para>
///         This is the class of defect a sentence-level gate cannot see: every surviving sentence
///         is individually clean and individually grounded, and only their ORDER makes one of them
///         wrong. It is therefore checked on the assembled read, after any mutation that removes a
///         sentence — the same shape as <see cref="SectionOrderDetector" />, which likewise reasons
///         about position rather than content.
///     </para>
///     <para>
///         Repairable, unlike <see cref="ConnectorFrequencyDetector" />'s contrast connectors: each
///         shape below is a pure DELETION that leaves grammatical prose ("Roadworks are also
///         slowing the Bruce Highway" -> "Roadworks are slowing the Bruce Highway"). Nothing is
///         invented and no claim changes, so this never needs an LLM round-trip.
///     </para>
/// </summary>
public static partial class OrphanedConnectiveDetector
{
    /// <summary>" also" / " as well" — additive, and a pure deletion in every position ARN writes.</summary>
    [GeneratedRegex(@"\s+\balso\b", RegexOptions.IgnoreCase)]
    private static partial Regex Also();

    [GeneratedRegex(@"\s+\bas well\b", RegexOptions.IgnoreCase)]
    private static partial Regex AsWell();

    /// <summary>
    ///     A leading "Meanwhile," / "Elsewhere," aside. Only ever stripped from the FIRST claim, so
    ///     the following word is sentence-initial and needs its capital restored.
    /// </summary>
    [GeneratedRegex(@"^\s*(?:meanwhile|elsewhere)\s*,\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingAside();

    public readonly record struct OrphanedConnectiveResult(
        bool HasOrphanedConnective,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public OrphanedConnectiveResult(bool hasOrphanedConnective, string feedback)
            : this(hasOrphanedConnective, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static OrphanedConnectiveResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new OrphanedConnectiveResult(false, string.Empty, []);

        var sentences = ContentSourceSignals.SplitSentences(script);
        int first = FirstClaimIndex(sentences, RoadNames(context));
        if (first < 0)
            return new OrphanedConnectiveResult(false, string.Empty, []);

        string sentence = sentences[first];
        string? connective = FirstConnective(sentence);
        if (connective is null)
            return new OrphanedConnectiveResult(false, string.Empty, []);

        string feedback =
            $"ORPHANED BACK-REFERENCE: the first thing the bulletin says about the roads uses "
            + $"\"{connective}\" (\"{sentence.Trim()}\"), but nothing has been said yet for it to add "
            + "to. An additive connective earns its place only AFTER a prior item. Open the read with "
            + "the claim stated plainly and save the connective for a later sentence.";
        var issue = ReviewIssue.ForSentence(first + 1, "ORPHANED_CONNECTIVE", feedback);

        return new OrphanedConnectiveResult(true, feedback, [issue]);
    }

    /// <summary>
    ///     Deletes the orphaned connective from the first traffic claim, leaving every other
    ///     sentence — and every fact — untouched. Returns the input unchanged when nothing is
    ///     orphaned.
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        var sentences = ContentSourceSignals.SplitSentences(script);
        int first = FirstClaimIndex(sentences, RoadNames(context));
        if (first < 0)
            return script;

        string original = sentences[first];
        string repaired = StripConnective(original);
        if (string.Equals(original, repaired, StringComparison.Ordinal))
            return script;

        // Rebuild by replacing that one sentence in the original text, so spacing and every other
        // sentence survive byte-for-byte — the splitter is not a round-trip.
        int at = script.IndexOf(original, StringComparison.Ordinal);

        return at < 0
            ? script
            : script[..at] + repaired + script[(at + original.Length)..];
    }

    private static string StripConnective(string sentence)
    {
        var aside = LeadingAside().Match(sentence);
        if (aside.Success)
        {
            string rest = sentence[aside.Length..];

            return rest.Length == 0 ? sentence : char.ToUpperInvariant(rest[0]) + rest[1..];
        }

        var also = Also().Match(sentence);
        if (also.Success)
            return sentence.Remove(also.Index, also.Length);

        var asWell = AsWell().Match(sentence);

        return asWell.Success ? sentence.Remove(asWell.Index, asWell.Length) : sentence;
    }

    private static string? FirstConnective(string sentence)
    {
        if (LeadingAside().IsMatch(sentence))
            return LeadingAside().Match(sentence).Value.Trim().TrimEnd(',');
        if (Also().IsMatch(sentence))
            return "also";

        return AsWell().IsMatch(sentence) ? "as well" : null;
    }

    /// <summary>
    ///     Index of the first sentence that makes a TRAFFIC claim — the first one naming a road the
    ///     market's own data carries. An LLM-written opener ("Checking out the roads...") names no
    ///     road, and weather/fuel lines are not traffic claims, so both are skipped without needing
    ///     to be recognised as such. -1 when the read makes no road claim at all.
    /// </summary>
    private static int FirstClaimIndex(IReadOnlyList<string> sentences, IReadOnlyList<string> roadNames)
    {
        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            if (ContentSourceSignals.IsWeatherLine(sentence) || ContentSourceSignals.IsFuelLine(sentence))
                continue;
            if (NamesKnownRoad(sentence, roadNames))
                return i;
        }

        return -1;
    }

    private static IReadOnlyList<string> RoadNames(BroadcastWithTraffic context) =>
        [.. context.Corridors.Select(c => c.RoadName).Where(n => !string.IsNullOrWhiteSpace(n))];

    private static bool NamesKnownRoad(string sentence, IReadOnlyList<string> roadNames)
    {
        foreach (var road in roadNames)
        {
            if (HasWholeWord(sentence, road))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Whole-phrase, case-insensitive presence check in the repo's whole-word style, matching
    ///     <see cref="SectionOrderDetector" />: the road name only counts when voiced as a word
    ///     ("M4" matches "the M4" but never "M40").
    /// </summary>
    private static bool HasWholeWord(string text, string name)
    {
        int from = 0;
        while (from <= text.Length - name.Length)
        {
            int at = text.IndexOf(name, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;

            bool leftOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            int after = at + name.Length;
            bool rightOk = after >= text.Length || !char.IsLetterOrDigit(text[after]);
            if (leftOk && rightOk)
                return true;

            from = at + 1;
        }

        return false;
    }
}

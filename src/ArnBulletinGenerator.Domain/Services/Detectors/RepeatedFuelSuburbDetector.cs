using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic "repeated fuel suburb" gate. A shared cheapest-fuel suburb must be voiced once
///     in the fuel line, while the prompt remains free to rotate the sentence frame. The old hard
///     "say there too" hint did prevent the repeat but over-steered every bulletin into one rigid
///     frame; after that hint was softened and four rotatable frames were added, the live pilot still
///     shipped "Cheapest unleaded is a dollar ninety-eight at Manoora, while diesel is two dollars
///     forty in Manoora" on Star 102.7 Cairns (observed 2026-08-24). Prompt-only no-repeat guidance
///     proved unreliable under sampling, so this narrow repair removes only the later location phrase.
///     <para>
///         The check is scoped to sentences that <see cref="ContentSourceSignals.IsFuelLine" />
///         identifies as fuel. A suburb may legitimately occur in a traffic item and the fuel item
///         in the same bulletin; traffic text is never touched here. Location phrases are read from
///         the fuel sentence itself so this detector also remains assertable by the text-only
///         subpar-read ratchet.
///     </para>
/// </summary>
public static partial class RepeatedFuelSuburbDetector
{
    [GeneratedRegex(@"\b(?i:at|in)\s+(?<suburb>[A-Z][\p{L}\p{M}\d'’\-]*(?:\s+[A-Z][\p{L}\p{M}\d'’\-]*)*)", RegexOptions.Compiled)]
    private static partial Regex LocationPhrase();
    [GeneratedRegex(@"\s+([,.;:!?])", RegexOptions.Compiled)]
    private static partial Regex WhitespaceBeforePunctuation();

    [GeneratedRegex(@",\s*,+", RegexOptions.Compiled)]
    private static partial Regex RepeatedComma();

    [GeneratedRegex(@",\s*(?=[.!?])", RegexOptions.Compiled)]
    private static partial Regex OrphanedComma();

    [GeneratedRegex(@"(?:,\s*)?\b(?:and|or|while|but)\b\s*(?=[.!?])", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DanglingConjunction();

    [GeneratedRegex(@"[ \t]{2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleSpaces();

    private readonly record struct LocationMention(string Suburb, int Start, int End);

    public readonly record struct RepeatedFuelSuburbResult(
        bool HasRepeatedFuelSuburb,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public RepeatedFuelSuburbResult(bool hasRepeatedFuelSuburb, string feedback)
            : this(hasRepeatedFuelSuburb, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static RepeatedFuelSuburbResult Detect(string script, BroadcastWithTraffic _)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new RepeatedFuelSuburbResult(false, string.Empty, []);

        var sentences = ContentSourceSignals.SplitSentences(script);
        for (int i = 0; i < sentences.Count; i++)
        {
            string sentence = sentences[i];
            if (!ContentSourceSignals.IsFuelLine(sentence))
                continue;

            var repeated = FindLocationMentions(sentence)
                .GroupBy(mention => mention.Suburb, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() >= 2);
            if (repeated is null)
                continue;

            string suburb = repeated.Key;
            int count = repeated.Count();
            string feedback =
                $"REPEATED FUEL SUBURB: \"{suburb}\" is named {count} times in one fuel sentence " +
                $"(\"{sentence}\"). Name the shared cheapest-price suburb once, while retaining the " +
                "location and varying the fuel sentence frame naturally.";
            var issue = ReviewIssue.ForSentence(i + 1, "REPEATED_FUEL_SUBURB", feedback);
            return new RepeatedFuelSuburbResult(true, feedback, [issue]);
        }

        return new RepeatedFuelSuburbResult(false, string.Empty, []);
    }

    /// <summary>
    ///     Mechanical last-resort fix: in each fuel sentence, retain the FIRST location phrase for
    ///     each repeated suburb and remove every later "at/in &lt;Suburb&gt;" phrase. This preserves one
    ///     spoken location, handles both observed prepositions, and leaves traffic sentences and
    ///     fuel lines with different suburbs untouched. The cleanup only removes spacing, orphaned
    ///     punctuation, or a conjunction stranded by the deleted phrase.
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic _)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        var sentences = ContentSourceSignals.SplitSentences(script).ToArray();
        bool changed = false;

        for (int i = 0; i < sentences.Length; i++)
        {
            if (!ContentSourceSignals.IsFuelLine(sentences[i]))
                continue;

            var mentions = FindLocationMentions(sentences[i]);
            var removals = mentions
                .GroupBy(mention => mention.Suburb, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() >= 2)
                .SelectMany(group => group.Skip(1))
                .OrderByDescending(mention => mention.Start)
                .ToList();
            if (removals.Count == 0)
                continue;

            string sentence = sentences[i];
            foreach (var location in removals)
                sentence = sentence.Remove(location.Start, location.End - location.Start);

            sentences[i] = CleanSentence(sentence);
            changed = true;
        }

        return changed ? string.Join(" ", sentences) : script;
    }

    private static List<LocationMention> FindLocationMentions(string sentence) =>
        LocationPhrase().Matches(sentence)
            .Select(match => new LocationMention(
                match.Groups["suburb"].Value,
                match.Index,
                match.Index + match.Length))
            .ToList();



    private static string CleanSentence(string sentence)
    {
        string cleaned = MultipleSpaces().Replace(sentence.Trim(), " ");
        cleaned = WhitespaceBeforePunctuation().Replace(cleaned, "$1");
        cleaned = RepeatedComma().Replace(cleaned, ",");
        cleaned = OrphanedComma().Replace(cleaned, string.Empty);
        cleaned = DanglingConjunction().Replace(cleaned, string.Empty);
        return cleaned.Trim();
    }
}

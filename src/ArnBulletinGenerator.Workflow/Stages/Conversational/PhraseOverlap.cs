using System.Text.RegularExpressions;
using ArnBulletinGenerator.Workflow.Model;

namespace ArnBulletinGenerator.Workflow.Stages.Conversational;

/// <summary>
///     Measures repeated phrasing between two reads without treating the names that make a traffic
///     bulletin useful as style repetition. The score is the fraction of eligible four-word windows
///     in the new read that occur anywhere in the previous read.
///     <para>
///         A window containing a road, suburb or other evidence anchor is excluded: consecutive reads
///         must repeat those names to remain intelligible, while they should not repeat the connective
///         idioms around them. The normaliser folds digits to <c>number</c>, so <c>M4</c> and a spoken
///         route number are compared on the same footing.
///     </para>
///     <para>
///         The blocking threshold is 0.35. Measured over the six consecutive pairs in the twelve
///         metro ATN samples (Gold, KIIS Melbourne, 96FM, CADA, WSFM and KIIS Sydney), the raw
///         directed overlap of the later read against the earlier read averaged 0.167 (median 0.134,
///         range 0.000–0.448) before evidence-anchor exclusion. A 0.35 threshold is therefore about
///         2.1 times the ordinary consecutive-pair mean: it catches copied idiom sentences without
///         objecting to the shared road vocabulary that anchor exclusion removes.
///     </para>
/// </summary>
public static class PhraseOverlap
{
    /// <summary>Score at which repeated phrasing becomes a deterministic check failure.</summary>
    public const double FailureThreshold = 0.35;

    private static readonly Regex NumberPattern = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Regex PunctuationPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <summary>
    ///     Returns the fraction of eligible four-word windows in <paramref name="read" /> that are
    ///     present in <paramref name="previousRead" />. When no input is supplied no windows are
    ///     excluded, which makes this overload useful for general deterministic comparisons.
    /// </summary>
    public static double Score(
        string read,
        string previousRead,
        NormalizedInput? input = null)
    {
        string[] currentWords = Normalise(read);
        string[] previousWords = Normalise(previousRead);
        if (currentWords.Length < 4 || previousWords.Length < 4)
            return 0;

        HashSet<string> previousWindows = Windows(previousWords).ToHashSet(StringComparer.Ordinal);
        HashSet<string> evidenceAnchors = input is null ? [] : EvidenceAnchors(input);

        int eligible = 0;
        int repeated = 0;
        foreach (string window in Windows(currentWords))
        {
            if (ContainsAnchor(window, evidenceAnchors))
                continue;

            eligible++;
            if (previousWindows.Contains(window))
                repeated++;
        }

        return eligible == 0 ? 0 : (double)repeated / eligible;
    }

    private static bool ContainsAnchor(string window, HashSet<string> anchors)
    {
        string[] words = window.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return anchors.Any(anchor =>
        {
            string[] anchorWords = anchor.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (anchorWords.Length > words.Length)
                return false;

            for (int start = 0; start + anchorWords.Length <= words.Length; start++)
            {
                if (anchorWords.SequenceEqual(words.Skip(start).Take(anchorWords.Length)))
                    return true;
            }

            return false;
        });
    }

    private static IEnumerable<string> Windows(IReadOnlyList<string> words)
    {
        for (int i = 0; i <= words.Count - 4; i++)
            yield return string.Join(' ', words.Skip(i).Take(4));
    }

    private static string[] Normalise(string text)
    {
        string numbersFolded = NumberPattern.Replace(text.ToLowerInvariant(), " number ");
        return PunctuationPattern.Replace(numbersFolded, " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static HashSet<string> EvidenceAnchors(NormalizedInput input)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (BulletinEvent bulletinEvent in input.Events)
        {
            if (bulletinEvent.Kind is BulletinEventKind.Corridor or BulletinEventKind.DriveTime)
                AddAnchor(anchors, bulletinEvent.Label);

            foreach (EventFact fact in bulletinEvent.Facts)
            {
                string field = fact.Field.ToLowerInvariant();
                if (field.Contains("road", StringComparison.Ordinal)
                    || field.Contains("suburb", StringComparison.Ordinal)
                    || field.Contains("location", StringComparison.Ordinal)
                    || field.Contains("anchor", StringComparison.Ordinal)
                    || field.Contains("address", StringComparison.Ordinal)
                    || field.Contains("street", StringComparison.Ordinal)
                    || field.Contains("highway", StringComparison.Ordinal)
                    || field.Contains("route", StringComparison.Ordinal)
                    || field.Contains("corridor", StringComparison.Ordinal)
                    || field.EndsWith("name", StringComparison.Ordinal))
                {
                    AddAnchor(anchors, fact.Value);
                }
            }
        }

        return anchors;
    }

    private static void AddAnchor(HashSet<string> anchors, string value)
    {
        string[] words = Normalise(value);
        if (words.Length == 0)
            return;

        // Add all contiguous phrases of length two or more so a four-gram can contain a partial
        // anchor ("near Richmond Road" must be excluded even when the evidence says "South Richmond
        // Road"). Single words are useful for one-word suburbs and route tokens, but common short
        // words are not names and would erase too much of the denominator.
        for (int length = 2; length <= words.Length; length++)
        {
            for (int start = 0; start + length <= words.Length; start++)
                anchors.Add(string.Join(' ', words.Skip(start).Take(length)));
        }

        foreach (string word in words.Where(word => word.Length >= 4))
            anchors.Add(word);
    }
}

using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Compares a synthesized bulletin's transcript (e.g. from Whisper) against its script to catch
///     mispronunciations — chiefly road and place names, where TTS most often fails on air. A target
///     term is "intelligible" if it survives transcription (exact substring or a close fuzzy window
///     match); terms that don't are reported as suspect pronunciations. Word error rate is reported as
///     an overall intelligibility signal.
/// </summary>
internal static partial class PronunciationScorer
{
    private const double TermMatchThreshold = 0.80;

    // Two or more consecutive capitalized words — likely a place/road proper noun.
    [GeneratedRegex(@"\b[A-Z][A-Za-z']+(?:\s+[A-Z][A-Za-z']+)+\b")]
    private static partial Regex MultiWordProperNounRegex();

    [GeneratedRegex(@"[^a-z0-9' ]")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static PronunciationResult Evaluate(
        string script, string transcript, IEnumerable<string>? knownPlaces = null)
    {
        string transcriptNorm = Normalize(transcript);
        string[] transcriptTokens = transcriptNorm.Length == 0 ? [] : transcriptNorm.Split(' ');

        var targets = ExtractTargetTerms(script, knownPlaces);
        var suspects = new List<string>();
        int matched = 0;

        foreach (string term in targets)
        {
            if (TermAppears(Normalize(term), transcriptTokens, transcriptNorm))
                matched++;
            else
                suspects.Add(term);
        }

        double wer = WordErrorRate(Normalize(script), transcriptNorm);
        return new PronunciationResult(targets.Count, matched, suspects, wer);
    }

    private static IReadOnlyList<string> ExtractTargetTerms(string script, IEnumerable<string>? knownPlaces)
    {
        var terms = new List<string>(FabricationScorer.ExtractRoadTerms(script));
        foreach (Match match in MultiWordProperNounRegex().Matches(script))
            terms.Add(match.Value.Trim());

        // Known place names (e.g. suburbs from the traffic data) that the script actually mentions —
        // catches bare single-word suburbs the regexes above miss.
        if (knownPlaces is not null)
        {
            string scriptNorm = Normalize(script);
            foreach (string place in knownPlaces)
            {
                string placeNorm = Normalize(place);
                if (placeNorm.Length > 0 && scriptNorm.Contains(placeNorm, StringComparison.Ordinal))
                    terms.Add(place.Trim());
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (string term in terms)
            if (Normalize(term).Length > 0 && seen.Add(Normalize(term)))
                unique.Add(term);
        return unique;
    }

    private static bool TermAppears(string term, string[] transcriptTokens, string transcriptNorm)
    {
        if (term.Length == 0)
            return true;
        if (transcriptNorm.Contains(term, StringComparison.Ordinal))
            return true;

        int k = term.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        for (int size = Math.Max(1, k - 1); size <= k + 1; size++)
            for (int start = 0; start + size <= transcriptTokens.Length; start++)
            {
                string window = string.Join(" ", transcriptTokens, start, size);
                if (Similarity(term, window) >= TermMatchThreshold)
                    return true;
            }

        return false;
    }

    private static double Similarity(string a, string b)
    {
        int max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1.0 : 1.0 - (double)Levenshtein(a, b) / max;
    }

    private static int Levenshtein(string a, string b)
    {
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    /// <summary>Word-level edit distance divided by reference length — standard ASR word error rate.</summary>
    private static double WordErrorRate(string reference, string hypothesis)
    {
        string[] refWords = reference.Length == 0 ? [] : reference.Split(' ');
        string[] hypWords = hypothesis.Length == 0 ? [] : hypothesis.Split(' ');
        if (refWords.Length == 0)
            return hypWords.Length == 0 ? 0.0 : 1.0;

        int[] previous = new int[hypWords.Length + 1];
        int[] current = new int[hypWords.Length + 1];
        for (int j = 0; j <= hypWords.Length; j++)
            previous[j] = j;

        for (int i = 1; i <= refWords.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= hypWords.Length; j++)
            {
                int cost = refWords[i - 1] == hypWords[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return (double)previous[hypWords.Length] / refWords.Length;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        string lower = NonWordRegex().Replace(text.ToLowerInvariant(), " ");
        return WhitespaceRegex().Replace(lower, " ").Trim();
    }
}

internal sealed record PronunciationResult(
    int TermsChecked,
    int TermsMatched,
    IReadOnlyList<string> SuspectTerms,
    double WordErrorRate)
{
    public double TermAccuracy => TermsChecked == 0 ? 1.0 : (double)TermsMatched / TermsChecked;

    /// <summary>Every key term survived transcription.</summary>
    public bool Passed => SuspectTerms.Count == 0;
}

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Detects a sentence that cannot stand alone on air because it ends mid-thought — a dangling
///     copula, conjunction or preposition ("The M4 and Western Motorway are", "Heavy traffic with").
///     Observed live (dev job b7c7b83b, 2026-08-17): an expansion produced a fragment sentence and
///     the duration trim's whole-sentence join then aired "The M4 and Western Motorway are Showery,
///     fourteen degrees." The guard is word-list based and deliberately conservative: it only ever
///     names fragments that end on function words no English sentence ends on, so a terse legitimate
///     sentence ("Victoria Road's a clear run") can never be flagged.
///     <para>
///         Callers MUST NOT apply this to locked bookend lines: a station outro is config and may
///         legitimately dangle ("…together, powered by" awaiting a sponsor read).
///     </para>
/// </summary>
public static class SentenceFragmentGuard
{
    private static readonly HashSet<string> DanglingLastWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "been", "being", "but", "by", "for", "from",
        "had", "has", "have", "in", "into", "is", "its", "near", "of", "on", "onto", "or",
        "the", "their", "to", "toward", "towards", "was", "were", "with"
    };

    /// <summary>True when the sentence ends on a word no complete sentence ends on.</summary>
    public static bool IsDangling(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
            return false;

        string? last = null;
        foreach (var token in sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string word = new(token.Where(char.IsLetter).ToArray());
            if (word.Length > 0)
                last = word;
        }

        return last is not null && DanglingLastWords.Contains(last);
    }
}

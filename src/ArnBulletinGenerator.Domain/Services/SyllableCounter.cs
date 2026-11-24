namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Estimates the number of syllables in text using a vowel-group heuristic.
/// </summary>
public static partial class SyllableCounter
{
    private static readonly char[] Separators =
        [' ', '\r', '\n', '\t', '.', ',', ';', ':', '!', '?', '-', '(', ')', '"'];

    private static readonly HashSet<string> PronouncedFinalE = new(StringComparer.OrdinalIgnoreCase)
    {
        "recipe",
        "catastrophe",
        "acme",
        "simile",
        "apostrophe",
        "hyperbole",
        "anemone",
        "epitome",
        "syncope",
        "calliope",
        "karate",
        "sesame",
        "abalone",
        "finale",
        "tamale",
        "guacamole",
        "adobe",
        "coyote",
        "peyote",
        "resumé"
    };

    /// <summary>
    ///     Counts the total estimated syllables in <paramref name="text" />.
    /// </summary>
    public static int CountSyllables(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        string[] words = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        int count = 0;
        foreach (string word in words) count += CountSyllablesInWord(word);

        return count;
    }

    private static int CountSyllablesInWord(string word)
    {
        word = word.ToLowerInvariant();
        if (word.Length <= 3) return 1;

        int syllables = 0;
        bool lastWasVowel = false;

        foreach (char c in word)
        {
            if (!char.IsLetter(c)) continue;
            bool isVowel = "aeiouy".Contains(c);
            if (isVowel && !lastWasVowel) syllables++;

            lastWasVowel = isVowel;
        }

        if (word.EndsWith('e') && !PronouncedFinalE.Contains(word)) syllables--;

        return syllables < 1 ? 1 : syllables;
    }
}

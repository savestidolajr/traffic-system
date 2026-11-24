using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Counts the syllables a token is expected to take when spoken aloud, including numerals and
///     short capitalised abbreviations that the written-word heuristic cannot represent.
/// </summary>
public static partial class SpokenSyllableCounter
{
    [GeneratedRegex(@"\d+|[A-Za-z]+")]
    private static partial Regex CharacterRunPattern();

    [GeneratedRegex(@"[^\w']")]
    private static partial Regex NonWordPattern();

    /// <summary>Counts the spoken syllables in one token.</summary>
    public static int Count(string? word)
    {
        string bare = NonWordPattern().Replace(word ?? string.Empty, string.Empty);
        if (bare.Length == 0)
            return 0;

        bool containsDigit = false;
        foreach (char c in bare)
        {
            if (char.IsDigit(c))
            {
                containsDigit = true;
                break;
            }
        }

        if (!containsDigit)
        {
            string letters = GetLetters(bare);
            if (letters.Length >= 2 && IsAllUpperCase(letters))
            {
                int total = 0;
                foreach (char c in letters)
                    total += LetterName(c);
                return total;
            }

            return SyllableCounter.CountSyllables(bare);
        }

        int spokenSyllables = 0;
        foreach (Match run in CharacterRunPattern().Matches(bare))
        {
            if (char.IsDigit(run.Value[0]))
            {
                spokenSyllables += NumberSyllables(run.Value);
            }
            else if (run.Value.Length <= 2 && IsAllUpperCase(run.Value))
            {
                foreach (char c in run.Value)
                    spokenSyllables += LetterName(c);
            }
            else
            {
                spokenSyllables += SyllableCounter.CountSyllables(run.Value);
            }
        }

        return Math.Max(1, spokenSyllables);
    }

    private static string GetLetters(string text)
    {
        Span<char> letters = stackalloc char[text.Length];
        int count = 0;
        foreach (char c in text)
        {
            if (char.IsLetter(c))
                letters[count++] = c;
        }

        return new string(letters[..count]);
    }

    private static bool IsAllUpperCase(string text)
    {
        foreach (char c in text)
        {
            if (char.IsLetter(c) && !char.IsUpper(c))
                return false;
        }

        return true;
    }

    private static int LetterName(char c) =>
        char.ToLowerInvariant(c) == 'w' ? 3 : 1;

    private static int NumberSyllables(string digits)
    {
        if (digits.Length > 4)
        {
            int total = 0;
            foreach (char digit in digits)
                total += digit == '0' ? 1 : Ones[digit - '0'];
            return total;
        }

        int value = int.Parse(digits);
        if (value < 10)
            return value == 0 ? 1 : Ones[value];
        if (value < 20)
            return Teens[value];
        if (value < 100)
        {
            int tens = value / 10;
            int ones = value % 10;
            return Tens[tens] + (ones == 0 ? 0 : Ones[ones]);
        }

        if (value < 1000)
        {
            int hundreds = value / 100;
            int remainder = value % 100;
            return Ones[hundreds] + 2 + (remainder == 0 ? 0 : NumberSyllables(remainder.ToString()));
        }

        int thousands = value / 1000;
        int rest = value % 1000;
        return Ones[thousands] + 2 + (rest == 0 ? 0 : NumberSyllables(rest.ToString()));
    }

    private static readonly int[] Ones = [0, 1, 1, 1, 1, 1, 2, 2, 1, 1];

    private static readonly int[] Teens =
        [0, 0, 0, 2, 2, 2, 3, 3, 2, 3, 1, 1, 1, 2, 2, 2, 3, 3, 2, 3];

    private static readonly int[] Tens = [0, 0, 2, 2, 2, 2, 3, 3, 2, 2];
}

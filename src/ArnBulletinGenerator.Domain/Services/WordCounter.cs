using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Counts spoken words in a script, ignoring TTS pacing tags
///     such as [short pause], [pause], and ellipses (...).
/// </summary>
public static partial class WordCounter
{
    private static readonly char[] WordSeparators = [' ', '\r', '\n', '\t'];

    /// <summary>
    ///     Returns the number of spoken words after stripping all TTS pacing cues.
    /// </summary>
    public static int Count(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        string cleaned = StripTtsTags(text);
        if (string.IsNullOrWhiteSpace(cleaned)) return 0;

        int result = cleaned.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Length;
        return result;
    }

    /// <summary>
    ///     Removes all TTS pacing cues from <paramref name="text" />:
    ///     bracketed tags ([short pause], [pause], etc.) and standalone ellipses.
    /// </summary>
    public static string StripTtsTags(string text)
    {
        // Remove bracketed pacing tags: [pause], [short pause], [long pause], etc.
        string result = BracketedTagRegex().Replace(text, " ");

        // Remove standalone ellipses (three or more dots) — but preserve sentence-ending periods.
        result = EllipsisRegex().Replace(result, " ");

        return result;
    }

    [GeneratedRegex(@"\[[^\]]*pause[^\]]*\]", RegexOptions.IgnoreCase)]
    private static partial Regex BracketedTagRegex();

    [GeneratedRegex(@"\.{3,}")]
    private static partial Regex EllipsisRegex();
}

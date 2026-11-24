using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Helpers for ElevenLabs v3 inline audio tags (e.g. <c>[urgent]</c>, <c>[calm]</c>,
///     <c>[strong Australian accent]</c>). Tags are bracketed delivery directions interpreted by
///     eleven_v3 — never spoken — so the rest of the pipeline (word/syllable/duration counting,
///     calibration, attribution) must measure the script with tags removed. The director is only
///     ever allowed to ADD tags, capitalisation and punctuation; <see cref="WordsPreserved" />
///     enforces that the spoken words are unchanged.
/// </summary>
public static partial class DeliveryTags
{
    [GeneratedRegex(@"\[[^\[\]]{0,80}\]")]
    private static partial Regex TagPattern();
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();
    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex WordToken();

    /// <summary>Removes inline audio tags, collapsing the whitespace they leave behind.</summary>
    public static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;
        if (!text.Contains('['))
            return text;
        var stripped = TagPattern().Replace(text, " ");
        return MultiSpace().Replace(stripped, " ").Trim();
    }

    /// <summary>True when the text contains at least one inline audio tag.</summary>
    public static bool HasTags(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains('[') && TagPattern().IsMatch(text);

    /// <summary>
    ///     True when the TTS model INTERPRETS inline audio tags. Only the eleven_v3 family does; every
    ///     other model reads a tag out as literal words ("open bracket, quietly…"), which is why a tag
    ///     must never reach one. A non-v3 voice does NOT disable delivery direction, though —
    ///     capitalisation and punctuation are model-agnostic, so the director still runs with an empty
    ///     palette. Returns false for a null or unknown model: the safe default is no tags.
    /// </summary>
    public static bool SupportsTags(string? ttsModel) =>
        ttsModel?.Contains("v3", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    ///     The text as it may safely be SPOKEN by <paramref name="ttsModel" />: unchanged when the
    ///     model interprets tags, otherwise with every tag stripped. A non-v3 voice reads a tag out
    ///     as literal words, so "[excitedly] The M1 is closed" would air as "excitedly, the M1 is
    ///     closed" — the tag must be removed rather than trusted to any upstream gate. Allocation
    ///     free when the text carries no tag (see <see cref="Strip" />), so this is cheap enough to
    ///     sit on the synthesis path unconditionally.
    /// </summary>
    public static string ForModel(string? text, string? ttsModel) =>
        SupportsTags(ttsModel) ? text ?? string.Empty : Strip(text);

    /// <summary>The inner labels of every inline audio tag, in order (brackets removed, trimmed).</summary>
    public static IReadOnlyList<string> Extract(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('['))
            return [];
        var labels = new List<string>();
        foreach (Match m in TagPattern().Matches(text))
            labels.Add(m.Value[1..^1].Trim());
        return labels;
    }

    /// <summary>
    ///     True when <paramref name="tagged" /> preserves the spoken words of
    ///     <paramref name="original" /> exactly — only added tags, capitalisation, and
    ///     punctuation are permitted. Compares the lowercased word-token sequence of both
    ///     texts with tags stripped, so emphasis (CAPS), pauses (…/—), and tags are ignored
    ///     while any inserted, dropped, or altered word is caught.
    /// </summary>
    public static bool WordsPreserved(string original, string tagged)
    {
        return Tokenize(original).SequenceEqual(Tokenize(tagged));
    }

    private static List<string> Tokenize(string text)
    {
        var stripped = Strip(text).ToLowerInvariant();
        var tokens = new List<string>();
        foreach (Match m in WordToken().Matches(stripped))
            tokens.Add(m.Value);
        return tokens;
    }
}

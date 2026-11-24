using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Splits bulletin script text into individual sentences, excluding bracketed
///     direction cues such as [friendly], [urgent]. Mirror of the frontend
///     <c>segmentScript</c> in <c>provenance.ts</c> — any deviation breaks
///     sentence-level lineage matching.
/// </summary>
public static partial class ScriptSentenceSplitter
{
    [GeneratedRegex(@"(\[[^\]]+\])")]
    private static partial Regex DirectionCuePattern();

    [GeneratedRegex(@"(?<=[.!?…])\s+(?=[A-Z0-9""\[\({])")]
    private static partial Regex SentenceBoundaryRegex();

    /// <summary>
    ///     Splits <paramref name="script"/> into ordered sentence texts.
    ///     Bracketed direction cues are discarded; adjacent text segments
    ///     are split on . ! ? and ellipsis (…) sentence boundaries. An
    ///     ellipsis only terminates when a capital, digit, quote or bracket
    ///     follows, so a mid-sentence delivery-pause ellipsis followed by a
    ///     lowercase word stays inside its sentence. Returns an empty list
    ///     for null, empty, or whitespace-only input.
    /// </summary>
    public static IReadOnlyList<string> Split(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return [];

        var segments = DirectionCuePattern().Split(script);
        var sentences = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            // Direction cues are captured by the split pattern — skip them.
            if (segment[0] == '[' && segment[^1] == ']')
                continue;

            var parts = SentenceBoundaryRegex().Split(segment);
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                    sentences.Add(trimmed);
            }
        }

        return sentences;
    }
}

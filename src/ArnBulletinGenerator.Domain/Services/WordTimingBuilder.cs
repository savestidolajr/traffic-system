using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Converts character-level TTS alignment into word-level timings on the FINAL audio.
///     The alignment timescale belongs to the raw synthesis output; duration fine-tuning
///     (speed retime, tail trim/pad) shifts that timescale, so callers supply a linear
///     <c>scale</c> factor (finalDuration / alignmentSourceDuration). Tail-only operations
///     make this an approximation, which is acceptable for click-to-seek UX.
///
///     Inline audio tags are EXCLUDED. The provider aligns the text it was sent, tags
///     included, but the operator reads the plain script — so counting <c>[solemn]</c> as a
///     word shifted every later highlight by one per preceding tag (measured on dev,
///     2026-08-02: a 74-word read with 2 tags produced 77 timings).
/// </summary>
public static partial class WordTimingBuilder
{
    public static IReadOnlyList<WordTiming> Build(
        AudioAlignment alignment,
        double scale,
        double maxDurationSeconds)
    {
        if (alignment.Characters.Count == 0 ||
            alignment.Characters.Count != alignment.StartSeconds.Count ||
            alignment.Characters.Count != alignment.EndSeconds.Count ||
            scale <= 0 || maxDurationSeconds <= 0)
            return [];

        var timings = new List<WordTiming>();
        var word = new System.Text.StringBuilder();
        double wordStart = 0;
        double wordEnd = 0;

        bool inTag = false;
        for (int i = 0; i < alignment.Characters.Count; i++)
        {
            string ch = alignment.Characters[i];

            // A tag is silent direction, not speech: drop it and the time it occupies.
            if (inTag)
            {
                if (ch == "]") inTag = false;
                continue;
            }
            if (ch == "[")
            {
                FlushWord();
                inTag = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(ch))
            {
                FlushWord();
                continue;
            }

            if (word.Length == 0)
                wordStart = alignment.StartSeconds[i];
            wordEnd = alignment.EndSeconds[i];
            word.Append(ch);
        }

        FlushWord();
        return timings;

        void FlushWord()
        {
            if (word.Length == 0)
                return;

            double start = Math.Clamp(wordStart * scale, 0, maxDurationSeconds);
            double end = Math.Clamp(wordEnd * scale, start, maxDurationSeconds);
            timings.Add(new WordTiming(word.ToString(), Math.Round(start, 3), Math.Round(end, 3)));
            word.Clear();
        }
    }
}

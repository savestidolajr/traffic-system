using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The weather and fuel sentences of one read, as the workflow that wrote them identified them
///     (<see cref="DraftedSentence.Section" />), plus whether the station requires them on air.
///     Every trim that shortens a read at or after synthesis consults this instead of guessing
///     from vocabulary: the word list it replaced protected "Partly cloudy, 17 degrees" and cut
///     "clear and 22 for Sydney" (dev 2e158de3, 2026-09-02) after the gates had passed it.
/// </summary>
public sealed class FixtureLines
{
    private readonly HashSet<string> _lines;

    private FixtureLines(HashSet<string> lines, bool required)
    {
        _lines = lines;
        Required = required;
    }

    /// <summary>
    ///     True when the station always carries the fixtures that were fetched: the lines are then
    ///     protected wherever they sit in the read, not only as a trailing close.
    /// </summary>
    public bool Required { get; }

    /// <summary>
    ///     Null when no sentence voices weather or fuel, so callers can treat "no fixtures" and
    ///     "no lineage" identically.
    /// </summary>
    public static FixtureLines? From(IEnumerable<DraftedSentence>? sentences, bool required)
    {
        if (sentences is null)
            return null;
        var lines = new HashSet<string>(StringComparer.Ordinal);
        foreach (DraftedSentence sentence in sentences)
        {
            if (sentence.Section is BulletinSegment.Weather or BulletinSegment.Fuel)
                lines.Add(Key(sentence.Text));
        }

        return lines.Count == 0 ? null : new FixtureLines(lines, required);
    }

    /// <summary>True when <paramref name="sentence" /> is one of the read's weather or fuel lines.</summary>
    public bool Contains(string sentence) => _lines.Contains(Key(sentence));

    /// <summary>
    ///     Identity after the same deterministic spoken-form normalization the engine applies
    ///     between composition and synthesis. This keeps a tagged fixture protected when, for
    ///     example, the workflow wrote "$1.93" and the final read says "a dollar ninety-three".
    ///     Delivery tags, case and punctuation are immaterial too.
    /// </summary>
    private static string Key(string text)
    {
        var chars = SpokenFormNormalizer.Normalize(DeliveryTags.Strip(text))
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .Select(char.ToLowerInvariant);
        return string.Join(" ", new string([.. chars]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

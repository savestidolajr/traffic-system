using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The exact vocabulary the evidence builder hands the writer to DESCRIBE a condition, which
///     the writer must translate into road-feel and never read aloud. Single-sourced here because
///     the same list is needed in two places that had drifted apart: the phrasing selector's
///     quality penalty, and the tests that pin what may reach air.
///     <para>
///         Source of truth is <c>TrafficNarrativeSummarizer.FormatImpact</c> and its delay-band
///         sibling — every string those methods can return belongs here. The selector's list was
///         hand-typed against a single 2026-08-20 leak ("minor impact") and carried six of them, so
///         the labels added to the emitter afterwards were never penalised. Measured on the
///         2026-08-25 end-to-end run, the uncovered ones duly aired: "Major delays with roadworks in
///         extended work zones" (sydney), "There's a short queue for about half a kay" (melbourne),
///         "Roadworks are creating an extended work zone" (melbourne).
///     </para>
///     <para>
///         Also covers the data FIELD names, which are a subtler leak than the severity bands
///         because they read as ordinary English: "impact"/"impacts" is the field the band is stored
///         in and aired on 6 of 13 reads ("Minor impacts are reported on Stuart Road"), and "planned
///         event" is a HERE incident-type code that aired twice. A presenter says a road is slower,
///         busier or closed; they never say it has an impact.
///     </para>
/// </summary>
public static partial class EvidenceLabelVocabulary
{
    /// <summary>
    ///     Severity and extent bands, verbatim as the evidence supplies them. Ordered longest-first
    ///     so a caller reporting the first match names the most specific label present.
    /// </summary>
    public static readonly IReadOnlyList<string> SeverityLabels =
    [
        // extent bands (FormatImpact, measured-extent branch)
        "extended work zone", "short work zone",
        "moderate queue", "short queue", "long queue",
        // condition bands (FormatImpact, no-measured-delay branch)
        "blocking — no measured delay", "slow but moving", "minor impact",
        // delay bands (FormatDelay)
        "severe delays", "heavy delays", "moderate delays", "minor delays", "major delays",
        // congestion bands
        "heavy congestion", "major congestion", "minor congestion",
        // magnitude words that are labels, not copy
        "heavy traffic", "heavy in patches",
    ];

    /// <summary>
    ///     Data field and classifier names. Matched on word boundaries because these are ordinary
    ///     English words outside this context, unlike the severity bands.
    /// </summary>
    public static readonly IReadOnlyList<string> FieldNames =
    [
        "impact", "impacts", "planned event", "standing footprint", "delay minutes",
    ];

    /// <summary>
    ///     Every label and field name as one boundary-anchored pattern. "road closed" is
    ///     deliberately ABSENT: it is both a label and the natural way to say the thing, and the
    ///     closure detectors already own whether a closure claim is honest.
    /// </summary>
    public static int CountLeaks(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : LeakPattern().Matches(text).Count;

    /// <summary>The first label present, for feedback that names what leaked.</summary>
    public static string? FirstLeak(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var match = LeakPattern().Match(text);
        return match.Success ? match.Value : null;
    }

    // Alternation is longest-first so "minor impact" reports as itself rather than as bare
    // "impact", and so a single leak is counted once rather than twice. Plurals are matched
    // explicitly: the emitter supplies the singular band, but the writer pluralises it when several
    // roads share the state, and the two forms that aired on 2026-08-25 were both plural
    // ("extended work zones", "moderate queues run for about three kays").
    [GeneratedRegex(
        @"\b(?:(?:extended|short)\s+work\s+zones?|(?:moderate|short|long)\s+queues?"
        + @"|blocking\s+—\s+no\s+measured\s+delay|slow\s+but\s+moving"
        + @"|minor\s+impacts?|(?:severe|heavy|moderate|minor|major)\s+delays"
        + @"|(?:heavy|major|minor)\s+congestion"
        + @"|heavy\s+traffic|heavy\s+in\s+patches|planned\s+events?|standing\s+footprints?"
        + @"|delay\s+minutes|impacts?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeakPattern();
}

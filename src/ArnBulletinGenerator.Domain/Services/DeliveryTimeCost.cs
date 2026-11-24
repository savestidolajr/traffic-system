using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     What actually makes a toned read longer than the plain one, so an overrunning read can be
///     reduced in COST ORDER instead of having its tone discarded wholesale.
///
///     Measured 2026-08-05 against a 4.9% duration noise floor (50 devices, 364 generations — see
///     <c>docs/reference/delivery-device-map.md</c>). Pause punctuation and sentence structure
///     dominate the cost: splitting into more sentences +12.3%, em dash +8.1%, ellipsis +5.3%.
///     Among tags, only the ones that insert audible non-speech cost anything: [short pause]
///     +13.0%, [exhales] +10.9%, [frustrated sigh] +7.6%, [clears throat] +6.7%, [sighs] +5.0%.
///
///     The manner tags are free or FASTER than the plain read — [brightly] −6.2%, [firmly] −5.5%,
///     [excitedly] −3.2%, [whispering] −1.3%. That is why "tags slow the read, so drop them all"
///     was wrong on its own terms: a job whose tone was [excitedly] + [brightly] + [exhales
///     sharply] had its tags stripped to buy time, and two of the three were time-NEGATIVE, so the
///     fallback recovered almost nothing and destroyed the operator's tone for it (dev,
///     2026-08-05, job de45b38b). Reduce the real cost first; drop tone last.
/// </summary>
public static partial class DeliveryTimeCost
{
    [GeneratedRegex(@"\[[^\[\]]{0,80}\]")]
    private static partial Regex TagPattern();

    /// <summary>Ellipsis in either form, with the whitespace around it.</summary>
    [GeneratedRegex(@"\s*(?:\.{3,}|…)\s*")]
    private static partial Regex Ellipsis();

    /// <summary>A spaced dash used as a pause. Hyphenated names ("Dandenong-Frankston") have no
    /// surrounding whitespace and are deliberately left alone.</summary>
    [GeneratedRegex(@"\s+[—–]\s+|\s+-\s+")]
    private static partial Regex DashPause();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex MultiSpace();

    /// <summary>
    ///     Tags that insert audible non-speech and therefore genuinely lengthen the read. Exact
    ///     strings, because the vocabulary is a lookup rather than a meaning: "sighs" costs time
    ///     and "sigh" does nothing at all.
    /// </summary>
    public static readonly IReadOnlySet<string> TimeCostingTags =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "short pause", "long pause", "pause",
            "sighs", "frustrated sigh", "exhales", "exhales sharply", "inhales deeply",
            "clears throat", "swallows", "gulps"
        };

    /// <summary>
    ///     The toned read with its pause punctuation flattened: ellipses and spaced dashes become
    ///     a single space. Words, capitalisation and tags are untouched, so
    ///     <see cref="DeliveryTags.WordsPreserved" /> still holds — this buys back the largest
    ///     single slice of time without touching what the operator asked for.
    /// </summary>
    public static string WithoutPausePunctuation(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;
        var flattened = Ellipsis().Replace(text, " ");
        flattened = DashPause().Replace(flattened, " ");
        return MultiSpace().Replace(flattened, " ").Trim();
    }

    /// <summary>
    ///     The toned read with ONLY the time-costing tags removed. Manner tags survive, because
    ///     they cost nothing and are usually the register the operator actually asked for.
    /// </summary>
    public static string WithoutTimeCostingTags(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('['))
            return text ?? string.Empty;
        var reduced = TagPattern().Replace(text, m =>
            TimeCostingTags.Contains(m.Value[1..^1].Trim()) ? " " : m.Value);
        return MultiSpace().Replace(reduced, " ").Trim();
    }

    /// <summary>True when <paramref name="text" /> carries any device that costs measurable time.</summary>
    public static bool HasCostlyDevices(string? text) =>
        !string.IsNullOrEmpty(text) &&
        (Ellipsis().IsMatch(text) || DashPause().IsMatch(text) ||
         DeliveryTags.Extract(text).Any(TimeCostingTags.Contains));
}

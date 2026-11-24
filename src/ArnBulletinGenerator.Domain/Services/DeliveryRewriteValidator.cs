using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The hard constraints on a register rewrite (<see cref="Interfaces.IDeliveryWriter" />).
///
///     A rewrite is allowed to change wording — that is the whole reason it exists, because on our
///     voices the words carry most of the delivery. But three things it may never do, and all three
///     were observed happening in practice before these guards existed:
///     a banned tag (which airs as a literal noise or does nothing), an over-budget script (which
///     makes the pipeline compress the read or trim a road off the end), and a lost fact.
/// </summary>
public static partial class DeliveryRewriteValidator
{
    /// <summary>
    ///     Tags that air as an undignified noise for a traffic desk, or that the voice ignores
    ///     outright. Measured 2026-08-05 (50 devices, 364 generations, Welch t-tests against an
    ///     8-run unchanged control — see docs/reference/delivery-device-map.md), replacing the
    ///     2026-08-03 listening pass that banned the sound-inserting family wholesale.
    ///
    ///     Two findings changed this list. First, sound-inserting tags are the ONLY ones that
    ///     reliably land, because they insert an acoustic event instead of colouring prosody — an
    ///     operator confirmed sighs, exhales, sharp exhales and throat clears by ear, so those are
    ///     now permitted and the writer prompt requires them. What stays banned here is the
    ///     genuinely undignified end of that family, which is an editorial line, not an audibility
    ///     one. Second, the vocabulary is an exact-string lookup: "sighs" fires and "sigh" does
    ///     nothing, "short pause" fires and bare "pause" does nothing. The inert spellings are
    ///     banned precisely because they look like they work and silently do not.
    /// </summary>
    public static readonly IReadOnlySet<string> BannedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Undignified on a traffic desk — audible, but a newsreader does not do them.
        "laugh", "laughs", "laughing", "chuckles", "giggles", "gasp", "gasps", "happy gasp",
        "crying", "sobbing", "snorts", "gulps", "swallows", "wheezing",
        // Inert spellings of tags that DO work in another form. Banned so a silent no-op cannot
        // ship: use "sighs", "exhales", "exhales sharply", "short pause" or "long pause" instead.
        "sigh", "sighing", "exhale", "inhale", "inhales", "inhales deeply", "breathes",
        "pause", "pauses", "beat",
        // Measured inaudible: the whole abstract-emotion vocabulary. Reaching for one of these is
        // the same as returning a flat read, which is why the writer must not spend a tag on it.
        "sad", "solemn", "excited", "professional", "serious", "happy", "angry",
        "urgent", "urgently", "emphatic", "upbeat", "warmly", "calm", "reassuring", "alarmed",
        "dramatically", "matter-of-fact", "cheerful", "bright", "curious", "amazed", "impressed",
        "assertive", "direct", "fast-paced", "deadpan"
    };

    /// <summary>
    ///     Growth ceiling against the approved script, as a percentage. The slot is fixed, so a
    ///     longer script is paid for by compression or by the duration fitter dropping trailing
    ///     content. Integer percent, not a double multiplier: `100 * 1.15` is 114.99999999999999,
    ///     which rejected an exactly-15% rewrite that should have been allowed.
    /// </summary>
    public const int MaxWordGrowthPercent = 115;

    /// <summary>Banned tags present in <paramref name="script" />, without brackets.</summary>
    public static IReadOnlyList<string> FindBannedTags(string? script)
    {
        if (string.IsNullOrWhiteSpace(script)) return [];
        return DeliveryTags.Extract(script)
            .Select(t => t.Trim('[', ']').Trim())
            .Where(BannedTags.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>True when the rewrite will not fit the slot the approved script was written for.</summary>
    public static bool ExceedsSlot(int spokenWordsBefore, int spokenWordsAfter) =>
        spokenWordsBefore > 0 &&
        spokenWordsAfter * 100 > spokenWordsBefore * MaxWordGrowthPercent;

    /// <summary>
    ///     Facts in the approved script that the rewrite dropped: named roads and any number. A
    ///     register change may re-order and re-word freely around a fact; losing one is a defect,
    ///     because the operator approved those facts and the listener acts on them.
    /// </summary>
    public static IReadOnlyList<string> FindMissingFacts(string? before, string? after)
    {
        if (string.IsNullOrWhiteSpace(before)) return [];
        string haystack = (after ?? string.Empty).ToLowerInvariant();
        return FactPattern().Matches(before)
            .Select(m => m.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !haystack.Contains(f.ToLowerInvariant(), StringComparison.Ordinal))
            .ToList();
    }

    // A capitalised word followed by a road-type noun ("Bell Street", "Monash Freeway"), or any
    // number. Deliberately narrow: a false positive here blocks a good rewrite, so it only claims
    // the fact shapes a traffic bulletin actually turns on.
    [GeneratedRegex(
        @"\b(?:[A-Z][A-Za-z']+(?:\s+[A-Z][A-Za-z']+)?\s+(?:Road|Street|Freeway|Highway|Bridge|Avenue|Bypass|Drive|Lane|Parade|Terrace|Tunnel|Motorway)|\d[\d.,]*)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex FactPattern();
}

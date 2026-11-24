namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Turns an evidence severity band into the words a presenter would actually say, in code,
///     before the writer ever sees it.
///     <para>
///         The pipeline used to hand the writer the band verbatim — <c>impact = minor delays</c> —
///         and instruct it in the prompt to translate that into road-feel and never read the label
///         aloud. Measured on the 2026-08-25 end-to-end run, that instruction failed on 6 of 13
///         reads: "Minor impacts are reported on Stuart Road", "Major delays with roadworks in
///         extended work zones", "There's a short queue for about half a kay". The selector's
///         label penalty cannot rescue those, because it can only choose between the phrasings the
///         writer offers, and on 7 of 22 label-carrying items measured across 160 captured draft
///         items EVERY phrasing echoed the label. A word the writer is never given is a word it
///         cannot echo, which is the same move that fixed the fuel grade and good-run leaks.
///     </para>
///     <para>
///         Each band supplies SEVERAL sanctioned phrasings rather than one. A single phrase per band
///         would make every corridor in that band read identically, which is the staccato/repeated
///         -frame defect one content type over; the writer picks per item and varies around them.
///         The wording mirrors the operator-editable road-feel palette bands.
///     </para>
/// </summary>
public static class RoadFeelTranslator
{
    // Two phrasings per band, both offered to the writer as alternatives (see Speak). Two, not
    // three: the sets below are the ones that measured 81% target shape on 2026-08-25, and a
    // three-option rewrite of them measured 68% on the same corpus. Ordinal-ignore-case, because
    // the emitter's bands are fixed strings matched exactly.
    //
    // Keep both phrasings of a band interchangeable — either may be the one the writer builds on.
    private static readonly Dictionary<string, string[]> Spoken = new(StringComparer.OrdinalIgnoreCase)
    {
        // delay bands
        ["minor delays"] = ["a touch slower", "an extra minute or two"],
        ["moderate delays"] = ["a slower run", "busy heading through"],
        ["heavy delays"] = ["a slow grind", "banked up"],
        ["severe delays"] = ["crawling", "bumper to bumper"],
        ["major delays"] = ["a slow grind", "banked up"],

        // queue bands, by measured extent
        ["short queue"] = ["a slight queue", "a brief delay"],
        ["moderate queue"] = ["a bit of a queue", "backed up"],
        // A third option here, "a long queue building", would itself carry the "long queue" label —
        // caught by NoSpokenFormIsItselfALabel, which is exactly what that invariant is for.
        ["long queue"] = ["queues stretching back", "banked up a long way"],

        // standing-footprint bands
        ["short work zone"] = ["works underway", "crews are out"],
        ["extended work zone"] = ["roadworks over a long stretch", "a long run of cones"],

        // condition bands with no measured delay
        ["blocking — no measured delay"] = ["blocked", "not getting through"],
        ["slow but moving"] = ["moving, but slowly", "stop-start"],
        ["minor impact"] = ["a touch slower", "barely noticeable"],

        // congestion bands
        ["minor congestion"] = ["a touch slower", "an extra minute or two"],
        ["major congestion"] = ["a slow grind", "banked up"],
        ["heavy congestion"] = ["crawling", "bumper to bumper"],
        ["heavy traffic"] = ["heavy going", "a slow grind"],
        ["heavy in patches"] = ["slow in spots", "patchy going"],
    };

    /// <summary>
    ///     The spoken form of a band for one road, or the band itself when it is already plain
    ///     speech. Falling through unchanged is deliberate: an unmapped value is far more likely to
    ///     be real prose from the incident description than a band we forgot, and swallowing it
    ///     would lose a fact. <c>EvidenceLabelVocabularyTests</c> pins that every band IS mapped.
    ///     <para>
    ///         Exactly ONE phrasing is handed over, chosen by <paramref name="roadName" />, because
    ///         any separator between alternatives leaks into the read. Both were tried on
    ///         2026-08-25 and both aired: "/" came back as "traffic's barely noticeable but a touch
    ///         slower", and " or " came back as "there's a slight queue or brief delay on the
    ///         Craigieburn Bypass". The writer treats the options as a compound description of one
    ///         condition rather than a choice between two ways of saying it.
    ///     </para>
    ///     <para>
    ///         The eval's target shape appeared to prefer offering alternatives (81% against 68%),
    ///         but three runs of one unchanged configuration scored 74/72/78 — the metric carries
    ///         about six points of noise at n=3 markets, so that apparent preference was not a
    ///         signal and this is decided on the deterministic evidence instead: a separator that
    ///         can reach air must not exist. Cross-road repetition, the cost of fixing the phrasing,
    ///         is already owned deterministically by <see cref="Detectors.SentenceShapeDetector" />
    ///         and RepeatedStatePhraseDetector, which both demonstrably fire on it.
    ///     </para>
    ///     <para>
    ///         The key is a stable FNV-1a hash, not <c>string.GetHashCode</c>, which is randomised
    ///         per process: a given road must read the same way on every run, so replay fixtures and
    ///         successive bulletins for one market stay comparable.
    ///     </para>
    /// </summary>
    public static string Speak(string? band, string? roadName = null)
    {
        if (string.IsNullOrWhiteSpace(band))
            return string.Empty;
        if (!Spoken.TryGetValue(band.Trim(), out string[]? options))
            return band;

        return options[StableIndex(roadName) % options.Length];
    }

    /// <summary>Every band this translator knows how to speak.</summary>
    public static IReadOnlyCollection<string> KnownBands => Spoken.Keys;

    /// <summary>All phrasings for a band, for tests and for the operator-facing palette view.</summary>
    public static IReadOnlyList<string> PhrasingsFor(string band) =>
        Spoken.TryGetValue(band, out string[]? options) ? options : [];

    private static int StableIndex(string? seed)
    {
        if (string.IsNullOrWhiteSpace(seed))
            return 0;

        // FNV-1a, 32-bit. Deterministic across processes and runs, unlike string.GetHashCode.
        uint hash = 2166136261u;
        foreach (char c in seed)
        {
            hash ^= char.ToLowerInvariant(c);
            hash *= 16777619u;
        }

        return (int)(hash & 0x7FFFFFFF);
    }
}

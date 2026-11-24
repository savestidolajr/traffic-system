using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services.Detectors;

/// <summary>
///     Shared sentence-level signals for weather and fuel content, used by every detector that
///     must know whether a script VOICED a source (<see cref="RequiredSourceDetector" />,
///     <see cref="SectionOrderDetector" />) or fabricated one it was not given
///     (<see cref="UnselectedSourceDetector" />, which originally owned these signals). Signals
///     are chosen to be unambiguous: a temperature or a named weather condition for weather;
///     fuel-type words, "a litre", and a "$1.xx" price for fuel. They deliberately EXCLUDE
///     road-ambiguous words ("clear", "fine", "cool", "wind") so a normal traffic read never
///     trips them.
///     <para>
///         The temperature signal matches the WORD "degrees", not a digit followed by it. A digit
///         match was a live false negative: the shipped read spells numbers out, so a deployed dev
///         bulletin voicing "Clear, seventeen degrees!" was scored as having no weather line at all,
///         which made <see cref="RequiredSourceDetector" /> demand a section that was already there
///         and hid the weather line from <see cref="SectionOrderDetector" />. "degrees" carries no
///         traffic meaning, so widening to the bare word costs no precision, and it closes the same
///         hole in the other direction: a fabricated spoken-word temperature on a traffic-only read
///         used to slip past <see cref="UnselectedSourceDetector" />.
///     </para>
///     <para>
///         A condition word that is functioning as part of a road NAME does not count: the signal
///         ignores a condition word immediately followed by a capitalised word or a road-type word
///         ("Fog Bay Road", "Sunny Street", "Storm King Drive"). A deployed dev bulletin voicing
///         "Fog Bay Road's slow going eastbound" tripped <see cref="UnselectedSourceDetector" /> as
///         fabricated weather because "Fog" in "Fog Bay Road" matched, burning a refine attempt and
///         shipping an unresolved-gate warning on a defect that did not exist; the same false
///         positive would make <see cref="RequiredSourceDetector" /> believe weather had aired when
///         it had not. The exclusion is lexical and context-free on purpose: these signals are used
///         where a whole-sentence decision must not depend on the corridor list. The case-sensitive
///         capitalised-word check is what separates a name ("Fog Bay") from prose ("fog about",
///         "Passing clouds"), which always continues lowercase in the shipped reads.
///     </para>
/// </summary>
public static partial class ContentSourceSignals
{
    // Degrees carries no traffic meaning, so it is matched unconditionally and never excluded as a
    // name part. The condition alternatives are excluded only when the next word is capitalised
    // (a proper-noun name part) or is a road-type word, i.e. the condition word is a name, not
    // weather. The inline (?i:...) groups keep the condition words case-insensitive while the
    // capitalised-word lookahead stays case-sensitive (RegexOptions.IgnoreCase would fold it).
    [GeneratedRegex(
        @"\b(?i:degrees?)\b|\b(?i:cloud|clouds|cloudy|overcast|rain|raining|showers?|drizzle|fog|foggy|misty|sunny|storm|storms|hail|humid|sleet)\b(?!\s+(?:[A-Z][a-zA-Z]*|(?i:road|street|st|avenue|ave|drive|dr|highway|hwy|freeway|fwy|way|lane|parade|pde|boulevard|blvd|place|court|crescent|cres|terrace|track|trail|bypass|circuit|esplanade|rd)\b))",
        RegexOptions.Compiled)]
    private static partial Regex WeatherSignal();

    [GeneratedRegex(
        @"\bunleaded\b|\bdiesel\b|\bpetrol\b|\bper\s+litre\b|\ba\s+litre\b|\$\s?\d",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex FuelSignal();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();

    /// <summary>True when the sentence reads as a weather line (a temperature or a named condition).</summary>
    public static bool IsWeatherLine(string sentence) => WeatherSignal().IsMatch(sentence);

    /// <summary>True when the sentence reads as a fuel/price line (a fuel type, "a litre", or a price).</summary>
    public static bool IsFuelLine(string sentence) => FuelSignal().IsMatch(sentence);

    /// <summary>Splits a script into its trimmed, non-empty sentences on terminal punctuation.</summary>
    public static IReadOnlyList<string> SplitSentences(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return [];

        return SentenceSplit().Split(script)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    /// <summary>
    ///     Weather and fuel are FIXTURES: each airs at most once, so no rewrite may leave a read
    ///     voicing more of them than it already did. Returns true when <paramref name="after" />
    ///     carries more weather lines, or more fuel lines, than <paramref name="before" />.
    ///     <para>
    ///         Observed live on dev <c>f553c893</c> (2026-08-24, aired): a content-limited read went
    ///         to the sentence refiner to close a 19.1s underrun in a 22s slot. The refiner was asked
    ///         to grow sentence 1 and answered with weather — a fact the read had already voiced in
    ///         its closing line — so the bulletin said the temperature twice. The expansion
    ///         instruction already forbids restating covered facts and the grounding gate approved
    ///         it anyway; a sentence-local check cannot see the duplication because each sentence is
    ///         individually fine. Only the whole-script before/after comparison is decisive.
    ///     </para>
    /// </summary>
    public static bool AddsFixtureLine(string before, string after)
    {
        var (weatherBefore, fuelBefore) = CountFixtureLines(before);
        var (weatherAfter, fuelAfter) = CountFixtureLines(after);
        return weatherAfter > weatherBefore || fuelAfter > fuelBefore;
    }

    /// <summary>How many sentences of a read voice weather, and how many voice fuel.</summary>
    public static (int Weather, int Fuel) CountFixtureLines(string script)
    {
        int weather = 0;
        int fuel = 0;
        foreach (string sentence in SplitSentences(script))
        {
            if (IsWeatherLine(sentence)) weather++;
            if (IsFuelLine(sentence)) fuel++;
        }

        return (weather, fuel);
    }

    /// <summary>
    ///     True when <paramref name="after" /> voices a road that <paramref name="before" /> had
    ///     ALREADY voiced somewhere else in the read — the road analogue of
    ///     <see cref="AddsFixtureLine" />, and the same defect one content type over.
    ///     <para>
    ///         Measured on the 2026-08-25 station panel: the sydney 30s read underran at 26.9s, went
    ///         to the sentence refiner to grow toward an 84-word target, and aired the Warringah
    ///         Freeway closure THREE times, the M8 and Sydney Gateway twice each — "The M8 is clear,
    ///         while the Warringah Freeway is closed citybound. Traffic is running clear on Sydney
    ///         Gateway, while the citybound Warringah Freeway is closed at the ramp to Alfred Street
    ///         North." It was the only read on the panel to duplicate a road, and the only 30s read
    ///         that took an extend attempt.
    ///     </para>
    ///     <para>
    ///         The mechanism is structural, not a model lapse. The expansion instruction orders the
    ///         refiner to voice only facts "the read has not yet voiced" and to "never restate what
    ///         the read already covers", but the refiner is handed ONE sentence and its evidence and
    ///         is never shown the read, so it cannot obey either clause. Asked to grow a sentence
    ///         that must also stand alone, it reaches for the most salient nearby fact — the lead
    ///         closure. Every sentence-local guard passes because each sentence is individually well
    ///         formed and fully grounded; only the whole-read before/after comparison is decisive.
    ///     </para>
    ///     <para>
    ///         A road named for the first time is always allowed: that is the cross-street or
    ///         newly-affected road the instruction asks for. Only a road whose mention count RISES
    ///         above a count that was already at least one is a restatement.
    ///     </para>
    /// </summary>
    public static bool AddsRepeatedRoadMention(string before, string after)
    {
        var beforeCounts = CountRoadMentions(before);
        foreach ((string road, int afterCount) in CountRoadMentions(after))
        {
            if (beforeCounts.TryGetValue(road, out int beforeCount)
                && beforeCount >= 1
                && afterCount > beforeCount)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     How many times each road is named in a read, keyed case-insensitively. Matches a
    ///     capitalised name followed by a road-type word ("Warringah Freeway", "Alfred Street"),
    ///     hyphenated and multi-word names ("Nerang-Broadbeach Road", "Sir Donald Bradman Drive"),
    ///     and bare route codes ("M8", "A1"). Deliberately conservative: an unmatched road simply
    ///     never blocks an expansion, whereas a false match would block a legitimate one.
    /// </summary>
    public static IReadOnlyDictionary<string, int> CountRoadMentions(string script)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(script))
            return counts;

        foreach (Match match in RoadMentionPattern().Matches(script))
        {
            string road = match.Value.Trim();
            counts[road] = counts.TryGetValue(road, out int seen) ? seen + 1 : 1;
        }

        return counts;
    }

    // A capitalised, possibly hyphenated or multi-word name followed by a road-type word, or a bare
    // route code. The road-type list mirrors the names that actually reach air in the ARN corpus.
    [GeneratedRegex(
        @"\b(?:[A-Z][a-z]+(?:[-\s][A-Z][a-z]+)*\s"
        + @"(?:Freeway|Motorway|Highway|Road|Street|Avenue|Drive|Tunnel|Circuit|Parade|Place|"
        + @"Gateway|Bypass|Boulevard|Lane|Terrace|Esplanade|Crescent|Way)"
        + @"|[AMB]\d{1,3})\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex RoadMentionPattern();
}

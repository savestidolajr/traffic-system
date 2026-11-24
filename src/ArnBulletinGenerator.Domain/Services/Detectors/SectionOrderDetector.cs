using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services.Detectors;

/// <summary>
///     Deterministic "supplementary section order" gate. A station's <see cref="FillSource" />
///     order is now also its ON-AIR read order for the trailing sections (weather and fuel), and
///     an unenforced read janks (ARN observed weather, then fuel, then an outro reading "That's
///     KIIS traffic and weather"). This fires when (a) the weather and fuel lines appear in the
///     opposite relative order to <see cref="ContentSectionOrder.Resolve" />, or (b) a
///     supplementary line is followed by a later sentence that is neither weather nor fuel, i.e.
///     a section stranded mid-body. <see cref="Repair" /> relocates PURE supplementary sentences
///     (exactly one signal and no corridor road name) to the tail in the configured order; a
///     mixed traffic+weather sentence is left in place and reported through feedback only,
///     mirroring <see cref="UnselectedSourceDetector.Repair" />'s documented posture.
/// </summary>
public static class SectionOrderDetector
{
    public readonly record struct SectionOrderResult(
        bool HasOutOfOrderSection,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public SectionOrderResult(bool hasOutOfOrderSection, string feedback)
            : this(hasOutOfOrderSection, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static SectionOrderResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new SectionOrderResult(false, string.Empty, []);

        var order = ContentSectionOrder.Resolve(context.FillOrder);
        var flags = ContentSourceSignals.SplitSentences(script)
            .Select(s => (Weather: ContentSourceSignals.IsWeatherLine(s), Fuel: ContentSourceSignals.IsFuelLine(s)))
            .ToList();

        // (a) Opposite relative order: the first line of the LATER section precedes the LAST line
        // of the EARLIER section, so the sections reverse or interleave.
        int firstSecond = FindIndex(flags, order[1], fromEnd: false);
        int lastFirst = FindIndex(flags, order[0], fromEnd: true);
        if (firstSecond >= 0 && lastFirst >= 0 && firstSecond < lastFirst)
        {
            string feedback =
                $"SECTION ORDER: {Name(order[1])} reads before {Name(order[0])} here, but this station's " +
                $"on-air order is {ContentSectionOrder.Describe(order)}. Move the {Name(order[1])} line(s) " +
                $"after the {Name(order[0])} line(s) so the read closes {ContentSectionOrder.Describe(order)}.";
            int orderIndex = firstSecond + 1;
            var issue = ReviewIssue.ForSentence(orderIndex, "SECTION_ORDER", feedback);
            return new SectionOrderResult(true, feedback, [issue]);
        }

        // (b) Stranded section: a supplementary line followed by a later sentence that is neither
        // weather nor fuel.
        for (int i = 0; i < flags.Count; i++)
        {
            if (!IsSupplementary(flags[i]))
                continue;
            bool stranded = false;
            for (int j = i + 1; j < flags.Count; j++)
            {
                if (!IsSupplementary(flags[j]))
                {
                    stranded = true;
                    break;
                }
            }

            if (stranded)
            {
                var line = flags[i];
                string feedback =
                    $"STRANDED SECTION: a {Name(line.Weather, line.Fuel)} line sits mid-read with traffic " +
                    $"after it. Move the {Name(line.Weather, line.Fuel)} line to the tail, after the last " +
                    $"traffic item, so the read closes {ContentSectionOrder.Describe(order)}.";
                int orderIndex = i + 1;
                var issue = ReviewIssue.ForSentence(orderIndex, "SECTION_ORDER", feedback);
                return new SectionOrderResult(true, feedback, [issue]);
            }
        }
        return new SectionOrderResult(false, string.Empty, []);
    }

    /// <summary>
    ///     Mechanical last-resort fix (mirrors the other detectors). Every PURE supplementary
    ///     sentence (it trips exactly one of the weather/fuel signals AND names no corridor road)
    ///     is relocated to the tail, in the configured on-air order; the traffic body keeps its
    ///     place. A mixed traffic+weather sentence cannot be safely split, so it stays where it is
    ///     and <see cref="Detect" /> keeps reporting it through feedback. Idempotent whenever
    ///     every offending sentence was pure: the relocated tail is in order and no section is
    ///     stranded.
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        if (!Detect(script, context).HasOutOfOrderSection)
            return script;

        var order = ContentSectionOrder.Resolve(context.FillOrder);
        var roadNames = context.Corridors
            .Select(c => c.RoadName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var kept = new List<string>();
        var weather = new List<string>();
        var fuel = new List<string>();

        foreach (var sentence in ContentSourceSignals.SplitSentences(script))
        {
            bool isWeather = ContentSourceSignals.IsWeatherLine(sentence);
            bool isFuel = ContentSourceSignals.IsFuelLine(sentence);
            bool pure = isWeather != isFuel && !NamesKnownRoad(sentence, roadNames);

            if (!pure)
            {
                kept.Add(sentence);
                continue;
            }

            if (isWeather)
                weather.Add(sentence);
            else
                fuel.Add(sentence);
        }

        foreach (var source in order)
        {
            if (source == FillSource.Weather)
                kept.AddRange(weather);
            else
                kept.AddRange(fuel);
        }

        return string.Join(" ", kept);
    }

    private static bool IsSupplementary((bool Weather, bool Fuel) flags) => flags.Weather || flags.Fuel;

    private static int FindIndex(List<(bool Weather, bool Fuel)> flags, FillSource source, bool fromEnd)
    {
        if (!fromEnd)
        {
            for (int i = 0; i < flags.Count; i++)
                if (source == FillSource.Weather ? flags[i].Weather : flags[i].Fuel)
                    return i;
            return -1;
        }

        for (int i = flags.Count - 1; i >= 0; i--)
            if (source == FillSource.Weather ? flags[i].Weather : flags[i].Fuel)
                return i;
        return -1;
    }

    private static string Name(FillSource source) => source == FillSource.Weather ? "weather" : "fuel";

    private static string Name(bool isWeather, bool isFuel) =>
        isWeather && isFuel ? "weather/fuel" : isWeather ? "weather" : "fuel";

    private static bool NamesKnownRoad(string sentence, IReadOnlyList<string> roadNames)
    {
        foreach (var road in roadNames)
            if (HasWholeWord(sentence, road))
                return true;
        return false;
    }

    /// <summary>
    ///     Whole-phrase, case-insensitive presence check in the repo's whole-word style: the road
    ///     name only counts when voiced as a word, i.e. bounded by non letter/digit characters
    ///     ("M4" matches "the M4" but never "M40" or "M4W"). Deliberately no longer-name
    ///     refinement here: this is a safety check, so the conservative direction is to treat ANY
    ///     whole-word mention of a known road as naming it and refuse to move the sentence.
    /// </summary>
    private static bool HasWholeWord(string text, string name)
    {
        int idx = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            bool leftOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            int end = idx + name.Length;
            bool rightOk = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (leftOk && rightOk)
                return true;
            idx = text.IndexOf(name, idx + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}

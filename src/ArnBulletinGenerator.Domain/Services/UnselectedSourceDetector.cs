using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministic "fabricated unselected source" gate. When weather or fuel was NOT selected for
///     a bulletin (no block reached the writer), the script must say nothing about it: a weather or
///     price line there is pure fabrication (no data backs it). The prompt bans it and the review
///     checks it, but the LLM review is probabilistic and let it ship intermittently (observed live,
///     Sydney CBD traffic-only: "It's 18 degrees and mostly cloudy. Unleaded's cheapest at $1.79 in
///     North Sydney."). This gates it in CODE. The sentence-level signals live in
///     <see cref="ContentSourceSignals" />; they are chosen to be unambiguous: temperature
///     ("18 degrees") and named weather conditions for weather; fuel-type words, "a litre", and a
///     "$1.xx" price for fuel, and deliberately EXCLUDE road-ambiguous words ("clear", "fine",
///     "wind") so a normal traffic read never trips it.
/// </summary>
public static class UnselectedSourceDetector
{
    public readonly record struct UnselectedSourceResult(
        bool HasFabrication,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public UnselectedSourceResult(bool hasFabrication, string feedback)
            : this(hasFabrication, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static UnselectedSourceResult Detect(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new UnselectedSourceResult(false, string.Empty, []);

        var sentences = ContentSourceSignals.SplitSentences(script);
        bool weatherFabricated = context.Weather.IsNone && sentences.Any(ContentSourceSignals.IsWeatherLine);
        bool fuelFabricated = context.Fuel.IsNone && sentences.Any(ContentSourceSignals.IsFuelLine);

        if (!weatherFabricated && !fuelFabricated)
            return new UnselectedSourceResult(false, string.Empty, []);

        string what = weatherFabricated && fuelFabricated ? "weather and fuel"
            : weatherFabricated ? "weather" : "fuel";
        string feedback =
            $"FABRICATED SOURCE: the script mentions {what}, but no {what} data was provided for this bulletin — " +
            "that is invented content. This is a traffic-only read: remove the " +
            (weatherFabricated ? "temperature/conditions" : "") +
            (weatherFabricated && fuelFabricated ? " and " : "") +
            (fuelFabricated ? "price/fuel" : "") +
            " line entirely and end on the last real traffic item.";

        var scriptSentences = ScriptSentenceSplitter.Split(script);
        var issues = new List<ReviewIssue>();

        for (int i = 0; i < scriptSentences.Count; i++)
        {
            string sentence = scriptSentences[i];
            int order = i + 1;
            if (weatherFabricated && ContentSourceSignals.IsWeatherLine(sentence))
            {
                issues.Add(ReviewIssue.ForSentence(
                    order,
                    "UNSELECTED_SOURCE",
                    "FABRICATED SOURCE: weather data was not provided for this bulletin. Remove this weather line."));
            }
            if (fuelFabricated && ContentSourceSignals.IsFuelLine(sentence))
            {
                issues.Add(ReviewIssue.ForSentence(
                    order,
                    "UNSELECTED_SOURCE",
                    "FABRICATED SOURCE: fuel data was not provided for this bulletin. Remove this fuel line."));
            }
        }

        if (issues.Count == 0)
        {
            issues.Add(ReviewIssue.ForUnresolvedSentence("UNSELECTED_SOURCE", feedback));
        }

        return new UnselectedSourceResult(true, feedback, issues);
    }

    /// <summary>
    ///     Mechanical last-resort fix (mirrors the other detectors): drop any sentence carrying the
    ///     fabricated-source signal. Weather and fuel are voiced as their own sentences, so removing
    ///     the offending sentence leaves the real traffic content intact. Only strips the class that
    ///     was actually unselected: a selected source's line is never touched.
    /// </summary>
    public static string Repair(string script, BroadcastWithTraffic context)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        bool stripWeather = context.Weather.IsNone;
        bool stripFuel = context.Fuel.IsNone;
        if (!stripWeather && !stripFuel)
            return script;

        var kept = ContentSourceSignals.SplitSentences(script)
            .Where(s => !(stripWeather && ContentSourceSignals.IsWeatherLine(s))
                && !(stripFuel && ContentSourceSignals.IsFuelLine(s)));

        return string.Join(" ", kept);
    }
}

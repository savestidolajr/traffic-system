using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services.Detectors;

/// <summary>
///     Deterministic "required source missing" gate. When a station marks weather or fuel as a
///     FIXTURE (selected AND required) and the provider's data was actually fetched, that section
///     must air: the whole point of "always include" is that the data backs the line, so a script
///     that fetched the data and then never voiced it silently broke the station's promise. This
///     fires only when the data IS present (the Option has a value): a source whose fetch failed
///     is logged and the bulletin airs without it, so this gate never invents or demands a fact
///     the bulletin does not hold. There is deliberately no <c>Repair</c>: a missing fact cannot
///     be mechanically invented, so this is a feedback-only blocking gate.
/// </summary>
public static class RequiredSourceDetector
{
    public readonly record struct RequiredSourceResult(
        bool HasMissingSource,
        string Feedback,
        IReadOnlyList<ReviewIssue> Issues)
    {
        public RequiredSourceResult(bool hasMissingSource, string feedback)
            : this(hasMissingSource, feedback, [])
        {
        }

        public ReviewIssue? Issue => Issues.Count > 0 ? Issues[0] : null;
    }

    public static RequiredSourceResult Detect(string script, BroadcastWithTraffic context)
    {
        var sentences = ContentSourceSignals.SplitSentences(script);

        bool weatherMissing = context.Content.RequiresWeather && context.Weather.HasValue
            && !sentences.Any(ContentSourceSignals.IsWeatherLine);
        bool fuelMissing = context.Content.RequiresFuel && context.Fuel.HasValue
            && !sentences.Any(ContentSourceSignals.IsFuelLine);

        if (!weatherMissing && !fuelMissing)
            return new RequiredSourceResult(false, string.Empty, []);

        string what = weatherMissing && fuelMissing ? "weather and fuel" : weatherMissing ? "weather" : "fuel";
        string data = weatherMissing && fuelMissing
            ? "weather and fuel data were fetched"
            : weatherMissing ? "weather data was fetched" : "fuel data was fetched";
        string cannotDrop = weatherMissing && fuelMissing
            ? "the weather and fuel sections cannot be dropped"
            : weatherMissing ? "the weather section cannot be dropped" : "the fuel section cannot be dropped";

        string feedback =
            $"REQUIRED SOURCE MISSING: {data} for this bulletin and this station always carries {what}, " +
            $"but no sentence in the script reads as {what}. Voice the supplied {what} data and place it " +
            $"with the read; {cannotDrop}.";

        // Script-scoped: the bulletin as a whole never voices fuel/weather. Order is null.
        var issue = ReviewIssue.ForScript("REQUIRED_SOURCE_MISSING", feedback);

        return new RequiredSourceResult(true, feedback, [issue]);
    }
}

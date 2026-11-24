using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Formats non-traffic content into the same factual-bullet style as the traffic data:
///     raw facts for the LLM, no prose — the model does the creative writing and decides
///     how much fits the word budget.
/// </summary>
public static partial class ContentFormatters
{
    /// <summary>
    ///     Translates HERE's raw condition vocabulary (aviation/METAR-style jargon like "Sprinkles",
    ///     "Broken clouds", "Overcast") to natural spoken-presenter phrasing (BFN-022: passing the raw
    ///     token through verbatim produced "Sprinkles and 17 degrees" on air, which no presenter would
    ///     say). Keyed on HERE Destination Weather's observed condition vocabulary; anything not in
    ///     the table falls through unchanged — most of HERE's phrasing ("Partly cloudy", "Sunny",
    ///     "Thunderstorms") is already natural English and needs no translation.
    /// </summary>
    private static readonly Dictionary<string, string> NaturalWeatherPhrasing = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sprinkles"] = "showery",
        ["Drizzle"] = "drizzly",
        ["Broken clouds"] = "patchy cloud",
        ["Scattered clouds"] = "partly cloudy",
        ["Overcast"] = "cloudy",
        ["Fair"] = "fine",
        ["Clear"] = "clear",
        ["Haze"] = "hazy",
        ["Fog"] = "foggy",
        ["Mist"] = "misty",
        ["Flurries"] = "snowy",
        ["Dust"] = "dusty",
        ["Smoke"] = "smoky",
    };

    private static string NaturalizeCondition(string condition) =>
        NaturalWeatherPhrasing.TryGetValue(condition.Trim(), out string? natural) ? natural : condition;

    /// <summary>
    ///     Provider alert wording rendered as ARN says it on air. The conditions table above exists
    ///     for the same reason; alerts never had one, so HERE's phrasing reached the writer verbatim
    ///     and then reached air.
    ///     <para>
    ///         Measured on test, 2026-08-25: four of eleven bulletins paired a dry condition with
    ///         "Heavy rain anticipated" and the operators rewrote EVERY one: to "Showers
    ///         today. 20 degrees.", to "Sunny, 18 degrees, with showers on the way", and to "Showers
    ///         across Sydney today". Two things jarred: "anticipated" is not radio English, and the
    ///         reading behind it was a 57% chance of precipitation, which does not support "heavy".
    ///         The replacement carries the forecast AND the time sense the operators added, so the
    ///         line stops contradicting the condition beside it.
    ///     </para>
    ///     <para>
    ///         Unknown alerts pass through untouched, so a genuine severe-weather warning is never
    ///         softened by this table — only wording ARN has explicitly ruled on is rewritten.
    ///     </para>
    /// </summary>
    private static readonly Dictionary<string, string> NaturalAlertPhrasing = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Heavy rain anticipated"] = "showers on the way",
        ["Rain anticipated"] = "showers on the way",
    };

    private static string NaturalizeAlert(string alert) =>
        NaturalAlertPhrasing.TryGetValue(alert.Trim(), out string? natural) ? natural : alert;

    /// <summary>Whole-word, case-insensitive check for the rain word family — used to avoid
    /// stating the same rain fact twice via two independent HERE fields (a text condition and a
    /// separate precipitation-probability percentage, or a text condition and a text alert).</summary>
    [GeneratedRegex(@"\b(rain|shower|drizzle|storm|downpour|precipitation)\w*\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RainWordRegex();

    private static bool ContainsRainWord(string text) => RainWordRegex().IsMatch(text);

    public static string FormatWeather(WeatherReport weather)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Weather data:");
        string rawCondition = weather.Conditions
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? weather.Conditions;
        string condition = NaturalizeCondition(rawCondition);
        sb.Append("- Conditions: ").Append(condition);
        long roundedTemp = (long)Math.Round(weather.TemperatureC);
        if (weather.IsForecast)
        {
            if (roundedTemp > 0)
                sb.Append(", around ").Append(roundedTemp.ToString(CultureInfo.InvariantCulture)).Append(" degrees today");
        }
        else
        {
            sb.Append(", ").Append(roundedTemp.ToString(CultureInfo.InvariantCulture)).Append(" degrees");
        }
        sb.AppendLine();

        foreach (string alert in weather.Alerts)
        {
            if (ContainsRainWord(condition) && ContainsRainWord(alert))
                continue;
            sb.Append("- ALERT: ").AppendLine(NaturalizeAlert(alert));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     Formats only the cheapest price for each supplied fuel type, retaining the station suburb
    ///     on the same factual line whenever the feed provides it.
    /// </summary>
    /// <remarks>
    ///     The previous same-suburb hint told the writer to say "there too". That preserved the
    ///     no-repetition intent but nudged the model toward one rigid fuel frame (the defect ARN
    ///     observed in live pilot reads). Since 2026-08-24 each price line keeps its literal suburb,
    ///     while a separate note allows one natural shared-suburb mention without dictating its shape.
    ///     Missing suburbs remain missing: they are never inferred from a neighbouring summary.
    /// </remarks>
    public static string FormatFuel(FuelPriceReport fuel)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Fuel price data (per litre, from station price reports in the coverage area):");
        string? previousSuburb = null;
        bool hasSharedSuburb = false;
        foreach (var summary in fuel.Summaries)
        {
            string? suburb = string.IsNullOrWhiteSpace(summary.CheapestStationSuburb)
                ? null
                : summary.CheapestStationSuburb.Trim();

            // Spoken, never numeric. ARN, 2026-08-25: give the agent the price already in the form
            // we want said. A bare "$2.23" reached air on the port lincoln panel read, and
            // ElevenLabs does not reliably speak a currency token — that is a misread, not a style
            // slip. The conversion is deterministic, so it is not the writer's job.
            // The grade as a presenter says it — see SpokenFuelGrade, which owns this for every
            // path after a hand-copied third instance aired the octane on dev brisbane e91805be.
            string grade = SpokenFuelGrade.Of(summary.FuelType);
            sb.Append("- ").Append(grade)
                .Append(": cheapest ")
                .Append(SpokenPriceNormalizer.Normalize(
                    "$" + summary.MinPrice.ToString("F2", CultureInfo.InvariantCulture)));
            if (suburb is not null)
            {
                if (string.Equals(previousSuburb, suburb, StringComparison.OrdinalIgnoreCase))
                    hasSharedSuburb = true;
                sb.Append(" in ").Append(suburb);
            }

            sb.AppendLine();
            previousSuburb = suburb;
        }

        if (hasSharedSuburb)
            sb.AppendLine(
                "Fuel wording note: when prices share a suburb, name it once naturally in a combined sentence; do not repeat the suburb mechanically or drop it.");

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     The good-run road names the prose is actually SHOWN. A quiet read is offered 4, a busy read
    ///     (3+ voiceable incidents) only 2 — so "are there flowing roads left to add?" must be asked
    ///     against this capped set, never the full <c>GoodRunRoads</c> list. Callers that get this wrong
    ///     conclude there is more to voice when the writer was never shown it (job fa754637).
    /// </summary>
    public static IReadOnlyList<string> NamedGoodRuns(
        IReadOnlyList<string> goodRunRoads, int voiceableIncidentCount) =>
        goodRunRoads.Take(voiceableIncidentCount >= 3 ? 2 : 4).ToList();

    /// <param name="flowPool">
    ///     The ranked flow pool the good-run names came from, used only to look up each road's
    ///     suburb. A clear-run mention is the one item carrying no cross-street and no detail, so
    ///     without this the writer is handed a bare "Victoria Road" — and Sydney has many (ARN,
    ///     2026-08-24). Empty or unmatched leaves the name alone rather than guessing.
    /// </param>
    public static string FormatGoodRuns(
        IReadOnlyList<string> goodRunRoads,
        int voiceableIncidentCount,
        IReadOnlyList<FlowRoad>? flowPool = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Clear \u2014 no incidents reported on these roads (voice as a clear run, varying the wording: \"moving nicely\", \"looking good\", \"no dramas\", \"a clear run\"). A suburb in brackets is part of that road's identity \u2014 voice it, because the name alone does not locate the road:");
        foreach (string road in NamedGoodRuns(goodRunRoads, voiceableIncidentCount))
        {
            string? suburb = flowPool?
                .FirstOrDefault(f => RoadNameFormatter.IsSameRoad(f.RoadName, road))?.Suburb;
            sb.Append("- ").AppendLine(string.IsNullOrWhiteSpace(suburb)
                ? RoadNameFormatter.ToSpeakable(road)
                : $"{RoadNameFormatter.ToSpeakable(road)} (at {suburb})");
        }

        if (voiceableIncidentCount >= 3)
            sb.AppendLine(
                "- (busy read: if voiced at all, name these TOGETHER in one short clause with one shared state " +
                "(\"X and Y are both moving nicely\") \u2014 never separate clauses repeating the same verb; a usual " +
                "hotspot that's clear IS news, otherwise skip them. NEVER claim anything about roads not named " +
                "here \u2014 no \"the rest is clear\", \"no other dramas\" sweeps: a clear claim is a fact about a " +
                "NAMED road or it is not said)");
        else if (voiceableIncidentCount > 0)
            sb.AppendLine(
                "- (quiet read: these clear roads are real content \u2014 name them plainly, grouped naturally " +
                "(\"X and Y are moving well\"). Speak ONLY about the roads named here; a general opener like " +
                "\"it's a good run across town\" is fine ONLY when a named incident is stated right alongside it; " +
                "never a claim about unnamed roads)");
        else
            sb.AppendLine(
                "- (all-clear read: the absence of trouble IS the story \u2014 lead with it plainly " +
                "(\"No major delays across [market] this morning\"), then name these roads as the evidence. " +
                "Never claim more than the named roads support)");

        return sb.ToString().TrimEnd();
    }

    public static string FormatFutureLessons(IReadOnlyList<FutureLessonEntry> entries, int maxLength = 300)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            string sanitized = PromptSafe.Sanitize(entry.Text);
            string text = sanitized.Length > maxLength
                ? sanitized[..maxLength] + "\u2026"
                : sanitized;
            sb.Append("- ");
            sb.Append('[').Append(entry.Category).Append("] ");
            sb.AppendLine(text);
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatGuidance(ComposedGuidance guidance)
    {
        var sb = new StringBuilder();
        foreach (var g in guidance.Entries)
        {
            if (g.Type == GuidanceType.HouseStyle)
                continue;
            sb.Append("- [").Append(g.Scope.ToString().ToLowerInvariant()).Append("] ").AppendLine(g.Text);
        }

        return sb.ToString().TrimEnd();
    }


}

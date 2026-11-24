using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class ContentFormattersTests
{
    // ─── FormatWeather ────────────────────────────────────────────────────

    [Fact]
    public void FormatWeather_ContainsConditionsAndRoundedTemp()
    {
        var weather = new WeatherReport("Sunny", 22.7, default, 10, []);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldContain("Weather data:");
        result.ShouldContain("Conditions: Sunny");
        result.ShouldContain("23 degrees");
    }

    [Theory]
    [InlineData("Sprinkles", "showery")]
    [InlineData("Overcast", "cloudy")]
    [InlineData("Fog", "foggy")]
    [InlineData("Broken clouds", "patchy cloud")]
    public void FormatWeather_NaturalizesHereConditionVocabulary(string hereCondition, string expectedPhrasing)
    {
        var weather = new WeatherReport(hereCondition, 20.0, default, 10, []);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldContain($"Conditions: {expectedPhrasing}, 20 degrees");
    }

    [Fact]
    public void FormatWeather_LeavesAlreadyNaturalConditionsUnchanged()
    {
        var weather = new WeatherReport("Partly cloudy", 20.0, default, 10, []);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldContain("Conditions: Partly cloudy");
    }

    [Fact]
    public void FormatWeather_DistilsMultiClauseConditionsToPrimary()
    {
        var weather = new WeatherReport("Sprinkles. Broken clouds. Cool.", 16.0, default, 30, []);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldContain("Conditions: showery, 16 degrees");
        result.ShouldNotContain("Sprinkles");
        result.ShouldNotContain("Broken clouds");
    }

    [Fact]
    public void FormatWeather_NeverRendersWind()
    {
        // BFN weather rule: ONE line — condition + temperature. Wind is deliberately not emitted
        // even when HERE reports it (frozen-eval baseline: 3 of 5 weather scripts voiced wind or
        // rain-chance under prompt-only enforcement). Alerts are the only extra channel.
        var weather = new WeatherReport("Cloudy", 18.0, "strong breeze", 30, []);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldNotContain("Wind");
        result.ShouldContain("Conditions: Cloudy, 18 degrees");
    }

    [Fact]
    public void FormatWeather_NeverRendersRainChance()
    {
        var weather = new WeatherReport("Cloudy", 18.0, default, 95, []);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldNotContain("Chance of rain");
    }

    [Fact]
    public void FormatWeather_IncludesAlertLines()
    {
        var weather = new WeatherReport("Stormy", 25.0, default, 60,
            ["Severe thunderstorm warning", "Flash flood watch"]);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldContain("ALERT: Severe thunderstorm warning");
        result.ShouldContain("ALERT: Flash flood watch");
    }


    [Fact]
    public void FormatWeather_RainConditionSuppressesRedundantRainAlert()
    {
        // The ALERT line is a THIRD independent HERE field; when it restates the same rain fact
        // Conditions already carries, it must also be suppressed — but HasMajorAlert on the
        // underlying WeatherReport model is untouched (still true), only the rendered text changes.
        var weather = new WeatherReport("Heavy rain", 11.0, default, 88, ["Heavy rain anticipated"]);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldNotContain("ALERT");
        weather.HasMajorAlert.ShouldBeTrue();
    }

    [Fact]
    public void FormatWeather_NonRainAlertAlongsideRainCondition_IsKept()
    {
        // A DIFFERENT hazard (heat, not rain) must survive even when Conditions mentions rain.
        var weather = new WeatherReport("Light rain", 38.0, default, 40, ["Extreme heat warning"]);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldContain("ALERT: Extreme heat warning");
    }

    [Fact]
    public void FormatWeather_RainAlertAlongsideNonRainCondition_IsKept()
    {
        // A rain alert with a non-rain condition text (e.g. "Cloudy") is genuinely new information
        // — must survive.
        var weather = new WeatherReport("Cloudy", 18.0, default, 60, ["Severe storm warning"]);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldContain("ALERT: Severe storm warning");
    }


    [Fact]
    public void FormatWeather_NoAlerts_OmitsAlertLines()
    {
        var weather = new WeatherReport("Fine", 30.0, default, 5, []);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldNotContain("ALERT");
    }

    [Fact]
    public void FormatWeather_NeverShowsOutlook()
    {
        var weather = new WeatherReport("Sunny", 22.0, default, 10, [],
            Outlook: "reaching 24 degrees later today; tomorrow showers, around 19 degrees");

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldNotContain("Outlook");
        result.ShouldNotContain("reaching 24");
    }

    [Fact]
    public void FormatWeather_OmitsOutlookWhenNone()
    {
        var weather = new WeatherReport("Sunny", 22.0, default, 10, []);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldNotContain("Outlook:");
    }

    [Fact]
    public void FormatWeather_ForecastOnly_VoicesForecastFramingNotCurrentReading()
    {
        // No current observation → forecast fallback. The high must read as a forecast
        // ("around N degrees today"), never as a current reading ("N degrees").
        var weather = new WeatherReport("Sunny", 25.0, default, 0, [], IsForecast: true);

        string result = ContentFormatters.FormatWeather(weather);

        result.ShouldContain("Conditions: Sunny, around 25 degrees today");
    }

    // ─── FormatFuel ───────────────────────────────────────────────────────

    [Fact]
    public void FormatFuel_FormatsMinPriceToTwoDecimals()
    {
        var fuel = new FuelPriceReport([
            new FuelPriceSummary("Unleaded", 1.8, 1.9234, 15),
        ]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldContain("cheapest a dollar eighty");
    }

    [Fact]
    public void FormatFuel_NeverIncludesAveragePriceOrStationCount()
    {
        // draft.md line 47 explicitly bans reciting the average price or station count — a
        // single price+suburb is the only fact the writer should ever be handed (observed live:
        // Gold Coast recited both under retry pressure when they were still fed to the model).
        // Distinct min/average/count values per summary so no assertion can pass by collision.
        var fuel = new FuelPriceReport([
            new FuelPriceSummary("Unleaded", 1.48, 1.9234, 15),
            new FuelPriceSummary("Diesel", 1.70, 1.8877, 19),
        ]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldNotContain("average");
        result.ShouldNotContain("1.92");
        result.ShouldNotContain("1.89");
        result.ShouldNotContain("stations)");
        result.ShouldNotContain("15");
        result.ShouldNotContain("19");

        // The min price still reaches the writer — now in the form it is read aloud in, so no
        // numeral can survive to synthesis. Both prices must always be present.
        result.ShouldContain("a dollar forty-eight");
        result.ShouldContain("a dollar seventy");
    }

    [Fact]
    public void FormatFuel_NeverIncludesLatestModifiedTimestamp()
    {
        // LatestModifiedUtc is data-feed metadata a presenter never voices — the model read it
        // aloud verbatim ("it's 14:33 UTC in the latest fuel reports") when handed it, so it is
        // deliberately kept out of the prompt data. The freshness gate lives in code.
        var fuel = new FuelPriceReport([
            new FuelPriceSummary("Unleaded", 1.8, 1.92, 15, DateTimeOffset.Parse("2026-06-17T03:30:00Z")),
        ]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldNotContain("latest update");
        result.ShouldNotContain("UTC");
    }

    [Fact]
    public void FormatFuel_MultipleSummaries()
    {
        var fuel = new FuelPriceReport([
            new FuelPriceSummary("Unleaded", 1.85, 1.92, 15),
            new FuelPriceSummary("Premium", 2.05, 2.12, 8),
        ]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldContain("Unleaded");
        result.ShouldContain("Premium");
    }

    [Fact]
    public void FormatFuel_EmptySummaries_ReturnsHeaderOnly()
    {
        var fuel = new FuelPriceReport([]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldBe("Fuel price data (per litre, from station price reports in the coverage area):");
    }

    [Fact]
    public void FormatFuel_IncludesSuburbWhenPresent()
    {
        var fuel = new FuelPriceReport([
            new FuelPriceSummary("Unleaded", 1.45, 1.60, 10, null, "Paddington"),
        ]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldContain("cheapest a dollar forty-five in Paddington");
    }

    [Fact]
    public void FormatFuel_DifferentSuburbsKeepsEachPriceLocation()
    {
        var fuel = new FuelPriceReport([
            new FuelPriceSummary("Unleaded 91", 1.93, 2.01, 7, CheapestStationSuburb: "Kilburn"),
            new FuelPriceSummary("Diesel", 2.38, 2.44, 5, CheapestStationSuburb: "Windsor Gardens"),
        ]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldContain("unleaded: cheapest a dollar ninety-three in Kilburn");
        result.ShouldContain("diesel: cheapest two dollars thirty-eight in Windsor Gardens");
    }

    [Fact]
    public void FormatFuel_SameSuburbKeepsEachLocationFact_WithoutRigidThereTooHint()
    {
        var fuel = new FuelPriceReport([
            new FuelPriceSummary("Unleaded 91", 1.98, 2.05, 12, CheapestStationSuburb: "Manoora"),
            new FuelPriceSummary("Diesel", 2.40, 2.49, 9, CheapestStationSuburb: "Manoora"),
        ]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldContain("unleaded: cheapest a dollar ninety-eight in Manoora");
        result.ShouldContain("diesel: cheapest two dollars forty in Manoora");
        result.ShouldContain("name it once naturally in a combined sentence");
        result.ShouldNotContain("there too");
    }

    [Fact]
    public void FormatFuel_OmitsSuburbWhenNull()
    {
        var fuel = new FuelPriceReport([
            new FuelPriceSummary("Unleaded", 1.45, 1.60, 10, null, null),
        ]);

        string result = ContentFormatters.FormatFuel(fuel);

        result.ShouldContain("cheapest a dollar forty-five");
        result.ShouldNotContain("cheapest a dollar forty-five in");
    }

    // ─── FormatGoodRuns ───────────────────────────────────────────────────

    [Fact]
    public void FormatGoodRuns_BusyRead_CapsAtTwoAndBansUnnamedClaims()
    {
        var result = ContentFormatters.FormatGoodRuns(["M1", "M2", "M3", "M4"], voiceableIncidentCount: 3);

        result.ShouldContain("M1");
        result.ShouldContain("M2");
        result.ShouldNotContain("M3"); // busy: at most two named, no residual claim offered
        result.ShouldContain("TOGETHER in one short clause");
        result.ShouldContain("NEVER claim anything about roads not named");
        result.ShouldStartWith("Clear — no incidents");
        result.ShouldNotContain("Priority roads"); // Rec1: backstage label never handed to the writer
        result.ShouldNotContain("flowing well");    // Rec1: no single stock verb mandated as the state
    }

    [Fact]
    public void FormatGoodRuns_QuietRead_NamesUpToFour_NoMarketWideClaim()
    {
        var result = ContentFormatters.FormatGoodRuns(["M1", "M2", "M3", "M4", "M5"], voiceableIncidentCount: 1);

        result.ShouldContain("M4"); // quiet: the names ARE the story (Darwin names five)
        result.ShouldNotContain("M5"); // capped under the roll-call gate
        result.ShouldContain("never a claim about unnamed roads");
    }

    [Fact]
    public void FormatGoodRuns_AllClearRead_LeadsMarketWide()
    {
        var result = ContentFormatters.FormatGoodRuns(["M1", "M2"], voiceableIncidentCount: 0);

        result.ShouldContain("all-clear read");
        result.ShouldContain("No major delays across");
    }

    [Fact]
    public void FormatGoodRuns_EmptyList_ReturnsHeaderAndAllClearNote()
    {
        string result = ContentFormatters.FormatGoodRuns([], voiceableIncidentCount: 0);

        result.ShouldStartWith("Clear — no incidents reported on these roads");
    }

    // ─── FormatFutureLessons ─────────────────────────────────────────────

    [Fact]
    public void FormatFutureLessons_EmitsClosedCategoryAndInstruction()
    {
        var entry = new FutureLessonEntry(
            "lesson-1",
            "KIIS 101.1",
            "job1",
            FutureLessonCategory.PriorityOrdering,
            "Lead with a full motorway closure before ordinary congestion.",
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            1,
            new DateTime(2026, 4, 15, 10, 30, 0, DateTimeKind.Utc));

        string result = ContentFormatters.FormatFutureLessons([entry]);

        result.ShouldBe(
            "- [PriorityOrdering] Lead with a full motorway closure before ordinary congestion.");
    }

    [Fact]
    public void FormatFutureLessons_TruncatesToPromptBudget()
    {
        string instruction = new('A', 210);
        var entry = new FutureLessonEntry(
            "lesson-1",
            "KIIS 101.1",
            "job1",
            FutureLessonCategory.WordingStyle,
            instruction,
            DateTime.UtcNow,
            DateTime.UtcNow,
            1,
            DateTime.UtcNow.AddDays(90));

        string result = ContentFormatters.FormatFutureLessons([entry], maxLength: 200);

        result.ShouldContain(new string('A', 200) + "…");
        result.ShouldNotContain(instruction);
    }

    [Fact]
    public void FormatFutureLessons_EmptyEntries_ReturnsEmpty()
    {
        ContentFormatters.FormatFutureLessons([]).ShouldBeEmpty();
    }
}

using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class UnselectedSourceDetectorTests
{
    // Traffic-only context by default: an unset Option<T> is None, so Ctx() has neither weather
    // nor fuel data — exactly the "was not selected" state the detector treats as fabricatable.
    private static BroadcastWithTraffic Ctx(
        Option<WeatherReport> weather = default,
        Option<FuelPriceReport> fuel = default) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Test",
            Traffic = string.Empty,
            Incidents = [],
            Weather = weather,
            Fuel = fuel
        };

    private static WeatherReport SomeWeather() => new("Cloudy", 18.0, default, 10, []);

    private static FuelPriceReport SomeFuel() =>
        new([new FuelPriceSummary("Unleaded", 1.79, 1.85, 10)]);

    // ─── Fabrication detection ────────────────────────────────────────────

    [Fact]
    public void Weather_only_unselected_temperature_and_condition_flags_weather()
    {
        var result = UnselectedSourceDetector.Detect(
            "The M4 is clear. It's 18 degrees and mostly cloudy.", Ctx());

        result.HasFabrication.ShouldBeTrue();
        result.Feedback.ShouldContain("FABRICATED SOURCE");
        result.Feedback.ShouldContain("weather");
        // weather-only: the feedback must not name fuel (would mean it mislabelled the source).
        result.Feedback.ShouldNotContain("fuel");
    }

    [Fact]
    public void Fuel_only_unselected_price_and_fueltype_flags_fuel()
    {
        var result = UnselectedSourceDetector.Detect(
            "Warringah Freeway is shut. Unleaded's cheapest at $1.79 in North Sydney.", Ctx());

        result.HasFabrication.ShouldBeTrue();
        result.Feedback.ShouldContain("FABRICATED SOURCE");
        result.Feedback.ShouldContain("fuel");
        result.Feedback.ShouldNotContain("weather");
    }

    [Fact]
    public void Both_sources_unselected_flags_and_names_weather_and_fuel()
    {
        var result = UnselectedSourceDetector.Detect(
            "The M4 is clear. It's 18 degrees and cloudy. Unleaded's cheapest at $1.79.", Ctx());

        result.HasFabrication.ShouldBeTrue();
        result.Feedback.ShouldContain("weather and fuel");
    }

    // ─── Selected sources are never fabrication ───────────────────────────

    [Fact]
    public void Selected_weather_is_not_flagged_even_with_temperature_and_condition()
    {
        // Same content that fabricates when weather is unselected: keyed off IsNone, not the words.
        var result = UnselectedSourceDetector.Detect(
            "It's 18 degrees and cloudy.", Ctx(weather: SomeWeather()));

        result.HasFabrication.ShouldBeFalse();
    }

    [Fact]
    public void Selected_fuel_is_not_flagged_even_with_a_price()
    {
        var result = UnselectedSourceDetector.Detect(
            "Unleaded's cheapest at $1.79.", Ctx(fuel: SomeFuel()));

        result.HasFabrication.ShouldBeFalse();
    }

    [Fact]
    public void Both_selected_never_fabricates_regardless_of_content()
    {
        var result = UnselectedSourceDetector.Detect(
            "It's 18 degrees and cloudy. Unleaded's cheapest at $1.79.",
            Ctx(weather: SomeWeather(), fuel: SomeFuel()));

        result.HasFabrication.ShouldBeFalse();
    }

    // ─── CRITICAL false-positive guard: road-ambiguous words ──────────────

    [Fact]
    public void Normal_clear_traffic_run_is_not_flagged_as_fabricated_weather()
    {
        // "clear" and "running well" describe roads, not weather — they must never trip the
        // weather signal on a traffic-only read (the whole reason the signal excludes them).
        var result = UnselectedSourceDetector.Detect(
            "Kuringa Drive is clear and the rest of the run's clear. City Road is running well.",
            Ctx());

        result.HasFabrication.ShouldBeFalse();
    }

    [Fact]
    public void Road_words_fine_and_wind_do_not_trip_the_weather_signal()
    {
        var result = UnselectedSourceDetector.Detect(
            "The Pacific Highway is fine. There's a fresh wind holding traffic up on the bridge.",
            Ctx());

        result.HasFabrication.ShouldBeFalse();
    }

    // Regression, deployed dev job 8d8e5ec1680b48488028d30fc91ed43b (darwin, weather NOT
    // selected, fuel selected), 2026-08-10: the road name "Fog Bay Road" begins with the weather
    // word "Fog", and the old signal scored the lead traffic sentence as fabricated weather,
    // burning a refine attempt and shipping an unresolved-gate warning on a defect that did not
    // exist. Verbatim shipped script on purpose; fuel is selected as in the deployed job, so the
    // only question is whether "Fog" fabricates weather.
    [Fact]
    public void Weather_word_inside_a_road_name_is_not_fabricated_weather()
    {
        const string script =
            "Fog Bay Road's slow going eastbound, with construction across about fifteen kays "
            + "between Charles Point Road and Railway Alice Springs. Cox Peninsula Road's got "
            + "construction across about fifteen kays citybound from Railway Alice Springs. "
            + "McMillans Road's a bit slow westbound with construction. Unleaded's CHEAPEST at "
            + "two-oh-four in Belyuen, diesel two-forty-three there too!";

        var result = UnselectedSourceDetector.Detect(script, Ctx(fuel: SomeFuel()));

        result.HasFabrication.ShouldBeFalse();
    }

    [Fact]
    public void Weather_word_road_names_do_not_trip_the_weather_signal()
    {
        // The same lexical rule applied to the other name shapes: a condition word followed by a
        // capitalised word or a road-type word is a road name, not weather. "Rainbow" was never
        // matched (word boundary), the others only by the exclusion.
        var result = UnselectedSourceDetector.Detect(
            "Sunny Street is closed for roadworks. Storm King Drive is chockers. Rainbow Beach "
            + "Road's slow going northbound.",
            Ctx());

        result.HasFabrication.ShouldBeFalse();
    }

    [Fact]
    public void Ordinary_weather_prose_still_flags_when_weather_is_unselected()
    {
        // Real corpus shapes (dev bulletins): the condition word continues into lowercase prose,
        // so the name-word exclusion must not silence them.
        var result = UnselectedSourceDetector.Detect(
            "The M4 is clear. Passing clouds, fourteen degrees! It's cloudy and nine degrees.",
            Ctx());

        result.HasFabrication.ShouldBeTrue();
        result.Feedback.ShouldContain("weather");
    }

    [Fact]
    public void Spoken_word_temperature_on_a_traffic_only_read_is_fabrication()
    {
        // Same root cause as the required-source false negative found on dev 2026-08-10: the
        // shipped read spells numbers out, so a digit-anchored temperature signal let a
        // fabricated "seventeen degrees" ship on a traffic-only bulletin. "degrees" carries no
        // traffic meaning, so matching the bare word costs no precision here either.
        var result = UnselectedSourceDetector.Detect(
            "The M4 is clear. Clear, seventeen degrees!", Ctx());

        result.HasFabrication.ShouldBeTrue();
        UnselectedSourceDetector.Repair("The M4 is clear. Clear, seventeen degrees!", Ctx())
            .ShouldBe("The M4 is clear.");
    }

    // ─── Repair ───────────────────────────────────────────────────────────

    [Fact]
    public void Repair_strips_the_fabricated_weather_sentence_and_keeps_traffic()
    {
        const string script = "The M4 is clear. It's 18 degrees and mostly cloudy.";

        string repaired = UnselectedSourceDetector.Repair(script, Ctx());

        repaired.ShouldContain("The M4 is clear");
        repaired.ShouldNotContain("degrees");
        repaired.ShouldNotContain("cloudy");
        // The repaired script must itself be clean of fabrication.
        UnselectedSourceDetector.Detect(repaired, Ctx()).HasFabrication.ShouldBeFalse();
    }

    [Fact]
    public void Repair_only_strips_the_unselected_class_keeping_the_selected_one()
    {
        // Weather is selected (Some), fuel is not: the temperature sentence stays, the fabricated
        // price sentence goes.
        const string script = "It's 18 degrees and cloudy. Unleaded's cheapest at $1.79.";

        string repaired = UnselectedSourceDetector.Repair(script, Ctx(weather: SomeWeather()));

        repaired.ShouldContain("18 degrees");
        repaired.ShouldContain("cloudy");
        repaired.ShouldNotContain("Unleaded");
        repaired.ShouldNotContain("1.79");
    }

    // ─── Blank input ──────────────────────────────────────────────────────

    [Fact]
    public void Blank_script_is_not_fabrication()
    {
        UnselectedSourceDetector.Detect(string.Empty, Ctx()).HasFabrication.ShouldBeFalse();
    }

    [Fact]
    public void Repair_leaves_a_blank_script_unchanged()
    {
        UnselectedSourceDetector.Repair(string.Empty, Ctx()).ShouldBe(string.Empty);
    }
}

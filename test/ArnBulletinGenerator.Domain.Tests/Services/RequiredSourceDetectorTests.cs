using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RequiredSourceDetectorTests
{
    // Content selection and provider fetch are set independently so the tests can exercise the
    // two axes: selected-but-not-required, required-but-fetch-failed, and required-and-fetched.
    private static BroadcastWithTraffic Ctx(
        bool weatherSelected = false,
        bool fuelSelected = false,
        bool weatherRequired = false,
        bool fuelRequired = false,
        Option<WeatherReport> weather = default,
        Option<FuelPriceReport> fuel = default) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Test",
            Traffic = string.Empty,
            Incidents = [],
            Content = new BulletinContentSelection(
                Traffic: true,
                Weather: weatherSelected,
                Fuel: fuelSelected,
                WeatherRequired: weatherRequired,
                FuelRequired: fuelRequired),
            Weather = weather,
            Fuel = fuel
        };

    private static WeatherReport SomeWeather() => new("Cloudy", 18.0, default, 10, []);

    private static FuelPriceReport SomeFuel() =>
        new([new FuelPriceSummary("Unleaded", 1.79, 1.85, 10)]);

    [Fact]
    public void Fires_when_weather_is_required_and_available_but_never_voiced()
    {
        var result = RequiredSourceDetector.Detect(
            "The M4 is clear. Warringah Freeway is moving well.",
            Ctx(weatherSelected: true, weatherRequired: true, weather: SomeWeather()));

        result.HasMissingSource.ShouldBeTrue();
        result.Feedback.ShouldContain("REQUIRED SOURCE MISSING");
        result.Feedback.ShouldContain("weather");
        // Weather-only: the feedback must not name fuel (would mean it mislabelled the source).
        result.Feedback.ShouldNotContain("fuel");
    }

    [Fact]
    public void Fires_for_fuel_when_required_and_available_but_never_voiced()
    {
        var result = RequiredSourceDetector.Detect(
            "The M4 is clear.",
            Ctx(fuelSelected: true, fuelRequired: true, fuel: SomeFuel()));

        result.HasMissingSource.ShouldBeTrue();
        result.Feedback.ShouldContain("REQUIRED SOURCE MISSING");
        result.Feedback.ShouldContain("fuel");
        result.Feedback.ShouldNotContain("weather");
    }

    [Fact]
    public void Does_not_fire_when_the_required_source_was_not_fetched()
    {
        // Required flag set, but the provider fetch returned nothing (Option None): the station
        // still airs without it, so there is no data to demand be voiced.
        var result = RequiredSourceDetector.Detect(
            "The M4 is clear.",
            Ctx(weatherSelected: true, weatherRequired: true));

        result.HasMissingSource.ShouldBeFalse();
    }

    [Fact]
    public void Does_not_fire_when_the_weather_line_airs()
    {
        var result = RequiredSourceDetector.Detect(
            "The M4 is clear. It's 18 degrees and mostly cloudy.",
            Ctx(weatherSelected: true, weatherRequired: true, weather: SomeWeather()));

        result.HasMissingSource.ShouldBeFalse();
    }

    // Regression, deployed dev jobs 77734311 (Gold Coast) and 98f3c558 (Port Lincoln), 2026-08-10:
    // the shipped read spells numbers out, so a digit-anchored temperature signal scored these
    // exact lines as "no weather aired" and the gate demanded a section that was already there,
    // burning a refine attempt and shipping an unresolved-gate warning. Verbatim on purpose.
    [Theory]
    [InlineData("Napper Road's slow at Greenacre Drive. Clear, seventeen degrees! Unleaded's one-ninety-two in Southport.")]
    [InlineData("No major delays across Port Lincoln this afternoon. Cool, fourteen degrees.")]
    [InlineData("The M4 is clear. Around twenty degrees today.")]
    public void Does_not_fire_when_the_temperature_is_spoken_as_words(string script)
    {
        var result = RequiredSourceDetector.Detect(
            script, Ctx(weatherSelected: true, weatherRequired: true, weather: SomeWeather()));

        result.HasMissingSource.ShouldBeFalse();
    }

    // Regression, deployed dev job 8d8e5ec1680b48488028d30fc91ed43b (darwin, 2026-08-10): the
    // road name "Fog Bay Road" begins with the weather word "Fog", and the old signal scored the
    // traffic sentence as a weather line, so this gate believed weather had aired when it had
    // not. Verbatim shipped sentence on purpose: with weather required and available but never
    // voiced, the gate must still fire.
    [Fact]
    public void Does_not_treat_a_weather_word_inside_a_road_name_as_a_weather_line()
    {
        const string script =
            "Fog Bay Road's slow going eastbound, with construction across about fifteen kays "
            + "between Charles Point Road and Railway Alice Springs.";

        var result = RequiredSourceDetector.Detect(
            script, Ctx(weatherSelected: true, weatherRequired: true, weather: SomeWeather()));

        result.HasMissingSource.ShouldBeTrue();
        result.Feedback.ShouldContain("REQUIRED SOURCE MISSING");
        result.Feedback.ShouldContain("weather");
    }

    [Fact]
    public void Does_not_fire_when_the_source_is_selected_but_not_required()
    {
        // Weather fetched and selected, but not flagged required: the writer may skip it.
        var result = RequiredSourceDetector.Detect(
            "The M4 is clear.",
            Ctx(weatherSelected: true, weather: SomeWeather()));

        result.HasMissingSource.ShouldBeFalse();
    }

    [Fact]
    public void Names_both_sources_when_both_are_required_and_missing()
    {
        var result = RequiredSourceDetector.Detect(
            "The M4 is clear.",
            Ctx(
                weatherSelected: true, fuelSelected: true,
                weatherRequired: true, fuelRequired: true,
                weather: SomeWeather(), fuel: SomeFuel()));

        result.HasMissingSource.ShouldBeTrue();
        result.Feedback.ShouldContain("weather and fuel");
    }

    [Fact]
    public void Blank_script_with_a_required_available_source_is_missing()
    {
        var result = RequiredSourceDetector.Detect(
            string.Empty,
            Ctx(weatherSelected: true, weatherRequired: true, weather: SomeWeather()));

        result.HasMissingSource.ShouldBeTrue();
    }

    [Fact]
    public void Detect_reports_no_sentence_order_and_script_scope()
    {
        var result = RequiredSourceDetector.Detect(
            "The M4 is moving nicely. Anzac Bridge is clear.",
            Ctx(weatherSelected: true, weatherRequired: true, weather: SomeWeather()));

        result.HasMissingSource.ShouldBeTrue();
        result.Issues.Count.ShouldBe(1);
        var issue = result.Issue!;
        issue.Order.ShouldBeNull();
        issue.Defect.ShouldBe("REQUIRED_SOURCE_MISSING");
        issue.Scope.ShouldBe(RefineScope.Script);
        issue.IsPatchable.ShouldBeFalse();
    }
}

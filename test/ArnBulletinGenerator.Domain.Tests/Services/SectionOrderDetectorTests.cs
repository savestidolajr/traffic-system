using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class SectionOrderDetectorTests
{
    // Weather-first fill order by default (the classic station); corridors empty unless a test
    // needs a named road to make a sentence "not pure".
    private static BroadcastWithTraffic Ctx(
        IReadOnlyList<FillSource>? fillOrder = null,
        IReadOnlyList<TrafficCorridor>? corridors = null) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Test",
            Traffic = string.Empty,
            Incidents = [],
            FillOrder = fillOrder ?? [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler],
            Corridors = corridors ?? []
        };

    private static TrafficCorridor M4() => new("M4", 4, 2, 2000, 50, "accident", "M4 accident", []);

    // ─── Order detection ─────────────────────────────────────────────────

    [Fact]
    public void Fires_on_fuel_before_weather_against_a_weather_first_order()
    {
        var result = SectionOrderDetector.Detect(
            "The M4 is clear. Unleaded's cheapest at $1.79. It's 18 degrees.", Ctx());

        result.HasOutOfOrderSection.ShouldBeTrue();
        result.Feedback.ShouldContain("SECTION ORDER");
        result.Feedback.ShouldContain("fuel");
        result.Feedback.ShouldContain("weather");
    }

    [Fact]
    public void Fires_on_weather_before_fuel_against_a_fuel_first_order()
    {
        var result = SectionOrderDetector.Detect(
            "The M4 is clear. It's 18 degrees. Unleaded's cheapest at $1.79.",
            Ctx(fillOrder: [FillSource.Fuel, FillSource.Weather]));

        result.HasOutOfOrderSection.ShouldBeTrue();
        result.Feedback.ShouldContain("SECTION ORDER");
        result.Feedback.ShouldContain("weather");
        result.Feedback.ShouldContain("fuel");
    }

    // Regression, deployed dev job 77734311 (Gold Coast, fuel-first station), 2026-08-10: the
    // spoken-word temperature made the weather line invisible to the detector, so a read that
    // plainly went weather-then-fuel against a fuel-first order was never flagged and shipped
    // in the wrong order. Verbatim shipped sentences.
    [Fact]
    public void Fires_on_a_spoken_temperature_weather_line_before_fuel_against_a_fuel_first_order()
    {
        const string script =
            "Napper Road's slow at Greenacre Drive. Clear, seventeen degrees! "
            + "Unleaded's one-ninety-two in Southport, diesel two-thirty-one there too.";

        var result = SectionOrderDetector.Detect(
            script, Ctx(fillOrder: [FillSource.Fuel, FillSource.Weather]));

        result.HasOutOfOrderSection.ShouldBeTrue();

        string repaired = SectionOrderDetector.Repair(
            script, Ctx(fillOrder: [FillSource.Fuel, FillSource.Weather]));

        repaired.ShouldEndWith("Clear, seventeen degrees!");
        repaired.ShouldContain("Napper Road's slow at Greenacre Drive.");
        SectionOrderDetector.Detect(repaired, Ctx(fillOrder: [FillSource.Fuel, FillSource.Weather]))
            .HasOutOfOrderSection.ShouldBeFalse();
    }

    // Regression, deployed dev job 8d8e5ec1680b48488028d30fc91ed43b (darwin, 2026-08-10): "Fog
    // Bay Road" begins with a weather word, and the old signal scored that traffic sentence as a
    // supplementary weather section, stranding it mid-read against the weather-first order.
    // Verbatim shipped sentences on purpose.
    [Fact]
    public void Does_not_treat_a_weather_word_inside_a_road_name_as_a_supplementary_section()
    {
        const string script =
            "Cox Peninsula Road's got construction across about fifteen kays citybound from "
            + "Railway Alice Springs. Fog Bay Road's slow going eastbound, with construction "
            + "across about fifteen kays between Charles Point Road and Railway Alice Springs. "
            + "McMillans Road's a bit slow westbound with construction.";

        var result = SectionOrderDetector.Detect(script, Ctx());

        result.HasOutOfOrderSection.ShouldBeFalse();
    }

    [Fact]
    public void Fires_on_a_stranded_section_followed_by_traffic()
    {
        var result = SectionOrderDetector.Detect(
            "The M4 is clear. It's 18 degrees and cloudy. Warringah Freeway is shut.", Ctx());

        result.HasOutOfOrderSection.ShouldBeTrue();
        result.Feedback.ShouldContain("STRANDED SECTION");
        result.Feedback.ShouldContain("weather");
    }

    [Fact]
    public void Is_clean_on_a_correctly_ordered_read()
    {
        var result = SectionOrderDetector.Detect(
            "The M4 is clear. It's 18 degrees. Unleaded's cheapest at $1.79.", Ctx());

        result.HasOutOfOrderSection.ShouldBeFalse();
        result.Feedback.ShouldBe(string.Empty);
    }

    // ─── Repair ──────────────────────────────────────────────────────────

    [Fact]
    public void Repair_relocates_a_pure_weather_line_and_is_idempotent()
    {
        const string script =
            "The M4 is clear. It's 18 degrees and cloudy. Warringah Freeway is shut.";

        string repaired = SectionOrderDetector.Repair(script, Ctx());

        repaired.ShouldBe("The M4 is clear. Warringah Freeway is shut. It's 18 degrees and cloudy.");
        // Idempotent: the repaired script must itself be clean.
        SectionOrderDetector.Detect(repaired, Ctx()).HasOutOfOrderSection.ShouldBeFalse();
    }

    [Fact]
    public void Repair_orders_the_tail_by_the_configured_order()
    {
        // Fuel read first: repair must reorder to the weather-first configured order, not
        // preserve the script's fuel-first sequence.
        const string script = "Unleaded's cheapest at $1.79. It's 18 degrees and cloudy.";

        string repaired = SectionOrderDetector.Repair(script, Ctx());

        repaired.ShouldBe("It's 18 degrees and cloudy. Unleaded's cheapest at $1.79.");
        SectionOrderDetector.Detect(repaired, Ctx()).HasOutOfOrderSection.ShouldBeFalse();
    }

    [Fact]
    public void Repair_leaves_a_mixed_traffic_weather_sentence_in_place()
    {
        // The weather sentence names the M4 corridor, so it is not pure: it cannot be safely
        // relocated, and the detector keeps reporting it through feedback only.
        const string script = "The M4 is clear and it's 18 degrees. Warringah Freeway is shut.";

        string repaired = SectionOrderDetector.Repair(script, Ctx(corridors: [M4()]));

        repaired.ShouldBe(script);
        repaired.ShouldContain("The M4 is clear and it's 18 degrees");
        SectionOrderDetector.Detect(repaired, Ctx(corridors: [M4()])).HasOutOfOrderSection.ShouldBeTrue();
    }

    // ─── Blank input ─────────────────────────────────────────────────────

    [Fact]
    public void Blank_script_is_clean()
    {
        SectionOrderDetector.Detect(string.Empty, Ctx()).HasOutOfOrderSection.ShouldBeFalse();
    }

    [Fact]
    public void Repair_leaves_a_blank_script_unchanged()
    {
        SectionOrderDetector.Repair(string.Empty, Ctx()).ShouldBe(string.Empty);
    }

    // ─── Reproduction: the script ARN reported on air, 2026-08-24 ─────────
    //
    // An ARN operator, KIIS 90s, twice ("did that thing again where it put an
    // incident after the weather"). This is the aired text verbatim from their
    // screenshot. The question this pins down: does the GATE miss it, or did
    // the offending line arrive after the gate had already run?
    [Fact]
    public void Reproduction_aired_good_run_after_weather_2026_08_24()
    {
        // Verbatim, minus the bookend outro (composed after generation).
        const string aired =
            "Now looking over Sydney. "
            + "Warringah Freeway's closed eastbound near Cammeray. "
            + "Mamre Road's closed northbound near Erskine Park, at McIntyre Avenue. "
            + "Mitchell Road near Erskineville's still blocked between Sydney Park Road and Maddox Street. "
            + "Sunny, sixteen degrees. "
            + "Pacific Motorway and WestConnex are both MOVING nicely!";

        var detected = SectionOrderDetector.Detect(aired, Ctx());
        string repaired = SectionOrderDetector.Repair(aired, Ctx());
        var afterRepair = SectionOrderDetector.Detect(repaired, Ctx());

        // Recorded as observed, not as desired — this is a characterisation test.
        detected.HasOutOfOrderSection.ShouldBeTrue(
            "the gate SHOULD see a weather line stranded before a traffic good run");
        repaired.ShouldEndWith("Sunny, sixteen degrees.");
        afterRepair.HasOutOfOrderSection.ShouldBeFalse();
    }

    // Same reproduction with the delivery-style audio tag the operator's copy
    // carried ("[brightly]"), to isolate whether the tag changes sentence
    // splitting and therefore whether the weather line still reads as PURE.
    [Fact]
    public void Reproduction_aired_good_run_after_weather_with_delivery_tag()
    {
        const string aired =
            "Now looking over Sydney. "
            + "Warringah Freeway's closed eastbound near Cammeray. "
            + "Mitchell Road near Erskineville's still blocked between Sydney Park Road and Maddox Street. "
            + "Sunny, sixteen degrees. [brightly] Pacific Motorway and WestConnex are both MOVING nicely!";

        var detected = SectionOrderDetector.Detect(aired, Ctx());
        string repaired = SectionOrderDetector.Repair(aired, Ctx());

        detected.HasOutOfOrderSection.ShouldBeTrue();
        repaired.ShouldEndWith("Sunny, sixteen degrees.");
    }
}

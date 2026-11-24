using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     REPRODUCTION — dev job f553c893 (cairns-4ca, 2026-08-24, aired).
///     <para>
///         A 19.1s read in a 22s slot was content-limited, so the word-target expansion asked the
///         sentence refiner to grow sentence 1. It answered with weather, which the read had already
///         voiced in its closing line, and the bulletin aired the temperature twice. Every guard on
///         that path is sentence-local — each sentence is well formed on its own — so only a
///         whole-script comparison can see it.
///     </para>
/// </summary>
public sealed class FixtureDuplicationTests
{
    // The read as it stood before expansion (44 words, content-limited).
    private const string BeforeExpansion =
        "A planned event is affecting westbound Mulgrave Road in Earlville. "
        + "Construction and a hazard are reported northbound on Kennedy Highway at Myola Road. "
        + "Unleaded's a dollar ninety-eight at Manoora, diesel's two dollars forty in Koah. "
        + "Cloudy, 23 degrees in Cairns.";

    // What the expansion actually produced — the refiner's weather sentence prepended, the
    // original weather line still in place.
    private const string AiredWithDuplicate =
        "It's cloudy, with temperatures around 23 degrees. "
        + "A planned event is affecting westbound Mulgrave Road in Earlville. "
        + "Construction and a hazard are reported northbound on Kennedy Highway at Myola Road. "
        + "Unleaded's a dollar ninety-eight at Manoora, diesel's two dollars forty in Koah. "
        + "Cloudy, 23 degrees in Cairns.";

    [Fact]
    public void The_aired_expansion_is_caught_as_a_duplicated_fixture()
    {
        ContentSourceSignals.AddsFixtureLine(BeforeExpansion, AiredWithDuplicate).ShouldBeTrue(
            "the expansion added a second weather line to a read that already voiced weather");
    }

    [Fact]
    public void Counts_the_aired_read_as_two_weather_lines_and_one_fuel_line()
    {
        var (weather, fuel) = ContentSourceSignals.CountFixtureLines(AiredWithDuplicate);

        weather.ShouldBe(2);
        fuel.ShouldBe(1);
    }

    // ── An honest expansion must still be allowed through ──
    [Fact]
    public void Growing_a_traffic_sentence_is_not_a_duplicated_fixture()
    {
        const string honestlyExpanded =
            "A planned event is affecting westbound Mulgrave Road between Toogood Road and "
            + "Mccoombe Street in Earlville. "
            + "Construction and a hazard are reported northbound on Kennedy Highway at Myola Road. "
            + "Unleaded's a dollar ninety-eight at Manoora, diesel's two dollars forty in Koah. "
            + "Cloudy, 23 degrees in Cairns.";

        ContentSourceSignals.AddsFixtureLine(BeforeExpansion, honestlyExpanded).ShouldBeFalse(
            "adding a cross-street the data supplied is exactly what expansion is for");
    }

    [Fact]
    public void Rewording_the_single_weather_line_in_place_is_allowed()
    {
        const string reworded =
            "A planned event is affecting westbound Mulgrave Road in Earlville. "
            + "Construction and a hazard are reported northbound on Kennedy Highway at Myola Road. "
            + "Unleaded's a dollar ninety-eight at Manoora, diesel's two dollars forty in Koah. "
            + "Cloudy skies over Cairns, with temperatures at 23 degrees.";

        ContentSourceSignals.AddsFixtureLine(BeforeExpansion, reworded).ShouldBeFalse(
            "the count is what matters, not the wording — one weather line in, one out");
    }

    [Fact]
    public void A_first_weather_line_on_a_read_that_had_none_is_allowed()
    {
        // Filler top-up legitimately introduces weather when the read carries none.
        const string trafficOnly =
            "A planned event is affecting westbound Mulgrave Road in Earlville.";
        const string withWeather =
            "A planned event is affecting westbound Mulgrave Road in Earlville. "
            + "Cloudy, 23 degrees in Cairns.";

        ContentSourceSignals.AddsFixtureLine(trafficOnly, withWeather).ShouldBeTrue(
            "this IS an increase and the caller decides whether it is wanted — the expansion path "
            + "rejects it, the fill path never asks");
    }
}

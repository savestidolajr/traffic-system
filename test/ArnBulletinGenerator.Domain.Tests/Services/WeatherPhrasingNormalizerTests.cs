using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     "temperatures on 13 degrees" aired on BOTH pilot stations in one run (2026-08-23). No existing
///     gate could see it: the read was grounded, fitted its slot, and broke no style rule.
/// </summary>
public sealed class WeatherPhrasingNormalizerTests
{
    [Theory]
    [InlineData("Clear skies across Sydney right now, with temperatures on 13 degrees.",
                "Clear skies across Sydney right now, with temperatures at 13 degrees.")]
    [InlineData("Clear skies across Cairns, with temperatures on 21 degrees.",
                "Clear skies across Cairns, with temperatures at 21 degrees.")]
    [InlineData("Sunny, with the temperature on 9 degrees.",
                "Sunny, with the temperature at 9 degrees.")]
    public void TheTemperaturePrepositionIsCorrected(string input, string expected) =>
        WeatherPhrasingNormalizer.Normalize(input).ShouldBe(expected);

    [Theory]
    // "on" is a perfectly good preposition everywhere else in a traffic read; a broader rule would
    // do real damage, so these must pass through untouched.
    [InlineData("Roadworks on Church Street are adding four minutes.")]
    [InlineData("Northbound on the Bruce Highway at Foster Road.")]
    [InlineData("A hazard on the M1 near 3 Bridges Road.")]
    [InlineData("Sunny, 18 degrees.")]
    [InlineData("Traffic is heavy on 5 separate approaches.")]
    public void EveryOtherUseOfOnIsUntouched(string input) =>
        WeatherPhrasingNormalizer.Normalize(input).ShouldBe(input);

    [Fact]
    public void ItIsIdempotent()
    {
        const string input = "Cloudy, with temperatures on 14 degrees.";
        string once = WeatherPhrasingNormalizer.Normalize(input);
        WeatherPhrasingNormalizer.Normalize(once).ShouldBe(once);
    }

    [Fact]
    public void TheConsolidatedSweepAppliesIt()
    {
        // The point of SpokenFormNormalizer: one call, every rewrite. This previously required
        // remembering five separate normalizers at each of four call sites.
        SpokenFormNormalizer.Normalize("The M1 is shut, with temperatures on 20 degrees.")
            .ShouldBe("The M1 is closed, with temperatures at 20 degrees.");
    }
}

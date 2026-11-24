using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
/// The octane has now aired from three different code paths on three different days. These pin the
/// shared helper AND the two Domain-side callers, so a fourth hand-copied conditional cannot appear
/// without a red test.
/// </summary>
public sealed class SpokenFuelGradeTests
{
    [Theory]
    [InlineData("Unleaded 91", "unleaded")]
    [InlineData("unleaded 91", "unleaded")]
    // Prefix-matched, so a re-badged grade cannot reopen this.
    [InlineData("Unleaded 95", "unleaded")]
    [InlineData("Unleaded", "unleaded")]
    [InlineData("Diesel", "diesel")]
    [InlineData("LPG", "lpg")]
    [InlineData("", "")]
    public void Of_SpeaksTheGradeWithoutTheOctane(string raw, string expected)
    {
        SpokenFuelGrade.Of(raw).ShouldBe(expected);
    }

    [Fact]
    public void Of_TreatsNullAsEmpty()
    {
        SpokenFuelGrade.Of(null).ShouldBe(string.Empty);
    }

    [Fact]
    public void FixtureLineComposer_DoesNotAirTheOctaneOrARawPrice()
    {
        // Verbatim from dev brisbane e91805be, 2026-08-25: this path spliced the fixture line AFTER
        // the writer-facing formatters had run, so its own copy of the rule was the one that aired.
        var report = new FuelPriceReport(
            [new FuelPriceSummary("Unleaded 91", 1.945, 2.031, 50) { CheapestStationSuburb = "Chermside West" }]);

        string line = FixtureLineComposer.FuelLines(report).ShouldHaveSingleItem();

        line.ShouldNotContain("91");
        line.ShouldNotContain("$");
        line.ShouldContain("unleaded");
        // Price still spoken, not printed - that half was already fixed on 2026-08-23.
        line.ShouldContain("a dollar ninety-five");
    }

    [Fact]
    public void ContentFormatters_DoesNotAirTheOctane()
    {
        var report = new FuelPriceReport(
            [new FuelPriceSummary("Unleaded 91", 1.945, 2.031, 50) { CheapestStationSuburb = "Chermside West" }]);

        string prose = ContentFormatters.FormatFuel(report);

        prose.ShouldNotContain("91");
        prose.ShouldContain("unleaded");
    }
}

using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The fixture composer is the repair ladder's deterministic re-splice source: its output must
///     always classify as its own fixture type (or a second drop would double-splice), and a
///     malformed upstream payload must never throw inside the review loop.
/// </summary>
public sealed class FixtureLineComposerTests
{
    [Fact]
    public void WeatherLine_RendersConditionAndRoundedDegrees_AndClassifiesAsWeather()
    {
        var report = new WeatherReport("Partly sunny. Becoming windy later.", 17.4, default, 0, []);

        string line = FixtureLineComposer.WeatherLine(report);

        line.ShouldBe("Partly sunny, 17 degrees.");
        ContentSourceSignals.IsWeatherLine(line).ShouldBeTrue();
    }

    [Fact]
    public void WeatherLine_WithAlert_AppendsTheAlertClause_AndStillClassifies()
    {
        var report = new WeatherReport("Stormy", 21.0, default, 0, ["severe thunderstorm warning."]);

        string line = FixtureLineComposer.WeatherLine(report);

        line.ShouldBe("Stormy, 21 degrees. There's a severe thunderstorm warning in place.");
        ContentSourceSignals.IsWeatherLine(line).ShouldBeTrue();
    }

    [Fact]
    public void WeatherLine_EmptyConditions_NeverThrows_AndStillClassifies()
    {
        var report = new WeatherReport("", 22.0, default, 0, []);

        string line = FixtureLineComposer.WeatherLine(report);

        line.ShouldBe("22 degrees.");
        ContentSourceSignals.IsWeatherLine(line).ShouldBeTrue();
    }

    [Fact]
    public void FuelLines_TwoFuelsOneSuburb_GroupAndNameTheSharedSuburb()
    {
        var report = new FuelPriceReport(
        [
            new FuelPriceSummary("Unleaded 91", 1.98, 2.05, 12, CheapestStationSuburb: "Manoora"),
            new FuelPriceSummary("Diesel", 2.40, 2.49, 9, CheapestStationSuburb: "Manoora")
        ]);

        var lines = FixtureLineComposer.FuelLines(report);

        lines.ShouldBe(["unleaded is a dollar ninety-eight, and diesel is two dollars forty in Manoora."]);
        ContentSourceSignals.IsFuelLine(lines[0]).ShouldBeTrue();
    }

    [Fact]
    public void FuelLines_TwoFuelsTwoSuburbs_NameEachPriceLocation()
    {
        var report = new FuelPriceReport(
        [
            new FuelPriceSummary("Unleaded 91", 1.93, 2.01, 7, CheapestStationSuburb: "Kilburn"),
            new FuelPriceSummary("Diesel", 2.38, 2.44, 5, CheapestStationSuburb: "Windsor Gardens")
        ]);

        var lines = FixtureLineComposer.FuelLines(report);

        lines.ShouldBe(
        [
            "unleaded is a dollar ninety-three in Kilburn.",
            "diesel is two dollars thirty-eight in Windsor Gardens."
        ]);
        lines.ShouldAllBe(line => ContentSourceSignals.IsFuelLine(line));
    }

    [Fact]
    public void FuelLines_OneFuelWithoutSuburb_DoesNotBorrowANeighbourLocation()
    {
        var report = new FuelPriceReport(
        [
            new FuelPriceSummary("Unleaded 91", 1.98, 2.05, 12, CheapestStationSuburb: null)
        ]);

        var lines = FixtureLineComposer.FuelLines(report);

        lines.ShouldBe(["unleaded is a dollar ninety-eight."]);
        lines[0].ShouldNotContain(" in ");
        ContentSourceSignals.IsFuelLine(lines[0]).ShouldBeTrue();
    }

    [Fact]
    public void FuelLines_MissingSuburbNeverBorrowsNamedGroupLocation()
    {
        var report = new FuelPriceReport(
        [
            new FuelPriceSummary("Unleaded 91", 1.98, 2.05, 12, CheapestStationSuburb: null),
            new FuelPriceSummary("Diesel", 2.40, 2.49, 9, CheapestStationSuburb: "Manoora")
        ]);

        var lines = FixtureLineComposer.FuelLines(report);

        lines.ShouldBe(
        [
            "unleaded is a dollar ninety-eight.",
            "diesel is two dollars forty in Manoora."
        ]);
    }

    [Fact]
    public void FuelLines_NeverEmitARawNumericPrice()
    {
        // The defect CLASS, not one phrasing: whatever the composer's wording becomes, a spliced fuel
        // line must never reach TTS carrying a PRICE numeral, because nothing downstream will fix it.
        // Scoped to price shapes on purpose — "Unleaded 91" is a fuel grade, and TTS reads the grade
        // correctly; an assertion of "no digits at all" fails on the product name and would push the
        // fix toward mangling it.
        var report = new FuelPriceReport(
        [
            new FuelPriceSummary("Unleaded 91", 1.93, 2.01, 7, CheapestStationSuburb: "Kilburn"),
            new FuelPriceSummary("Diesel", 2.38, 2.44, 5, CheapestStationSuburb: "Windsor Gardens")
        ]);

        var lines = FixtureLineComposer.FuelLines(report);

        lines.ShouldNotBeEmpty();
        foreach (string line in lines)
        {
            line.ShouldNotContain("$");
            System.Text.RegularExpressions.Regex.IsMatch(line, @"\d+\.\d{2}|\b\d{3}\b")
                .ShouldBeFalse($"Spliced fuel line still carries a price numeral: \"{line}\"");
        }
    }

}

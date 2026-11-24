using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RepeatedFuelSuburbDetectorTests
{
    private static BroadcastWithTraffic Ctx(params string[] suburbs) =>
        new()
        {
            TargetDurationSeconds = 30,
            JobId = "t",
            Location = "Cairns",
            Traffic = string.Empty,
            Incidents = [],
            Fuel = new FuelPriceReport(suburbs
                .Select((suburb, index) => new FuelPriceSummary(
                    index == 0 ? "Unleaded 91" : "Diesel",
                    index == 0 ? 1.98 : 2.40,
                    index == 0 ? 2.05 : 2.49,
                    10,
                    CheapestStationSuburb: suburb))
                .ToList())
        };

    [Fact]
    public void Repairs_the_live_manoora_case_and_keeps_one_location()
    {
        const string script =
            "Cheapest unleaded is a dollar ninety-eight at Manoora, while diesel is two dollars forty in Manoora.";
        var context = Ctx("Manoora", "Manoora");

        var result = RepeatedFuelSuburbDetector.Detect(script, context);

        result.HasRepeatedFuelSuburb.ShouldBeTrue();
        RepeatedFuelSuburbDetector.Repair(script, context).ShouldBe(
            "Cheapest unleaded is a dollar ninety-eight at Manoora, while diesel is two dollars forty.");
    }

    [Fact]
    public void Leaves_different_suburbs_byte_identical()
    {
        const string script =
            "Cheapest unleaded is a dollar ninety-three at Kilburn, while diesel is two dollars thirty-eight in Windsor Gardens.";
        var context = Ctx("Kilburn", "Windsor Gardens");

        RepeatedFuelSuburbDetector.Detect(script, context).HasRepeatedFuelSuburb.ShouldBeFalse();
        RepeatedFuelSuburbDetector.Repair(script, context).ShouldBe(script);
    }

    [Fact]
    public void Reduces_three_mentions_to_one_and_cleans_the_sentence()
    {
        const string script =
            "Unleaded is a dollar ninety-eight at Manoora, diesel is two dollars forty in Manoora and premium is two dollars fifty at Manoora.";
        var context = Ctx("Manoora", "Manoora", "Manoora");

        RepeatedFuelSuburbDetector.Detect(script, context).HasRepeatedFuelSuburb.ShouldBeTrue();
        RepeatedFuelSuburbDetector.Repair(script, context).ShouldBe(
            "Unleaded is a dollar ninety-eight at Manoora, diesel is two dollars forty and premium is two dollars fifty.");
    }

    [Fact]
    public void Leaves_a_single_suburb_mention_alone()
    {
        const string script = "Cheapest unleaded is a dollar ninety-eight at Manoora.";
        var context = Ctx("Manoora");

        RepeatedFuelSuburbDetector.Detect(script, context).HasRepeatedFuelSuburb.ShouldBeFalse();
        RepeatedFuelSuburbDetector.Repair(script, context).ShouldBe(script);
    }

    [Fact]
    public void Ignores_a_traffic_mention_when_the_fuel_line_mentions_the_same_suburb_once()
    {
        const string script =
            "Traffic is heavy through Manoora. Cheapest unleaded is a dollar ninety-eight at Manoora.";
        var context = Ctx("Manoora");

        RepeatedFuelSuburbDetector.Detect(script, context).HasRepeatedFuelSuburb.ShouldBeFalse();
        RepeatedFuelSuburbDetector.Repair(script, context).ShouldBe(script);
    }

    [Fact]
    public void Repair_is_idempotent()
    {
        const string script =
            "Cheapest unleaded is a dollar ninety-eight at Manoora, while diesel is two dollars forty in Manoora.";
        var context = Ctx("Manoora", "Manoora");

        string repaired = RepeatedFuelSuburbDetector.Repair(script, context);

        RepeatedFuelSuburbDetector.Repair(repaired, context).ShouldBe(repaired);
    }
}

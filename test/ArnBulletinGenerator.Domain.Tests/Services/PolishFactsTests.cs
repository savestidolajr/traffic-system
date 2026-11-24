using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

/// <summary>
///     The role labels handed to the naturalness-polish FACTS block: condition bucket, congestion
///     severity band, and the per-sentence corridor mapping the block is built from.
/// </summary>
public class PolishFactsTests
{
    private static TrafficCorridor Corridor(string road, string type, int? delay = null,
        string? direction = null) =>
        new(road, 1, 3, 500, 5, type, "desc", [],
            Direction: direction is null ? Option<string>.None : Option<string>.Some(direction),
            DriveTimeMinutes: delay is null ? Option<int>.None : Option<int>.Some(delay.Value),
            NormalDriveTimeMinutes: delay is null ? Option<int>.None : Option<int>.Some(0));

    [Theory]
    [InlineData("roadClosure", "closure")]
    [InlineData("accident", "crash")]
    [InlineData("disabledVehicle", "breakdown")]
    [InlineData("construction", "roadworks")]
    [InlineData("congestion", "congestion")]
    [InlineData("", "congestion")]
    public void ConditionOf_bucketsPrimaryType(string primaryType, string expected) =>
        PolishFacts.ConditionOf(Corridor("M4", primaryType)).ShouldBe(expected);

    [Theory]
    [InlineData(9, "heavy")]
    [InlineData(8, "heavy")]
    [InlineData(5, "moderate")]
    [InlineData(3, "moderate")]
    [InlineData(2, "minor")]
    [InlineData(0, "minor")]
    public void SeverityBandOf_bandsByDelay(int delay, string expected) =>
        PolishFacts.SeverityBandOf(Corridor("M4", "congestion", delay)).ShouldBe(expected);

    [Fact]
    public void SeverityBandOf_nullWhenNoDelay() =>
        PolishFacts.SeverityBandOf(Corridor("M4", "construction")).ShouldBeNull();

    [Fact]
    public void MapCorridors_attachesNamedCorridorsPerSentence()
    {
        var corridors = new[]
        {
            Corridor("Warringah Freeway", "roadClosure", direction: "citybound"),
            Corridor("M4", "congestion", delay: 6)
        };
        var result = ScriptProvenanceMapper.MapCorridors(
            "The Warringah Freeway is shut citybound. The M4 is busy. Skies are clear.", corridors);

        result.Count.ShouldBe(3);
        result[0].Corridors.Select(c => c.RoadName).ShouldBe(["Warringah Freeway"]);
        result[1].Corridors.Select(c => c.RoadName).ShouldBe(["M4"]);
        result[2].Corridors.ShouldBeEmpty(); // clear/weather sentence names no corridor
    }
}

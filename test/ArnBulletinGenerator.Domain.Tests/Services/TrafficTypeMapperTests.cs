using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class TrafficTypeMapperTests
{
    [Theory]
    [InlineData("accident", "Crash")]
    [InlineData("roadclosure", "Road closure")]
    [InlineData("disabledvehicle", "Breakdown")]
    [InlineData("roadhazard", "Hazard")]
    [InlineData("lanerestriction", "Lane closure")]
    [InlineData("construction", "Roadworks")]
    // Was "Planned event", which aired verbatim twice on the 2026-08-25 end-to-end run.
    [InlineData("plannedevent", "An event")]
    // Was "Heavy congestion", a banded severity label EvidenceLabelVocabulary bans outright.
    [InlineData("congestion", "Congestion")]
    public void MapType_KnownTypes_ReturnsSpeakableWords(string input, string expected)
    {
        TrafficTypeMapper.MapType(input).ShouldBe(expected);
    }

    [Fact]
    public void MapType_NeverReturnsAnEvidenceLabel()
    {
        // These words reach the writer as evidence, so none of them may be a label it must not say.
        string[] hereTypes =
        [
            "accident", "roadClosure", "disabledVehicle", "roadHazard",
            "laneRestriction", "construction", "plannedEvent", "congestion", "somethingElse",
        ];

        foreach (string type in hereTypes)
            EvidenceLabelVocabulary.CountLeaks(TrafficTypeMapper.MapType(type)).ShouldBe(0, type);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void MapType_NullOrEmpty_ReturnsIncident(string? input)
    {
        TrafficTypeMapper.MapType(input!).ShouldBe("Incident");
    }

    [Fact]
    public void MapType_UnknownType_ReturnsIncident()
    {
        TrafficTypeMapper.MapType("earthquake").ShouldBe("Incident");
    }

    [Theory]
    [InlineData("ACCIDENT", "Crash")]
    [InlineData("Accident", "Crash")]
    [InlineData("ROADCLOSURE", "Road closure")]
    [InlineData("Construction", "Roadworks")]
    public void MapType_IsCaseInsensitive(string input, string expected)
    {
        TrafficTypeMapper.MapType(input).ShouldBe(expected);
    }
}

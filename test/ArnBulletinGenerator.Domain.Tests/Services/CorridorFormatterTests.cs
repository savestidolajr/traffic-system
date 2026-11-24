using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class CorridorFormatterTests
{
    private static TrafficCorridor MakeCorridor(
        string road = "M1",
        int severity = 2,
        string primaryType = "accident",
        Option<string> direction = default,
        double totalLengthMeters = 0,
        Option<int> driveTime = default,
        Option<int> normalDriveTime = default,
        IReadOnlyList<TrafficIncident>? incidents = null)
    {
        return new TrafficCorridor(
            road,
            severity,
            1,
            totalLengthMeters,
            100,
            primaryType,
            "Test corridor",
            incidents ?? [],
            Direction: direction,
            DriveTimeMinutes: driveTime,
            NormalDriveTimeMinutes: normalDriveTime);
    }

    private static TrafficIncident MakeIncident(string id, string desc = "Some incident")
    {
        return new TrafficIncident(id, desc, 2, "Test Location", "accident");
    }

    [Fact]
    public void Format_EmptyList_ReturnsNoIncidentsMessage()
    {
        string result = CorridorFormatter.Format([]);
        result.ShouldBe("No significant traffic incidents to report.");
    }

    [Fact]
    public void Format_SingleCorridorWithDirection_IncludesRoadNameAndDirection()
    {
        var corridor = MakeCorridor("M4", direction: Option<string>.Some("Outbound"));
        string result = CorridorFormatter.Format([corridor]);
        result.ShouldContain("**M4 (Outbound)**");
    }

    [Fact]
    public void Format_CorridorWithoutDirection_ShowsJustRoadName()
    {
        var corridor = MakeCorridor("Parramatta Road");
        string result = CorridorFormatter.Format([corridor]);
        result.ShouldContain("**Parramatta Road**");
        result.ShouldNotContain("(");
    }

    [Theory]
    [InlineData(1, "Critical")]
    [InlineData(2, "Major")]
    [InlineData(3, "Minor")]
    [InlineData(4, "Low")]
    [InlineData(99, "Low")]
    public void Format_SeverityLabels_MappedCorrectly(int severity, string expected)
    {
        var corridor = MakeCorridor(severity: severity);
        string result = CorridorFormatter.Format([corridor]);
        result.ShouldContain($"- Severity: {expected}");
    }

    [Fact]
    public void Format_AffectedLength_ShownWhenGreaterThanZero()
    {
        var corridor = MakeCorridor(totalLengthMeters: 2500);
        string result = CorridorFormatter.Format([corridor]);
        result.ShouldContain("- Affected length: 2.5 km");
    }

    [Fact]
    public void Format_AffectedLength_HiddenWhenZero()
    {
        var corridor = MakeCorridor(totalLengthMeters: 0);
        string result = CorridorFormatter.Format([corridor]);
        result.ShouldNotContain("Affected length");
    }

    [Fact]
    public void Format_DriveTimeAndDelay_ShownWhenAvailable()
    {
        var corridor = MakeCorridor(
            driveTime: Option<int>.Some(25),
            normalDriveTime: Option<int>.Some(15));
        string result = CorridorFormatter.Format([corridor]);

        result.ShouldContain("- Current drive time: 25 minutes");
        result.ShouldContain("- Typical drive time: 15 minutes (delay of 10 minutes)");
    }

    [Fact]
    public void Format_DriveTimeOnly_NoDelayLine()
    {
        var corridor = MakeCorridor(driveTime: Option<int>.Some(20));
        string result = CorridorFormatter.Format([corridor]);

        result.ShouldContain("- Current drive time: 20 minutes");
        result.ShouldNotContain("Typical drive time");
    }

    [Fact]
    public void Format_ZeroDelay_NoDelayLine()
    {
        var corridor = MakeCorridor(
            driveTime: Option<int>.Some(15),
            normalDriveTime: Option<int>.Some(15));
        string result = CorridorFormatter.Format([corridor]);

        result.ShouldNotContain("Typical drive time");
    }

    [Fact]
    public void Format_IncidentDetails_LimitedToThree()
    {
        var incidents = Enumerable.Range(1, 5)
            .Select(i => MakeIncident($"inc-{i}", $"Desc {i}"))
            .ToList();

        var corridor = MakeCorridor(incidents: incidents);
        string result = CorridorFormatter.Format([corridor]);

        result.ShouldContain("• Desc 1");
        result.ShouldContain("• Desc 2");
        result.ShouldContain("• Desc 3");
        result.ShouldNotContain("• Desc 4");
        result.ShouldContain("... and 2 more");
    }

    [Fact]
    public void Format_ThreeOrFewerIncidents_NoMoreLabel()
    {
        var incidents = new[] { MakeIncident("a", "Alpha"), MakeIncident("b", "Beta") };
        var corridor = MakeCorridor(incidents: incidents);
        string result = CorridorFormatter.Format([corridor]);

        result.ShouldContain("• Alpha");
        result.ShouldContain("• Beta");
        result.ShouldNotContain("more");
    }

    [Fact]
    public void Format_ShowsTypeFromTrafficTypeMapper()
    {
        var corridor = MakeCorridor(primaryType: "roadClosure");
        string result = CorridorFormatter.Format([corridor]);
        result.ShouldContain("- Type: Road closure");
    }

    [Fact]
    public void Format_MultipleCorridors_SeparatedByDoubleNewline()
    {
        var corridors = new[]
        {
            MakeCorridor(),
            MakeCorridor("M2")
        };
        string result = CorridorFormatter.Format(corridors);
        result.ShouldContain("\n\n");
        result.ShouldContain("**M1**");
        result.ShouldContain("**M2**");
    }
}

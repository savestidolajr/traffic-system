using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RoadNamePlausibilityTests
{
    [Theory]
    [InlineData("Adjungbillt-Threeway Road")]
    [InlineData("oConnell")]
    [InlineData("Main Road Main Road")]
    [InlineData("Road!!!")]
    public void MangledOrNonNameShapesAreSuspicious(string name)
    {
        var result = RoadNamePlausibility.Check(name);

        result.IsPlausible.ShouldBeFalse();
        result.Reasons.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("Nerang-Broadbeach Road")]
    [InlineData("Sir Donald Bradman Drive")]
    [InlineData("Cairns Western Arterial Road")]
    [InlineData("Kings Highway")]
    [InlineData("O'Connell Street")]
    [InlineData("Bringelly Road")]
    [InlineData("Banksia Street")]
    [InlineData("Port Douglas Road")]
    [InlineData("Canterbury Road")]
    [InlineData("Gannons Road")]
    [InlineData("Lyons Road")]
    [InlineData("M1")]
    [InlineData("A3 / Centenary Dr")]
    public void RealFixtureRoadNamesPass(string name)
    {
        RoadNamePlausibility.Check(name).IsPlausible.ShouldBeTrue();
    }

    [Theory]
    [InlineData("10")]
    [InlineData("1W")]
    public void NonRoadNumericLabelsAreSuspicious(string name)
    {
        RoadNamePlausibility.IsPlausible(name).ShouldBeFalse();
    }

    [Theory]
    [InlineData("M1")]
    [InlineData("A3")]
    [InlineData("State Route 10")]
    [InlineData("95E Road")]
    public void AustralianRouteFormsRemainPlausible(string name)
    {
        RoadNamePlausibility.IsPlausible(name).ShouldBeTrue();
    }

    [Fact]
    public void DiscretionaryFlowPoolDropsOnlySuspiciousNames()
    {
        var pool = new[]
        {
            Flow("Adjungbillt-Threeway Road"),
            Flow("Nerang-Broadbeach Road"),
            Flow("Sir Donald Bradman Drive")
        };

        var filtered = RoadNamePlausibility.FilterDiscretionaryFlowPool(pool);

        filtered.Select(f => f.RoadName).ShouldBe(["Nerang-Broadbeach Road", "Sir Donald Bradman Drive"]);
    }

    [Fact]
    public void SuspiciousIncidentRoadsAreReportedWithoutDroppingCorridors()
    {
        var corridors = new[]
        {
            Corridor("Adjungbillt-Threeway Road"),
            Corridor("Kings Highway")
        };

        var suspicious = RoadNamePlausibility.SuspiciousIncidentRoads(corridors);

        suspicious.ShouldBe(["Adjungbillt-Threeway Road"]);
        corridors.Length.ShouldBe(2);
    }

    private static FlowRoad Flow(string roadName) =>
        new(roadName, JamFactor: 0.5, LengthMeters: 1000, IsWeighted: false, FunctionalClass: 3, Score: 20);

    private static TrafficCorridor Corridor(string roadName) =>
        new(roadName, Severity: 1, FunctionalClass: 3, TotalLengthMeters: 1000, ImpactScore: 20,
            PrimaryType: "roadClosure", Description: "closed", Incidents: []);
}

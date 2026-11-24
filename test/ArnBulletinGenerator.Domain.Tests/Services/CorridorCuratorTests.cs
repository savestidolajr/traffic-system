using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class CorridorCuratorTests
{
    private static TrafficCorridor Corridor(string roadName, double impactScore = 50)
    {
        return new TrafficCorridor(
            roadName, Severity: 1, FunctionalClass: 1, TotalLengthMeters: 1000,
            ImpactScore: impactScore, PrimaryType: "congestion",
            Description: $"Traffic on {roadName}", Incidents: []);
    }

    [Fact]
    public void SelectionFiltersAndOrders()
    {
        var fresh = new List<TrafficCorridor>
        {
            Corridor("M3"),
            Corridor("M1"),
            Corridor("M2"),
        };

        var result = CorridorCurator.Apply(fresh, ["M2", "M3"]);

        result.Corridors.Count.ShouldBe(2);
        result.Corridors[0].RoadName.ShouldBe("M2");
        result.Corridors[1].RoadName.ShouldBe("M3");
        result.ExcludedFreshCount.ShouldBe(1);
        result.MissingSelections.ShouldBeEmpty();
    }

    [Fact]
    public void CaseInsensitiveAndTrimmedMatching()
    {
        var fresh = new List<TrafficCorridor>
        {
            Corridor("  M1 Motorway  "),
        };

        var result = CorridorCurator.Apply(fresh, ["m1 motorway"]);

        result.Corridors.ShouldHaveSingleItem();
        result.Corridors[0].RoadName.ShouldBe("  M1 Motorway  ");
        result.ExcludedFreshCount.ShouldBe(0);
        result.MissingSelections.ShouldBeEmpty();
    }

    [Fact]
    public void MissingSelection_ReportedInMissingSelections()
    {
        var fresh = new List<TrafficCorridor> { Corridor("M1") };

        var result = CorridorCurator.Apply(fresh, ["M1", "M2"]);

        result.Corridors.Count.ShouldBe(1);
        result.Corridors[0].RoadName.ShouldContain("M1");
        result.MissingSelections.ShouldBe(["M2"]);
        result.ExcludedFreshCount.ShouldBe(0);
    }

    [Fact]
    public void DuplicateCorridorNames_EachSelectionConsumesDistinctCorridor()
    {
        var fresh = new List<TrafficCorridor>
        {
            Corridor("M1", impactScore: 10),
            Corridor("M1", impactScore: 20),
        };

        var result = CorridorCurator.Apply(fresh, ["M1", "M1"]);

        result.Corridors.Count.ShouldBe(2);
        // First selection consumed first M1 (index 0), second consumed second M1 (index 1)
        result.Corridors[0].ImpactScore.ShouldBe(10);
        result.Corridors[1].ImpactScore.ShouldBe(20);
        result.MissingSelections.ShouldBeEmpty();
        result.ExcludedFreshCount.ShouldBe(0);
    }

    [Fact]
    public void EmptySelection_AllFreshExcluded()
    {
        var fresh = new List<TrafficCorridor>
        {
            Corridor("M1"),
            Corridor("M2"),
        };

        var result = CorridorCurator.Apply(fresh, []);

        result.Corridors.ShouldBeEmpty();
        result.MissingSelections.ShouldBeEmpty();
        result.ExcludedFreshCount.ShouldBe(2);
    }

    [Fact]
    public void NoFreshCorridors_SelectionAllMissing()
    {
        var result = CorridorCurator.Apply([], ["M1", "M2"]);

        result.Corridors.ShouldBeEmpty();
        result.MissingSelections.ShouldBe(["M1", "M2"]);
        result.ExcludedFreshCount.ShouldBe(0);
    }
}

using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class PriorityRoadsTests
{
    private static TrafficCorridor Corridor(string roadName, double impactScore)
    {
        return new TrafficCorridor(
            roadName, Severity: 1, FunctionalClass: 1, TotalLengthMeters: 1000,
            ImpactScore: impactScore, PrimaryType: "congestion",
            Description: $"Traffic on {roadName}", Incidents: []);
    }

    // ─── IsMatch ──────────────────────────────────────────────────────────

    [Fact]
    public void IsMatch_Substring_BothDirections()
    {
        PriorityRoads.IsMatch("M1", "M1 Pacific Motorway").ShouldBeTrue();
        PriorityRoads.IsMatch("M1 Pacific Motorway", "M1").ShouldBeTrue();
    }

    [Fact]
    public void IsMatch_CaseInsensitive()
    {
        PriorityRoads.IsMatch("m1", "M1 Pacific Motorway").ShouldBeTrue();
        PriorityRoads.IsMatch("M1", "m1 pacific motorway").ShouldBeTrue();
    }

    [Fact]
    public void IsMatch_EmptyStrings_NeverMatch()
    {
        PriorityRoads.IsMatch("", "M1").ShouldBeFalse();
        PriorityRoads.IsMatch("M1", "").ShouldBeFalse();
        PriorityRoads.IsMatch("", "").ShouldBeFalse();
    }

    [Fact]
    public void IsMatch_NoMatch_ReturnsFalse()
    {
        PriorityRoads.IsMatch("M1", "M2").ShouldBeFalse();
        PriorityRoads.IsMatch("M4", "M1 Pacific Motorway").ShouldBeFalse();
    }

    [Theory]
    // A bare substring test made every route-numbered road a prefix of its neighbours, so a
    // priority road "M1" silently boosted M10/M11/M12 too (dev, 2026-07-28).
    [InlineData("M11", "M1")]
    [InlineData("M1", "M11")]
    [InlineData("M10 Ring Road", "M1")]
    [InlineData("A1", "A10 Northern Highway")]
    public void IsMatch_NeighbouringRouteNumbers_DoNotMatch(string corridor, string priority)
    {
        PriorityRoads.IsMatch(corridor, priority).ShouldBeFalse();
    }

    [Theory]
    // The looseness still has to earn its keep: short corridor keys must match the fuller
    // names an expert types into the priority-roads manager.
    [InlineData("M1", "M1 Pacific Motorway")]
    [InlineData("M1 Pacific Motorway", "M1")]
    [InlineData("Western Ring Road", "Western Ring Road (M80)")]
    [InlineData("M80", "Western Ring Road (M80)")]
    [InlineData("Bells Line of Road", "Bells Line")]
    public void IsMatch_WholeWordContainment_StillMatches(string corridor, string priority)
    {
        PriorityRoads.IsMatch(corridor, priority).ShouldBeTrue();
    }

    // ─── BoostAndSort ─────────────────────────────────────────────────────

    [Fact]
    public void BoostAndSort_BoostsMatchingAndResorts()
    {
        var corridors = new List<TrafficCorridor>
        {
            Corridor("M1", 80),
            Corridor("Local St", 100),
        };

        var result = PriorityRoads.BoostAndSort(corridors, ["M1"]);

        result[0].RoadName.ShouldBe("M1"); // 80 * 1.3 = 104 > 100
        result[0].ImpactScore.ShouldBe(104.0);
        result[1].RoadName.ShouldBe("Local St");
        result[1].ImpactScore.ShouldBe(100);
    }

    [Fact]
    public void BoostAndSort_EmptyPriorityRoads_ReturnsSameOrder()
    {
        var corridors = new List<TrafficCorridor>
        {
            Corridor("M2", 90),
            Corridor("M1", 80),
        };

        var result = PriorityRoads.BoostAndSort(corridors, []);

        result.Count.ShouldBe(2);
        result[0].RoadName.ShouldBe("M2");
        result[1].RoadName.ShouldBe("M1");
        // Impact scores unchanged
        result[0].ImpactScore.ShouldBe(90);
        result[1].ImpactScore.ShouldBe(80);
    }

    [Fact]
    public void BoostAndSort_MultiplePriorityMatches()
    {
        var corridors = new List<TrafficCorridor>
        {
            Corridor("M1", 50),
            Corridor("M2", 60),
            Corridor("M3", 70),
        };

        var result = PriorityRoads.BoostAndSort(corridors, ["M1", "M3"]);

        // M3: 70 * 1.3 = 91, M1: 50 * 1.3 = 65, M2: 60
        result[0].RoadName.ShouldBe("M3");
        result[0].ImpactScore.ShouldBe(91.0);
        result[1].RoadName.ShouldBe("M1");
        result[1].ImpactScore.ShouldBe(65.0);
        result[2].RoadName.ShouldBe("M2");
        result[2].ImpactScore.ShouldBe(60.0);
    }

    // ─── EnabledIncidentBoostNames ────────────────────────────────────────

    [Fact]
    public void EnabledIncidentBoostNames_FiltersByBoostIncidents_PreservesSourceOrder()
    {
        var roads = new List<PriorityRoad>
        {
            new("M1 Pacific Motorway", BoostIncidents: true),
            new("M2 Motorway", BoostIncidents: false),
            new("M4 Western Motorway", BoostIncidents: true),
        };

        var result = PriorityRoads.EnabledIncidentBoostNames(roads);

        result.ShouldBe(["M1 Pacific Motorway", "M4 Western Motorway"]);
    }

    [Fact]
    public void EnabledIncidentBoostNames_SkipsRoadsSwitchedOff()
    {
        // `Enabled` is the manager's on/off switch and was documented as "skipped at
        // generation time", but nothing read it — a road switched off kept boosting
        // its incidents into the bulletin (dev, 2026-07-28).
        var roads = new List<PriorityRoad>
        {
            new("M1 Pacific Motorway", BoostIncidents: true, Enabled: false),
            new("M4 Western Motorway", BoostIncidents: true, Enabled: true),
        };

        PriorityRoads.EnabledIncidentBoostNames(roads).ShouldBe(["M4 Western Motorway"]);
    }

    [Fact]
    public void EnabledIncidentBoostNames_AllDisabled_ReturnsEmpty()
    {
        var roads = new List<PriorityRoad>
        {
            new("M1", BoostIncidents: false),
            new("M2", BoostIncidents: false),
        };

        PriorityRoads.EnabledIncidentBoostNames(roads).ShouldBeEmpty();
    }

    [Fact]
    public void EnabledIncidentBoostNames_EmptyInput_ReturnsEmpty()
    {
        PriorityRoads.EnabledIncidentBoostNames([]).ShouldBeEmpty();
    }


    // ─── FlowTierBonus ────────────────────────────────────────────────────

    [Fact]
    public void FlowTierBonus_MonotonicByTier_ZeroForNone()
    {
        PriorityRoads.FlowTierBonus(FlowPriorityTier.None).ShouldBe(0);
        PriorityRoads.FlowTierBonus(FlowPriorityTier.Normal)
            .ShouldBeLessThan(PriorityRoads.FlowTierBonus(FlowPriorityTier.High));
        PriorityRoads.FlowTierBonus(FlowPriorityTier.High)
            .ShouldBeLessThan(PriorityRoads.FlowTierBonus(FlowPriorityTier.Critical));
    }

    [Fact]
    public void FlowTier_High_ReachesMinorIncidentContention()
    {
        // A flowing arterial at High lands in the minor-incident band (≥80) without reaching moderate.
        double score = CorridorAggregator.CalculateFlowScore(3, 5000)
                       + PriorityRoads.FlowTierBonus(FlowPriorityTier.High);
        score.ShouldBeGreaterThanOrEqualTo(80);
        score.ShouldBeLessThan(150);
    }

    [Fact]
    public void FlowTier_Critical_ReachesModerateIncidentContention()
    {
        double score = CorridorAggregator.CalculateFlowScore(1, 5000)
                       + PriorityRoads.FlowTierBonus(FlowPriorityTier.Critical);
        score.ShouldBeGreaterThanOrEqualTo(150);
    }

}

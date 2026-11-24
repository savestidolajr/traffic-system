using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class BulletinContentSelectorTests
{
    private static TrafficCorridor Corridor(string name, double impact) =>
        new(name, 1, 1, 1000, impact, "accident", "x", []);

    private static FlowRoad Flow(string name, double score) =>
        new(name, 1.0, 5000, IsWeighted: false, FunctionalClass: 1, Score: score);

    [Fact]
    public void MajorIncidents_ExcludeGoodRuns()
    {
        var result = BulletinContentSelector.Select(
            [Corridor("A", 200), Corridor("B", 190), Corridor("C", 180)],
            [Flow("M1", 80)],
            slotCapacity: 2,
            maxGoodRuns: 8);

        result.Corridors.Select(c => c.RoadName).ShouldBe(["A", "B"]);
        result.GoodRuns.ShouldBeEmpty();
    }

    [Fact]
    public void MinorIncidents_GoodRunMakesTheCut()
    {
        // A clear major road (80) out-ranks a very minor incident (40) for the second slot.
        var result = BulletinContentSelector.Select(
            [Corridor("Minor1", 60), Corridor("Minor2", 40)],
            [Flow("M1", 80)],
            slotCapacity: 2,
            maxGoodRuns: 8);

        result.Corridors.Select(c => c.RoadName).ShouldBe(["Minor1"]);
        result.GoodRuns.Select(f => f.RoadName).ShouldBe(["M1"]);
    }

    [Fact]
    public void TieScore_IncidentWins()
    {
        var result = BulletinContentSelector.Select(
            [Corridor("Incident", 80)],
            [Flow("M1", 80)],
            slotCapacity: 1,
            maxGoodRuns: 8);

        result.Corridors.Select(c => c.RoadName).ShouldBe(["Incident"]);
        result.GoodRuns.ShouldBeEmpty();
    }

    [Fact]
    public void NoIncidents_FillsWithTopFlow()
    {
        var result = BulletinContentSelector.Select(
            [],
            [Flow("M1", 80), Flow("M2", 60), Flow("M3", 40)],
            slotCapacity: 2,
            maxGoodRuns: 8);

        result.Corridors.ShouldBeEmpty();
        result.GoodRuns.Select(f => f.RoadName).ShouldBe(["M1", "M2"]);
    }

    [Fact]
    public void RespectsMaxGoodRuns()
    {
        var result = BulletinContentSelector.Select(
            [],
            [Flow("M1", 80), Flow("M2", 70), Flow("M3", 60)],
            slotCapacity: 5,
            maxGoodRuns: 2);

        result.GoodRuns.Count.ShouldBe(2);
    }

    [Fact]
    public void HighTierFlow_BeatsMinorIncident_ButNotMajor()
    {
        // A High-tier flowing priority road (~92) out-ranks a minor incident (70) for the slot, but a
        // major incident (200) still buries it — the operator tier competes, it is never a guarantee.
        var quiet = BulletinContentSelector.Select(
            [Corridor("Minor", 70)], [Flow("WesternRing", 92)], slotCapacity: 1, maxGoodRuns: 8);
        quiet.GoodRuns.Select(f => f.RoadName).ShouldBe(["WesternRing"]);
        quiet.Corridors.ShouldBeEmpty();

        var busy = BulletinContentSelector.Select(
            [Corridor("Major", 200)], [Flow("WesternRing", 92)], slotCapacity: 1, maxGoodRuns: 8);
        busy.Corridors.Select(c => c.RoadName).ShouldBe(["Major"]);
        busy.GoodRuns.ShouldBeEmpty();
    }

    [Fact]
    public void SuppressedMinors_FreeTheirAirtimeToGoodRuns()
    {
        // Sydney fresh-eval shape: one Major closure + a pile of minor congestion corridors. The
        // minors win rundown slots on impact score but are tier-suppressed at render (never voiced
        // under a Major headline) — without backfill the bulletin airs ONE item and fills 18s with
        // generic filler while real flowing roads sit unused. The freed airtime must go to the
        // next-ranked good runs.
        TrafficCorridor Closure(string name, double impact) =>
            new(name, 1, 1, 1000, impact, "roadClosure", "shut", []);
        TrafficCorridor MinorCongestion(string name, double impact) =>
            new(name, 1, 1, 1000, impact, "congestion", "slow", []);

        var result = BulletinContentSelector.Select(
            [
                Closure("Warringah", 200),
                MinorCongestion("SideStreet1", 150),
                MinorCongestion("SideStreet2", 140),
                MinorCongestion("SideStreet3", 130)
            ],
            [Flow("M1", 80), Flow("M2", 70), Flow("M3", 60)],
            slotCapacity: 4,
            maxGoodRuns: 8);

        // Rundown selection unchanged: all four corridors keep their slots (operator visibility).
        result.Corridors.Select(c => c.RoadName)
            .ShouldBe(["Warringah", "SideStreet1", "SideStreet2", "SideStreet3"]);
        // But three of them render as nothing — their airtime backfills with real flowing roads.
        result.GoodRuns.Select(f => f.RoadName).ShouldBe(["M1", "M2", "M3"]);
    }
}

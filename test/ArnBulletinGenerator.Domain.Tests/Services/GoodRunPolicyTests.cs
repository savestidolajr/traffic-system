using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class GoodRunPolicyTests
{
    private static FlowRoad Road(string name, bool weighted = false, double lengthM = 1000) =>
        new(name, JamFactor: 1.0, LengthMeters: lengthM, IsWeighted: weighted);

    // ── (1) No incidents → fill ───────────────────────────────────────────

    [Fact]
    public void NoIncidents_FillsWithTopRoads_FillMode()
    {
        var pool = new[] { Road("A"), Road("B"), Road("C") };

        var result = GoodRunPolicy.SelectForGeneration(
            pool, problemCorridorCount: 0, slotCorridorCapacity: 6, maxGoodRuns: 8);

        result.Mode.ShouldBe(GoodRunPolicy.Mode.Fill);
        result.Roads.ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void NoIncidents_RespectsMaxGoodRuns()
    {
        var pool = Enumerable.Range(0, 10).Select(i => Road($"R{i}")).ToList();

        var result = GoodRunPolicy.SelectForGeneration(
            pool, problemCorridorCount: 0, slotCorridorCapacity: 6, maxGoodRuns: 3);

        result.Roads.Count.ShouldBe(3);
    }

    // ── (2) Incidents present ─────────────────────────────────────────────

    [Fact]
    public void IncidentsPresent_SlotFull_WeightedOnly_SecondaryMode()
    {
        var pool = new[] { Road("Watched", weighted: true), Road("Generic1"), Road("Generic2") };

        // problemCorridorCount >= capacity → the slot is full → no generic dilution.
        var result = GoodRunPolicy.SelectForGeneration(
            pool, problemCorridorCount: 6, slotCorridorCapacity: 6, maxGoodRuns: 8);

        result.Mode.ShouldBe(GoodRunPolicy.Mode.Secondary);
        result.Roads.ShouldBe(["Watched"]);
    }

    [Fact]
    public void IncidentsPresent_SlotNotFull_WeightedThenGenericFill()
    {
        var pool = new[] { Road("Watched", weighted: true), Road("Generic1"), Road("Generic2") };

        // Fewer problem corridors than the slot holds → generic roads fill the rest.
        var result = GoodRunPolicy.SelectForGeneration(
            pool, problemCorridorCount: 2, slotCorridorCapacity: 6, maxGoodRuns: 8);

        result.Mode.ShouldBe(GoodRunPolicy.Mode.Secondary);
        result.Roads.ShouldBe(["Watched", "Generic1", "Generic2"]);
    }

    [Fact]
    public void IncidentsPresent_SlotFull_NoWeighted_Empty()
    {
        var pool = new[] { Road("Generic1"), Road("Generic2") };

        var result = GoodRunPolicy.SelectForGeneration(
            pool, problemCorridorCount: 6, slotCorridorCapacity: 6, maxGoodRuns: 8);

        result.Roads.ShouldBeEmpty();
        result.Mode.ShouldBe(GoodRunPolicy.Mode.Secondary);
    }

    // ── (3) Operator override (data-backed) ───────────────────────────────

    [Fact]
    public void OperatorSelection_RestrictedToPool_PreservesOperatorOrder()
    {
        var pool = new[] { Road("A"), Road("B"), Road("C") };
        Option<IReadOnlyList<string>> pick = Option<IReadOnlyList<string>>.Some(["C", "NotInPool", "A"]);

        var result = GoodRunPolicy.SelectForGeneration(
            pool, problemCorridorCount: 3, slotCorridorCapacity: 6, maxGoodRuns: 8, operatorSelection: pick);

        // "NotInPool" is dropped (no fabrication); the operator's order is preserved.
        result.Roads.ShouldBe(["C", "A"]);
    }

    [Fact]
    public void OperatorSelection_LooseNameMatch_AgainstPool()
    {
        var pool = new[] { Road("M1 Pacific Motorway") };
        Option<IReadOnlyList<string>> pick = Option<IReadOnlyList<string>>.Some(["M1"]);

        var result = GoodRunPolicy.SelectForGeneration(
            pool, problemCorridorCount: 0, slotCorridorCapacity: 6, maxGoodRuns: 8, operatorSelection: pick);

        result.Roads.ShouldBe(["M1"]);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void EmptyPool_ReturnsEmpty()
    {
        GoodRunPolicy.SelectForGeneration([], problemCorridorCount: 0, slotCorridorCapacity: 6, maxGoodRuns: 8)
            .Roads.ShouldBeEmpty();
    }

    [Fact]
    public void ZeroMax_ReturnsEmpty()
    {
        var pool = new[] { Road("A") };
        GoodRunPolicy.SelectForGeneration(pool, problemCorridorCount: 0, slotCorridorCapacity: 6, maxGoodRuns: 0)
            .Roads.ShouldBeEmpty();
    }

    // ── Corridor-collision dedup ──────────────────────────────────────────

    private static TrafficCorridor Corridor(string roadName) =>
        new(roadName, Severity: 2, FunctionalClass: 3, TotalLengthMeters: 500,
            ImpactScore: 10, PrimaryType: "roadClosure", Description: "", Incidents: []);

    [Fact]
    public void ExcludeCorridorRoads_DropsRoadSharingNameWithIncident()
    {
        var pool = new[] { Road("Pacific Motorway"), Road("Stewart Road"), Road("Gooding Drive") };
        var corridors = new[] { Corridor("Pacific Motorway") };

        GoodRunPolicy.ExcludeCorridorRoads(pool, corridors)
            .Select(f => f.RoadName).ShouldBe(["Stewart Road", "Gooding Drive"]);
    }

    [Fact]
    public void ExcludeIncidentRoads_DropsRoadNamedByAnyIncident_EvenBeyondTheCorridorCap()
    {
        // Issue 9. ExcludeCorridorRoads is fed `allCorridors`, which is already capped at
        // BroadcastOptions.MaxCorridorsForStorage (20). An incident on a road ranked 21st therefore
        // never suppressed its road from the flow pool, so HERE could report a crash on a road the
        // bulletin then called "moving nicely". Exclusion must consider every incident HERE returned,
        // not just the corridors that survived the cap.
        var pool = new[] { Road("Hume Highway"), Road("Gooding Drive") };

        GoodRunPolicy.ExcludeIncidentRoads(pool, ["Hume Hwy"])
            .Select(f => f.RoadName).ShouldBe(["Gooding Drive"]);
    }

    [Fact]
    public void ExcludeIncidentRoads_IgnoresUnnamedAndPlaceholderLocations()
    {
        // An incident HERE gave no usable road name for must not silently wipe the good-run pool.
        var pool = new[] { Road("Hume Highway") };

        GoodRunPolicy.ExcludeIncidentRoads(pool, ["", "  ", "Unidentified Road", "Unknown Location"])
            .Select(f => f.RoadName).ShouldBe(["Hume Highway"]);
    }

    [Fact]
    public void ExcludeCorridorRoads_IsCaseInsensitiveAndTrims()
    {
        var pool = new[] { Road("  pacific motorway ") };
        var corridors = new[] { Corridor("Pacific Motorway") };

        GoodRunPolicy.ExcludeCorridorRoads(pool, corridors).ShouldBeEmpty();
    }

    [Fact]
    public void ExcludeCorridorRoads_KeepsDistinctRoadSharingAPrefix()
    {
        // A "Smith Street" incident must NOT drop the distinct "Smith Street Motorway" good-run.
        var pool = new[] { Road("Smith Street Motorway") };
        var corridors = new[] { Corridor("Smith Street") };

        GoodRunPolicy.ExcludeCorridorRoads(pool, corridors)
            .Select(f => f.RoadName).ShouldBe(["Smith Street Motorway"]);
    }

    [Fact]
    public void ExcludeCorridorRoads_DropsParenthesizedDirectionVariant()
    {
        // HERE's flow feed can spell the same road with a parenthesized direction residue
        // ("PACIFIC MOTORWAY (NORTHBOUND)") while the incident corridor carries a trailing
        // direction word ("Pacific Motorway citybound") — same canonical road, so the flow
        // entry must still be suppressed, not treated as a different road.
        var pool = new[] { Road("PACIFIC MOTORWAY (NORTHBOUND)"), Road("Stewart Road") };
        var corridors = new[] { Corridor("Pacific Motorway citybound") };

        GoodRunPolicy.ExcludeCorridorRoads(pool, corridors)
            .Select(f => f.RoadName).ShouldBe(["Stewart Road"]);
    }

    [Fact]
    public void ExcludeCorridorRoads_DropsWordSuffixDirectionVariant()
    {
        var pool = new[] { Road("Bruce Highway outbound"), Road("Gooding Drive") };
        var corridors = new[] { Corridor("Bruce Highway") };

        GoodRunPolicy.ExcludeCorridorRoads(pool, corridors)
            .Select(f => f.RoadName).ShouldBe(["Gooding Drive"]);
    }

    [Fact]
    public void ExcludeCorridorRoads_KeepsDistinctRoadDespiteDirectionSuffix()
    {
        // Direction-stripping must not fold a genuinely distinct road ("Smith Street Motorway")
        // into an incident on "Smith Street" just because the flow entry also carries a
        // parenthesized direction residue.
        var pool = new[] { Road("SMITH STREET MOTORWAY (NORTHBOUND)") };
        var corridors = new[] { Corridor("Smith Street") };

        GoodRunPolicy.ExcludeCorridorRoads(pool, corridors)
            .Select(f => f.RoadName).ShouldBe(["SMITH STREET MOTORWAY (NORTHBOUND)"]);
    }

    [Fact]
    public void ExcludeCorridorRoads_NoCorridors_ReturnsPoolUnchanged()
    {
        var pool = new[] { Road("A"), Road("B") };
        GoodRunPolicy.ExcludeCorridorRoads(pool, []).Select(f => f.RoadName).ShouldBe(["A", "B"]);
    }
}

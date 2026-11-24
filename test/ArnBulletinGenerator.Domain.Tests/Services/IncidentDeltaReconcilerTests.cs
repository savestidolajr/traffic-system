using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class IncidentDeltaReconcilerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static IncidentDeltaItem ClearedItem(
        string id, string roadName, Option<string>? road = null) =>
        new(id, "incident", 2, road ?? Option<string>.Some(roadName), Option<string>.None,
            Option<string>.None, 100, Option<string>.None);

    private static TrafficCorridor Corridor(string roadName) =>
        new(roadName, 3, 3, 500, 1.0, "accident", "desc",
            Array.Empty<TrafficIncident>());

    private static IncidentDeltaContext Delta(
        IReadOnlyList<IncidentDeltaItem>? cleared = null,
        IReadOnlyList<IncidentDeltaItem>? @new = null,
        IReadOnlyList<IncidentDeltaItem>? updated = null,
        IReadOnlyList<IncidentDeltaItem>? unchanged = null) =>
        new(
            @new ?? [],
            updated ?? [],
            cleared ?? [],
            unchanged ?? []);

    // ── 1. same-road match → cleared item is removed ────────────────────────

    [Fact]
    public void SuppressedClearedOnActiveRoads_ClearedMatchesActiveRoadByExactMatch_RemovedFromCleared()
    {
        var cleared = new[] { ClearedItem("c1", "A1") };
        var corridors = new[] { Corridor("A1") };
        var delta = Delta(cleared: cleared);

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, corridors);

        result.ClearedIncidents.ShouldBeEmpty();
    }

    [Fact]
    public void SuppressedClearedOnActiveRoads_ClearedMatchesActiveRoadByCaseOrAbbreviation_RemovedFromCleared()
    {
        // RoadNameFormatter.IsSameRoad uses NormalizeForComparison (lowercase + strip whitespace)
        // so "a1" and "A1" must match — proves it is NOT a naive exact string compare.
        var cleared = new[] { ClearedItem("c1", "a1") };
        var corridors = new[] { Corridor("A1") };
        var delta = Delta(cleared: cleared);

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, corridors);

        result.ClearedIncidents.ShouldBeEmpty();
    }

    [Fact]
    public void SuppressedClearedOnActiveRoads_ClearedMatchesOneOfMultipleCorridors_ThatItemRemoved()
    {
        var cleared = new[] { ClearedItem("c1", "M4"), ClearedItem("c2", "B56") };
        var corridors = new[] { Corridor("M4"), Corridor("B56") };
        var delta = Delta(cleared: cleared);

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, corridors);

        result.ClearedIncidents.ShouldBeEmpty();
    }

    // ── 2. no match → cleared item is kept ──────────────────────────────────

    [Fact]
    public void SuppressedClearedOnActiveRoads_ClearedDoesNotMatchAnyActiveRoad_KeptInCleared()
    {
        var cleared = new[] { ClearedItem("c1", "Punt Road") };
        var corridors = new[] { Corridor("A1"), Corridor("M4") };
        var delta = Delta(cleared: cleared);

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, corridors);

        result.ClearedIncidents.Count.ShouldBe(1);
        result.ClearedIncidents[0].IncidentId.ShouldBe("c1");
    }

    [Fact]
    public void SuppressedClearedOnActiveRoads_OnlyMatchingClearedItemRemoved_OthersPreserved()
    {
        var cleared = new[]
        {
            ClearedItem("c1", "A1"),   // matches corridor → suppressed
            ClearedItem("c2", "B56"),  // does not match   → kept
        };
        var corridors = new[] { Corridor("A1") };
        var delta = Delta(cleared: cleared);

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, corridors);

        result.ClearedIncidents.Count.ShouldBe(1);
        result.ClearedIncidents[0].IncidentId.ShouldBe("c2");
    }

    // ── 3. empty inputs → delta returned unchanged (same instance) ───────────

    [Fact]
    public void SuppressedClearedOnActiveRoads_EmptyClearedIncidents_ReturnsSameDelta()
    {
        var delta = Delta(cleared: []);

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, new[] { Corridor("A1") });

        ReferenceEquals(result, delta).ShouldBeTrue();
    }

    [Fact]
    public void SuppressedClearedOnActiveRoads_EmptyActiveCorridors_ReturnsSameDelta()
    {
        var delta = Delta(cleared: new[] { ClearedItem("c1", "A1") });

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, []);

        ReferenceEquals(result, delta).ShouldBeTrue();
    }

    // ── 4. RoadName.None → always kept ──────────────────────────────────────

    [Fact]
    public void SuppressedClearedOnActiveRoads_ClearedItemWithNoneRoadName_KeptEvenIfCorridorsPresent()
    {
        var cleared = new[] { ClearedItem("c1", "x", road: Option<string>.None) };
        var corridors = new[] { Corridor("A1") };
        var delta = Delta(cleared: cleared);

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, corridors);

        result.ClearedIncidents.Count.ShouldBe(1);
        result.ClearedIncidents[0].IncidentId.ShouldBe("c1");
    }

    // ── 5. other lists never touched ────────────────────────────────────────

    [Fact]
    public void SuppressedClearedOnActiveRoads_NewUpdatedUnchangedIncidentsNeverModified()
    {
        var newItem = new IncidentDeltaItem("n1", "new", 1, Option<string>.Some("A1"),
            Option<string>.None, Option<string>.None, 0, Option<string>.None);
        var updatedItem = new IncidentDeltaItem("u1", "updated", 1, Option<string>.Some("M4"),
            Option<string>.None, Option<string>.None, 0, Option<string>.None);
        var unchangedItem = new IncidentDeltaItem("uc1", "unchanged", 1, Option<string>.Some("B56"),
            Option<string>.None, Option<string>.None, 0, Option<string>.None);
        var cleared = new[] { ClearedItem("c1", "A1") };

        var delta = Delta(
            @new: new[] { newItem },
            updated: new[] { updatedItem },
            cleared: cleared,
            unchanged: new[] { unchangedItem });

        var corridors = new[] { Corridor("A1") };
        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, corridors);

        result.NewIncidents.ShouldBeSameAs(delta.NewIncidents);
        result.UpdatedIncidents.ShouldBeSameAs(delta.UpdatedIncidents);
        result.UnchangedIncidents.ShouldBeSameAs(delta.UnchangedIncidents);
        result.ClearedIncidents.ShouldBeEmpty();
    }

    // ── 6. no match found → same instance returned (reconciled.Count == original) ─

    [Fact]
    public void SuppressedClearedOnActiveRoads_NoMatchAmongCleared_ReturnsSameDeltaInstance()
    {
        var cleared = new[] { ClearedItem("c1", "Punt Road"), ClearedItem("c2", "B56") };
        var corridors = new[] { Corridor("A1"), Corridor("M4") };
        var delta = Delta(cleared: cleared);

        var result = IncidentDeltaReconciler.SuppressClearedOnActiveRoads(delta, corridors);

        // No cleared item matched any corridor → reconciled count == original count → same ref
        ReferenceEquals(result, delta).ShouldBeTrue();
        result.ClearedIncidents.Count.ShouldBe(2);
    }
}

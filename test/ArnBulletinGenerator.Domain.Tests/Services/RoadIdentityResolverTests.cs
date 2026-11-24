using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public sealed class RoadIdentityResolverTests
{
    private static IReadOnlyDictionary<string, SegmentRoad> Map(params (string Ref, string? Name)[] entries) =>
        entries.ToDictionary(e => e.Ref, e => new SegmentRoad(e.Name, 3, false), StringComparer.Ordinal);

    [Fact]
    public void EverySegmentAgreeing_IsUnambiguous()
    {
        // Measured: 88.0% of incidents and 90.3% of flow rows land here.
        var segments = Enumerable.Range(0, 6).Select(i => ($"seg-{i}", 120.0)).ToList();
        var roads = Map([.. Enumerable.Range(0, 6).Select(i => ($"seg-{i}", (string?)"Cabramatta Rd"))]);

        var identity = RoadIdentityResolver.Resolve(segments, roads);

        identity.Verdict.ShouldBe(IdentityVerdict.Unambiguous);
        identity.Name.ShouldBe("Cabramatta Rd");
        identity.NamedShare.ShouldBe(1.0);
    }

    [Fact]
    public void TwoComparableRoads_IsSplitAndWithholdsTheName()
    {
        // A record spanning two roads has no single identity, so it is reported as neither. Naming it
        // after whichever segment came first is how events were named after the side street they clip.
        var identity = RoadIdentityResolver.Resolve(
            [("seg-a", 504), ("seg-b", 440)],
            Map(("seg-a", "Railway Pde"), ("seg-b", "Atchison Rd")));

        identity.Verdict.ShouldBe(IdentityVerdict.Split);
        identity.Name.ShouldBeNull();
        identity.NamedShare.ShouldBeLessThan(RoadIdentityResolver.DominantShareFloor);
    }

    [Fact]
    public void ALengthDominantRoad_NamesTheRecord()
    {
        // Weighting by LENGTH is the point: 900 m of arterial is the road, and the 100 m of side street
        // the record clips is not. Counting references instead of metres gets that backwards.
        var identity = RoadIdentityResolver.Resolve(
            [("seg-a", 900), ("seg-b", 100)],
            Map(("seg-a", "Hume Hwy"), ("seg-b", "Todman Rd")));

        identity.Verdict.ShouldBe(IdentityVerdict.Dominant);
        identity.Name.ShouldBe("Hume Hwy");
        identity.NamedShare.ShouldBe(0.9, 0.001);
    }

    [Fact]
    public void PartialMapCoverage_MustStillDominateTotalMeasuredLength()
    {
        var insufficient = RoadIdentityResolver.Resolve(
            [("mapped", 600), ("unresolved", 400)],
            Map(("mapped", "Hume Hwy")));
        var sufficient = RoadIdentityResolver.Resolve(
            [("mapped", 800), ("unresolved", 200)],
            Map(("mapped", "Hume Hwy")));

        insufficient.Name.ShouldBeNull();
        insufficient.Verdict.ShouldBe(IdentityVerdict.Unnamed);
        insufficient.NamedShare.ShouldBe(0.6, 0.001);
        sufficient.Name.ShouldBe("Hume Hwy");
        sufficient.Verdict.ShouldBe(IdentityVerdict.Dominant);
        sufficient.NamedShare.ShouldBe(0.8, 0.001);
    }

    [Fact]
    public void NoNamedSegment_IsUnnamed()
    {
        RoadIdentityResolver.Resolve([("seg-a", 300)], Map(("seg-a", null)))
            .Verdict.ShouldBe(IdentityVerdict.Unnamed);
        RoadIdentityResolver.Resolve([("seg-a", 300)], Map())
            .Verdict.ShouldBe(IdentityVerdict.Unnamed);
        RoadIdentityResolver.Resolve([], Map(("seg-a", "Hume Hwy")))
            .Verdict.ShouldBe(IdentityVerdict.Unnamed);
    }

    [Fact]
    public void ZeroLengthSegmentsStillCount()
    {
        // HERE omits `length` on some references; a weight of zero would silently drop the only
        // evidence the record has.
        var identity = RoadIdentityResolver.Resolve(
            [("seg-a", 0), ("seg-b", 0)],
            Map(("seg-a", "Wallgrove Rd"), ("seg-b", "Wallgrove Rd")));

        identity.Verdict.ShouldBe(IdentityVerdict.Unambiguous);
        identity.Name.ShouldBe("Wallgrove Rd");
    }

    [Fact]
    public void RampFlagFollowsTheLengthWeightedMajority()
    {
        var roads = new Dictionary<string, SegmentRoad>(StringComparer.Ordinal)
        {
            ["ramp"] = new("Warringah Fwy", 1, true),
            ["main"] = new("Warringah Fwy", 1, false)
        };

        RoadIdentityResolver.Resolve([("ramp", 800), ("main", 100)], roads).IsRamp.ShouldBeTrue();
        RoadIdentityResolver.Resolve([("ramp", 100), ("main", 800)], roads).IsRamp.ShouldBeFalse();
    }

    [Fact]
    public void FunctionalClassIsTheMostImportantNamedSegment()
    {
        var roads = new Dictionary<string, SegmentRoad>(StringComparer.Ordinal)
        {
            ["a"] = new("Hume Hwy", 5, false),
            ["b"] = new("Hume Hwy", 2, false)
        };

        RoadIdentityResolver.Resolve([("a", 500), ("b", 500)], roads).FunctionalClass.ShouldBe(2);
    }
}

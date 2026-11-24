using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

/// <summary>
///     Unit coverage for the queue-extent enrichment ("from A to B") — the ATN device that voices
///     magnitude as WHERE a stretch runs instead of a minute count. Covers the pure logic
///     (composition rule, endpoint resolution, name cleaning); the live revgeocode is exercised by
///     the eval harness.
/// </summary>
public class ExtentEnrichmentTests
{
    private static TrafficIncident Incident(string id, double len, params (double Lat, double Lng)[] shape) =>
        new(id, "Crash", 1, "loc", "accident", LengthMeters: len,
            Shape: shape.Length > 0 ? shape.Select(p => new CoveragePoint(p.Lat, p.Lng)).ToList() : null);

    private static TrafficCorridor Corridor(
        string road, double totalLen, IReadOnlyList<TrafficIncident> incidents,
        Option<(double Lat, double Lng)> start = default,
        Option<(double Lat, double Lng)> end = default) =>
        new(road, 1, 3, totalLen, 50, "accident", "desc", incidents, StartPoint: start, EndPoint: end);

    // ── ComposeExtent: the "from A to B" composition rule ──

    [Fact]
    public void ComposeExtent_DistinctNames_BuildsFromAToB()
    {
        var extent = HereTrafficProvider.ComposeExtent("Homebush", "Parramatta", "M4");
        extent.HasValue.ShouldBeTrue();
        extent.Value.ShouldBe("from Homebush to Parramatta");
    }

    [Fact]
    public void ComposeExtent_SameEnds_None() =>
        HereTrafficProvider.ComposeExtent("Pine Street", "pine street", "King Street")
            .HasValue.ShouldBeFalse();

    [Fact]
    public void ComposeExtent_EndRestatesOwnRoad_None() =>
        // "from Church Street to M4" on the M4 corridor locates nothing — the end is just the road.
        HereTrafficProvider.ComposeExtent("Church Street", "M4", "M4").HasValue.ShouldBeFalse();

    [Fact]
    public void ComposeExtent_EndRestatesOwnRoadAsAbbreviation_None()
    {
        // Observed live (Gold Coast): shipped as "closed from Helensvale Road to Country Club
        // Drive" — the corridor's own road was reverse-geocoded back abbreviated ("Helensvale Rd")
        // against the corridor's spelled-out RoadName ("Helensvale Road citybound"), so the OLD
        // exact-string self-reference check missed it entirely.
        HereTrafficProvider.ComposeExtent("Helensvale Rd", "Country Club Dr", "Helensvale Road citybound")
            .HasValue.ShouldBeFalse();
    }

    [Fact]
    public void ComposeExtent_MissingEnd_None() =>
        HereTrafficProvider.ComposeExtent("Church Street", null, "M4").HasValue.ShouldBeFalse();

    // ── CleanEndpointName: reduce a revgeocode result to a speakable name ──

    [Fact]
    public void CleanEndpointName_StripsSuburbStateSuffix() =>
        HereTrafficProvider.CleanEndpointName("Pine Street, Pyrmont NSW 2009").ShouldBe("Pine Street");

    [Theory]
    [InlineData("Unidentified Road")]
    [InlineData("Unknown Location")]
    [InlineData("99")]
    [InlineData("")]
    public void CleanEndpointName_PlaceholderOrNumeric_Empty(string raw) =>
        HereTrafficProvider.CleanEndpointName(raw).ShouldBe(string.Empty);

    // ── ResolveCorridorEndpoints: where the two ends come from ──

    [Fact]
    public void ResolveEndpoints_PrefersRoutedStartEndPoints()
    {
        var corridor = Corridor("M4", 2000, [Incident("i1", 2000)],
            start: Option<(double Lat, double Lng)>.Some((-33.8, 151.0)),
            end: Option<(double Lat, double Lng)>.Some((-33.9, 151.2)));

        var ends = HereTrafficProvider.ResolveCorridorEndpoints(corridor);

        ends.ShouldNotBeNull();
        ends!.Value.Start.ShouldBe((-33.8, 151.0));
        ends.Value.End.ShouldBe((-33.9, 151.2));
    }

    [Fact]
    public void ResolveEndpoints_FallsBackToShapeEnds_WhenLongEnough()
    {
        var corridor = Corridor("M4", 1200,
            [Incident("i1", 1200, (-33.80, 151.00), (-33.82, 151.05), (-33.85, 151.10))]);

        var ends = HereTrafficProvider.ResolveCorridorEndpoints(corridor);

        ends.ShouldNotBeNull();
        ends!.Value.Start.ShouldBe((-33.80, 151.00));
        ends.Value.End.ShouldBe((-33.85, 151.10));
    }

    [Fact]
    public void ResolveEndpoints_ShortStretch_Null() =>
        // A point incident has no honest extent.
        HereTrafficProvider.ResolveCorridorEndpoints(
            Corridor("Side Street", 200, [Incident("i1", 200, (-33.80, 151.00), (-33.8001, 151.0001))]))
            .ShouldBeNull();

    [Fact]
    public void ResolveEndpoints_NoPointsNoShape_Null() =>
        HereTrafficProvider.ResolveCorridorEndpoints(
            Corridor("Side Street", 1500, [Incident("i1", 1500)]))
            .ShouldBeNull();
}

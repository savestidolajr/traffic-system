using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

/// <summary>
///     NSW structured direction and lane-impact reductions, using the authority's live GeoJSON
///     property names and values measured on 2026-08-24.
/// </summary>
public sealed class OfficialFeedNswImpactTests
{
    private static readonly CoverageArea Sydney = CoverageArea.Create(-33.8688, 151.2093, 25.0).Value;

    [Fact]
    public void NswImpactLane_ParsesDirectionCountsAndExtent_WithoutCreatingClosure()
    {
        var parsed = Parse("""
            {
              "headline": "Crash",
              "roads": [{
                "mainStreet": "M5",
                "crossStreet": "Marsh Street",
                "suburb": "Mascot",
                "impactedLanes": [{
                  "affectedDirection": "Southbound",
                  "closedLanes": "1",
                  "numberOfLanes": "2",
                  "description": "Lane 2",
                  "extent": "Lanes closed",
                  "roadType": "Road"
                }]
              }],
              "periods": [{"direction": "Both directions"}]
            }
            """);

        parsed.Direction.ShouldBe("Southbound");
        parsed.LanesClosed.ShouldBe(1);
        parsed.LanesTotal.ShouldBe(2);
        OfficialIncidentNormalizer.ToIncident(parsed, DateTimeOffset.UtcNow).RoadClosed.ShouldBeFalse();
    }

    /// <summary>
    ///     A direction the AUTHORITY published structurally must not be traced to HERE's prose.
    ///     <see cref="DirectionSource" /> exists so a direction can be traced rather than trusted, and
    ///     the official-feed path stamped every direction as <c>HereDescription</c> - harmless only
    ///     while it never produced one. Parsing it (2026-08-24) made the provenance wrong, not absent.
    /// </summary>
    [Fact]
    public void NswDirection_IsTracedToTheAuthority_NotToHereProse()
    {
        var parsed = Parse("""
            {
              "headline": "Crash",
              "roads": [{"mainStreet": "M5", "impactedLanes": []}],
              "periods": [{"direction": "Westbound"}]
            }
            """);

        var incident = OfficialIncidentNormalizer.ToIncident(parsed, DateTimeOffset.UtcNow);

        incident.DirectionSource.ShouldBe(DirectionSource.AuthorityStructured);
        incident.Fact!.DirectionSource.ShouldBe(DirectionSource.AuthorityStructured);
    }

    [Theory]
    [InlineData("Both directions")]
    [InlineData("All directions")]
    public void NswDirection_FiltersNonSpecificDirection(string direction)
    {
        var parsed = Parse($$"""
            {
              "headline": "Hazard",
              "roads": [{"mainStreet": "M5", "impactedLanes": [{"affectedDirection": "{{direction}}"}]}]
            }
            """);

        parsed.Direction.ShouldBeNull();
    }

    [Fact]
    public void NswDirection_FallsBackToPeriod_WhenImpactedLanesAreAbsent()
    {
        var parsed = Parse("""
            {
              "headline": "Roadwork",
              "roads": [{"mainStreet": "M5"}],
              "periods": [{"direction": "Westbound"}]
            }
            """);

        parsed.Direction.ShouldBe("Westbound");
    }

    /// <summary>
    ///     The shape 96% of the feed actually has. Measured 2026-08-24: 310 of 322 live NSW records
    ///     carry <c>impactedLanes</c> as an EMPTY array, so treating the key's PRESENCE as lane data
    ///     suppresses the periods fallback and drops direction on nearly every record - 1.2% coverage
    ///     where <c>periods[].direction</c> supplies 93%. Only real entries may outrank periods.
    /// </summary>
    [Fact]
    public void NswDirection_FallsBackToPeriod_WhenImpactedLanesIsAnEmptyArray()
    {
        var parsed = Parse("""
            {
              "headline": "Crash",
              "roads": [{"mainStreet": "M5", "impactedLanes": []}],
              "periods": [{"direction": "Westbound"}]
            }
            """);

        parsed.Direction.ShouldBe("Westbound");
    }

    [Fact]
    public void NswDirection_ConflictingSpecificPeriods_AreNotCollapsed()
    {
        var parsed = Parse("""
            {
              "headline": "Hazard",
              "roads": [{"mainStreet": "M5"}],
              "periods": [
                {"direction": "Northbound"},
                {"direction": "Southbound"}
              ]
            }
            """);

        parsed.Direction.ShouldBeNull();
    }

    [Fact]
    public void NswLaneCounts_EmptyStringsRemainAbsent_NotZero()
    {
        var parsed = Parse("""
            {
              "headline": "Roadwork",
              "roads": [{
                "mainStreet": "M5",
                "impactedLanes": [{
                  "affectedDirection": "Westbound",
                  "closedLanes": "",
                  "numberOfLanes": "",
                  "description": "",
                  "extent": "Affected"
                }]
              }]
            }
            """);

        parsed.LanesClosed.ShouldBeNull();
        parsed.LanesTotal.ShouldBeNull();
        parsed.ExtentDescription.ShouldBe("Affected");
    }

    [Fact]
    public void NswLaneCounts_SelectWorstClosedImpact_AndDropAnUnreliableTotal()
    {
        var parsed = Parse("""
            {
              "headline": "Roadwork",
              "roads": [{
                "mainStreet": "M5",
                "impactedLanes": [
                  {
                    "affectedDirection": "Westbound",
                    "closedLanes": "1",
                    "numberOfLanes": "2",
                    "extent": "Affected"
                  },
                  {
                    "affectedDirection": "Westbound",
                    "closedLanes": "2",
                    "numberOfLanes": "1",
                    "extent": "Lanes closed"
                  }
                ]
              }]
            }
            """);

        parsed.LanesClosed.ShouldBe(2);
        parsed.LanesTotal.ShouldBeNull();
        parsed.ExtentDescription.ShouldBe("Lanes closed");
    }

    [Fact]
    public void NswRoadClosureCategory_WithAffectedExtent_DoesNotSetRoadClosed()
    {
        var parsed = Parse("""
            {
              "headline": "Roadwork",
              "roads": [{"mainStreet": "M5"}],
              "periods": [{
                "closureType": "ROAD_CLOSURE",
                "roadextent": "Affected",
                "direction": "Westbound"
              }]
            }
            """);

        OfficialIncidentNormalizer.ToIncident(parsed, DateTimeOffset.UtcNow).RoadClosed.ShouldBeFalse();
        parsed.Direction.ShouldBe("Westbound");
    }

    [Fact]
    public void NswCapabilities_TreatDirectionAndLaneCountsAsStructured()
    {
        var feed = new LiveTrafficNswFeed(
            new HttpClient(),
            Options.Create(new OfficialFeedsOptions()),
            NullLogger<LiveTrafficNswFeed>.Instance);

        feed.Capabilities.StructuredFields.ShouldContain(ParsedField.Direction);
        feed.Capabilities.StructuredFields.ShouldContain(ParsedField.LanesClosed);
        feed.Capabilities.StructuredFields.ShouldContain(ParsedField.LanesTotal);
        feed.Capabilities.ProseFields.ShouldContain(ParsedField.OtherAssets);
        feed.Capabilities.ProseFields.ShouldNotContain(ParsedField.Direction);
        feed.Capabilities.ProseFields.ShouldNotContain(ParsedField.LanesClosed);
        feed.Capabilities.ProseFields.ShouldNotContain(ParsedField.LanesTotal);
    }

    private static OfficialTrafficEvent Parse(string properties)
    {
        string payload = $$"""
            {
              "type": "FeatureCollection",
              "features": [{
                "type": "Feature",
                "id": "nsw-impact-test",
                "geometry": {"type": "Point", "coordinates": [151.2093, -33.8688]},
                "properties": {{properties}}
              }]
            }
            """;
        using var document = JsonDocument.Parse(payload);
        return OfficialFeedGeoJson.ParseNsw(document, "livetraffic-nsw", true, Sydney).Single();
    }
}

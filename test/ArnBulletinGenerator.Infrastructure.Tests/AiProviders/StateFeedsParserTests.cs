using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public class StateFeedsParserTests
{
    private static readonly CoverageArea SydneyArea = CoverageArea.Create(-33.8688, 151.2093, 50.0).Value;
    private static readonly CoverageArea MelbourneArea = CoverageArea.Create(-37.8136, 144.9631, 50.0).Value;

    [Fact]
    public void ParseQld_InterstateMirrorRecords_AreDropped()
    {
        // QLDTraffic mirrors other states' records (73 NSW-district rows observed live
        // 2026-08-18: "West Botany Street, Rockdale, district NSW" inside the QLD feed). A
        // mirror is second-hand — the home authority owns its own roads — and ingesting the
        // copy stamped 'qldtraffic' provenance onto Sydney corridors.
        string json = """
        {
            "features": [
                {
                    "properties": {
                        "id": 711036,
                        "event_type": "Hazard",
                        "road_summary": { "road_name": "West Botany Street", "locality": "Rockdale", "district": "NSW" }
                    },
                    "geometry": { "type": "MultiPoint", "coordinates": [[151.14488, -33.96134]] }
                },
                {
                    "properties": {
                        "id": 1,
                        "event_type": "Crash",
                        "road_summary": { "road_name": "Kingsford Smith Drive", "locality": "Hamilton", "district": "Metropolitan" }
                    },
                    "geometry": { "type": "Point", "coordinates": [153.062, -27.439] }
                }
            ]
        }
        """;
        var brisbane = CoverageArea.Create(-27.4698, 153.0251, 50.0).Value;

        using var doc = JsonDocument.Parse(json);
        OfficialFeedGeoJson.ParseQld(doc, "qldtraffic", SydneyArea).ShouldBeEmpty();
        OfficialFeedGeoJson.ParseQld(doc, "qldtraffic", brisbane)
            .ShouldHaveSingleItem().RoadName.ShouldBe("Kingsford Smith Drive");
    }

    [Fact]
    public void ParseVic_ValidDisruption_ParsesCorrectly()
    {
        string json = """
        {
            "features": [
                {
                    "properties": {
                        "id": "vic-123",
                        "title": "Crash on West Gate Fwy",
                        "road_name": "West Gate Freeway",
                        "suburb": "Port Melbourne",
                        "description": "Right lanes closed",
                        "type": "accident"
                    },
                    "geometry": {
                        "coordinates": [144.93, -37.82]
                    }
                }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var events = OfficialFeedGeoJson.ParseVic(doc, "victraffic", MelbourneArea);

        events.Count.ShouldBe(1);
        var ev = events[0];
        ev.SourceId.ShouldBe("victraffic");
        ev.Id.ShouldBe("vic-123");
        ev.Headline.ShouldBe("Crash on West Gate Fwy");
        ev.RoadName.ShouldBe("West Gate Freeway");
        ev.Suburb.ShouldBe("Port Melbourne");
        ev.IsIncidentClass.ShouldBeTrue();
    }

    [Fact]
    public void ParseArcGis_WaFeature_ParsesCorrectly()
    {
        var perthArea = CoverageArea.Create(-31.9505, 115.8605, 50.0).Value;
        string json = """
        {
            "features": [
                {
                    "attributes": {
                        "OBJECTID": 9876,
                        "ROAD_NAME": "Kwinana Freeway",
                        "DESCRIPTION": "Vehicle rollover",
                        "EVENT_TYPE": "Crash"
                    },
                    "geometry": {
                        "x": 115.85,
                        "y": -31.96
                    }
                }
            ]
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var events = OfficialFeedGeoJson.ParseArcGis(doc, "mainroads-wa", perthArea);

        events.Count.ShouldBe(1);
        var ev = events[0];
        ev.SourceId.ShouldBe("mainroads-wa");
        ev.Id.ShouldBe("9876");
        ev.RoadName.ShouldBe("Kwinana Freeway");
        ev.IsIncidentClass.ShouldBeTrue();
    }
}

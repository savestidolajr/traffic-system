using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public class HereGeoFilterTests
{
    [Fact]
    public void Build_Circle_ProducesCircleFilter()
    {
        var area = CoverageArea.Create(-37.8176, 144.97, 50).Value;

        string filter = HereGeoFilter.Build(area);

        filter.ShouldBe("circle:-37.8176,144.97;r=50000");
    }

    [Fact]
    public void Build_CircleOverHereLimit_ClampsTo50Km()
    {
        var area = CoverageArea.Create(-37.8176, 144.97, 200).Value;

        string filter = HereGeoFilter.Build(area);

        filter.ShouldBe("circle:-37.8176,144.97;r=50000");
    }

    [Fact]
    public void Build_BoundingBox_ProducesBboxFilter()
    {
        var area = CoverageArea.CreateBoundingBox(144.5, -38.1, 145.2, -37.5).Value;

        string filter = HereGeoFilter.Build(area);

        filter.ShouldBe("bbox:144.5,-38.1,145.2,-37.5");
    }

    [Fact]
    public void Build_Corridor_EncodesFlexiblePolyline()
    {
        // The exact example from the HERE geospatial-filters documentation:
        // 5 Berlin points with radius 230 m encode to this polyline.
        var points = new List<CoveragePoint>
        {
            new(52.529583, 13.379416),
            new(52.532820, 13.399157),
            new(52.538458, 13.395896),
            new(52.541277, 13.408856),
            new(52.539085, 13.424048)
        };
        var area = CoverageArea.CreateCorridor(points, 230).Value;

        string filter = HereGeoFilter.Build(area);

        filter.ShouldBe("corridor:BG-6kmkDw1zwZqqG6xmBsgL5rGmwFgqZ_oEw1d;r=230");
    }

    [Fact]
    public void Build_CorridorRadiusOverLimit_ClampsTo5Km()
    {
        var points = new List<CoveragePoint> { new(-37.8, 144.9), new(-37.9, 145.0) };
        // Domain validation caps at 5000, but stored data could predate a
        // rule change — the filter clamps defensively.
        var area = CoverageArea.CreateCorridor(points, 5000).Value with { CorridorRadiusMeters = 9000 };

        string filter = HereGeoFilter.Build(area);

        filter.ShouldEndWith(";r=5000");
    }

    [Fact]
    public void Build_Polygon_ProducesBoundingBoxOfPoints()
    {
        var points = new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        };
        var area = CoverageArea.CreatePolygon(points).Value;

        string filter = HereGeoFilter.Build(area);

        // Polygon is queried via its enclosing bbox
        filter.ShouldBe("bbox:151.2,-33.89,151.22,-33.85");
    }

    [Fact]
    public void Build_PolygonSpanningOverOneDegree_ClampsToBboxAroundCentre()
    {
        // Polygon points whose bbox exceeds 1 degree in latitude
        var points = new List<CoveragePoint>
        {
            new(-33.0, 151.0),
            new(-33.5, 151.5),
            new(-34.2, 151.0)
        };
        // Domain validation rejects >1°, so construct a valid area first
        // then override PolygonPoints (mirrors the corridor radius clamp
        // test's approach with over-sized stored data).
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value with { PolygonPoints = points };
        string filter = HereGeoFilter.Build(area);

        // Lat span clamped to 1° around centre; lng span (0.5°) unchanged
        filter.ShouldBe("bbox:151,-34.1,151.5,-33.1");
    }

    [Fact]
    public void CoverageArea_BoundingBoxOverMaxShapeSpan_FailsValidation()
    {
        var result = CoverageArea.CreateBoundingBox(144.0, -43.0, 150.5, -37.0);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(CoverageArea.MaxShapeSpanDegrees.ToString());
    }

    [Fact]
    public void CoverageArea_BoundingBoxOverOneDegreeButUnderMaxSpan_Succeeds()
    {
        // 1.5° per side — over the old 1° per-tile limit, but HereGeoFilter now
        // tiles bbox/polygon shapes, so anything under MaxShapeSpanDegrees (5°) is valid.
        var result = CoverageArea.CreateBoundingBox(144.0, -38.5, 145.5, -37.0);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CoverageArea_Corridor_DerivesEnclosingCircle()
    {
        var points = new List<CoveragePoint> { new(-37.80, 144.90), new(-37.90, 145.00) };

        var area = CoverageArea.CreateCorridor(points, 1000).Value;

        // Centre at the midpoint of the points' bounding box
        area.Latitude.ShouldBe(-37.85, 0.001);
        area.Longitude.ShouldBe(144.95, 0.001);
        // Radius covers the half-diagonal plus the corridor width
        area.RadiusKm.ShouldBeGreaterThan(5);
        area.ShapeType.ShouldBe(CoverageShapeType.Corridor);
    }

    [Fact]
    public void CoverageArea_LegacyCircleJson_DeserializesAsCircle()
    {
        const string legacyJson = """{"Latitude":-33.8688,"Longitude":151.2093,"RadiusKm":50}""";

        var area = System.Text.Json.JsonSerializer.Deserialize<CoverageArea>(legacyJson);

        area.ShouldNotBeNull();
        area.ShapeType.ShouldBe(CoverageShapeType.Circle);
        area.RadiusKm.ShouldBe(50);
    }

    [Fact]
    public void BuildTiles_Circle_ReturnsSingleTile()
    {
        var area = CoverageArea.Create(-37.8176, 144.97, 50.0).Value;

        var tiles = HereGeoFilter.BuildTiles(area);

        tiles.Count.ShouldBe(1);
        tiles[0].ShouldBe("circle:-37.8176,144.97;r=50000");
    }

    [Fact]
    public void BuildTiles_SmallBoundingBox_ReturnsSingleTile()
    {
        var area = CoverageArea.CreateBoundingBox(144.5, -38.1, 145.2, -37.5).Value;

        var tiles = HereGeoFilter.BuildTiles(area);

        tiles.ShouldBe(["bbox:144.5,-38.1,145.2,-37.5"]);
    }

    [Fact]
    public void BuildTiles_BoundingBoxOverOneDegreePerSide_SplitsIntoGrid()
    {
        // 3° lng x 2° lat: exceeds the 1° per-tile limit on both axes.
        var area = CoverageArea.CreateBoundingBox(144.0, -38.0, 147.0, -36.0).Value;

        var tiles = HereGeoFilter.BuildTiles(area);

        tiles.ShouldBe(
        [
            "bbox:144,-38,145,-37",
            "bbox:145,-38,146,-37",
            "bbox:146,-38,147,-37",
            "bbox:144,-37,145,-36",
            "bbox:145,-37,146,-36",
            "bbox:146,-37,147,-36"
        ]);
    }

    [Fact]
    public void BuildTiles_LargePolygon_SplitsEnclosingBoxIntoGrid()
    {
        // Enclosing box: lng 151.0-152.5 (1.5°), lat -38.0..-36.0 (2°) — Eyre-Peninsula-shaped
        // long/thin market, over the 1° per-tile limit but under the 5° drawable cap.
        var points = new List<CoveragePoint>
        {
            new(-36.0, 151.0),
            new(-36.0, 152.5),
            new(-38.0, 151.75)
        };
        var area = CoverageArea.CreatePolygon(points).Value;

        var tiles = HereGeoFilter.BuildTiles(area);

        tiles.Count.ShouldBe(4); // ceil(1.5/1)=2 cols x ceil(2/1)=2 rows
        tiles.ShouldAllBe(t => t.StartsWith("bbox:"));
    }

    [Fact]
    public void BuildTiles_Corridor_ReturnsSingleTile()
    {
        var points = new List<CoveragePoint>
        {
            new(52.529583, 13.379416),
            new(52.541277, 13.408856)
        };
        var area = CoverageArea.CreateCorridor(points, 230).Value;

        var tiles = HereGeoFilter.BuildTiles(area);

        tiles.Count.ShouldBe(1);
    }

    [Fact]
    public void CoveragePolygon_RegionalPeninsulaMarket_AcceptedAndTiled()
    {
        // Regression for a real regional broadcast market (Eyre Peninsula, SA — Port
        // Lincoln/Magic FM 89.9): an elongated 11-point coastal polygon, ~16,700 km²,
        // whose enclosing box spans ~2.6 degrees latitude — over the old 1-degree cap
        // that used to hard-reject this exact shape ("Too large. HERE caps the area
        // at ~111 km per side").
        var points = new List<CoveragePoint>
        {
            new(-33.10, 134.55),
            new(-33.05, 134.70),
            new(-34.30, 136.20),
            new(-34.55, 136.05),
            new(-34.75, 135.85),
            new(-35.10, 135.65),
            new(-35.25, 135.20),
            new(-34.95, 134.90),
            new(-34.55, 134.70),
            new(-33.90, 134.55),
            new(-33.40, 134.50)
        };

        var result = CoverageArea.CreatePolygon(points);

        result.IsSuccess.ShouldBeTrue();
        var area = result.Value;

        var tiles = HereGeoFilter.BuildTiles(area);

        // ~2.6 deg lat, ~1.7 deg lng enclosing box -> multiple 1-degree tiles, all valid bbox filters
        tiles.Count.ShouldBeGreaterThan(1);
        tiles.ShouldAllBe(t => t.StartsWith("bbox:"));
    }
}

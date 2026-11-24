using ArnBulletinGenerator.Domain.Models;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Models;

public class CoverageAreaTests
{
    // ── CreatePolygon ────────────────────────────────────────────────

    [Fact]
    public void CreatePolygon_ValidTriangle_DerivesCentreAndRadiusFromBbox()
    {
        var points = new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        };

        var result = CoverageArea.CreatePolygon(points);

        result.IsSuccess.ShouldBeTrue();
        var area = result.Value;
        area.ShapeType.ShouldBe(CoverageShapeType.Polygon);

        // Centre = midpoint of bounding box
        area.Latitude.ShouldBe(-33.87, 0.001); // (-33.89 + -33.85) / 2
        area.Longitude.ShouldBe(151.21, 0.001); // (151.20 + 151.22) / 2

        // Radius covers half the box diagonal
        area.RadiusKm.ShouldBeGreaterThan(1.0);
        area.PolygonPoints.ShouldBe(points);
    }

    [Fact]
    public void CreatePolygon_LessThanThreePoints_Fails()
    {
        var result = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22)
        });

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("3");
    }

    [Fact]
    public void CreatePolygon_NullPoints_Fails()
    {
        var result = CoverageArea.CreatePolygon(null!);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("3");
    }

    [Fact]
    public void CreatePolygon_MoreThanMaxPoints_Fails()
    {
        var points = Enumerable.Range(0, CoverageArea.MaxPolygonPoints + 1)
            .Select(i => new CoveragePoint(-33.8 + i * 0.001, 151.2 + i * 0.001))
            .ToList();

        var result = CoverageArea.CreatePolygon(points);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(CoverageArea.MaxPolygonPoints.ToString());
    }

    [Fact]
    public void CreatePolygon_ExtentOverMaxShapeSpan_Fails()
    {
        var points = new List<CoveragePoint>
        {
            new(-33.0, 151.0),
            new(-38.5, 151.5),
            new(-33.3, 151.3)
        };

        var result = CoverageArea.CreatePolygon(points);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(CoverageArea.MaxShapeSpanDegrees.ToString());
    }

    [Fact]
    public void CreatePolygon_ExtentOverOneDegreeButUnderMaxSpan_Succeeds()
    {
        // 1.5° span — over the old 1° per-tile limit, but HereGeoFilter now tiles
        // polygon/bbox shapes, so anything under MaxShapeSpanDegrees (5°) is valid.
        var points = new List<CoveragePoint>
        {
            new(-33.0, 151.0),
            new(-34.5, 151.5),
            new(-33.3, 151.3)
        };

        var result = CoverageArea.CreatePolygon(points);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void CreatePolygon_InvalidWgs84_Fails()
    {
        var result = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 222.0),
            new(-33.89, 151.20)
        });

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("WGS84");
    }

    [Theory]
    [InlineData(0)] // A,B,A — the dev 2026-08-05 probe: start, end, start, zero-area and self-touching
    [InlineData(1)] // Three distinct collinear points — a degenerate sliver
    public void CreatePolygon_ZeroAreaRing_Fails(int variant)
    {
        // A zero-area ring contains no point, so every incident fetched for the enclosing bbox
        // is post-filtered out and the station silently airs empty content (saved 200 on dev
        // 2026-08-05). It must be rejected at save time with an actionable message.
        var points = variant == 0
            ? new List<CoveragePoint> { new(-27.5, 152.9), new(-27.5, 153.1), new(-27.5, 152.9) }
            : new List<CoveragePoint> { new(-27.5, 152.9), new(-27.5, 153.0), new(-27.5, 153.1) };

        var result = CoverageArea.CreatePolygon(points);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("non-zero area");
        result.Error.ShouldContain("degenerate");
    }

    [Fact]
    public void CreatePolygon_SelfIntersectingBowtie_Fails()
    {
        // An asymmetric bow-tie: the two lobes wind in opposite directions (net area is non-zero,
        // so it passes the area floor) but edges AB and CD cross — the ring folds back on itself
        // and the point-in-polygon ray-cast classifies the crossing lobe inconsistently.
        var points = new List<CoveragePoint>
        {
            new(0.0, 0.0),
            new(2.0, 2.0),
            new(0.0, 2.0),
            new(4.0, 0.0)
        };

        var result = CoverageArea.CreatePolygon(points);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("self-intersect");
    }

    [Fact]
    public void CreatePolygon_ValidConcaveQuadrilateral_Succeeds()
    {
        // A valid simple ring (concave, no crossing) must still pass the new geometry checks.
        var points = new List<CoveragePoint>
        {
            new(-37.80, 144.90),
            new(-37.70, 144.95),
            new(-37.75, 145.05),
            new(-37.85, 145.00)
        };

        var result = CoverageArea.CreatePolygon(points);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShapeType.ShouldBe(CoverageShapeType.Polygon);
        result.Value.PolygonPoints.ShouldBe(points);
    }

    // ── Operating-region sanity bound (finding 47) ───────────────────
    // Enforced at the schedule-PUT input boundary via WithinOperatingRegion, NOT inside the
    // Create* factories (those must reconstruct rows persisted before the bound existed).

    [Fact]
    public void WithinOperatingRegion_MelbourneCircle_IsTrue()
    {
        CoverageArea.WithinOperatingRegion(CoverageArea.Create(-37.8136, 144.9631, 5.0).Value)
            .ShouldBeTrue();
    }

    [Fact]
    public void WithinOperatingRegion_ParisCircle_IsFalse()
    {
        // A coverage on the wrong continent would silently fetch and air the wrong region's
        // incidents — the exact class of error the input-boundary bound exists to reject.
        CoverageArea.WithinOperatingRegion(CoverageArea.Create(48.8566, 2.3522, 5.0).Value)
            .ShouldBeFalse();
    }

    [Fact]
    public void WithinOperatingRegion_NullIsland_IsFalse()
    {
        // lat/lng (0,0) is the degenerate default some read-path fallbacks use; it must NOT be
        // treated as an operator-valid coverage, but Create() itself stays permissive so a
        // persisted corrupt-row fallback never throws on reconstruction.
        CoverageArea.WithinOperatingRegion(CoverageArea.Create(0, 0, 1).Value).ShouldBeFalse();
    }

    [Fact]
    public void WithinOperatingRegion_SydneyBoundingBox_IsTrue()
    {
        var area = CoverageArea.CreateBoundingBox(150.5, -34.2, 151.5, -33.5).Value;
        CoverageArea.WithinOperatingRegion(area).ShouldBeTrue();
    }

    [Fact]
    public void WithinOperatingRegion_NorthernHemisphereBoundingBox_IsFalse()
    {
        // BoundingBox is configured nowhere on dev (all 13 markets are circle or polygon), so
        // this shape is unexercised by live data — the domain test keeps it honest anyway.
        var area = CoverageArea.CreateBoundingBox(-0.5, 51.0, 0.5, 52.0).Value;
        CoverageArea.WithinOperatingRegion(area).ShouldBeFalse();
    }

    [Fact]
    public void WithinOperatingRegion_CorridorWithOverseasPoint_IsFalse()
    {
        // A corridor that starts in-region but wanders overseas must fail as a whole.
        var area = CoverageArea.CreateCorridor(
            new List<CoveragePoint> { new(-37.8, 144.9), new(48.85, 2.35) }, 1000).Value;
        CoverageArea.WithinOperatingRegion(area).ShouldBeFalse();
    }

    [Fact]
    public void WithinOperatingRegion_MelbournePolygon_IsTrue()
    {
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-37.80, 144.90),
            new(-37.70, 144.95),
            new(-37.75, 145.05),
            new(-37.85, 145.00)
        }).Value;
        CoverageArea.WithinOperatingRegion(area).ShouldBeTrue();
    }

    [Fact]
    public void WithinOperatingRegion_ParisPolygon_IsFalse()
    {
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(48.85, 2.35),
            new(48.86, 2.36),
            new(48.84, 2.37)
        }).Value;
        CoverageArea.WithinOperatingRegion(area).ShouldBeFalse();
    }

    // ── ContainsPoint ────────────────────────────────────────────────

    [Fact]
    public void ContainsPoint_InsideTriangle_ReturnsTrue()
    {
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        // Point inside the triangle
        bool result = area.ContainsPoint(-33.86, 151.205);

        result.ShouldBeTrue();
    }

    [Fact]
    public void ContainsPoint_OutsideTriangle_ReturnsFalse()
    {
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        // Point outside the triangle (right of the right edge) but inside the bbox
        bool result = area.ContainsPoint(-33.88, 151.215);

        result.ShouldBeFalse();
    }

    [Fact]
    public void ContainsPoint_ConcaveLShape_NotchPointReturnsFalse()
    {
        // L-shape polygon within 1-degree extent. Notch at (0.6, 0.6) is
        // inside bbox but outside the L.
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(0, 0),
            new(0.8, 0),
            new(0.8, 0.4),
            new(0.4, 0.4),
            new(0.4, 0.8),
            new(0, 0.8)
        }).Value;

        bool result = area.ContainsPoint(0.6, 0.6);

        result.ShouldBeFalse();
    }

    [Fact]
    public void ContainsPoint_NonPolygonArea_ReturnsTrue()
    {
        var area = CoverageArea.Create(-33.87, 151.21, 10).Value;

        // Any point: non-polygon shapes have exact HERE-side filtering
        bool result = area.ContainsPoint(-90.0, -180.0);

        result.ShouldBeTrue();
    }

    [Fact]
    public void ContainsPoint_OnPolygonEdge_ReturnsTrue()
    {
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        area.ContainsPoint(-33.86, 151.21).ShouldBeTrue();
    }

    [Fact]
    public void IntersectsPolygon_LineCrossingRing_ReturnsTrue()
    {
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        area.IntersectsPolygon(
        [
            new(-33.88, 151.19),
            new(-33.86, 151.23)
        ]).ShouldBeTrue();
    }

    [Fact]
    public void IntersectsPolygon_EmptyShape_ReturnsFalse()
    {
        var area = CoverageArea.CreatePolygon(new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        }).Value;

        area.IntersectsPolygon([]).ShouldBeFalse();
    }

    // ── Canonicalize ─────────────────────────────────────────────────

    [Fact]
    public void Canonicalize_Polygon_RecomputesStaleDerivedFieldsFromPoints()
    {
        var points = new List<CoveragePoint>
        {
            new(-33.85, 151.20),
            new(-33.87, 151.22),
            new(-33.89, 151.20)
        };
        var fresh = CoverageArea.CreatePolygon(points).Value;

        // Simulate a persisted row whose derived Latitude/Longitude/RadiusKm drifted from the
        // current formula (rounded on write, or written by an older formula) while the polygon
        // points themselves — the actual authored shape — are untouched.
        var stale = fresh with { Latitude = -33.9, Longitude = 151.2, RadiusKm = 1.0 };

        var canonical = stale.Canonicalize();

        canonical.Latitude.ShouldBe(fresh.Latitude);
        canonical.Longitude.ShouldBe(fresh.Longitude);
        canonical.RadiusKm.ShouldBe(fresh.RadiusKm);
        canonical.PolygonPoints.ShouldBe(points);
    }

    [Fact]
    public void Canonicalize_BoundingBox_RecomputesStaleDerivedFieldsFromCorners()
    {
        var fresh = CoverageArea.CreateBoundingBox(151.0, -34.0, 151.2, -33.8).Value;
        var stale = fresh with { Latitude = 0, Longitude = 0, RadiusKm = 1.0 };

        var canonical = stale.Canonicalize();

        canonical.Latitude.ShouldBe(fresh.Latitude);
        canonical.Longitude.ShouldBe(fresh.Longitude);
        canonical.RadiusKm.ShouldBe(fresh.RadiusKm);
    }

    [Fact]
    public void Canonicalize_Corridor_RecomputesStaleDerivedFieldsFromPointsAndRadius()
    {
        var points = new List<CoveragePoint> { new(-33.85, 151.20), new(-33.89, 151.24) };
        var fresh = CoverageArea.CreateCorridor(points, 500).Value;
        var stale = fresh with { Latitude = 0, Longitude = 0, RadiusKm = 1.0 };

        var canonical = stale.Canonicalize();

        canonical.Latitude.ShouldBe(fresh.Latitude);
        canonical.Longitude.ShouldBe(fresh.Longitude);
        canonical.RadiusKm.ShouldBe(fresh.RadiusKm);
    }

    [Fact]
    public void Canonicalize_Circle_ReturnsUnchanged()
    {
        var circle = CoverageArea.Create(-33.87, 151.21, 10).Value;

        var canonical = circle.Canonicalize();

        canonical.Latitude.ShouldBe(circle.Latitude);
        canonical.Longitude.ShouldBe(circle.Longitude);
        canonical.RadiusKm.ShouldBe(circle.RadiusKm);
    }
}

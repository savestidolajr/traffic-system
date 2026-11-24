using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Models;

// =============================================================================
// Market Schedule - Mutable for persistence compatibility, validated at boundaries
// =============================================================================

/// <summary>
///     The geometry kind a coverage area uses for traffic retrieval.
///     Circle, bbox and corridor mirror HERE Traffic API v7 geospatial
///     filters. Polygon has no native HERE filter: we fetch the polygon's
///     enclosing bbox and post-filter incidents point-in-polygon, so the
///     bulletin only covers the space the polygon actually fills.
///     (segmentRef excluded: it is not a drawable shape and needs
///     per-market route caching.)
/// </summary>
public enum CoverageShapeType
{
    Circle = 0,
    BoundingBox = 1,
    Corridor = 2,
    Polygon = 3
}

/// <summary>A WGS84 point on a coverage corridor or polygon ring.</summary>
public sealed record CoveragePoint(double Lat, double Lng);

/// <summary>
///     Geographic coverage for traffic data retrieval. Latitude/Longitude/RadiusKm
///     are ALWAYS populated — for bbox and corridor shapes they hold the derived
///     enclosing circle (centre + radius), so every consumer that only understands
///     circles (maps, geocoding, regeneration refresh, cache keys) degrades to a
///     slightly wider net instead of breaking. Optional WeatherCoverage and
///     FuelCoverage circles override only their matching HERE source.
/// </summary>
public sealed record CoverageArea
{
    /// <summary>HERE bbox/polygon-enclosing-box filters reject a single query
    ///     extent over 1 degree per side. <see cref="HereGeoFilter" /> tiles any
    ///     shape larger than this into a grid of sub-1-degree queries, so this
    ///     constant is the per-tile size, not a limit on what a station can draw.</summary>
    public const double MaxBoundingBoxDegrees = 1.0;

    /// <summary>Outer sanity cap on a drawn bbox/polygon's extent, independent of
    ///     the per-tile HERE limit above. At 5° per side the worst case is a 5x5
    ///     grid (25 tiles) per HERE call — enough for any realistic broadcast
    ///     licence area (e.g. a 500km-long coastal or regional market) without
    ///     letting an operator mistake accidentally fan out to hundreds of calls.</summary>
    public const double MaxShapeSpanDegrees = 5.0;

    /// <summary>Outer sanity cap on a drawn circle's radius, matching the ~5°
    ///     span the box/polygon shapes allow (5° of latitude ≈ 555 km). Without
    ///     this, a fat-fingered RadiusKm (e.g. 100000) would silently accept a
    ///     coverage area spanning half the globe.</summary>
    public const double MaxCircleRadiusKm = 555.0;

    /// <summary>HERE corridor filters: 2..300 points, radius 1..5000 m. We cap
    ///     points lower to keep GET URLs within limits.</summary>
    public const int MaxCorridorPoints = 100;
    public const double MaxCorridorRadiusMeters = 5000;

    /// <summary>Polygon coverage: same point cap as corridors (GET URL size
    ///     is not a concern — only the enclosing bbox is sent to HERE — but a
    ///     ring beyond this is operator error, not a coverage area).</summary>
    public const int MaxPolygonPoints = 100;

    /// <summary>
    ///     Smallest ring area (square degrees) a polygon coverage may enclose. The
    ///     shoelace measurement is planar on lat/lng — exact enough at the enforced
    ///     sub-5° extents and consistent with <see cref="ContainsPoint" />'s planar
    ///     ray-cast. At these latitudes a square degree is roughly 100 km × 110 km,
    ///     so the floor is about a 100 m × 100 m patch: every hand-drawn coverage
    ///     passes, while a degenerate ring (repeated or collinear points) — which
    ///     contains no point, so every incident fetched for the enclosing bbox is
    ///     post-filtered out and the station silently airs empty content — is caught
    ///     at save time instead.
    /// </summary>
    public const double MinPolygonAreaDegreesSquared = 1e-6;

    /// <summary>
    ///     Plausible operating region for coverage geometry, expressed as a latitude/
    ///     longitude box. A sanity bound on LOCATION to complement the size bounds
    ///     above: this platform broadcasts the Australian Traffic Network's markets, so
    ///     an operator-supplied area anywhere else on Earth (a Paris polygon, a sign-typo
    ///     of "45" instead of "-45") is operator error that would silently fetch — and
    ///     air — the wrong region's incidents. The box is deliberately generous — all of
    ///     Australia, New Zealand and the surrounding South Pacific — and comfortably
    ///     contains every configured market (verified against the 13 live dev schedules,
    ///     2026-08-06). Enforced at the INPUT boundary (<see cref="CoverageArea.WithinOperatingRegion" />,
    ///     called by ScheduleHttpTrigger), NOT inside the Create* factories: those must stay
    ///     permissive enough to faithfully reconstruct rows persisted before this bound
    ///     existed. Widen these constants (or move them to configuration) if ARN ever
    ///     serves markets beyond this band.
    /// </summary>
    public const double MinOperatingLatitude = -50.0;
    public const double MaxOperatingLatitude = 0.0;
    public const double MinOperatingLongitude = 100.0;
    public const double MaxOperatingLongitude = 180.0;

    /// <summary>
    ///     Creates a circular <see cref="CoverageArea" /> with validation.
    /// </summary>
    public static Result<CoverageArea> Create(double latitude, double longitude, double radiusKm)
    {
        if (latitude < -90.0 || latitude > 90.0)
            return Result<CoverageArea>.Failure("Latitude must be between -90 and 90.");
        if (longitude < -180.0 || longitude > 180.0)
            return Result<CoverageArea>.Failure("Longitude must be between -180 and 180.");
        if (radiusKm <= 0.0)
            return Result<CoverageArea>.Failure("RadiusKm must be greater than 0.");
        if (radiusKm > MaxCircleRadiusKm)
            return Result<CoverageArea>.Failure($"RadiusKm must not exceed {MaxCircleRadiusKm} km.");

        return Result<CoverageArea>.Success(new CoverageArea
        {
            Latitude = latitude,
            Longitude = longitude,
            RadiusKm = radiusKm
        });
    }

    /// <summary>
    ///     Creates a bounding-box coverage. Centre and radius are derived
    ///     (centre = box midpoint, radius = half-diagonal).
    /// </summary>
    public static Result<CoverageArea> CreateBoundingBox(double west, double south, double east, double north)
    {
        if (south < -90.0 || north > 90.0 || south >= north)
            return Result<CoverageArea>.Failure("Box latitudes must satisfy -90 <= south < north <= 90.");
        if (west < -180.0 || east > 180.0 || west >= east)
            return Result<CoverageArea>.Failure("Box longitudes must satisfy -180 <= west < east <= 180.");
        if (north - south > MaxShapeSpanDegrees || east - west > MaxShapeSpanDegrees)
            return Result<CoverageArea>.Failure($"A coverage box must fit inside {MaxShapeSpanDegrees} degrees per side.");

        double centreLat = (south + north) / 2.0;
        double centreLng = (west + east) / 2.0;
        double radiusKm = Math.Max(1.0, GreatCircleKm(centreLat, centreLng, north, east));

        return Result<CoverageArea>.Success(new CoverageArea
        {
            Latitude = centreLat,
            Longitude = centreLng,
            RadiusKm = radiusKm,
            ShapeType = CoverageShapeType.BoundingBox,
            West = west,
            South = south,
            East = east,
            North = north
        });
    }

    /// <summary>
    ///     Creates a corridor coverage (polyline ± radius). Centre and radius
    ///     are derived from the points' bounding box.
    /// </summary>
    public static Result<CoverageArea> CreateCorridor(IReadOnlyList<CoveragePoint> points, double radiusMeters)
    {
        if (points is null || points.Count < 2)
            return Result<CoverageArea>.Failure("A corridor needs at least 2 points.");
        if (points.Count > MaxCorridorPoints)
            return Result<CoverageArea>.Failure($"A corridor supports at most {MaxCorridorPoints} points.");
        if (radiusMeters < 1 || radiusMeters > MaxCorridorRadiusMeters)
            return Result<CoverageArea>.Failure("Corridor radius must be between 1 and 5000 metres.");
        if (points.Any(p => p.Lat < -90 || p.Lat > 90 || p.Lng < -180 || p.Lng > 180))
            return Result<CoverageArea>.Failure("Corridor points must be valid WGS84 coordinates.");

        double south = points.Min(p => p.Lat);
        double north = points.Max(p => p.Lat);
        double west = points.Min(p => p.Lng);
        double east = points.Max(p => p.Lng);
        double centreLat = (south + north) / 2.0;
        double centreLng = (west + east) / 2.0;
        double radiusKm = Math.Max(1.0,
            GreatCircleKm(centreLat, centreLng, north, east) + radiusMeters / 1000.0);

        return Result<CoverageArea>.Success(new CoverageArea
        {
            Latitude = centreLat,
            Longitude = centreLng,
            RadiusKm = radiusKm,
            ShapeType = CoverageShapeType.Corridor,
            CorridorPoints = points,
            CorridorRadiusMeters = radiusMeters
        });
    }

    /// <summary>
    ///     Creates a polygon coverage (closed ring; the last point connects
    ///     back to the first implicitly). HERE has no polygon filter, so
    ///     traffic is fetched for the enclosing box (tiled into sub-1-degree
    ///     HERE queries when larger — see <see cref="HereGeoFilter" />) and
    ///     post-filtered point-in-polygon. Centre and radius are derived from
    ///     the points' bounding box.
    /// </summary>
    public static Result<CoverageArea> CreatePolygon(IReadOnlyList<CoveragePoint> points)
    {
        if (points is null || points.Count < 3)
            return Result<CoverageArea>.Failure("A polygon needs at least 3 points.");
        if (points.Count > MaxPolygonPoints)
            return Result<CoverageArea>.Failure($"A polygon supports at most {MaxPolygonPoints} points.");
        if (points.Any(p => p.Lat < -90 || p.Lat > 90 || p.Lng < -180 || p.Lng > 180))
            return Result<CoverageArea>.Failure("Polygon points must be valid WGS84 coordinates.");

        double south = points.Min(p => p.Lat);
        double north = points.Max(p => p.Lat);
        double west = points.Min(p => p.Lng);
        double east = points.Max(p => p.Lng);
        if (north - south > MaxShapeSpanDegrees || east - west > MaxShapeSpanDegrees)
            return Result<CoverageArea>.Failure(
                $"A polygon must fit inside {MaxShapeSpanDegrees} degrees per side (its enclosing box is what HERE is queried with).");

        // Ring area via the shoelace formula, planar on lat/lng (exact enough at the enforced
        // sub-5° extents and consistent with ContainsPoint's planar ray-cast). A zero- or
        // near-zero-area ring (repeated or collinear points) contains no point, so every
        // incident fetched for the enclosing bbox is post-filtered out and the station would
        // silently air empty content — reject it at save time (dev 2026-08-05: A,B,A saved 200).
        if (ShoelaceAreaDegreesSquared(points) < MinPolygonAreaDegreesSquared)
            return Result<CoverageArea>.Failure(
                "A polygon must enclose a non-zero area; this ring is degenerate (repeated or collinear points).");

        if (SelfIntersects(points))
            return Result<CoverageArea>.Failure(
                "A polygon ring must not self-intersect; its edges cross or touch outside consecutive points.");

        double centreLat = (south + north) / 2.0;
        double centreLng = (west + east) / 2.0;
        double radiusKm = Math.Max(1.0, GreatCircleKm(centreLat, centreLng, north, east));

        return Result<CoverageArea>.Success(new CoverageArea
        {
            Latitude = centreLat,
            Longitude = centreLng,
            RadiusKm = radiusKm,
            ShapeType = CoverageShapeType.Polygon,
            PolygonPoints = points
        });
    }

    /// <summary>
    ///     Ray-casting point-in-polygon test against <see cref="PolygonPoints" />.
    ///     Planar treatment of lat/lng is fine at the enforced sub-degree
    ///     extents. Non-polygon shapes return true: the HERE-side filter is
    ///     already exact for them, so callers can apply this unconditionally.
    /// </summary>
    public bool ContainsPoint(double lat, double lng)
    {
        if (ShapeType != CoverageShapeType.Polygon || PolygonPoints is not { Count: >= 3 })
            return true;

        bool inside = false;
        for (int i = 0, j = PolygonPoints.Count - 1; i < PolygonPoints.Count; j = i++)
        {
            double yi = PolygonPoints[i].Lat, xi = PolygonPoints[i].Lng;
            double yj = PolygonPoints[j].Lat, xj = PolygonPoints[j].Lng;
            double boundaryOrientation = (xj - xi) * (lat - yi) - (yj - yi) * (lng - xi);
            if (Math.Abs(boundaryOrientation) < 1e-12
                && lng >= Math.Min(xi, xj) && lng <= Math.Max(xi, xj)
                && lat >= Math.Min(yi, yj) && lat <= Math.Max(yi, yj))
                return true;

            bool crosses = yi > lat != yj > lat &&
                           lng < (xj - xi) * (lat - yi) / (yj - yi) + xi;
            if (crosses)
                inside = !inside;
        }

        return inside;
    }

    /// <summary>
    ///     True when a point or polyline intersects this area's polygon. Unlike
    ///     <see cref="ContainsPoint" />, this method rejects non-polygon shapes because it is
    ///     intended for callers that already have a shape and need an exact polygon boundary
    ///     decision. A line crossing the polygon is retained even when both sampled endpoints
    ///     lie outside the ring.
    /// </summary>
    public bool IntersectsPolygon(IReadOnlyList<CoveragePoint> shape)
    {
        if (ShapeType != CoverageShapeType.Polygon || PolygonPoints is not { Count: >= 3 })
            return true;
        if (shape is null || shape.Count == 0)
            return false;

        if (shape.Any(point => ContainsPoint(point.Lat, point.Lng)))
            return true;

        for (int i = 1; i < shape.Count; i++)
        {
            var start = shape[i - 1];
            var end = shape[i];
            for (int j = 0; j < PolygonPoints.Count; j++)
            {
                var ringStart = PolygonPoints[j];
                var ringEnd = PolygonPoints[(j + 1) % PolygonPoints.Count];
                if (SegmentsIntersect(start, end, ringStart, ringEnd))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Returns a copy with <see cref="Latitude" />/<see cref="Longitude" />/<see cref="RadiusKm" />
    ///     recomputed from the shape's source geometry (bbox corners / corridor or polygon points).
    ///     Those three fields are DERIVED for every non-circle shape — never authored directly — so a
    ///     persisted value can drift from the current formula (an older import, a hand-edited row, a
    ///     future formula tweak) while the actual shape (the points/corners an operator drew) is
    ///     unchanged. Callers that need to know whether two coverage areas describe the SAME shape
    ///     (e.g. the config-transfer import diff) MUST canonicalize both sides first — comparing raw
    ///     derived fields treats harmless recomputation drift as a real change. Circle shapes are
    ///     returned unchanged: their Lat/Lng/RadiusKm ARE the source geometry, not derived from it.
    /// </summary>
    public CoverageArea Canonicalize()
    {
        switch (ShapeType)
        {
            case CoverageShapeType.BoundingBox when West is { } w && South is { } s && East is { } e && North is { } n:
                {
                    double centreLat = (s + n) / 2.0;
                    double centreLng = (w + e) / 2.0;
                    double radiusKm = Math.Max(1.0, GreatCircleKm(centreLat, centreLng, n, e));
                    return this with { Latitude = centreLat, Longitude = centreLng, RadiusKm = radiusKm };
                }
            case CoverageShapeType.Corridor when CorridorPoints is { Count: >= 2 } corridorPoints:
                {
                    double south = corridorPoints.Min(p => p.Lat), north = corridorPoints.Max(p => p.Lat);
                    double west = corridorPoints.Min(p => p.Lng), east = corridorPoints.Max(p => p.Lng);
                    double centreLat = (south + north) / 2.0;
                    double centreLng = (west + east) / 2.0;
                    double radiusKm = Math.Max(1.0,
                        GreatCircleKm(centreLat, centreLng, north, east) + (CorridorRadiusMeters ?? 0) / 1000.0);
                    return this with { Latitude = centreLat, Longitude = centreLng, RadiusKm = radiusKm };
                }
            case CoverageShapeType.Polygon when PolygonPoints is { Count: >= 3 } polygonPoints:
                {
                    double south = polygonPoints.Min(p => p.Lat), north = polygonPoints.Max(p => p.Lat);
                    double west = polygonPoints.Min(p => p.Lng), east = polygonPoints.Max(p => p.Lng);
                    double centreLat = (south + north) / 2.0;
                    double centreLng = (west + east) / 2.0;
                    double radiusKm = Math.Max(1.0, GreatCircleKm(centreLat, centreLng, north, east));
                    return this with { Latitude = centreLat, Longitude = centreLng, RadiusKm = radiusKm };
                }
            default:
                return this;
        }
    }

    /// <summary>True when a point lies outside the platform's operating region (see the
    ///     Min/MaxOperating* constants).</summary>
    private static bool OutsideOperatingRegion(CoveragePoint p) =>
        p.Lat < MinOperatingLatitude || p.Lat > MaxOperatingLatitude
        || p.Lng < MinOperatingLongitude || p.Lng > MaxOperatingLongitude;

    /// <summary>
    ///     True when the area lies entirely inside the platform's operating region (see the
    ///     Min/MaxOperating* constants). The input-boundary check for operator-supplied
    ///     coverage — deliberately NOT enforced by the Create* factories, which must stay
    ///     permissive enough to reconstruct persisted rows.
    /// </summary>
    public static bool WithinOperatingRegion(CoverageArea area)
    {
        bool outerWithin = area.ShapeType switch
        {
            // The box is axis-aligned, so its two extreme corners bound every point of it.
            CoverageShapeType.BoundingBox
                when area.West is { } w && area.South is { } s && area.East is { } e && area.North is { } n =>
                !OutsideOperatingRegion(new CoveragePoint(s, w))
                && !OutsideOperatingRegion(new CoveragePoint(n, e)),
            CoverageShapeType.Corridor when area.CorridorPoints is { Count: > 0 } corridorPoints =>
                !corridorPoints.Any(OutsideOperatingRegion),
            CoverageShapeType.Polygon when area.PolygonPoints is { Count: > 0 } polygonPoints =>
                !polygonPoints.Any(OutsideOperatingRegion),
            _ => !OutsideOperatingRegion(new CoveragePoint(area.Latitude, area.Longitude))
        };

        return outerWithin
               && (area.WeatherCoverage is null || WithinOperatingRegion(area.WeatherCoverage))
               && (area.FuelCoverage is null || WithinOperatingRegion(area.FuelCoverage));
    }

    /// <summary>
    ///     Absolute ring area in square degrees via the shoelace formula, treated planar on
    ///     lat/lng (exact enough at the enforced sub-5° extents). The ring is implicitly
    ///     closed: the last point connects back to the first.
    /// </summary>
    private static double ShoelaceAreaDegreesSquared(IReadOnlyList<CoveragePoint> ring)
    {
        double sum = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            sum += a.Lng * b.Lat - b.Lng * a.Lat;
        }

        return Math.Abs(sum) / 2.0;
    }

    /// <summary>
    ///     True when any two NON-adjacent ring edges intersect — properly, at an endpoint, or
    ///     along an overlapping collinear span. Adjacent edges share exactly one vertex by
    ///     construction (the ring's corners), so they are excluded; every other meeting means
    ///     the ring folds back on itself, which makes the point-in-polygon ray-cast ambiguous
    ///     (a bow-tie would classify the crossing lobe inconsistently).
    /// </summary>
    private static bool SelfIntersects(IReadOnlyList<CoveragePoint> ring)
    {
        int n = ring.Count;
        for (int i = 0; i < n; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % n];
            for (int j = i + 1; j < n; j++)
            {
                // Adjacent edges legitimately share a vertex: e_i and e_{i+1} share the corner
                // between them, and e_0 and e_{n-1} share the ring's closing vertex.
                if (j == i + 1 || (i == 0 && j == n - 1))
                    continue;

                var c = ring[j];
                var d = ring[(j + 1) % n];
                if (SegmentsIntersect(a, b, c, d))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Standard segment intersection test: a proper crossing, or any endpoint/overlap touch
    ///     (orientation zero plus on-segment). Exact double arithmetic is fine at these
    ///     magnitudes; near-degenerate rings are already excluded by the area floor.
    /// </summary>
    private static bool SegmentsIntersect(CoveragePoint a, CoveragePoint b, CoveragePoint c, CoveragePoint d)
    {
        double o1 = Orientation(a, b, c);
        double o2 = Orientation(a, b, d);
        double o3 = Orientation(c, d, a);
        double o4 = Orientation(c, d, b);

        if (o1 * o2 < 0 && o3 * o4 < 0)
            return true;

        return (o1 == 0 && OnSegment(a, b, c))
               || (o2 == 0 && OnSegment(a, b, d))
               || (o3 == 0 && OnSegment(c, d, a))
               || (o4 == 0 && OnSegment(c, d, b));
    }

    /// <summary>Signed orientation of the triple (a, b, c): positive = counter-clockwise,
    ///     negative = clockwise, zero = collinear. Planar on lat/lng, like the shoelace area.</summary>
    private static double Orientation(CoveragePoint a, CoveragePoint b, CoveragePoint c) =>
        (b.Lng - a.Lng) * (c.Lat - a.Lat) - (b.Lat - a.Lat) * (c.Lng - a.Lng);

    private static bool OnSegment(CoveragePoint a, CoveragePoint b, CoveragePoint p) =>
        Math.Min(a.Lng, b.Lng) <= p.Lng && p.Lng <= Math.Max(a.Lng, b.Lng)
        && Math.Min(a.Lat, b.Lat) <= p.Lat && p.Lat <= Math.Max(a.Lat, b.Lat);

    private static double GreatCircleKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusKm = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLng = (lng2 - lng1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                   Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    // Parameterless constructor for object-initializer use by the factories and
    // for JSON reconstitution of persisted (already-validated) coverage data.
    [JsonConstructor]
    private CoverageArea()
    {
    }

    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double RadiusKm { get; init; }

    public CoverageShapeType ShapeType { get; init; } = CoverageShapeType.Circle;

    // Optional per-source overrides. Traffic continues to use the outer coverage; weather and fuel
    // providers use their matching override when present and otherwise inherit this area.
    public CoverageArea? WeatherCoverage { get; init; }
    public CoverageArea? FuelCoverage { get; init; }

    // Bounding box (ShapeType == BoundingBox)
    public double? West { get; init; }
    public double? South { get; init; }
    public double? East { get; init; }
    public double? North { get; init; }

    // Corridor (ShapeType == Corridor)
    public IReadOnlyList<CoveragePoint>? CorridorPoints { get; init; }
    public double? CorridorRadiusMeters { get; init; }

    // Polygon ring (ShapeType == Polygon); implicitly closed
    public IReadOnlyList<CoveragePoint>? PolygonPoints { get; init; }
}

/// <summary>
///     A source that can fill remaining bulletin slot time once traffic incidents alone
///     don't reach the target duration (BFN-003). A station's <see cref="MarketSchedule.FillOrder" />
///     is the sequence these are tried in — e.g. some stations prefer a filler statement before
///     weather/fuel, others the reverse (a station whose following news segment already covers
///     weather may want to de-prioritise it here).
///     <para>
///         The list carries a second, narrower meaning: the relative order of <see cref="Weather" />
///         and <see cref="Fuel" /> in it is also their ON-AIR read order, resolved by
///         <c>ContentSectionOrder</c> and enforced by <c>SectionOrderDetector</c>. Whichever of the
///         two sits earlier reads earlier, after the traffic body. <see cref="MinorTraffic" /> and
///         <see cref="Filler" /> keep the fill-only meaning: minor traffic is voiced with the traffic
///         body, and filler is appended deterministically after generation, so it always closes.
///     </para>
/// </summary>
public enum FillSource
{
    Weather,
    Fuel,

    /// <summary>
    ///     Lower-severity ("Minor" magnitude) traffic incidents — real, source-grounded roadworks
    ///     and light delays that <c>TrafficNarrativeSummarizer</c> otherwise suppresses entirely
    ///     once a Major-tier incident (a closure, a blocking event) leads the bulletin. Escalating
    ///     into this tier supplies MORE REAL DATA before falling back to Filler or a stand-by
    ///     bulletin — preferred over both, since it is still true traffic content, not editorial
    ///     filler or a non-answer.
    /// </summary>
    MinorTraffic,

    Filler
}

/// <summary>
///     Market schedule for persistence. All fields have explicit values.
///     Validation happens at the API boundary when converting from DTOs.
/// </summary>
public sealed class MarketSchedule
{
    /// <summary>
    ///     Private constructor for factory/object-initializer use and JSON reconstitution
    ///     of persisted (already-validated) schedules. External construction goes through
    ///     <see cref="Create" /> so validation cannot be bypassed.
    /// </summary>
    [JsonConstructor]
    private MarketSchedule()
    {
    }

    /// <summary>
    ///     Creates a NEW schedule. The strict door: structural validation plus every admission
    ///     invariant a stored row must satisfy. Use this for anything an operator, the API or a
    ///     config import is asking to bring into existence.
    ///     <para>
    ///         Loading an EXISTING row uses <see cref="Rehydrate" /> instead. Keeping the two apart
    ///         is deliberate: this factory used to serve both, so tightening it would have made
    ///         already-stored rows that violate a NEW rule fail to load — and the repository treats
    ///         a load failure as "corrupt, skip and log", so the station would have silently
    ///         disappeared from the console and the poller. Admission rules belong on the way in;
    ///         rehydration has to stay total. Add new invariants here, never to
    ///         <see cref="Rehydrate" />.
    ///     </para>
    /// </summary>
    public static Result<MarketSchedule> Create(
        string marketId,
        string name,
        string station,
        string timezone,
        string cronExpression,
        bool enabled,
        CoverageArea coverage,
        string voiceProfileId,
        string outputPath,
        double targetDurationSeconds,
        int notifyBeforePlayoutMinutes,
        int escalationBeforePlayoutMinutes,
        DateTime createdAt,
        DateTime updatedAt,
        string deliveryStyleId = "",
        string pronunciationDictionaryId = "",
        string bookendId = "",
        IReadOnlyDictionary<string, string>? bookendTokens = null,
        bool curationEnabled = false,
        bool playoutEnabled = true,
        string playbookId = "",
        Option<BulletinContentSelection> contentSelection = default)
    {
        var admission = ValidateAlertOrdering(notifyBeforePlayoutMinutes, escalationBeforePlayoutMinutes);
        if (admission.IsFailure) return Result<MarketSchedule>.Failure(admission.Error);
        return Rehydrate(
            marketId, name, station, timezone, cronExpression, enabled, coverage, voiceProfileId,
            outputPath, targetDurationSeconds, notifyBeforePlayoutMinutes,
            escalationBeforePlayoutMinutes, createdAt, updatedAt, deliveryStyleId,
            pronunciationDictionaryId, bookendId, bookendTokens, curationEnabled, playoutEnabled,
            playbookId, contentSelection);
    }

    /// <summary>
    ///     Rebuilds a schedule that is ALREADY persisted. Validates only what makes a row
    ///     structurally usable (required fields, bounds), never the admission invariants in
    ///     <see cref="Create" />, so a historical row written before a rule existed still loads and
    ///     can be seen and corrected in the console instead of vanishing from the console and
    ///     poller.
    /// </summary>
    public static Result<MarketSchedule> Rehydrate(
        string marketId,
        string name,
        string station,
        string timezone,
        string cronExpression,
        bool enabled,
        CoverageArea coverage,
        string voiceProfileId,
        string outputPath,
        double targetDurationSeconds,
        int notifyBeforePlayoutMinutes,
        int escalationBeforePlayoutMinutes,
        DateTime createdAt,
        DateTime updatedAt,
        string deliveryStyleId = "",
        string pronunciationDictionaryId = "",
        string bookendId = "",
        IReadOnlyDictionary<string, string>? bookendTokens = null,
        bool curationEnabled = false,
        bool playoutEnabled = true,
        string playbookId = "",
        Option<BulletinContentSelection> contentSelection = default)
    {
        Result check = ValidateRequired(marketId, "MarketId");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateRequired(name, "Name");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateRequired(station, "Station");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateRequired(timezone, "Timezone");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateRequired(cronExpression, "CronExpression");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateRequired(voiceProfileId, "VoiceProfileId");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateRequired(outputPath, "OutputPath");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateDuration(targetDurationSeconds, "TargetDurationSeconds");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateNonNegative(notifyBeforePlayoutMinutes, "NotifyBeforePlayoutMinutes");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        check = ValidateNonNegative(escalationBeforePlayoutMinutes, "EscalationBeforePlayoutMinutes");
        if (check.IsFailure) return Result<MarketSchedule>.Failure(check.Error);
        if (contentSelection.HasValue)
        {
            if (!contentSelection.Value.HasValidSegmentOrder)
                return Result<MarketSchedule>.Failure(
                    "SegmentOrder must be a unique permutation of enabled content segments.");
            if (!contentSelection.Value.HasValidTrafficOrder)
                return Result<MarketSchedule>.Failure(
                    "TrafficOrder must be WorstFirst or WorstLast.");
        }
        return Result<MarketSchedule>.Success(new MarketSchedule
        {
            MarketId = marketId,
            Name = name,
            Station = station,
            Timezone = timezone,
            CronExpression = cronExpression,
            Enabled = enabled,
            CurationEnabled = curationEnabled,
            PlayoutEnabled = playoutEnabled,
            Coverage = coverage,
            VoiceProfileId = voiceProfileId,
            DeliveryStyleId = NormalizeOptionalId(deliveryStyleId),
            PlaybookId = NormalizeOptionalId(playbookId),
            PronunciationDictionaryId = NormalizeOptionalId(pronunciationDictionaryId),
            BookendId = NormalizeOptionalId(bookendId),
            BookendTokens = NormalizeBookendTokens(bookendTokens),
            OutputPath = outputPath,
            ContentSelection = contentSelection,
            TargetDurationSeconds = targetDurationSeconds,
            NotifyBeforePlayoutMinutes = notifyBeforePlayoutMinutes,
            EscalationBeforePlayoutMinutes = escalationBeforePlayoutMinutes,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        });
    }

    /// <summary>
    ///     Applies a partial update, validating and assigning each supplied field with EXACTLY the
    ///     same rules <see cref="Create" /> enforces — both call the shared private validators below,
    ///     so the rules exist in one place instead of being re-derived at every update site. A field
    ///     left <see cref="Option{T}.IsNone" /> is untouched. This mutates the current instance (it
    ///     does not reconstruct the aggregate), so runtime state (<see cref="NextRunUtc" />,
    ///     <see cref="LastRunUtc" />, <see cref="CurrentJobId" />) is never touched — there is nothing
    ///     to copy back. <see cref="MarketId" /> has no parameter here: identity is fixed at creation
    ///     and callers have no field to change it with.
    public Result ApplyUpdates(
        Option<string> name,
        Option<string> station,
        Option<string> timezone,
        Option<string> cronExpression,
        Option<bool> enabled,
        Option<CoverageArea> coverage,
        Option<string> voiceProfileId,
        Option<string> outputPath,
        Option<double> targetDurationSeconds,
        Option<int> notifyBeforePlayoutMinutes,
        Option<int> escalationBeforePlayoutMinutes,
        Option<string> deliveryStyleId,
        Option<string> pronunciationDictionaryId,
        Option<BulletinContentSelection> contentSelection,
        Option<IReadOnlyList<FillSource>> fillOrder,
        Option<VoiceRotationPolicy> voiceRotation,
        Option<string> bookendId,
        Option<BookendRotationPolicy> bookendRotation,
        Option<IReadOnlyDictionary<string, string>> bookendTokens,
        Option<ScheduleCalendar> calendar,
        DateTime utcNow,
        Option<bool> curationEnabled = default,
        Option<bool> playoutEnabled = default,
        Option<string> playbookId = default)
    {
        if (name.HasValue)
        {
            var check = ValidateRequired(name.Value, "Name");
            if (check.IsFailure) return check;
            Name = name.Value;
        }

        if (station.HasValue)
        {
            var check = ValidateRequired(station.Value, "Station");
            if (check.IsFailure) return check;
            Station = station.Value;
        }

        if (timezone.HasValue)
        {
            var check = ValidateRequired(timezone.Value, "Timezone");
            if (check.IsFailure) return check;
            Timezone = timezone.Value;
        }

        if (cronExpression.HasValue)
        {
            var check = ValidateRequired(cronExpression.Value, "CronExpression");
            if (check.IsFailure) return check;
            CronExpression = cronExpression.Value;
        }

        if (voiceProfileId.HasValue)
        {
            var check = ValidateRequired(voiceProfileId.Value, "VoiceProfileId");
            if (check.IsFailure) return check;
            VoiceProfileId = voiceProfileId.Value;
        }

        if (outputPath.HasValue)
        {
            var check = ValidateRequired(outputPath.Value, "OutputPath");
            if (check.IsFailure) return check;
            OutputPath = outputPath.Value;
        }

        if (targetDurationSeconds.HasValue)
        {
            var check = ValidateDuration(targetDurationSeconds.Value, "TargetDurationSeconds");
            if (check.IsFailure) return check;
            TargetDurationSeconds = targetDurationSeconds.Value;
        }

        // The alert tiers are a PAIR, so a partial update has to be judged on the pair it would
        // leave behind, not on the field it happens to carry: raising escalation alone, or lowering
        // notify alone, can invert the ordering just as easily as sending both. Resolve the
        // resulting values first, validate, and only then assign — otherwise a rejected update
        // would still have mutated one half of the pair.
        if (notifyBeforePlayoutMinutes.HasValue || escalationBeforePlayoutMinutes.HasValue)
        {
            int resultingNotify = notifyBeforePlayoutMinutes.GetValueOr(NotifyBeforePlayoutMinutes);
            int resultingEscalation =
                escalationBeforePlayoutMinutes.GetValueOr(EscalationBeforePlayoutMinutes);

            var check = ValidateNonNegative(resultingNotify, "NotifyBeforePlayoutMinutes");
            if (check.IsFailure) return check;
            check = ValidateNonNegative(resultingEscalation, "EscalationBeforePlayoutMinutes");
            if (check.IsFailure) return check;
            check = ValidateAlertOrdering(resultingNotify, resultingEscalation);
            if (check.IsFailure) return check;

            NotifyBeforePlayoutMinutes = resultingNotify;
            EscalationBeforePlayoutMinutes = resultingEscalation;
        }

        if (curationEnabled.HasValue)
            CurationEnabled = curationEnabled.Value;

        if (playoutEnabled.HasValue)
            PlayoutEnabled = playoutEnabled.Value;

        if (enabled.HasValue)
            Enabled = enabled.Value;

        if (coverage.HasValue)
            Coverage = coverage.Value;

        if (deliveryStyleId.HasValue)
            DeliveryStyleId = NormalizeOptionalId(deliveryStyleId.Value);
        if (playbookId.HasValue)
            PlaybookId = NormalizeOptionalId(playbookId.Value);

        if (pronunciationDictionaryId.HasValue)
            PronunciationDictionaryId = NormalizeOptionalId(pronunciationDictionaryId.Value);

        if (contentSelection.HasValue)
        {
            if (!contentSelection.Value.HasValidSegmentOrder)
                return Result.Failure(
                    "SegmentOrder must be a unique permutation of enabled content segments.");
            if (!contentSelection.Value.HasValidTrafficOrder)
                return Result.Failure(
                    "TrafficOrder must be WorstFirst or WorstLast.");
            ContentSelection = contentSelection;
        }

        if (fillOrder.HasValue)
            FillOrder = fillOrder.Value;

        // Apply a provided rotation, else leave the existing one untouched. Preserve the
        // round-robin cursor across edits; reset it to 0 only when the operator changes the
        // selection mode.
        if (voiceRotation.HasValue)
        {
            var oldMode = VoiceRotation.Match(p => p.Mode, () => VoiceSelectionMode.Single);
            var newMode = voiceRotation.Value.Mode;
            VoiceRotation = voiceRotation;
            if (oldMode != newMode)
                VoiceRotationCursor = 0;
        }

        if (bookendId.HasValue)
            BookendId = NormalizeOptionalId(bookendId.Value);

        // Mirror the voice-rotation rule: an edit keeps the round-robin cursor unless the
        // selection mode changed.
        if (bookendRotation.HasValue)
        {
            var oldMode = BookendRotation.Match(p => p.Mode, () => BookendSelectionMode.Single);
            var newMode = bookendRotation.Value.Mode;
            BookendRotation = bookendRotation;
            if (oldMode != newMode)
                BookendRotationCursor = 0;
        }

        if (bookendTokens.HasValue)
            BookendTokens = NormalizeBookendTokens(bookendTokens.Value);
        if (calendar.HasValue)
            Calendar = calendar;


        UpdatedAt = utcNow;
        return Result.Success();
    }

    /// <summary>
    ///     Field-for-field copy, used by update flows that must validate and apply a change without
    ///     mutating the caller's instance unless (and until) the update succeeds.
    /// </summary>
    public MarketSchedule Clone()
    {
        return new MarketSchedule
        {
            MarketId = MarketId,
            Name = Name,
            Station = Station,
            Timezone = Timezone,
            CronExpression = CronExpression,
            Enabled = Enabled,
            CurationEnabled = CurationEnabled,
            PlayoutEnabled = PlayoutEnabled,
            Coverage = Coverage,
            VoiceProfileId = VoiceProfileId,
            DeliveryStyleId = DeliveryStyleId,
            PlaybookId = PlaybookId,
            PronunciationDictionaryId = PronunciationDictionaryId,
            BookendId = BookendId,
            ContentSelection = ContentSelection,
            FillOrder = FillOrder,
            VoiceRotation = VoiceRotation,
            Calendar = Calendar,
            VoiceRotationCursor = VoiceRotationCursor,
            BookendRotation = BookendRotation,
            BookendRotationCursor = BookendRotationCursor,
            BookendTokens = BookendTokens,
            OutputPath = OutputPath,
            TargetDurationSeconds = TargetDurationSeconds,
            NotifyBeforePlayoutMinutes = NotifyBeforePlayoutMinutes,
            EscalationBeforePlayoutMinutes = EscalationBeforePlayoutMinutes,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            LastRunUtc = LastRunUtc,
            NextRunUtc = NextRunUtc,
            CurrentJobId = CurrentJobId
        };
    }

    private static Result ValidateRequired(string value, string fieldName)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Result.Failure($"{fieldName} is required.")
            : Result.Success();
    }

    /// <summary>
    ///     A broadcast slot has physical bounds. Only "&gt; 0" was enforced, so 99,999 seconds
    ///     (27 hours) and 0.001 seconds were both accepted and stored on dev (2026-07-28), each
    ///     leaving the generator chasing an impossible target. The window is deliberately wide —
    ///     real ARN slots run 15-60s — so it rejects nonsense without constraining programming.
    /// </summary>
    internal const double MinDurationSeconds = 5;

    internal const double MaxDurationSeconds = 600;

    private static Result ValidateDuration(double value, string fieldName)
    {
        if (value <= 0)
            return Result.Failure($"{fieldName} must be positive.");
        return value < MinDurationSeconds || value > MaxDurationSeconds
            ? Result.Failure(
                $"{fieldName} must be between {MinDurationSeconds:0} and {MaxDurationSeconds:0} seconds.")
            : Result.Success();
    }

    private static Result ValidateNonNegative(int value, string fieldName)
    {
        return value < 0
            ? Result.Failure($"{fieldName} must be non-negative.")
            : Result.Success();
    }

    /// <summary>
    ///     The escalation reminder must land STRICTLY closer to playout than the first notification,
    ///     otherwise the two alert tiers collapse into one and <c>JobWatchdog</c> can never
    ///     distinguish "remind" from "escalate".
    ///     <para>
    ///         This lived only in the console's form schema until 2026-08-10, so every non-browser
    ///         write path — the HTTP PUT, config import, a seeded row — could persist a pair the
    ///         console then refused to open: the adelaide station on dev stored 5/5, which made its
    ///         whole studio unsaveable with a disabled Save button reading only "Fix the highlighted
    ///         steps first". Validating here closes every path at once, so a row the operator cannot
    ///         edit can no longer be created.
    ///     </para>
    /// </summary>
    private static Result ValidateAlertOrdering(int notifyMinutes, int escalationMinutes)
    {
        return escalationMinutes >= notifyMinutes
            ? Result.Failure(
                $"EscalationBeforePlayoutMinutes ({escalationMinutes}) must be less than "
                + $"NotifyBeforePlayoutMinutes ({notifyMinutes}): the escalation tier has to fire "
                + "closer to playout than the first reminder.")
            : Result.Success();
    }

    private static string NormalizeOptionalId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "" : value;
    }

    /// <summary>
    ///     Normalises a bookend-token dictionary: blank keys/values are dropped, both sides are
    ///     trimmed, and the result is always case-insensitive so "{City}" and "{city}" resolve
    ///     identically. Null (no tokens configured) degrades to an empty dictionary.
    /// </summary>
    private static IReadOnlyDictionary<string, string> NormalizeBookendTokens(
        IReadOnlyDictionary<string, string>? tokens)
    {
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tokens is null)
            return normalized;

        foreach (var (key, value) in tokens)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;
            normalized[key.Trim()] = value.Trim();
        }

        return normalized;
    }

    public string MarketId { get; init; } = null!;
    [JsonInclude] public string Name { get; private set; } = null!;
    [JsonInclude] public string Station { get; private set; } = null!;
    [JsonInclude] public string Timezone { get; private set; } = null!;
    [JsonInclude] public string CronExpression { get; private set; } = null!;
    public bool Enabled { get; set; }
    /// <summary>
    ///     Per-station opt-in for the measured five-minute operator curation window. False keeps
    ///     every existing schedule on the legacy fetch-and-generate path.
    /// </summary>
    public bool CurationEnabled { get; set; } = false;

    /// <summary>
    ///     Per-station master switch for playout delivery. A station can be fully ACTIVE —
    ///     generating, reviewing and publishing on its schedule — while this is false, in which
    ///     case <c>JobService.PublishJobAsync</c> skips the drop write and says so in the job log.
    ///     <para>
    ///         Defaults TRUE, and an absent stored column rehydrates as true, because every station
    ///         that existed before this flag was airing: a false default would have silently taken
    ///         production off air on deploy. Deliberately independent of <c>Zetta:Enabled</c>, the
    ///         environment-wide gate — delivery needs BOTH. That is what lets an environment run
    ///         with playout switched on at the platform level while each station is armed or held
    ///         back individually, so two environments can be live without both writing the same
    ///         file name for the same station.
    ///     </para>
    /// </summary>
    public bool PlayoutEnabled { get; set; } = true;
    [JsonInclude] public CoverageArea Coverage { get; private set; } = null!;
    [JsonInclude] public string VoiceProfileId { get; private set; } = null!;

    /// <summary>
    ///     Per-station delivery style (ElevenLabs v3 audio-tag policy) applied to every bulletin.
    ///     Empty = neutral (read exactly as today). Resolved live by the poller, like the voice profile.
    /// </summary>
    public string DeliveryStyleId { get; set; } = "";
    /// <summary>
    ///     Per-station bulletin playbook overlay applied to the writer instructions. Empty = no
    ///     station-specific playbook. Resolved live by the poller, like the delivery style.
    /// </summary>
    public string PlaybookId { get; set; } = "";

    /// <summary>
    ///     Per-station pronunciation dictionary (ElevenLabs alias/phoneme rules) applied to every
    ///     bulletin. Empty = the configured default (or none). Resolved live by the poller, like
    ///     the voice profile and delivery style.
    /// </summary>
    public string PronunciationDictionaryId { get; set; } = "";

    /// <summary>
    ///     Per-station bookend pair (shared library): the fixed lines read before and after every
    ///     bulletin. Empty = no bookend, and the bulletin reads exactly as it did before the
    ///     feature existed. Resolved live by the poller, like the voice profile and delivery style.
    /// </summary>
    public string BookendId { get; set; } = "";

    /// <summary>
    ///     Per-market content sources (WS1: traffic + weather + fuel selection).
    ///     None = traffic-only default.
    /// </summary>
    public Option<BulletinContentSelection> ContentSelection { get; set; }

    /// <summary>
    ///     The order remaining-slot fill sources are tried in once traffic incidents alone can't
    ///     reach the target duration (BFN-003), AND, for weather versus fuel, their on-air read
    ///     order (see <see cref="FillSource" />). Default: weather and fuel are already
    ///     unconditionally offered to the writer when selected; MinorTraffic escalates to more
    ///     real (if lower-severity) traffic content before falling to Filler, which comes last.
    ///     Threaded into the draft prompt as an explicit priority order — the writer still decides
    ///     HOW MUCH of each to use, but not which to reach for first, nor where the section sits.
    /// </summary>
    public IReadOnlyList<FillSource> FillOrder { get; set; } =
        [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler];

    /// <summary>
    ///     Per-station voice selection policy. None = the single primary <see cref="VoiceProfileId" />
    ///     (exact legacy behaviour). RoundRobin cycles a set; TimeOfDay maps local-time windows to
    ///     voices. Resolved live by the poller, like the voice profile, delivery style and dictionary.
    /// </summary>
    public Option<VoiceRotationPolicy> VoiceRotation { get; set; }

    /// <summary>
    ///     Monotonic round-robin cursor. Advanced once per completed run (see
    ///     <see cref="AdvanceVoiceRotation" />); the poller resolves <c>cursor % candidates</c> so
    ///     consecutive bulletins cycle voices. Irrelevant to single-voice schedules.
    ///     Never negative: a negative assignment (corrupt storage row, buggy caller) clamps to 0
    ///     so the poller's modulo indexing cannot go out of range.
    /// </summary>
    public int VoiceRotationCursor
    {
        get => _voiceRotationCursor;
        set => _voiceRotationCursor = value < 0 ? 0 : value;
    }

    private int _voiceRotationCursor;

    /// <summary>
    ///     Per-station bookend selection policy. None = the single primary
    ///     <see cref="BookendId" /> (or no bookend). RoundRobin cycles a set of pairs; TimeOfDay
    ///     maps local-time windows to pairs. Resolved live by the poller, exactly like
    ///     <see cref="VoiceRotation" />.
    /// </summary>
    public Option<BookendRotationPolicy> BookendRotation { get; set; }

    /// <summary>
    ///     Monotonic round-robin cursor for the bookend rotation policy.
    ///     Advanced once per completed run when a rotation is configured (see
    ///     <see cref="AdvanceBookendRotation" />); the poller resolves <c>cursor % candidates</c> so
    ///     consecutive bulletins cycle bookend pairs. Irrelevant without a rotation policy.
    ///     Never negative: a negative assignment (corrupt storage row, buggy caller) clamps to 0
    ///     so the poller's modulo indexing cannot go out of range.
    /// </summary>
    public int BookendRotationCursor
    {
        get => _bookendRotationCursor;
        set => _bookendRotationCursor = value < 0 ? 0 : value;
    }

    private int _bookendRotationCursor;

    /// <summary>
    ///     Per-station token values for bookend templates: station-specific overrides of the
    ///     built-ins (city/market/station) plus any station-private tokens. Overrides always win
    ///     over the built-ins. Always stored trimmed and case-insensitive; blank keys and values
    ///     are dropped on assignment so a garbage entry can never air as an empty word.
    /// </summary>
    public IReadOnlyDictionary<string, string> BookendTokens
    {
        get => _bookendTokens;
        set => _bookendTokens = NormalizeBookendTokens(value);
    }

    private IReadOnlyDictionary<string, string> _bookendTokens =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    ///     Optional station-local calendar-year overlay. None preserves the original single-cron
    ///     schedule behaviour exactly.
    /// </summary>
    public Option<ScheduleCalendar> Calendar { get; set; }

    public string OutputPath { get; set; } = null!;
    public double TargetDurationSeconds { get; set; }
    public int NotifyBeforePlayoutMinutes { get; set; }
    public int EscalationBeforePlayoutMinutes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Execution state - these have explicit "not set" semantics via HasValue checks
    public Option<DateTime> LastRunUtc { get; set; }
    public Option<DateTime> NextRunUtc { get; set; }
    public Option<string> CurrentJobId { get; set; }

    // State queries - explicit, no sentinel values
    public bool HasEverRun => LastRunUtc.HasValue;
    public bool IsRunning => CurrentJobId.HasValue;
    public bool HasNextRun => NextRunUtc.HasValue;

    // State transitions
    public void StartRun(string jobId, DateTime utcNow)
    {
        CurrentJobId = Option<string>.Some(jobId);
        UpdatedAt = utcNow;
    }

    public void CompleteRun(DateTime utcNow, Option<DateTime> nextRunUtc)
    {
        CurrentJobId = Option<string>.None;
        LastRunUtc = Option<DateTime>.Some(utcNow);
        NextRunUtc = nextRunUtc;
        UpdatedAt = utcNow;
        // Advance the round-robin cursor exactly once per completed run, so the next bulletin
        // picks the next voice. No-op for single-voice schedules (no rotation policy).
        if (VoiceRotation.HasValue)
            AdvanceVoiceRotation();
        // Bookend rotations advance INDEPENDENTLY of the voice rotation: a station that rotates
        // bookends but not voices (or vice versa) must still cycle the configured side.
        if (BookendRotation.HasValue)
            AdvanceBookendRotation();
    }

    /// <summary>Advances the round-robin cursor, wrapping at <see cref="int.MaxValue" />.</summary>
    public void AdvanceVoiceRotation()
    {
        VoiceRotationCursor = VoiceRotationCursor == int.MaxValue ? 0 : VoiceRotationCursor + 1;
    }

    /// <summary>
    ///     Advances the shared bookend round-robin cursor, wrapping at <see cref="int.MaxValue" />.
    /// </summary>
    public void AdvanceBookendRotation()
    {
        BookendRotationCursor = BookendRotationCursor == int.MaxValue ? 0 : BookendRotationCursor + 1;
    }

    public void ScheduleNextRun(Option<DateTime> nextRunUtc)
    {
        NextRunUtc = nextRunUtc;
    }
}

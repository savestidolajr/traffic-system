using System.Text;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Builds the HERE Traffic API v7 <c>in=</c> geospatial filter for a
///     coverage area: <c>circle</c>, <c>bbox</c>, <c>corridor</c>
///     (flexible-polyline encoded), or <c>polygon</c> (queried via its
///     enclosing bounding box — HERE has no native polygon filter, so
///     incidents are post-filtered point-in-polygon server-side). Shape-
///     specific HERE limits are clamped here so an over-sized stored shape
///     degrades instead of failing the fetch (the same policy as the 50 km
///     circle clamp). <see cref="BuildTiles" /> is the tiling-aware entry
///     point callers should use for bbox/polygon shapes; <see cref="Build" />
///     stays available for circle/corridor (always single-tile) and as the
///     clamped fallback for a single filter string.
/// </summary>
internal static class HereGeoFilter
{
    private const int MaxCircleRadiusMeters = 50_000;
    private const int MaxCorridorRadiusMeters = 5_000;

    /// <summary>
    ///     Splits a coverage area into one or more HERE geospatial filter
    ///     strings, each guaranteed to fit HERE's per-request limits (50 km
    ///     circle radius, 1° bbox side). Circles and corridors always fit in
    ///     a single HERE request. Bbox and polygon shapes are allowed up to
    ///     <see cref="CoverageArea.MaxShapeSpanDegrees" /> per side; anything
    ///     over the 1° per-tile limit is split into a grid of sub-1° bbox
    ///     filters. Callers must fetch every tile and merge/de-duplicate the
    ///     results — HERE has no larger single-shape option.
    /// </summary>
    public static IReadOnlyList<string> BuildTiles(CoverageArea area)
    {
        switch (area.ShapeType)
        {
            case CoverageShapeType.BoundingBox when area is
                { West: not null, South: not null, East: not null, North: not null }:
                return TileBoundingBox(area.West.Value, area.South.Value, area.East.Value, area.North.Value);

            case CoverageShapeType.Polygon when area.PolygonPoints is { Count: >= 3 }:
            {
                double west = area.PolygonPoints.Min(p => p.Lng);
                double south = area.PolygonPoints.Min(p => p.Lat);
                double east = area.PolygonPoints.Max(p => p.Lng);
                double north = area.PolygonPoints.Max(p => p.Lat);
                return TileBoundingBox(west, south, east, north);
            }

            default:
                return [Build(area)];
        }
    }

    /// <summary>
    ///     Splits a west/south/east/north box into a grid of tiles no larger
    ///     than <see cref="CoverageArea.MaxBoundingBoxDegrees" /> per side,
    ///     each rendered as a HERE <c>bbox:</c> filter. A box already within
    ///     the limit returns a single tile.
    /// </summary>
    private static IReadOnlyList<string> TileBoundingBox(double west, double south, double east, double north)
    {
        double maxDeg = CoverageArea.MaxBoundingBoxDegrees;
        int cols = Math.Max(1, (int)Math.Ceiling((east - west) / maxDeg));
        int rows = Math.Max(1, (int)Math.Ceiling((north - south) / maxDeg));
        double colWidth = (east - west) / cols;
        double rowHeight = (north - south) / rows;

        var tiles = new List<string>(cols * rows);
        for (int r = 0; r < rows; r++)
        {
            double tileSouth = south + r * rowHeight;
            double tileNorth = r == rows - 1 ? north : tileSouth + rowHeight;
            for (int c = 0; c < cols; c++)
            {
                double tileWest = west + c * colWidth;
                double tileEast = c == cols - 1 ? east : tileWest + colWidth;
                tiles.Add(FormattableString.Invariant($"bbox:{tileWest},{tileSouth},{tileEast},{tileNorth}"));
            }
        }

        return tiles;
    }

    public static string Build(CoverageArea area)
    {
        switch (area.ShapeType)
        {
            case CoverageShapeType.BoundingBox when area is
                { West: not null, South: not null, East: not null, North: not null }:
            {
                // HERE caps bbox extents at 1 degree per side; shrink towards
                // the centre rather than failing.
                double west = area.West.Value, south = area.South.Value;
                double east = area.East.Value, north = area.North.Value;
                ClampToMaxDegrees(ref south, ref north);
                ClampToMaxDegrees(ref west, ref east);

                return FormattableString.Invariant($"bbox:{west},{south},{east},{north}");
            }

            case CoverageShapeType.Polygon when area.PolygonPoints is { Count: >= 3 }:
            {
                // HERE has no polygon filter; query the enclosing bbox and
                // post-filter incident points server-side.
                double west = area.PolygonPoints.Min(p => p.Lng);
                double south = area.PolygonPoints.Min(p => p.Lat);
                double east = area.PolygonPoints.Max(p => p.Lng);
                double north = area.PolygonPoints.Max(p => p.Lat);
                ClampToMaxDegrees(ref south, ref north);
                ClampToMaxDegrees(ref west, ref east);

                return FormattableString.Invariant($"bbox:{west},{south},{east},{north}");
            }

            case CoverageShapeType.Corridor when area.CorridorPoints is { Count: >= 2 }:
            {
                int radius = Math.Clamp((int)(area.CorridorRadiusMeters ?? 1000), 1, MaxCorridorRadiusMeters);
                string polyline = FlexiblePolyline.Encode(area.CorridorPoints);
                return FormattableString.Invariant($"corridor:{polyline};r={radius}");
            }

            default:
            {
                int radius = Math.Min((int)(area.RadiusKm * 1000), MaxCircleRadiusMeters);
                return FormattableString.Invariant($"circle:{area.Latitude},{area.Longitude};r={radius}");
            }
        }
    }

    /// <summary>
    ///     Shrinks a dimension (latitude or longitude span) towards its
    ///     centre so it does not exceed <see cref="CoverageArea.MaxBoundingBoxDegrees" />.
    ///     HERE rejects bbox extents beyond 1 degree per side.
    /// </summary>
    private static void ClampToMaxDegrees(ref double low, ref double high)
    {
        if (high - low > CoverageArea.MaxBoundingBoxDegrees)
        {
            double mid = (low + high) / 2.0;
            low = mid - CoverageArea.MaxBoundingBoxDegrees / 2.0;
            high = mid + CoverageArea.MaxBoundingBoxDegrees / 2.0;
        }
    }
}

/// <summary>
///     HERE flexible-polyline encoder (https://github.com/heremaps/flexible-polyline),
///     2D variant with precision 6 — matches the encoding in HERE's own
///     corridor-filter documentation example (verified byte-for-byte).
///     Encoding only; we never decode.
/// </summary>
internal static class FlexiblePolyline
{
    private const string EncodingTable = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
    private const int Precision = 6;

    public static string Encode(IReadOnlyList<CoveragePoint> points)
    {
        var sb = new StringBuilder();

        // Header: version 1, then content byte (precision 6, no 3rd dimension)
        EncodeUnsigned(1, sb);
        EncodeUnsigned(Precision, sb); // precision in low 4 bits, 3rd dim 0

        long multiplier = (long)Math.Pow(10, Precision);
        long prevLat = 0, prevLng = 0;

        foreach (var point in points)
        {
            long lat = (long)Math.Round(point.Lat * multiplier);
            long lng = (long)Math.Round(point.Lng * multiplier);

            EncodeSigned(lat - prevLat, sb);
            EncodeSigned(lng - prevLng, sb);

            prevLat = lat;
            prevLng = lng;
        }

        return sb.ToString();
    }

    private static void EncodeSigned(long value, StringBuilder sb)
    {
        long encoded = value << 1;
        if (value < 0) encoded = ~encoded;
        EncodeUnsigned(encoded, sb);
    }

    private static void EncodeUnsigned(long value, StringBuilder sb)
    {
        while (value > 0x1F)
        {
            sb.Append(EncodingTable[(int)((value & 0x1F) | 0x20)]);
            value >>= 5;
        }

        sb.Append(EncodingTable[(int)value]);
    }
}

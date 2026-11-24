using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Derives a compass direction of travel from an incident's shape geometry. HERE incident
///     locations are directional polylines ordered along the affected roadway; the octant of the
///     shape's own bearing is the only direction the geometry can support.
///     <para>
///         "Citybound"/"outbound" is deliberately NOT derived here. It was resolved against the
///         COVERAGE AREA's centre, which made the direction a property of which station fetched the
///         record rather than of the road: one 457 m record aired "citybound" 15 times and "outbound"
///         once from byte-identical input. A market CBD reference point does not exist in
///         configuration (MonitoredRoutes.Markets holds route endpoints, not a centre), so the
///         derivation cannot be made sound where it stands.
///     </para>
/// </summary>
public static partial class CompassDirection
{
    /// <summary>Shapes shorter than this give unreliable bearings.</summary>
    public const double MinShapeLengthMeters = 150;

    /// <summary>
    ///     Initial great-circle bearing from (lat1, lng1) to (lat2, lng2),
    ///     in degrees clockwise from north [0, 360).
    /// </summary>
    public static double Bearing(double lat1, double lng1, double lat2, double lng2)
    {
        double phi1 = lat1 * Math.PI / 180.0;
        double phi2 = lat2 * Math.PI / 180.0;
        double dLambda = (lng2 - lng1) * Math.PI / 180.0;

        double y = Math.Sin(dLambda) * Math.Cos(phi2);
        double x = Math.Cos(phi1) * Math.Sin(phi2) -
                   Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLambda);
        double theta = Math.Atan2(y, x) * 180.0 / Math.PI;
        return (theta + 360.0) % 360.0;
    }

    /// <summary>
    ///     The compass octant a shape running from <paramref name="start" /> to <paramref name="end" />
    ///     travels in, or none for a degenerate (near-zero-length) shape. Callers gate this on HERE's
    ///     own segment direction signs agreeing — the shape alone does not say which carriageway.
    /// </summary>
    public static Option<string> ResolveCompass(
        (double Lat, double Lng) start,
        (double Lat, double Lng) end)
    {
        if (Math.Abs(start.Lat - end.Lat) < 1e-6 && Math.Abs(start.Lng - end.Lng) < 1e-6)
            return default;

        double travel = Bearing(start.Lat, start.Lng, end.Lat, end.Lng);
        return Option<string>.Some(travel switch
        {
            >= 315.0 or < 45.0 => "northbound",
            >= 45.0 and < 135.0 => "eastbound",
            >= 135.0 and < 225.0 => "southbound",
            _ => "westbound"
        });
    }
}

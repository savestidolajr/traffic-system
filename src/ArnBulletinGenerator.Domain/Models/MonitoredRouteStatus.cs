namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Live-vs-typical drive time for a station's configured commuter route, backing the monitored-route headline claim in a bulletin script.</summary>
public sealed record MonitoredRouteStatus(
    string Name,
    CoveragePoint Origin,
    CoveragePoint Destination,
    int DriveTimeMinutes,
    int NormalDriveTimeMinutes,
    double DistanceKm)
{
    public int DelayMinutes => DriveTimeMinutes - NormalDriveTimeMinutes;
}

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Live versus typical drive time and distance between two points, from which the current delay is derived.</summary>
public sealed record DriveTimeResult(
    int DurationMinutes,
    int TypicalDurationMinutes,
    double DistanceKm)
{
    public int DelayMinutes => DurationMinutes - TypicalDurationMinutes;
}

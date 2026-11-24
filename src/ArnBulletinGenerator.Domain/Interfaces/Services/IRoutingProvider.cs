using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Fetches live-vs-typical drive time and distance between two points, backing monitored-route headlines and detour comparisons.</summary>
public interface IRoutingProvider
{
    Task<Result<DriveTimeResult>> GetDriveTimeAsync(
        (double Lat, double Lng) origin,
        (double Lat, double Lng) destination,
        CancellationToken cancellationToken = default);
}

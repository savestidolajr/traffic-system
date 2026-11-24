using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Finds an alternate route between two points that avoids named congested or closed road segments, for detour advice in a bulletin.</summary>
public interface IDetourRoutingProvider
{
    Task<Result<DetourSuggestion>> GetSafeDetourAsync(
        (double Lat, double Lng) origin,
        (double Lat, double Lng) destination,
        IReadOnlyList<string> avoidSegments,
        DriveTimeResult currentRoute,
        CancellationToken cancellationToken = default);
}

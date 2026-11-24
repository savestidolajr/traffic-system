using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for detour routing: returns a fixed named detour with a
///     small, current-route-relative time saving instead of calling HERE Routing.
/// </summary>
public sealed class FakeDetourRoutingProvider(FakeExternalCallLog log) : IDetourRoutingProvider
{
    public Task<Result<DetourSuggestion>> GetSafeDetourAsync(
        (double Lat, double Lng) origin,
        (double Lat, double Lng) destination,
        IReadOnlyList<string> avoidSegments,
        DriveTimeResult currentRoute,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"detour avoid={string.Join(',', avoidSegments)}");
        var saved = Math.Max(1, Math.Min(8, currentRoute.DelayMinutes - 2));
        return Task.FromResult(Result<DetourSuggestion>.Success(new DetourSuggestion(
            Math.Max(1, currentRoute.DurationMinutes - saved), saved, Math.Max(1.0, currentRoute.DistanceKm + 1.8),
            "via Nerang Broadbeach Road")));
    }
}

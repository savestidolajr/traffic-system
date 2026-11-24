using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for drive-time routing: returns a fixed duration, delay,
///     and distance for any origin/destination pair instead of calling HERE Routing.
/// </summary>
public sealed class FakeRoutingProvider(FakeExternalCallLog log) : IRoutingProvider
{
    public Task<Result<DriveTimeResult>> GetDriveTimeAsync(
        (double Lat, double Lng) origin,
        (double Lat, double Lng) destination,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"routing origin={origin.Lat:F4},{origin.Lng:F4} destination={destination.Lat:F4},{destination.Lng:F4}");
        return Task.FromResult(Result<DriveTimeResult>.Success(new DriveTimeResult(24, 14, 18.6)));
    }
}

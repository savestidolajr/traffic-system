using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>Deterministic ARN_E2E stand-in for HERE Weather: always returns a fixed fine, 23 degree, light-wind report instead of calling the live API.</summary>
public sealed class FakeWeatherProvider(FakeExternalCallLog log) : IWeatherProvider
{
    public Task<Result<WeatherReport>> GetWeatherAsync(CoverageArea coverage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"weather lat={coverage.Latitude:F4} lng={coverage.Longitude:F4}");
        return Task.FromResult(Result<WeatherReport>.Success(new WeatherReport(
            "Fine", 23.0, "light easterly wind", 0, [])));
    }
}

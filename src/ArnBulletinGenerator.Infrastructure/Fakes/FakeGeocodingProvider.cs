using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for geocoding: resolves a small hardcoded set of Gold
///     Coast/Sydney/Brisbane place names to fixed coordinates and fabricates reverse-geocode
///     results by latitude instead of calling HERE Geocoding.
/// </summary>
public sealed class FakeGeocodingProvider(FakeExternalCallLog log) : IGeocodingProvider
{
    private static readonly Dictionary<string, (double Lat, double Lng)> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gold coast"] = (-28.0167, 153.4000),
        ["southport"] = (-27.9676, 153.4032),
        ["smith street motorway"] = (-27.9510, 153.3640),
        ["m1 pacific motorway"] = (-27.9670, 153.3830),
        ["gold coast highway"] = (-28.0290, 153.4310),
        ["sydney"] = (-33.8688, 151.2093),
        ["brisbane"] = (-27.4698, 153.0251)
    };

    public Task<Result<CoverageArea>> GeocodeAsync(
        string query,
        double defaultRadiusKm = 5.0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"geocode query={query}");

        foreach (var pair in Known)
        {
            if (query.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(CoverageArea.Create(pair.Value.Lat, pair.Value.Lng, defaultRadiusKm));
        }

        return Task.FromResult(Result<CoverageArea>.Failure($"Fake geocoding has no match for '{query}'"));
    }

    public Task<Result<string>> ReverseGeocodeAsync(double lat, double lng, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"reverse-geocode lat={lat:F4} lng={lng:F4}");
        return Task.FromResult(Result<string>.Success(lat < -30 ? "Sydney NSW" : "Gold Coast QLD"));
    }

    public Task<Result<IDictionary<int, string>>> BatchReverseGeocodeAsync(
        IEnumerable<(double Lat, double Lng)> coordinates,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = coordinates.Select((_, index) => new { index, name = index % 2 == 0 ? "Gold Coast QLD" : "Sydney NSW" })
            .ToDictionary(x => x.index, x => x.name);
        return Task.FromResult(Result<IDictionary<int, string>>.Success(result));
    }
}

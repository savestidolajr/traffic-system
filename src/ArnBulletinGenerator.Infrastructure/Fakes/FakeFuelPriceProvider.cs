using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for fuel prices: returns fixed Unleaded/Diesel prices with
///     a fixed stale-looking timestamp instead of calling the HERE Fuel Prices API.
/// </summary>
public sealed class FakeFuelPriceProvider(FakeExternalCallLog log) : IFuelPriceProvider
{
    public Task<Result<FuelPriceReport>> GetFuelPricesAsync(
        CoverageArea coverage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"fuel lat={coverage.Latitude:F4} lng={coverage.Longitude:F4}");
        return Task.FromResult(Result<FuelPriceReport>.Success(new FuelPriceReport(
        [
            new FuelPriceSummary("Unleaded", 189.9, 189.9, 1, DateTimeOffset.Parse("2026-06-22T23:00:00Z")),
            new FuelPriceSummary("Diesel", 197.9, 197.9, 1, DateTimeOffset.Parse("2026-06-22T23:00:00Z"))
        ])));
    }
}

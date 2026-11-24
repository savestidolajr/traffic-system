using System.Globalization;
using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Fetches nearby fuel station prices from the HERE Fuel Prices v3 API for a market's coverage
///     area, or its optional per-market fuel override, resolving the fuel-specific API key with
///     fallback to the shared HERE key.
/// </summary>
public sealed class HereFuelPriceProvider(
    HttpClient httpClient,
    IOptions<FuelOptions> fuelOptions,
    IOptions<HereOptions> hereOptions,
    ILogger<HereFuelPriceProvider> logger) : IFuelPriceProvider
{
    private static readonly Dictionary<string, string> FuelTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["36"] = "Unleaded 91",
        ["56"] = "Unleaded 95",
        ["60"] = "Unleaded 98",
        ["1"] = "Diesel",
        ["55"] = "Premium Diesel"
    };

    public async Task<Result<FuelPriceReport>> GetFuelPricesAsync(
        CoverageArea coverage,
        CancellationToken cancellationToken = default)
    {
        if (!fuelOptions.Value.Enabled)
            return Result<FuelPriceReport>.Failure(
                "Fuel provider is disabled (Fuel:Enabled=false); enable it platform-wide before selecting fuel for a market.");

        var apiKey = ResolveApiKey();
        if (apiKey.IsFailure)
            return Result<FuelPriceReport>.Failure(apiKey.Error);

        var opts = fuelOptions.Value;
        var fuelCoverage = coverage.FuelCoverage ?? coverage;
        string lat = fuelCoverage.Latitude.ToString("0.000000", CultureInfo.InvariantCulture);
        string lng = fuelCoverage.Longitude.ToString("0.000000", CultureInfo.InvariantCulture);
        int radiusMeters = (int)Math.Round(Math.Min(fuelCoverage.RadiusKm, opts.MaxRadiusKm) * 1000);

        string url =
            $"{opts.BaseUrl}/stations?in=circle:{lat},{lng};r={radiusMeters}&fuelTypes={opts.FuelTypes}&limit={opts.MaxStations}&apiKey={apiKey.Value}";

        var result = await HereApiHelper.ExecuteGetAsync<FuelApiResponse>(httpClient, url, logger, "Fuel", cancellationToken);
        if (result.IsFailure)
            return Result<FuelPriceReport>.Failure(result.Error);

        return ParseFuelResponse(result.Value, opts);
    }

    private Result<string> ResolveApiKey()
    {
        var fuelKey = fuelOptions.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(fuelKey))
            return fuelKey;

        var hereKey = hereOptions.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(hereKey))
            return hereKey;

        logger.LogError("Fuel API key not configured");
        return Result<string>.Failure("Fuel API key not configured");
    }

    private Result<FuelPriceReport> ParseFuelResponse(FuelApiResponse response, FuelOptions opts)
    {
        var stations = response.Stations;
        if (stations is null || stations.Count == 0)
        {
            logger.LogInformation("Fuel: no stations — coverage may not include fuel data");
            return Result<FuelPriceReport>.Success(new FuelPriceReport([]));
        }

        var fuelTypeIds = opts.FuelTypes.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Aggregate prices per fuel type, tracking distinct stations and which specific
        // station reports the minimum price (so we can say WHERE the cheapest price is).
        var priceByType = new Dictionary<string, List<double>>();
        var stationIdsByType = new Dictionary<string, HashSet<string>>();
        var latestModifiedByType = new Dictionary<string, DateTimeOffset>();
        var cheapestSuburbByType = new Dictionary<string, (double Price, string? Suburb)>();

        foreach (var station in stations)
        {
            var stationId = station.Id ?? string.Empty;
            if (station.Prices is null) continue;
            string? suburb = station.Address?.District;

            foreach (var price in station.Prices)
            {
                if (price.Price <= 0) continue;
                if (price.Available == false) continue;

                var typeId = price.FuelType;
                if (string.IsNullOrWhiteSpace(typeId)) continue;

                if (!priceByType.TryGetValue(typeId, out var list))
                {
                    list = [];
                    priceByType[typeId] = list;
                    stationIdsByType[typeId] = [];
                }
                list.Add(price.Price);
                stationIdsByType[typeId].Add(stationId);
                if (price.Modified.HasValue &&
                    (!latestModifiedByType.TryGetValue(typeId, out var current) || price.Modified.Value > current))
                {
                    latestModifiedByType[typeId] = price.Modified.Value;
                }
                if (!cheapestSuburbByType.TryGetValue(typeId, out var cheapest) || price.Price < cheapest.Price)
                    cheapestSuburbByType[typeId] = (price.Price, suburb);
            }
        }

        var summaries = new List<FuelPriceSummary>();
        int stationCount = stations.Count;

        foreach (var typeId in fuelTypeIds)
        {
            if (!priceByType.TryGetValue(typeId, out var prices) || prices.Count == 0)
                continue;

            string name = FuelTypeNames.TryGetValue(typeId, out var mapped)
                ? mapped
                : $"Fuel type {typeId}";

            double minPrice = Math.Round(prices.Min(), 3);
            double avgPrice = Math.Round(prices.Average(), 3);
            int contributingStations = stationIdsByType.TryGetValue(typeId, out var ids) ? ids.Count : prices.Count;
            DateTimeOffset? latestModified = latestModifiedByType.TryGetValue(typeId, out var modified)
                ? modified
                : null;
            string? cheapestSuburb = cheapestSuburbByType.TryGetValue(typeId, out var cheapestEntry)
                ? cheapestEntry.Suburb
                : null;

            summaries.Add(new FuelPriceSummary(name, minPrice, avgPrice, contributingStations, latestModified, cheapestSuburb));
        }

        int priceSummaryCount = summaries.Count;
        if (priceSummaryCount == 0)
        {
            logger.LogInformation("Fuel: {StationCount} stations but no prices matched — coverage may not include fuel data", stationCount);
        }
        else
        {
            logger.LogInformation("Fuel: {StationCount} stations, {SummaryCount} fuel type(s) with prices", stationCount, priceSummaryCount);
        }

        return Result<FuelPriceReport>.Success(new FuelPriceReport(summaries));
    }

    // ── Response DTOs ──────────────────────────────────────────────

    internal sealed record FuelApiResponse(
        [property: JsonPropertyName("stations")] List<FuelStation>? Stations = null);

    internal sealed record FuelStation(
        [property: JsonPropertyName("id")] string? Id = null,
        [property: JsonPropertyName("prices")] List<FuelPriceEntry>? Prices = null,
        [property: JsonPropertyName("address")] FuelStationAddress? Address = null);

    /// <summary>District is the suburb name in HERE's fuel-station address (confirmed via a
    /// live API call 2026-07-01, e.g. "Woolloomooloo", "Paddington") — used to say WHERE the
    /// cheapest price is, not just what it is.</summary>
    internal sealed record FuelStationAddress(
        [property: JsonPropertyName("district")] string? District = null);

    internal sealed record FuelPriceEntry(
        [property: JsonPropertyName("fuelType")] string FuelType,
        [property: JsonPropertyName("price")] double Price,
        [property: JsonPropertyName("currency")] string? Currency,
        [property: JsonPropertyName("unit")] string? Unit,
        [property: JsonPropertyName("available")] bool? Available,
        [property: JsonPropertyName("modified")] DateTimeOffset? Modified);
}

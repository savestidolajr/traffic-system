using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Configuration for the HERE Fuel Prices integration: enable flag, base URL, tracked fuel
///     types, station/radius limits, and API key fallback.
/// </summary>
public sealed class FuelOptions
{
    public const string SectionName = "Fuel";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://fuel.hereapi.com/v3";

    /// <summary>
    ///     HERE fuel type ids: 36=Unleaded 91, 1=Diesel.
    /// </summary>
    public string FuelTypes { get; set; } = "36,1";

    [Range(1, 100)]
    public int MaxStations { get; set; } = 50;

    /// <summary>
    ///     Maximum search radius in km (clamped from coverage radius).
    /// </summary>
    [Range(1, 100)]
    public double MaxRadiusKm { get; set; } = 30;

    /// <summary>
    ///     When empty, falls back to <see cref="HereOptions.ApiKey" />.
    /// </summary>
    public string ApiKey { get; set; } = "";

    [Range(5, 120)]
    public int TimeoutSeconds { get; set; } = 15;
}

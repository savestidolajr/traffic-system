using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Configuration for the HERE Destination Weather integration: enable flag, base URL,
///     requested products, and API key fallback.
/// </summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://weather.hereapi.com/v3";

    public string Products { get; set; } = "observation,forecast7daysSimple,alerts";

    /// <summary>
    ///     When empty, falls back to <see cref="HereOptions.ApiKey" />.
    /// </summary>
    public string ApiKey { get; set; } = "";

    [Range(5, 120)]
    public int TimeoutSeconds { get; set; } = 15;
}

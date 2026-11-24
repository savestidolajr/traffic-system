using System.Globalization;
using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Fetches current conditions, forecast, and alerts from the HERE Destination Weather v3
///     API for a market's coverage area or its optional per-market weather override, resolving
///     the API key with fallback to the shared key.
/// </summary>
public sealed class HereWeatherProvider(
    HttpClient httpClient,
    IOptions<WeatherOptions> weatherOptions,
    IOptions<HereOptions> hereOptions,
    ILogger<HereWeatherProvider> logger) : IWeatherProvider
{
    public async Task<Result<WeatherReport>> GetWeatherAsync(
        CoverageArea coverage,
        CancellationToken cancellationToken = default)
    {
        if (!weatherOptions.Value.Enabled)
            return Result<WeatherReport>.Failure(
                "Weather provider is disabled (Weather:Enabled=false); enable it platform-wide before selecting weather for a market.");

        var apiKey = ResolveApiKey();
        if (apiKey.IsFailure)
            return Result<WeatherReport>.Failure(apiKey.Error);

        var weatherCoverage = coverage.WeatherCoverage ?? coverage;
        string lat = weatherCoverage.Latitude.ToString("0.000000", CultureInfo.InvariantCulture);
        string lng = weatherCoverage.Longitude.ToString("0.000000", CultureInfo.InvariantCulture);
        var opts = weatherOptions.Value;
        string url =
            $"{opts.BaseUrl}/report?products={opts.Products}&location={lat},{lng}&units=metric&lang=en-US&oneObservation=true&apiKey={apiKey.Value}";

        var result = await HereApiHelper.ExecuteGetAsync<WeatherApiResponse>(httpClient, url, logger, "Weather", cancellationToken);
        if (result.IsFailure)
            return Result<WeatherReport>.Failure(result.Error);

        return ParseWeatherResponse(result.Value);
    }

    private Result<string> ResolveApiKey()
    {
        var weatherKey = weatherOptions.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(weatherKey))
            return weatherKey;

        var hereKey = hereOptions.Value.ApiKey;
        if (!string.IsNullOrWhiteSpace(hereKey))
            return hereKey;

        logger.LogError("Weather API key not configured");
        return Result<string>.Failure("Weather API key not configured");
    }

    private Result<WeatherReport> ParseWeatherResponse(WeatherApiResponse response)
    {
        // Alerts and the daily forecast are needed by BOTH the observation and forecast-fallback
        // paths, so resolve them once up front.
        var alerts = (response.Alerts ?? [])
            .Concat(response.Places?.SelectMany(p => p.Alerts ?? Enumerable.Empty<WeatherAlert>())
                    ?? Enumerable.Empty<WeatherAlert>())
            .Select(a => a.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct()
            .Take(3)
            .ToList() as IReadOnlyList<string>;

        var forecasts = response.Places?
            .SelectMany(p => p.DailyForecasts ?? Enumerable.Empty<DailyForecast>()).FirstOrDefault()?.Forecasts;
        var outlook = BuildOutlook(forecasts);

        var obs = response.Places?
            .SelectMany(p => p.Observations ?? Enumerable.Empty<WeatherObservation>()).FirstOrDefault();

        if (obs is null)
        {
            // No current observation — common for sparse regional coverage. Fall back to today's
            // forecast so the bulletin still carries a TRUTHFUL weather line (voiced as "around N
            // degrees today", never as a current reading) instead of dropping weather entirely.
            var today = forecasts is { Count: > 0 } ? forecasts[0] : null;
            if (today is null)
            {
                logger.LogWarning("Weather response had neither a current observation nor a forecast");
                return Result<WeatherReport>.Failure("Weather response contained no observation or forecast");
            }

            double high = double.TryParse(today.HighTemperature, NumberStyles.Any,
                CultureInfo.InvariantCulture, out double t) ? t : 0;
            var forecastReport = new WeatherReport(
                today.Description ?? "Unknown", high, default, 0, alerts, outlook, IsForecast: true);
            logger.LogInformation(
                "Weather: forecast-only (no current observation) — {Conditions}, high {Temp}°C, {AlertCount} alert(s)",
                forecastReport.Conditions, high, alerts.Count);
            return Result<WeatherReport>.Success(forecastReport);
        }

        Option<string> windDescription = default;
        if (obs.WindSpeed >= 20)
        {
            string dir = (obs.WindDesc ?? "").Trim();
            string strength = obs.WindSpeed >= 45 ? "strong" : obs.WindSpeed >= 30 ? "blustery" : "fresh";
            windDescription = Option<string>.Some(
                dir.Length > 0 ? $"{strength} {dir} wind" : $"{strength} wind");
        }

        var report = new WeatherReport(
            obs.Description ?? "Unknown",
            obs.Temperature,
            windDescription,
            obs.PrecipitationProbability ?? 0,
            alerts,
            outlook);

        logger.LogInformation(
            "Weather: {Conditions}, {Temp}°C, {AlertCount} alert(s)",
            report.Conditions, report.TemperatureC, report.Alerts.Count);

        return Result<WeatherReport>.Success(report);
    }

    /// <summary>
    ///     A short, fill-friendly outlook ("reaching N degrees later today; tomorrow ...") from the
    ///     daily forecast. Voiced only by no-traffic bulletins (the prompt gates it) to round out the
    ///     slot when there is little else to report. None when no forecast was returned.
    /// </summary>
    private static Option<string> BuildOutlook(IReadOnlyList<DailyForecastEntry>? forecasts)
    {
        if (forecasts is not { Count: > 0 })
            return default;

        static string High(DailyForecastEntry e) =>
            double.TryParse(e.HighTemperature, NumberStyles.Any, CultureInfo.InvariantCulture, out double t)
                ? $"{Math.Round(t).ToString(CultureInfo.InvariantCulture)} degrees"
                : "";
        static string Desc(DailyForecastEntry e) => (e.Description ?? "").Trim().TrimEnd('.').ToLowerInvariant();

        var parts = new List<string>();
        string todayHigh = High(forecasts[0]);
        if (todayHigh.Length > 0)
            parts.Add($"reaching {todayHigh} later today");
        if (forecasts.Count > 1)
        {
            string tomDesc = Desc(forecasts[1]);
            string tomHigh = High(forecasts[1]);
            string tomPart = (tomDesc, tomHigh) switch
            {
                ({ Length: > 0 }, { Length: > 0 }) => $"tomorrow {tomDesc}, around {tomHigh}",
                ({ Length: > 0 }, _) => $"tomorrow {tomDesc}",
                (_, { Length: > 0 }) => $"tomorrow around {tomHigh}",
                _ => ""
            };
            if (tomPart.Length > 0)
                parts.Add(tomPart);
        }

        return parts.Count > 0 ? Option<string>.Some(string.Join("; ", parts)) : default;
    }

    // ── Response DTOs ──────────────────────────────────────────────

    internal sealed record WeatherApiResponse(
        [property: JsonPropertyName("places")] List<WeatherPlace>? Places = null,
        [property: JsonPropertyName("alerts")] List<WeatherAlert>? Alerts = null);

    internal sealed record WeatherPlace(
        [property: JsonPropertyName("observations")] List<WeatherObservation>? Observations = null,
        [property: JsonPropertyName("dailyForecasts")] List<DailyForecast>? DailyForecasts = null,
        [property: JsonPropertyName("alerts")] List<WeatherAlert>? Alerts = null);

    internal sealed record DailyForecast(
        [property: JsonPropertyName("forecasts")] List<DailyForecastEntry>? Forecasts = null);

    internal sealed record DailyForecastEntry(
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("highTemperature")] string? HighTemperature);

    internal sealed record WeatherObservation(
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("windDesc")] string? WindDesc,
        [property: JsonPropertyName("windSpeed")] double WindSpeed,
        [property: JsonPropertyName("precipitationProbability")] int? PrecipitationProbability);

    internal sealed record WeatherAlert(
        [property: JsonPropertyName("description")] string? Description);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Current-conditions weather for a coverage area. When the supplied area carries a per-market
///     <see cref="CoverageArea.WeatherCoverage" /> override, implementations use that location.
///     Implementations fail with a Result — the pipeline treats weather as fail-soft garnish
///     (traffic-first per Workshop 1): a provider outage produces a traffic-only bulletin, never
///     a failed one.
/// </summary>
public interface IWeatherProvider
{
    Task<Result<WeatherReport>> GetWeatherAsync(CoverageArea coverage, CancellationToken cancellationToken = default);
}

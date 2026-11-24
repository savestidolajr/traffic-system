using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Fuel price summaries for a coverage area. When the supplied area carries a per-market
///     <see cref="CoverageArea.FuelCoverage" /> override, implementations use that circle for
///     the station search; provider errors or empty coverage degrade to a bulletin without fuel.
///     The seam exists so an AU state-government source (e.g. NSW FuelCheck) can replace HERE
///     without touching the pipeline.
/// </summary>
public interface IFuelPriceProvider
{
    Task<Result<FuelPriceReport>> GetFuelPricesAsync(
        CoverageArea coverage,
        CancellationToken cancellationToken = default);
}

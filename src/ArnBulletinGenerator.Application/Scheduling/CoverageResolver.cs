using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Resolves coverage information for orchestration requests by geocoding
///     the location when coverage is not explicitly provided.
/// </summary>
public sealed class CoverageResolver(
    IGeocodingProvider geocodingProvider,
    ILogger<CoverageResolver> logger) : ICoverageResolver
{
    /// <summary>
    ///     Ensures the <paramref name="dto" /> has coverage data, geocoding the location to fill it.
    ///     <para>
    ///     A blank location is REJECTED rather than defaulted. This method used to substitute a
    ///     hardcoded "Melbourne, VIC, Australia" here, which ran before
    ///     <c>OrchestrationValidation.Validate</c> and therefore made its "Location is required"
    ///     check unreachable — a caller who omitted the field silently received a fully generated
    ///     Melbourne bulletin (observed on dev 2026-07-28, three times).
    ///     </para>
    /// </summary>
    public async Task<Result<OrchestrationRequestDto>> ResolveCoverageAsync(
        OrchestrationRequestDto dto, CancellationToken ct)
    {
        if (dto.Coverage is not null)
            return dto;

        if (string.IsNullOrWhiteSpace(dto.Location))
            return Result<OrchestrationRequestDto>.Failure(
                "Location is required: name the station or place this bulletin is for.");

        logger.LogInformation("Coverage missing for '{Location}'. Geocoding...", dto.Location);
        var geoResult = await geocodingProvider.GeocodeAsync(dto.Location, cancellationToken: ct);
        if (geoResult.IsFailure)
            return Result<OrchestrationRequestDto>.Failure(
                $"Could not resolve location '{dto.Location}': {geoResult.Error}");

        var area = geoResult.Value;
        logger.LogInformation("Resolved location to {Lat}, {Lng}", area.Latitude, area.Longitude);
        return dto with { Coverage = new CoverageDto(area.Latitude, area.Longitude, area.RadiusKm) };
    }
}

using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Converts between the wire-level coverage DTO (shape discriminated by a
///     string) and the validated <see cref="CoverageArea" /> domain model.
/// </summary>
public static partial class CoverageMapping
{
    public static Result<CoverageArea> ToDomain(CoverageDto dto)
    {
        var shapeResult = ToShapeDomain(dto);
        if (shapeResult.IsFailure)
            return shapeResult;

        var weatherResult = ToSupplementaryDomain(dto.WeatherCoverage, "Weather");
        if (weatherResult.IsFailure)
            return Result<CoverageArea>.Failure(weatherResult.Error);

        var fuelResult = ToSupplementaryDomain(dto.FuelCoverage, "Fuel");
        if (fuelResult.IsFailure)
            return Result<CoverageArea>.Failure(fuelResult.Error);

        return Result<CoverageArea>.Success(shapeResult.Value with
        {
            WeatherCoverage = weatherResult.Value,
            FuelCoverage = fuelResult.Value
        });
    }

    private static Result<CoverageArea?> ToSupplementaryDomain(CoverageDto? dto, string sourceName)
    {
        if (dto is null)
            return Result<CoverageArea?>.Success(null);

        if (!string.IsNullOrWhiteSpace(dto.ShapeType)
            && !string.Equals(dto.ShapeType, "circle", StringComparison.OrdinalIgnoreCase))
        {
            return Result<CoverageArea?>.Failure(
                $"{sourceName} coverage must be a circle; its provider uses a single location.");
        }

        if (dto.WeatherCoverage is not null || dto.FuelCoverage is not null)
            return Result<CoverageArea?>.Failure(
                $"{sourceName} coverage cannot contain nested source coverage.");

        var result = ToShapeDomain(dto);
        return result.IsFailure
            ? Result<CoverageArea?>.Failure(result.Error)
            : Result<CoverageArea?>.Success(result.Value);
    }

    private static Result<CoverageArea> ToShapeDomain(CoverageDto dto)
    {
        switch (dto.ShapeType?.ToLowerInvariant())
        {
            case "bbox" or "boundingbox":
                if (dto is not { West: not null, South: not null, East: not null, North: not null })
                    return Result<CoverageArea>.Failure("A bbox coverage needs west, south, east and north.");
                return CoverageArea.CreateBoundingBox(dto.West.Value, dto.South.Value, dto.East.Value,
                    dto.North.Value);

            case "corridor":
                if (dto.CorridorPoints is not { Count: >= 2 })
                    return Result<CoverageArea>.Failure("A corridor coverage needs at least 2 points.");
                return CoverageArea.CreateCorridor(
                    dto.CorridorPoints.Select(p => new CoveragePoint(p.Lat, p.Lng)).ToList(),
                    dto.CorridorRadiusMeters ?? 1000);

            case "polygon":
                if (dto.PolygonPoints is not { Count: >= 3 })
                    return Result<CoverageArea>.Failure("A polygon coverage needs at least 3 points.");
                return CoverageArea.CreatePolygon(
                    dto.PolygonPoints.Select(p => new CoveragePoint(p.Lat, p.Lng)).ToList());

            case null or "" or "circle":
                return CoverageArea.Create(dto.Latitude, dto.Longitude, dto.RadiusKm);

            default:
                return Result<CoverageArea>.Failure(
                    $"Unknown coverage shape '{dto.ShapeType}'. Supported: circle, bbox, corridor, polygon.");
        }
    }

    public static CoverageDto ToDto(CoverageArea area)
    {
        return new CoverageDto(
            area.Latitude,
            area.Longitude,
            area.RadiusKm,
            area.ShapeType switch
            {
                CoverageShapeType.BoundingBox => "bbox",
                CoverageShapeType.Corridor => "corridor",
                CoverageShapeType.Polygon => "polygon",
                _ => "circle"
            },
            area.West,
            area.South,
            area.East,
            area.North,
            area.CorridorPoints?.Select(p => new CoveragePointDto(p.Lat, p.Lng)).ToList(),
            area.CorridorRadiusMeters,
            area.PolygonPoints?.Select(p => new CoveragePointDto(p.Lat, p.Lng)).ToList(),
            area.WeatherCoverage is { } weather ? ToDto(weather) : null,
            area.FuelCoverage is { } fuel ? ToDto(fuel) : null);
    }
}

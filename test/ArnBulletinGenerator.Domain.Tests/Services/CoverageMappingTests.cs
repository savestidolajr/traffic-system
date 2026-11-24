using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class CoverageMappingTests
{
    // ── Polygon round-trip ───────────────────────────────────────────

    [Fact]
    public void ToDomain_ToDto_PolygonRoundTrip_PreservesPoints()
    {
        var dto = new CoverageDto(
            Latitude: -33.87,
            Longitude: 151.21,
            RadiusKm: 10.0,
            ShapeType: "polygon",
            PolygonPoints: new List<CoveragePointDto>
            {
                new(-33.85, 151.20),
                new(-33.87, 151.22),
                new(-33.89, 151.20)
            });

        var domainResult = CoverageMapping.ToDomain(dto);

        domainResult.IsSuccess.ShouldBeTrue();
        var area = domainResult.Value;
        area.ShapeType.ShouldBe(CoverageShapeType.Polygon);
        area.PolygonPoints.ShouldNotBeNull();
        area.PolygonPoints.Count.ShouldBe(3);
        area.PolygonPoints[0].Lat.ShouldBe(-33.85);
        area.PolygonPoints[0].Lng.ShouldBe(151.20);

        // Round-trip back to DTO
        var roundDto = CoverageMapping.ToDto(area);

        roundDto.ShapeType.ShouldBe("polygon");
        roundDto.PolygonPoints.ShouldNotBeNull();
        roundDto.PolygonPoints.Count.ShouldBe(3);
        roundDto.PolygonPoints[0].Lat.ShouldBe(-33.85);
        roundDto.PolygonPoints[0].Lng.ShouldBe(151.20);
    }

    [Fact]
    public void ToDomain_PolygonWithTwoPoints_Fails()
    {
        var dto = new CoverageDto(
            Latitude: -33.87,
            Longitude: 151.21,
            RadiusKm: 10.0,
            ShapeType: "polygon",
            PolygonPoints: new List<CoveragePointDto>
            {
                new(-33.85, 151.20),
                new(-33.87, 151.22)
            });

        var result = CoverageMapping.ToDomain(dto);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("3");
    }

    [Fact]
    public void ToDomain_PolygonNullPoints_Fails()
    {
        var dto = new CoverageDto(
            Latitude: -33.87,
            Longitude: 151.21,
            RadiusKm: 10.0,
            ShapeType: "polygon");

        var result = CoverageMapping.ToDomain(dto);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("3");
    }

    [Fact]
    public void ToDomain_ToDto_CarriesSourceSpecificCircleOverrides()
    {
        var dto = new CoverageDto(
            Latitude: -16.92,
            Longitude: 145.77,
            RadiusKm: 80,
            ShapeType: "bbox",
            West: 144.9,
            South: -17.5,
            East: 146.4,
            North: -16.2,
            FuelCoverage: new CoverageDto(-16.92, 145.77, 12),
            WeatherCoverage: new CoverageDto(-16.91, 145.75, 1));

        var result = CoverageMapping.ToDomain(dto);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FuelCoverage!.RadiusKm.ShouldBe(12);
        result.Value.WeatherCoverage!.Longitude.ShouldBe(145.75);

        var roundTrip = CoverageMapping.ToDto(result.Value);
        roundTrip.FuelCoverage!.RadiusKm.ShouldBe(12);
        roundTrip.WeatherCoverage!.Latitude.ShouldBe(-16.91);
    }

    [Fact]
    public void ToDomain_RejectsNonCircleSourceSpecificCoverage()
    {
        var dto = new CoverageDto(
            -33.87,
            151.21,
            10,
            FuelCoverage: new CoverageDto(
                -33.87,
                151.21,
                10,
                ShapeType: "polygon",
                PolygonPoints:
                [
                    new(-33.8, 151.1),
                    new(-33.9, 151.2),
                    new(-33.8, 151.3)
                ]));

        var result = CoverageMapping.ToDomain(dto);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Fuel coverage must be a circle");
    }
}

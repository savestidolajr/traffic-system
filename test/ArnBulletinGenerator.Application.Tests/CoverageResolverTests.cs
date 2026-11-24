using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class CoverageResolverTests
{
    private readonly IGeocodingProvider _geocoding = Substitute.For<IGeocodingProvider>();
    private readonly CoverageResolver _sut;

    public CoverageResolverTests()
    {
        _sut = new CoverageResolver(_geocoding, Substitute.For<ILogger<CoverageResolver>>());
    }

    private static OrchestrationRequestDto MakeDto(
        CoverageDto? coverage = null,
        string? location = null)
    {
        return new OrchestrationRequestDto(45, location, coverage);
    }

    [Fact]
    public async Task ResolveCoverage_CoverageAlreadyPresent_ReturnsDtoUnchanged()
    {
        var existing = new CoverageDto(-37.8, 144.9, 5.0);
        var dto = MakeDto(existing);

        var result = await _sut.ResolveCoverageAsync(dto, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Coverage.ShouldBeSameAs(existing);
        await _geocoding.DidNotReceive()
            .GeocodeAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveCoverage_MissingCoverage_LocationProvided_GeocodesLocation()
    {
        var area = CoverageArea.Create(-33.8, 151.2, 5.0).Value;
        _geocoding.GeocodeAsync("Sydney, NSW", Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Result<CoverageArea>.Success(area));

        var dto = MakeDto(location: "Sydney, NSW");

        var result = await _sut.ResolveCoverageAsync(dto, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Coverage.ShouldNotBeNull();
        result.Value.Coverage!.Latitude.ShouldBe(-33.8);
        result.Value.Coverage!.Longitude.ShouldBe(151.2);
    }

    [Fact]
    public async Task ResolveCoverage_MissingCoverageAndLocation_IsRejected()
    {
        // This asserted a silent fallback to "Melbourne, VIC, Australia" until 2026-07-28. That made
        // sense when Melbourne was the only market; with 13 stations it meant a caller who omitted
        // the field received a fully generated MELBOURNE bulletin under a 202 (reproduced on dev
        // three times). It also made OrchestrationValidation's own "Location is required" check
        // unreachable, because this substitution ran first. The request is now refused.
        var dto = MakeDto();

        var result = await _sut.ResolveCoverageAsync(dto, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Location is required");
        await _geocoding.DidNotReceive()
            .GeocodeAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveCoverage_GeocodingFails_ReturnsFailure()
    {
        _geocoding.GeocodeAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<CancellationToken>())
            .Returns(Result<CoverageArea>.Failure("Service unavailable"));

        var dto = MakeDto(location: "Nowhere");

        var result = await _sut.ResolveCoverageAsync(dto, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Service unavailable");
    }
}

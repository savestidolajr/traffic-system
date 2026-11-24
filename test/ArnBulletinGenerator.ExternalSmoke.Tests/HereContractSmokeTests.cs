using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ArnBulletinGenerator.ExternalSmoke.Tests;

[Collection(ExternalSmokeCollection.Name)]
[Trait("Category", "ExternalSmoke")]
public sealed class HereContractSmokeTests(ExternalSmokeFixture fixture)
{
    [Fact]
    public async Task HereProviders_ParseKnownCoverageWithoutContractErrors()
    {
        if (!fixture.Enabled) return;
        var coverage = CoverageArea.Create(-28.0167, 153.4, 10).Value;

        var traffic = fixture.Services.GetRequiredService<ITrafficProvider>();
        var corridors = await traffic.GetCorridorsAsync(coverage, 3);
        corridors.IsSuccess.ShouldBeTrue(corridors.Error);

        var geocoding = fixture.Services.GetRequiredService<IGeocodingProvider>();
        var geocode = await geocoding.GeocodeAsync("Gold Coast Queensland", 10);
        geocode.IsSuccess.ShouldBeTrue(geocode.Error);

        var routing = fixture.Services.GetRequiredService<IRoutingProvider>();
        var route = await routing.GetDriveTimeAsync((-28.0836, 153.4072), (-27.9659, 153.4122));
        route.IsSuccess.ShouldBeTrue(route.Error);

        var weather = fixture.Services.GetRequiredService<IWeatherProvider>();
        var weatherResult = await weather.GetWeatherAsync(coverage);
        weatherResult.IsSuccess.ShouldBeTrue(weatherResult.Error);

        var fuel = fixture.Services.GetRequiredService<IFuelPriceProvider>();
        var fuelResult = await fuel.GetFuelPricesAsync(coverage);
        fuelResult.IsSuccess.ShouldBeTrue(fuelResult.Error);
    }
}

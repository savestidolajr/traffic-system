using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public sealed class HereFuelPriceProviderTests
{
    private const string TestApiKey = "fuel-test-key";
    private static readonly CoverageArea TestArea = CoverageArea.Create(-33.8688, 151.2093, 10.0).Value;

    private static FuelOptions DefaultFuelOptions(string? apiKey = TestApiKey)
    {
        return new FuelOptions
        {
            Enabled = true,
            BaseUrl = "https://fuel.hereapi.com/v3",
            FuelTypes = "36,1",
            MaxStations = 50,
            MaxRadiusKm = 30,
            ApiKey = apiKey ?? string.Empty,
            TimeoutSeconds = 15
        };
    }

    private static HereOptions DefaultHereOptions(string? apiKey = null)
    {
        return new HereOptions
        {
            ApiKey = apiKey ?? string.Empty
        };
    }

    private static HereFuelPriceProvider CreateProvider(
        HttpMessageHandler handler,
        FuelOptions? fuelOpts = null,
        HereOptions? hereOpts = null)
    {
        var httpClient = new HttpClient(handler);
        return new HereFuelPriceProvider(
            httpClient,
            Options.Create(fuelOpts ?? DefaultFuelOptions()),
            Options.Create(hereOpts ?? DefaultHereOptions()),
            NullLogger<HereFuelPriceProvider>.Instance);
    }

    private static string BuildStationsJson(params StationData[] stations)
    {
        return JsonSerializer.Serialize(new
        {
            stations = stations.Select(s => new
            {
                id = s.Id,
                address = s.District is not null ? new { district = s.District } : null,
                prices = s.Prices.Select(p => new
                {
                    fuelType = p.FuelType,
                    price = p.Price,
                    currency = p.Currency ?? "AUD",
                    unit = p.Unit ?? "L",
                    available = p.Available,
                    modified = p.Modified
                }).ToArray()
            }).ToArray()
        });
    }

    private sealed record StationData(string Id, PriceData[] Prices, string? District = null);
    private sealed record PriceData(string FuelType, double Price, string? Currency = null, string? Unit = null, bool? Available = null, DateTimeOffset? Modified = null);

    // ── Success ────────────────────────────────────────────────────

    [Fact]
    public async Task GetFuelPricesAsync_AggregatesCorrectly_TwoStationsTwoTypes()
    {
        var json = BuildStationsJson(
            new StationData("station-1", [
                new PriceData("36", 1.849),
                new PriceData("1", 1.749)
            ]),
            new StationData("station-2", [
                new PriceData("36", 1.879),
                new PriceData("1", 1.789)
            ]));

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasPrices.ShouldBeTrue();
        result.Value.Summaries.Count.ShouldBe(2);

        // Order follows options.FuelTypes: "36" then "1"
        var u91 = result.Value.Summaries[0];
        u91.FuelType.ShouldBe("Unleaded 91");
        u91.MinPrice.ShouldBe(1.849);
        u91.AveragePrice.ShouldBe(1.864); // (1.849 + 1.879) / 2 = 1.864
        u91.StationCount.ShouldBe(2);

        var diesel = result.Value.Summaries[1];
        diesel.FuelType.ShouldBe("Diesel");
        diesel.MinPrice.ShouldBe(1.749);
        diesel.AveragePrice.ShouldBe(1.769); // (1.749 + 1.789) / 2 = 1.769
        diesel.StationCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetFuelPricesAsync_CarriesLatestModifiedTimestampPerFuelType()
    {
        var older = DateTimeOffset.Parse("2026-06-17T01:00:00Z");
        var newer = DateTimeOffset.Parse("2026-06-17T03:30:00Z");
        var json = BuildStationsJson(
            new StationData("station-1", [
                new PriceData("36", 1.849, Modified: older)
            ]),
            new StationData("station-2", [
                new PriceData("36", 1.879, Modified: newer)
            ]));

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summaries[0].LatestModifiedUtc.ShouldBe(newer);
    }

    [Fact]
    public async Task GetFuelPricesAsync_OptionsOrder_Respected()
    {
        // Reversed fuel type order in options
        var json = BuildStationsJson(
            new StationData("station-1", [
                new PriceData("36", 1.849),
                new PriceData("1", 1.749)
            ]));

        var opts = DefaultFuelOptions();
        opts.FuelTypes = "1,36";
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler, fuelOpts: opts);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summaries.Count.ShouldBe(2);
        result.Value.Summaries[0].FuelType.ShouldBe("Diesel");
        result.Value.Summaries[1].FuelType.ShouldBe("Unleaded 91");
    }

    // ── Empty coverage ─────────────────────────────────────────────

    [Fact]
    public async Task GetFuelPricesAsync_EmptyStations_ReturnsEmptyReport()
    {
        var json = """{"stations": []}""";
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasPrices.ShouldBeFalse();
        result.Value.Summaries.ShouldBeEmpty();
    }

    // ── Edge cases ─────────────────────────────────────────────────

    [Fact]
    public async Task GetFuelPricesAsync_UnavailablePrices_Excluded()
    {
        var json = BuildStationsJson(
            new StationData("station-1", [
                new PriceData("36", 1.849, Available: true),
                new PriceData("36", 1.899, Available: false)
            ]));

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summaries.Count.ShouldBe(1);
        result.Value.Summaries[0].MinPrice.ShouldBe(1.849);
        result.Value.Summaries[0].StationCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetFuelPricesAsync_ZeroPrices_Excluded()
    {
        var json = BuildStationsJson(
            new StationData("station-1", [
                new PriceData("36", 0.0),
                new PriceData("36", 1.849)
            ]));

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summaries.Count.ShouldBe(1);
        result.Value.Summaries[0].MinPrice.ShouldBe(1.849);
    }

    [Fact]
    public async Task GetFuelPricesAsync_UnknownFuelType_MappedWithFallbackName()
    {
        var json = BuildStationsJson(
            new StationData("station-1", [
                new PriceData("99", 2.049)
            ]));

        var opts = DefaultFuelOptions();
        opts.FuelTypes = "99";
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler, fuelOpts: opts);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summaries.Count.ShouldBe(1);
        result.Value.Summaries[0].FuelType.ShouldBe("Fuel type 99");
    }

    // ── Error cases ────────────────────────────────────────────────

    [Fact]
    public async Task GetFuelPricesAsync_HttpError_ReturnsFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, "server error");
        var provider = CreateProvider(handler);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("500");
    }

    [Fact]
    public async Task GetFuelPricesAsync_NoApiKey_ReturnsFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        var provider = CreateProvider(handler,
            fuelOpts: DefaultFuelOptions(apiKey: ""),
            hereOpts: DefaultHereOptions(apiKey: ""));

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("API key");
    }

    // ── District / suburb ──────────────────────────────────────────

    [Fact]
    public async Task GetFuelPricesAsync_CapturesCheapestStationSuburb()
    {
        var json = BuildStationsJson(
            new StationData("cheap-stn", [
                new PriceData("36", 1.65)
            ], District: "Paddington"),
            new StationData("expensive-stn", [
                new PriceData("36", 1.89)
            ], District: "Woolloomooloo"));

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summaries.Count.ShouldBe(1);
        result.Value.Summaries[0].MinPrice.ShouldBe(1.65);
        result.Value.Summaries[0].CheapestStationSuburb.ShouldBe("Paddington");
    }

    [Fact]
    public async Task GetFuelPricesAsync_NoAddress_SuburbIsNull()
    {
        var json = BuildStationsJson(
            new StationData("station-1", [
                new PriceData("36", 1.849)
            ]),
            new StationData("station-2", [
                new PriceData("36", 1.879)
            ]));

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetFuelPricesAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summaries.Count.ShouldBe(1);
        result.Value.Summaries[0].CheapestStationSuburb.ShouldBeNull();
    }

    [Fact]
    public async Task GetFuelPricesAsync_UsesFuelCoverageOverride()
    {
        var json = BuildStationsJson(
            new StationData("station-1", [new PriceData("36", 1.65)]));
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);
        var fuelArea = CoverageArea.Create(-33.95, 151.05, 4).Value;

        var result = await provider.GetFuelPricesAsync(TestArea with { FuelCoverage = fuelArea });

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequestUri.ShouldNotBeNull();
        handler.LastRequestUri!.Query.ShouldContain("circle:-33.950000,151.050000;r=4000");
    }

    // ── FakeHandler ────────────────────────────────────────────────

    private sealed class FakeHandler(HttpStatusCode statusCode, string content)
        : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });
        }
    }
}

using System.Globalization;
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

public sealed class HereWeatherProviderTests
{
    private const string TestApiKey = "weather-test-key";
    private static readonly CoverageArea TestArea = CoverageArea.Create(-33.8688, 151.2093, 10.0).Value;

    private static WeatherOptions DefaultWeatherOptions(string? apiKey = TestApiKey)
    {
        return new WeatherOptions
        {
            Enabled = true,
            BaseUrl = "https://weather.hereapi.com/v3",
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

    private static HereWeatherProvider CreateProvider(
        HttpMessageHandler handler,
        WeatherOptions? weatherOpts = null,
        HereOptions? hereOpts = null)
    {
        var httpClient = new HttpClient(handler);
        return new HereWeatherProvider(
            httpClient,
            Options.Create(weatherOpts ?? DefaultWeatherOptions()),
            Options.Create(hereOpts ?? DefaultHereOptions()),
            NullLogger<HereWeatherProvider>.Instance);
    }

    private static string BuildSuccessResponse(
        string description = "Mostly sunny",
        double temperature = 22.5,
        string? windDesc = "S",
        double windSpeed = 15.0,
        int? precipitationProbability = 10,
        string[]? alertDescriptions = null)
    {
        var obs = new
        {
            description,
            temperature,
            windDesc,
            windSpeed,
            precipitationProbability
        };

        var alerts = (alertDescriptions ?? [])
            .Select(a => new { description = a })
            .ToArray();

        return JsonSerializer.Serialize(new
        {
            places = new[]
            {
                new
                {
                    observations = new[] { obs }
                }
            },
            alerts
        });
    }

    // ── Success ────────────────────────────────────────────────────

    [Fact]
    public async Task GetWeatherAsync_ReturnsWeatherReport_OnSuccess()
    {
        var json = BuildSuccessResponse(
            description: "Partly cloudy",
            temperature: 18.3,
            windDesc: "SE",
            windSpeed: 25.0,
            precipitationProbability: 30);

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        var report = result.Value;
        report.Conditions.ShouldBe("Partly cloudy");
        report.TemperatureC.ShouldBe(18.3);
        report.PrecipitationProbabilityPercent.ShouldBe(30);
    }

    [Fact]
    public async Task GetWeatherAsync_MapsAlerts_CappedAt3()
    {
        var json = BuildSuccessResponse(
            windSpeed: 10,
            alertDescriptions: ["Alert A", "Alert B", "Alert C", "Alert D"]);

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Alerts.Count.ShouldBe(3);
        result.Value.Alerts.ShouldContain("Alert A");
        result.Value.Alerts.ShouldContain("Alert B");
        result.Value.Alerts.ShouldContain("Alert C");
        result.Value.Alerts.ShouldNotContain("Alert D");
    }

    [Fact]
    public async Task GetWeatherAsync_ConditionsTemperatureAndAlerts_Mapped()
    {
        var json = BuildSuccessResponse(
            description: "Thunderstorms",
            temperature: 28.0,
            windDesc: "W",
            windSpeed: 35.0,
            precipitationProbability: 80,
            alertDescriptions: ["Severe thunderstorm warning"]);

        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        var report = result.Value;
        report.Conditions.ShouldBe("Thunderstorms");
        report.TemperatureC.ShouldBe(28.0);
        report.PrecipitationProbabilityPercent.ShouldBe(80);
        report.Alerts.Count.ShouldBe(1);
        report.Alerts[0].ShouldBe("Severe thunderstorm warning");
    }

    // ── WindDescription ────────────────────────────────────────────

    [Fact]
    public async Task GetWeatherAsync_WindBelowThreshold_NoWindDescription()
    {
        var json = BuildSuccessResponse(windDesc: "N", windSpeed: 15.0);
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WindDescription.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetWeatherAsync_WindAtOrAboveThreshold_HasQualitativeWindDescription()
    {
        var json = BuildSuccessResponse(windDesc: "SE", windSpeed: 40.0);
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WindDescription.HasValue.ShouldBeTrue();
        result.Value.WindDescription.Value.ShouldContain("SE");
        result.Value.WindDescription.Value.ShouldContain("blustery");
        result.Value.WindDescription.Value.ShouldNotContain("km/h");
    }

    // ── Error cases ────────────────────────────────────────────────

    [Fact]
    public async Task GetWeatherAsync_MissingObservation_ReturnsFailure()
    {
        var json = """{"places": [{"observations": []}]}""";
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("no observation");
    }

    [Fact]
    public async Task GetWeatherAsync_NoObservationButForecast_FallsBackToForecast()
    {
        // Sparse regional coverage: no current observation, but a daily forecast exists. Fall back
        // to the forecast so weather still airs — flagged IsForecast so it is voiced truthfully.
        const string json =
            """{"places": [{"observations": [], "dailyForecasts": [{"forecasts": [{"description": "Sunny", "highTemperature": "25"}]}]}]}""";
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsForecast.ShouldBeTrue();
        result.Value.Conditions.ShouldBe("Sunny");
        result.Value.TemperatureC.ShouldBe(25);
    }

    [Fact]
    public async Task GetWeatherAsync_Http429_ReturnsFailureWithStatus()
    {
        var handler = new FakeHandler(HttpStatusCode.TooManyRequests, "rate limited");
        var provider = CreateProvider(handler);

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("429");
    }

    [Fact]
    public async Task GetWeatherAsync_NoApiKey_ReturnsFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "{}");
        // No HTTP call should happen — we never pass the handler
        var provider = CreateProvider(handler,
            weatherOpts: DefaultWeatherOptions(apiKey: ""),
            hereOpts: DefaultHereOptions(apiKey: ""));

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("API key");
    }

    [Fact]
    public async Task GetWeatherAsync_FallbackToHereApiKey_Succeeds()
    {
        var json = BuildSuccessResponse(windSpeed: 5);
        var handler = new FakeHandler(HttpStatusCode.OK, json);
        var provider = CreateProvider(handler,
            weatherOpts: DefaultWeatherOptions(apiKey: ""),
            hereOpts: DefaultHereOptions(apiKey: TestApiKey));

        var result = await provider.GetWeatherAsync(TestArea);

        result.IsSuccess.ShouldBeTrue();
    }

    // ── FakeHandler ────────────────────────────────────────────────

    [Fact]
    public async Task GetWeatherAsync_UsesWeatherCoverageOverride()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, BuildSuccessResponse());
        var provider = CreateProvider(handler);
        var weatherArea = CoverageArea.Create(-33.95, 151.05, 1).Value;

        var result = await provider.GetWeatherAsync(TestArea with { WeatherCoverage = weatherArea });

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequestUri.ShouldNotBeNull();
        handler.LastRequestUri!.Query.ShouldContain("location=-33.950000,151.050000");
    }

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

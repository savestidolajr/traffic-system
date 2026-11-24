using System.Globalization;
using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public class HereRoutingProviderTests
{
    private readonly MockHttpMessageHandler _mockHandler = new();
    private readonly ILogger<HereRoutingProvider> _logger = Substitute.For<ILogger<HereRoutingProvider>>();
    private readonly HereRoutingProvider _provider;

    public HereRoutingProviderTests()
    {
        var httpClient = new HttpClient(_mockHandler);
        var options = Options.Create(new HereOptions { ApiKey = "test_key" });
        _provider = new HereRoutingProvider(httpClient, options, _logger);
    }

    [Fact]
    public async Task GetDriveTimeAsync_ValidResponse_ReturnsDurationAndDistance()
    {
        _mockHandler.SetResponse(BuildRoutingJson(1800, 25000, 1500), HttpStatusCode.OK);

        var result = await _provider.GetDriveTimeAsync((-33.87, 151.21), (-33.85, 151.19));

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationMinutes.ShouldBe(30);
        result.Value.TypicalDurationMinutes.ShouldBe(25);
        result.Value.DistanceKm.ShouldBe(25.0);
        result.Value.DelayMinutes.ShouldBe(5);
    }

    [Fact]
    public async Task GetDriveTimeAsync_WithoutTypicalDuration_UsesBaseDuration()
    {
        _mockHandler.SetResponse(BuildRoutingJson(600, 5000, null), HttpStatusCode.OK);

        var result = await _provider.GetDriveTimeAsync((-33.87, 151.21), (-33.85, 151.19));

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationMinutes.ShouldBe(10);
        result.Value.TypicalDurationMinutes.ShouldBe(10);
        result.Value.DelayMinutes.ShouldBe(0);
    }

    [Fact]
    public async Task GetDriveTimeAsync_HttpError_ReturnsFailure()
    {
        _mockHandler.SetResponse("error", HttpStatusCode.InternalServerError);

        var result = await _provider.GetDriveTimeAsync((-33.87, 151.21), (-33.85, 151.19));

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GetDriveTimeAsync_BadRequest_LogsHereReasonWithRedactedApiKey()
    {
        _mockHandler.SetResponse(
            """{"title":"Invalid request","status":400,"cause":"spans requested but no polyline requested"}""",
            HttpStatusCode.BadRequest);

        var result = await _provider.GetDriveTimeAsync((-33.87, 151.21), (-33.85, 151.19));

        result.IsFailure.ShouldBeTrue();
        var call = _logger.ReceivedCalls().Single(c => c.GetArguments()[0] is LogLevel.Error);
        string message = call.GetArguments()[2]!.ToString()!;
        // HERE's own error body is carried into the log so the next failure self-explains…
        message.ShouldContain("Status: BadRequest");
        message.ShouldContain("spans requested but no polyline requested");
        // …and the apiKey is redacted in both the URL and any echoed response body.
        message.ShouldContain("apiKey=***");
        message.ShouldNotContain("test_key");
    }

    [Fact]
    public async Task GetDriveTimeAsync_NoRoute_ReturnsFailure()
    {
        _mockHandler.SetResponse("""{"routes": []}""", HttpStatusCode.OK);

        var result = await _provider.GetDriveTimeAsync((-33.87, 151.21), (-33.85, 151.19));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("No route found");
    }

    [Fact]
    public async Task GetDriveTimeAsync_NoSummary_ReturnsFailure()
    {
        string json = JsonSerializer.Serialize(new
        {
            routes = new[]
            {
                new { sections = new[] { new { summary = (object?)null } } }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.GetDriveTimeAsync((-33.87, 151.21), (-33.85, 151.19));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("No route found");
    }

    [Fact]
    public async Task GetDriveTimeAsync_CeilsPartialMinutes()
    {
        _mockHandler.SetResponse(BuildRoutingJson(90, 1000, 90), HttpStatusCode.OK);

        var result = await _provider.GetDriveTimeAsync((-33.87, 151.21), (-33.85, 151.19));

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationMinutes.ShouldBe(2);
    }

    [Fact]
    public async Task GetDriveTimeAsync_UsesInvariantCulture_ForLatLng()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            _mockHandler.SetResponse(BuildRoutingJson(1800, 25000, 1500), HttpStatusCode.OK);

            await _provider.GetDriveTimeAsync((-33.87, 151.21), (-33.85, 151.19));

            _mockHandler.LastRequestUrl.ShouldNotBeNull();
            _mockHandler.LastRequestUrl.ShouldContain("-33.87");
            _mockHandler.LastRequestUrl.ShouldContain("151.21");
            // Under de-DE, a non-invariant format would produce comma decimals
            _mockHandler.LastRequestUrl.ShouldNotContain("-33,87");
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }
    }

    [Fact]
    public async Task GetSafeDetourAsync_AvoidsSegmentsAndReturnsFasterRoute()
    {
        _mockHandler.SetResponse(BuildRoutingJson(1200, 22000, null), HttpStatusCode.OK);

        var result = await _provider.GetSafeDetourAsync(
            (-33.87, 151.21),
            (-33.85, 151.19),
            ["here:cm:segment:207551710#+"],
            new DriveTimeResult(30, 20, 20.0));

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationMinutes.ShouldBe(20);
        result.Value.SavedMinutes.ShouldBe(10);
        string url = _mockHandler.LastRequestUrl!;
        url.ShouldContain("alternatives=2");
        url.ShouldContain("avoid");
        url.ShouldContain("here%3Acm%3Asegment%3A207551710");
    }

    [Fact]
    public async Task GetSafeDetourAsync_RequestsPolylineWithSpans()
    {
        _mockHandler.SetResponse(BuildRoutingJson(1200, 22000, null), HttpStatusCode.OK);

        var result = await _provider.GetSafeDetourAsync(
            (-33.87, 151.21),
            (-33.85, 151.19),
            ["here:cm:segment:207551710#+"],
            new DriveTimeResult(30, 20, 20.0));

        result.IsSuccess.ShouldBeTrue();
        string url = _mockHandler.LastRequestUrl!;
        // HERE Routing v8 rejects spans=names unless return also includes polyline
        // (E605013 "Spans requested but no polyline requested") — a plain return=summary
        // 400s every detour request.
        url.ShouldContain("return=summary,polyline");
        url.ShouldContain("spans=names");
    }

    [Fact]
    public async Task GetSafeDetourAsync_RejectsSlowerAlternatives()
    {
        _mockHandler.SetResponse(BuildRoutingJson(2100, 22000, null), HttpStatusCode.OK);

        var result = await _provider.GetSafeDetourAsync(
            (-33.87, 151.21),
            (-33.85, 151.19),
            ["here:cm:segment:207551710#+"],
            new DriveTimeResult(30, 20, 20.0));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("No safe faster detour");
    }

    private static string BuildRoutingJson(int duration, int length, int? typicalDuration)
    {
        return JsonSerializer.Serialize(new
        {
            routes = new[]
            {
                new
                {
                    sections = new[]
                    {
                        new
                        {
                            summary = new
                            {
                                duration,
                                length,
                                typicalDuration
                            }
                        }
                    }
                }
            }
        });
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public string? LastRequestUrl { get; private set; }
        private string _content = "";
        private HttpStatusCode _statusCode = HttpStatusCode.OK;

        public void SetResponse(string content, HttpStatusCode statusCode)
        {
            _content = content;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUrl = request.RequestUri?.ToString();
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = new StringContent(_content)
            });
        }
    }
}

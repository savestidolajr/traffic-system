using System.Globalization;
using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public class HereGeocodingProviderTests
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly HereGeocodingProvider _provider;

    public HereGeocodingProviderTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_mockHandler);

        var options = Options.Create(new HereOptions
        {
            ApiKey = "test_key",
            BatchReverseGeocodeUrl = "https://test.api/batch",
            UseBatchGeocoding = true
        });

        var logger = Substitute.For<ILogger<HereGeocodingProvider>>();

        _provider = new HereGeocodingProvider(httpClient, options, logger);
    }

    [Fact]
    public async Task BatchReverseGeocodeAsync_ShouldReturnMappedAddresses()
    {
        // Arrange
        var coordinates = new[]
        {
            (52.530, 13.385),
            (48.856, 2.352)
        };

        // Construct response
        // Match new API response: { "results": [{ "id": "0", "items": [{"address":{...}}] }] }
        var responseContent = new
        {
            results = new object[]
            {
                new
                {
                    id = "0",
                    items = new object[]
                    {
                        new
                        {
                            address = new { label = "Berlin Label", street = (string?)null }
                        }
                    }
                },
                new
                {
                    id = "1",
                    items = new object[]
                    {
                        new
                        {
                            address = new { label = (string?)null, street = "Paris Street" }
                        }
                    }
                }
            }
        };

        _mockHandler.SetResponse(JsonSerializer.Serialize(responseContent), HttpStatusCode.OK);

        // Act
        var result = await _provider.BatchReverseGeocodeAsync(coordinates);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var map = result.Value;
        map.Count().ShouldBe(2);
        map[0].ShouldBe("Berlin Label");
        map[1].ShouldBe("Paris Street");
    }

    [Fact]
    public async Task GeocodeAsync_ValidResponse_ReturnsCoverageArea()
    {
        string json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    position = new { lat = -33.8688, lng = 151.2093 },
                    address = new { label = "Sydney, NSW, Australia", street = (string?)null }
                }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.GeocodeAsync("Sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Latitude.ShouldBe(-33.8688, 0.001);
        result.Value.Longitude.ShouldBe(151.2093, 0.001);
        result.Value.RadiusKm.ShouldBe(5.0);
    }

    [Fact]
    public async Task GeocodeAsync_NoResults_ReturnsFailure()
    {
        _mockHandler.SetResponse("""{"items": []}""", HttpStatusCode.OK);

        var result = await _provider.GeocodeAsync("nonexistent place");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("nonexistent place");
    }

    [Fact]
    public async Task GeocodeAsync_HttpError_ReturnsFailure()
    {
        _mockHandler.SetResponse("error", HttpStatusCode.InternalServerError);

        var result = await _provider.GeocodeAsync("Sydney");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task ReverseGeocodeAsync_ValidResponse_ReturnsStreetName()
    {
        string json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    position = new { lat = -33.87, lng = 151.21 },
                    address = new { label = "Sydney, NSW", street = "George Street" }
                }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.ReverseGeocodeAsync(-33.87, 151.21);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("George Street");
    }

    /// <summary>
    ///     Issue 2. When HERE's "street" is a bare house number or a route code, the old fallback
    ///     substituted the DISTRICT and returned it as the location — so "Arundel", "Greensborough"
    ///     and "Laverton" were all voiced as roads. A locality must come back as a suburb, and the
    ///     road name must come back absent, so nothing downstream can name a road HERE never named.
    /// </summary>
    [Fact]
    public async Task BatchReverseGeocodePlacesAsync_ReturnsLocalityAsSuburb_NeverAsARoadName()
    {
        string json = JsonSerializer.Serialize(new
        {
            results = new[]
            {
                new
                {
                    id = "0",
                    items = new[]
                    {
                        new
                        {
                            position = new { lat = -27.94, lng = 153.36 },
                            address = new { label = "10, Arundel QLD 4214, Australia", street = "10", district = "Arundel" }
                        }
                    }
                }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.BatchReverseGeocodePlacesAsync([(-27.94, 153.36)]);

        result.IsSuccess.ShouldBeTrue();
        var place = result.Value[0];
        place.HasRoadName.ShouldBeFalse();
        place.Suburb.GetValueOr("").ShouldBe("Arundel");
    }

    /// <summary>A real street name still comes back as the road, with its suburb alongside.</summary>
    [Fact]
    public async Task BatchReverseGeocodePlacesAsync_ReturnsBothWhenHereNamesTheStreet()
    {
        string json = JsonSerializer.Serialize(new
        {
            results = new[]
            {
                new
                {
                    id = "0",
                    items = new[]
                    {
                        new
                        {
                            position = new { lat = -33.86676, lng = 150.84729 },
                            address = new
                            {
                                label = "Wallgrove Rd, Cecil Park NSW 2178, Australia",
                                street = "Wallgrove Rd",
                                district = "Cecil Park"
                            }
                        }
                    }
                }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.BatchReverseGeocodePlacesAsync([(-33.86676, 150.84729)]);

        var place = result.Value[0];
        place.RoadName.GetValueOr("").ShouldBe("Wallgrove Rd");
        place.Suburb.GetValueOr("").ShouldBe("Cecil Park");
    }

    [Fact]
    public async Task ReverseGeocodeAsync_NoStreet_FallsBackToLabel()
    {
        string json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    position = new { lat = -33.87, lng = 151.21 },
                    address = new { label = "Sydney, NSW, Australia", street = (string?)null }
                }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.ReverseGeocodeAsync(-33.87, 151.21);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Sydney, NSW, Australia");

    }

    [Fact]
    public async Task ReverseGeocodeAsync_RouteCodeStreet_FallsBackToLabel()
    {
        // Live bug: Port Augusta B56 (Horrocks Pass Road) was returned as the bare
        // route code because the old heuristic (street.Any(char.IsLetter)) treated
        // "B56" as a valid named street.
        string json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    position = new { lat = -32.49, lng = 137.76 },
                    address = new { label = "Horrocks Pass Road, Willowie SA, Australia", street = "B56" }
                }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.ReverseGeocodeAsync(-32.49, 137.76);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Horrocks Pass Road, Willowie SA, Australia");
    }

    [Fact]
    public async Task ReverseGeocodeAsync_AlphaRouteCode_A1_FallsBackToLabel()
    {
        string json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    position = new { lat = -32.49, lng = 137.76 },
                    address = new { label = "Augusta Hwy, Port Augusta SA", street = "A1" }
                }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.ReverseGeocodeAsync(-32.49, 137.76);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Augusta Hwy, Port Augusta SA");
    }

    [Fact]
    public async Task ReverseGeocodeAsync_BareNumericStreet_FallsBackToLabel()
    {
        string json = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    position = new { lat = -33.87, lng = 151.21 },
                    address = new { label = "Sydney, NSW, Australia", street = "99" }
                }
            }
        });
        _mockHandler.SetResponse(json, HttpStatusCode.OK);

        var result = await _provider.ReverseGeocodeAsync(-33.87, 151.21);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("Sydney, NSW, Australia");
    }

    [Fact]
    public async Task ReverseGeocodeAsync_UsesInvariantCulture_ForLatLng()
    {
        var savedCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            string json = JsonSerializer.Serialize(new
            {
                items = new[]
                {
                    new
                    {
                        position = new { lat = -33.87, lng = 151.21 },
                        address = new { label = "Sydney, NSW", street = "George Street" }
                    }
                }
            });
            _mockHandler.SetResponse(json, HttpStatusCode.OK);

            await _provider.ReverseGeocodeAsync(-33.87, 151.21);

            _mockHandler.LastRequestUrl.ShouldNotBeNull();
            _mockHandler.LastRequestUrl.ShouldContain("-33.87");
            _mockHandler.LastRequestUrl.ShouldContain("151.21");
            // Under de-DE, a non-invariant format would produce comma decimals
            _mockHandler.LastRequestUrl.ShouldNotContain("-33,87");
            _mockHandler.LastRequestUrl.ShouldNotContain("151,21");
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }
    }

    [Fact]
    public async Task BatchReverseGeocodeAsync_BatchApiFails_FallsBackToSequential()
    {
        // Arrange — batch POST returns 403, but individual reverse-geocode GETs succeed.
        // Route on lat values to distinguish the two sequential calls.
        var coordinates = new[] { (-33.87, 151.21), (-37.81, 144.96) };

        var revGeoResponse1 = JsonSerializer.Serialize(new
        {
            items = new[] { new { address = new { label = "Sydney NSW", street = (string?)null } } }
        });
        var revGeoResponse2 = JsonSerializer.Serialize(new
        {
            items = new[] { new { address = new { label = "Melbourne VIC", street = (string?)null } } }
        });

        _mockHandler.SetRouteResponse("/batch", "Forbidden", HttpStatusCode.Forbidden);
        _mockHandler.SetRouteResponse("-33.87", revGeoResponse1, HttpStatusCode.OK);
        _mockHandler.SetRouteResponse("-37.81", revGeoResponse2, HttpStatusCode.OK);

        // Act
        var result = await _provider.BatchReverseGeocodeAsync(coordinates);

        // Assert — overall result is success despite batch failure
        result.IsSuccess.ShouldBeTrue();
        var map = result.Value;
        map.Count.ShouldBe(2);
        map[0].ShouldBe("Sydney NSW");
        map[1].ShouldBe("Melbourne VIC");

        // Prove the batch URL was actually attempted (fell back, not skipped)
        _mockHandler.RequestedUrls.ShouldContain(u => u.Contains("batch"));
    }

    [Fact]
    public async Task BatchReverseGeocodeAsync_BatchApiThrows_FallsBackToSequential()
    {
        // HereApiHelper.ExecutePostAsync catches exceptions into Result.Failure,
        // so BatchReverseGeocodeAsync should degrade to sequential, not propagate.
        var coordinates = new[] { (-33.87, 151.21) };

        var revGeoResponse = JsonSerializer.Serialize(new
        {
            items = new[] { new { address = new { label = "Sydney NSW", street = "Main Street" } } }
        });

        // Set up routes: batch returns 500 (HereApiHelper wraps into Result.Failure),
        // sequential returns valid data
        _mockHandler.SetRouteResponse("/batch", "Server Error", HttpStatusCode.InternalServerError);
        _mockHandler.SetRouteResponse("-33.87", revGeoResponse, HttpStatusCode.OK);

        // Act — must NOT throw; fallback is graceful
        var result = await _provider.BatchReverseGeocodeAsync(coordinates);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        var map = result.Value;
        map.Count.ShouldBe(1);
        map[0].ShouldBe("Main Street");

        // Batch was attempted
        _mockHandler.RequestedUrls.ShouldContain(u => u.Contains("batch"));
    }

    [Fact]
    public async Task BatchReverseGeocodeAsync_UseBatchGeocodingDisabled_SkipsBatchCallEntirely()
    {
        // Arrange — provider with UseBatchGeocoding = false
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new HereOptions
        {
            ApiKey = "test_key",
            UseBatchGeocoding = false
        });
        var logger = Substitute.For<ILogger<HereGeocodingProvider>>();
        var provider = new HereGeocodingProvider(httpClient, options, logger);

        var coordinates = new[] { (-33.87, 151.21), (-37.81, 144.96) };

        var revGeoResponse1 = JsonSerializer.Serialize(new
        {
            items = new[] { new { address = new { label = "Sydney NSW", street = (string?)null } } }
        });
        var revGeoResponse2 = JsonSerializer.Serialize(new
        {
            items = new[] { new { address = new { label = "Melbourne VIC", street = (string?)null } } }
        });

        // If the batch URL is hit, return 403. Sequential calls return valid data.
        handler.SetRouteResponse("/batch", "Forbidden", HttpStatusCode.Forbidden);
        handler.SetRouteResponse("-33.87", revGeoResponse1, HttpStatusCode.OK);
        handler.SetRouteResponse("-37.81", revGeoResponse2, HttpStatusCode.OK);

        // Act
        var result = await provider.BatchReverseGeocodeAsync(coordinates);

        // Assert — result comes from sequential path, batch was never touched
        result.IsSuccess.ShouldBeTrue();
        var map = result.Value;
        map.Count.ShouldBe(2);
        map[0].ShouldBe("Sydney NSW");
        map[1].ShouldBe("Melbourne VIC");

        // Prove the batch URL was never hit
        handler.RequestedUrls.ShouldNotContain(u => u.Contains("batch"));
        handler.RequestedUrls.ShouldAllBe(u => u.Contains("revgeocode"));
    }

    [Fact]
    public async Task BatchReverseGeocodeAsync_CoalescesDuplicateCoordinates()
    {
        _mockHandler.SetResponse(
            """
            {"results":[
              {"id":"0","items":[{"address":{"label":"Sydney","street":null}}]},
              {"id":"1","items":[{"address":{"label":"Melbourne","street":null}}]}
            ]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.BatchReverseGeocodeAsync([
            (-33.87, 151.21),
            (-33.87, 151.21),
            (-37.81, 144.96)
        ]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(3);
        result.Value[0].ShouldBe("Sydney");
        result.Value[1].ShouldBe("Sydney");
        result.Value[2].ShouldBe("Melbourne");
    }

    [Fact]
    public async Task BatchReverseGeocodeAsync_BatchFailure_FallsBackToAtMostTwentyCoordinates()
    {
        _mockHandler.SetResponse(
            """{"items":[{"address":{"label":"Fallback place","street":null}}]}""",
            HttpStatusCode.OK);
        _mockHandler.SetRouteResponse("/batch", "Forbidden", HttpStatusCode.Forbidden);

        var coordinates = Enumerable.Range(0, 25)
            .Select(index => (-33.87 + index * 0.001, 151.21 + index * 0.001));
        var result = await _provider.BatchReverseGeocodeAsync(coordinates);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(20);
        _mockHandler.RequestedUrls.Count.ShouldBeLessThanOrEqualTo(21);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public string? LastRequestUrl { get; private set; }
        public List<string> RequestedUrls { get; } = new();
        private string _content = "";
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private readonly Dictionary<string, (string Content, HttpStatusCode StatusCode)> _routeResponses = new();

        public void SetResponse(string content, HttpStatusCode statusCode)
        {
            _content = content;
            _statusCode = statusCode;
        }

        /// <summary>
        ///     Registers a response for requests whose URL contains <paramref name="urlSubstring"/>.
        ///     When multiple routes match, the longest (most specific) substring wins.
        /// </summary>
        public void SetRouteResponse(string urlSubstring, string content, HttpStatusCode statusCode)
        {
            _routeResponses[urlSubstring] = (content, statusCode);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            LastRequestUrl = url;
            lock (RequestedUrls)
                RequestedUrls.Add(url);

            // Route-specific response takes priority over the default.
            string? bestMatch = null;
            foreach (var route in _routeResponses.Keys)
            {
                if (url.Contains(route) && (bestMatch is null || route.Length > bestMatch.Length))
                    bestMatch = route;
            }

            if (bestMatch is not null)
            {
                var (content, statusCode) = _routeResponses[bestMatch];
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = statusCode,
                    Content = new StringContent(content)
                });
            }

            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = _statusCode,
                Content = new StringContent(_content)
            });
        }
    }
}

using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ArnBulletinGenerator.Infrastructure.Tests.WireMock;

/// <summary>
///     WireMock integration tests for <c>HereTrafficProvider</c>.
///     Exercises HTTP client deserialization, error handling, and domain model mapping
///     without requiring HERE Maps API credentials.
/// </summary>
[Trait("Category", "Slow")]
public sealed class HereTrafficWireMockTests : WireMockTestBase
{
    private const string IncidentsPath = "/incidents";

    /// <summary>
    ///     Realistic HERE Traffic API response with 3 incidents of varying types.
    ///     Response shape matches the <c>HereTrafficResponse</c> DTO.
    /// </summary>
    private static readonly string RealisticTrafficResponse = /* language=json */ """
    {
      "results": [
        {
          "location": {
            "shape": {
              "links": [
                {
                  "points": [{ "lat": -37.815, "lng": 144.965 }],
                  "length": 3500.0,
                  "functionalClass": 1
                }
              ]
            }
          },
          "incidentDetails": {
            "id": "here:inc:abc123",
            "type": "congestion",
            "criticality": "critical",
            "description": { "value": "M1 Monash Freeway inbound congestion from Huntingdale Road to Warrigal Road" }
          }
        },
        {
          "location": {
            "shape": {
              "links": [
                {
                  "points": [{ "lat": -37.820, "lng": 144.920 }],
                  "length": 4200.0,
                  "functionalClass": 1
                }
              ]
            }
          },
          "incidentDetails": {
            "id": "here:inc:def456",
            "type": "accident",
            "criticality": "critical",
            "description": { "value": "West Gate Freeway outbound multi-vehicle crash near Bolte Bridge" }
          }
        },
        {
          "location": {
            "shape": {
              "links": [
                {
                  "points": [{ "lat": -37.810, "lng": 144.980 }],
                  "length": 500.0,
                  "functionalClass": 4
                }
              ]
            }
          },
          "incidentDetails": {
            "id": "here:inc:ghi789",
            "type": "construction",
            "criticality": "minor",
            "description": { "value": "Warrigal Road lane closures near High Street roadworks" }
          }
        }
      ]
    }
    """;

    private static readonly string BatchGeocodeResponse = /* language=json */ """
    {
      "items": [
        { "id": "0", "address": { "label": "M1 Monash Freeway, Melbourne VIC", "street": "Monash Freeway" }, "position": { "lat": -37.815, "lng": 144.965 } },
        { "id": "1", "address": { "label": "West Gate Freeway, Melbourne VIC", "street": "West Gate Freeway" }, "position": { "lat": -37.820, "lng": 144.920 } },
        { "id": "2", "address": { "label": "Warrigal Road, Melbourne VIC", "street": "Warrigal Road" }, "position": { "lat": -37.810, "lng": 144.980 } }
      ]
    }
    """;

    private static readonly string RouteResponse = /* language=json */ """
    {
      "routes": [{ "sections": [{ "summary": { "duration": 480, "length": 8500, "typicalDuration": 360 } }] }]
    }
    """;

    protected override Task OnInitializedAsync()
    {
        // Stub: traffic incidents GET
        Server.Given(Request.Create().WithPath(new RegexMatcher(IncidentsPath)).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(RealisticTrafficResponse));

        // Stub: batch reverse geocoding POST
        Server.Given(Request.Create().WithPath(new RegexMatcher("/multi-revgeocode")).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(BatchGeocodeResponse));

        // Stub: routing GET
        Server.Given(Request.Create().WithPath(new RegexMatcher("/routes")).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(RouteResponse));

        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetCorridorsAsync_ReturnsCorridors()
    {
        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetCorridorsAsync(area, 5, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value);
    }

    /// <summary>
    ///     The provider honours <see cref="IncidentExclusions" /> — and does it BEFORE aggregation,
    ///     which is the whole contract: filtering the corridors afterwards leaves every derived
    ///     field (direction, extent, length, impact, provenance) describing the excluded record, and
    ///     that put a false directional claim to air on 2026-08-24.
    /// </summary>
    [Fact]
    public async Task GetCorridorsAsync_ExcludedRecord_NeverBecomesOrShapesACorridor()
    {
        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;
        // No originalId in this payload, so the ledger key is the source-qualified record id.
        var exclusions = new IncidentExclusions(["here:record:here:inc:def456"], []);

        var baseline = await provider.GetCorridorsAsync(area, 5, CancellationToken.None);
        var filtered = await provider.GetCorridorsAsync(area, 5, CancellationToken.None, exclusions);

        Assert.True(baseline.IsSuccess, $"Expected success but got: {baseline.Error}");
        Assert.True(filtered.IsSuccess, $"Expected success but got: {filtered.Error}");
        // By record identity, not road name: this payload carries no segment topology, so
        // RoadIdentityResolver withholds real names and every corridor reads "an unnamed road".
        var baselineIncidents = baseline.Value.SelectMany(c => c.Incidents).ToList();
        var filteredIncidents = filtered.Value.SelectMany(c => c.Incidents).ToList();
        Assert.Contains(baselineIncidents, i => i.Id == "here:inc:def456");
        Assert.DoesNotContain(filteredIncidents, i => i.Id == "here:inc:def456");
        // Only that record goes: an exclusion is not a reason to lose the rest of the fetch.
        Assert.Equal(baselineIncidents.Count - 1, filteredIncidents.Count);
        Assert.Single(exclusions.Drops);
        Assert.Equal(IncidentExclusions.Reason.DeskVerdict, exclusions.Drops[0].Reason);
    }

    [Fact]
    public async Task GetIncidentsAsync_ReturnsSortedIncidents()
    {
        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetIncidentsAsync(area, CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);
        var incidents = result.Value.ToList();
        Assert.NotEmpty(incidents);
        // Incidents should be sorted by severity (lower = more severe)
        for (int i = 1; i < incidents.Count; i++)
            Assert.True(incidents[i - 1].Severity <= incidents[i].Severity,
                $"Incidents should be sorted by severity; index {i - 1} (sev {incidents[i - 1].Severity}) > index {i} (sev {incidents[i].Severity})");
    }

    [Fact]
    public async Task GetCorridorsAsync_ReturnsFailure_WhenTrafficApiFails()
    {
        Server.Given(Request.Create().WithPath(new RegexMatcher(IncidentsPath)).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("Internal Server Error"));

        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetCorridorsAsync(area, 5, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetCorridorsAsync_ReturnsEmpty_WhenNoIncidentsInResponse()
    {
        Server.Given(Request.Create().WithPath(new RegexMatcher(IncidentsPath)).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"results":[]}"""));

        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetCorridorsAsync(area, 5, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task GetCorridorsAsync_ReturnsFailure_WhenResponseHasNoResultsStructure()
    {
        Server.Given(Request.Create().WithPath(new RegexMatcher(IncidentsPath)).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(""""{}""""));

        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetCorridorsAsync(area, 5, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetCorridorsAsync_ReturnsFailure_WhenCancelled()
    {
        Server.Given(Request.Create().WithPath(new RegexMatcher(IncidentsPath)).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithDelay(TimeSpan.FromSeconds(10)));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetCorridorsAsync(area, 5, cts.Token);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetIncidentsAsync_ReturnsFailure_WhenApiReturns401()
    {
        Server.Given(Request.Create().WithPath(new RegexMatcher(IncidentsPath)).UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(401)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"error":"Unauthorized"}"""));

        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetIncidentsAsync(area, CancellationToken.None);

        Assert.True(result.IsFailure);
        // HttpStatusCode interpolates to the text name "Unauthorized", not the numeric code
        Assert.Contains("Unauthorized", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── Contract Tests ────────────────────────────────────────────────

    [Fact]
    public void TrafficResponse_HasResultsArray()
    {
        using var doc = JsonDocument.Parse(RealisticTrafficResponse);
        var root = doc.RootElement;

        // The parser reads results array from HereTrafficResponse
        Assert.True(root.TryGetProperty("results", out var results), "Missing 'results'");
        Assert.True(results.ValueKind == JsonValueKind.Array, "'results' should be an array");
        Assert.NotEmpty(results.EnumerateArray());
    }

    [Fact]
    public void TrafficResponse_HasRequiredNestedFields()
    {
        using var doc = JsonDocument.Parse(RealisticTrafficResponse);
        var root = doc.RootElement;
        var results = root.GetProperty("results");

        foreach (var item in results.EnumerateArray())
        {
            // HereIncidentItem has Location and IncidentDetails
            Assert.True(item.TryGetProperty("location", out var location),
                "Missing result.location");
            Assert.True(location.TryGetProperty("shape", out var shape),
                "Missing location.shape");

            // The parser reads shape.links[].points[], length, functionalClass
            Assert.True(shape.TryGetProperty("links", out var links), "Missing shape.links");
            foreach (var link in links.EnumerateArray())
            {
                Assert.True(link.TryGetProperty("points", out _), "Missing link.points");
                Assert.True(link.TryGetProperty("length", out _), "Missing link.length");
                Assert.True(link.TryGetProperty("functionalClass", out _), "Missing link.functionalClass");
            }

            // IncidentDetails has id, type, criticality, description.value
            Assert.True(item.TryGetProperty("incidentDetails", out var details),
                "Missing result.incidentDetails");
            Assert.True(details.TryGetProperty("id", out _), "Missing incidentDetails.id");
            Assert.True(details.TryGetProperty("type", out var type), "Missing incidentDetails.type");
            Assert.True(type.ValueKind == JsonValueKind.String, "incidentDetails.type should be string");
            Assert.True(details.TryGetProperty("criticality", out _), "Missing incidentDetails.criticality");

            Assert.True(details.TryGetProperty("description", out var desc),
                "Missing incidentDetails.description");
            Assert.True(desc.TryGetProperty("value", out var value), "Missing description.value");
            Assert.True(value.ValueKind == JsonValueKind.String, "description.value should be string");
        }
    }

    [Fact]
    public async Task GetCorridorsAsync_Request_HasApiKeyParam()
    {
        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetCorridorsAsync(area, 5, CancellationToken.None);
        Assert.True(result.IsSuccess);

        // The HERE provider makes multiple requests: traffic -> geocode -> routing.
        // Find the traffic incident request specifically.
        var trafficRequest = RequestTo("/incidents");

        var url = trafficRequest.Url;
        Assert.Contains("apiKey=", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("in=circle:", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("locationReferencing=shape", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCorridorsAsync_Request_ReachesCorrectPath()
    {
        var provider = Services.GetRequiredService<ITrafficProvider>();
        var area = CoverageArea.Create(-37.8136, 144.9631, 10).Value;

        var result = await provider.GetCorridorsAsync(area, 5, CancellationToken.None);
        Assert.True(result.IsSuccess);

        RequestTo("/incidents");
    }
}

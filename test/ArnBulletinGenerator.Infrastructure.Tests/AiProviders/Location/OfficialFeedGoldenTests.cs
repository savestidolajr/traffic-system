using System.Net;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

/// <summary>
///     The official-feed clients against captured payloads (2026-08-17 to 2026-08-19): Live Traffic NSW
///     hazards, QLDTraffic v2 events, Main Roads WA ArcGIS layers, and SA TrafficSATime. These pin
///     the GeoJSON and ArcGIS reductions — coverage filtering, class splits, field mapping, and
///     malformed/unpublished-feature tolerance.
/// </summary>
public class OfficialFeedGoldenTests
{
    private static string Asset(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "OfficialFeeds", name));

    private static readonly CoverageArea Sydney = CoverageArea.Create(-33.8688, 151.2093, 25.0).Value;
    private static readonly CoverageArea Cairns = CoverageArea.Create(-16.9186, 145.7781, 30.0).Value;
    private static readonly CoverageArea Perth = CoverageArea.Create(-31.9900, 115.9500, 25.0).Value;
    private static readonly CoverageArea Adelaide = CoverageArea.Create(-34.8850, 138.6000, 25.0).Value;

    [Fact]
    public async Task NswFeed_ParsesCapturedHazards_SplitsIncidentClassByFeedKind()
    {
        var handler = new RoutedHandler(url =>
            url.Contains("incident-open") ? Asset("nsw-incident-2026-08-17.json")
            : url.Contains("roadwork-open") ? Asset("nsw-roadwork-2026-08-17.json")
            : throw new InvalidOperationException(url));
        var feed = new LiveTrafficNswFeed(new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions()), Substitute.For<ILogger<LiveTrafficNswFeed>>());

        var result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldNotBeEmpty();
        result.Value.ShouldAllBe(e => e.SourceId == "livetraffic-nsw");
        result.Value.ShouldContain(e => e.IsIncidentClass);
        result.Value.ShouldContain(e => !e.IsIncidentClass);
        result.Value.ShouldAllBe(e => e.Shape.Count > 0);
        result.Value.ShouldAllBe(e => !string.IsNullOrWhiteSpace(e.Id));
        // The authority's clock parses with start and publication DISTINCT: fusion chooses the
        // age signal explicitly, and collapsing them would call a future-scheduled worksite old.
        var timed = result.Value.Where(e => e.StartTime.HasValue && e.PublishedTime.HasValue).ToList();
        timed.ShouldNotBeEmpty();
        timed.ShouldContain(e => e.StartTime!.Value != e.PublishedTime!.Value);
        // Roadwork records carry empty headlines upstream; the parser labels them.
        result.Value.Where(e => !e.IsIncidentClass).ShouldAllBe(e => e.Headline.Length > 0);
    }
    [Fact]
    public async Task NswFeed_PopulatesOfficialMetadata_WithoutMigratingAdviceIntoDetail()
    {
        var handler = new RoutedHandler(url =>
            url.Contains("incident-open") ? Asset("nsw-incident-2026-08-17.json")
            : url.Contains("roadwork-open") ? Asset("nsw-roadwork-2026-08-17.json")
            : throw new InvalidOperationException(url));
        var feed = new LiveTrafficNswFeed(new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions()), Substitute.For<ILogger<LiveTrafficNswFeed>>());

        var result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.Any(e => e.IsStateRoad == true).ShouldBeTrue();
        result.Value.Any(e => e.IsPlanned == true).ShouldBeTrue();
        result.Value.Any(e => !string.IsNullOrWhiteSpace(e.Category)).ShouldBeTrue();
        result.Value.Any(e => !string.IsNullOrWhiteSpace(e.SubCategory)).ShouldBeTrue();
        result.Value.Any(e => e.AdviceTags != null && e.AdviceTags.Count > 0).ShouldBeTrue();
        result.Value.Any(e => e.SpeedLimitKph > 0).ShouldBeTrue();
        result.Value.Any(e => e.LastUpdated.HasValue).ShouldBeTrue();
        result.Value.Any(e => !string.IsNullOrWhiteSpace(e.Detail)).ShouldBeTrue();

        var advice = new[]
        {
            "Allow extra travel time", "Check signage", "Exercise caution",
            "Use an alternative route", "Plan your journey", "Reduce your speed",
            "Follow directions on site"
        };
        result.Value.ShouldAllBe(e => advice.All(tag =>
            e.Detail == null || !e.Detail.Contains(tag, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task QldFeed_PopulatesAuthorityPriorityAndImpactTriple()
    {
        var handler = new RoutedHandler(_ => Asset("qld-events-2026-08-17.json"));
        var feed = new QldTrafficFeed(new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions()), Substitute.For<ILogger<QldTrafficFeed>>());

        var result = await feed.GetEventsAsync(Cairns);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.Any(e => e.AuthorityPriority is "High" or "Medium" or "Low").ShouldBeTrue();
        result.Value.Any(e => !string.IsNullOrWhiteSpace(e.ImpactType)).ShouldBeTrue();
        result.Value.Any(e => !string.IsNullOrWhiteSpace(e.ImpactSubtype)).ShouldBeTrue();
        result.Value.Any(e => !string.IsNullOrWhiteSpace(e.QualitativeDelay)).ShouldBeTrue();
    }

    [Fact]
    public async Task QldFeed_PopulatesStartEndAndPublishedTimes()
    {
        var handler = new RoutedHandler(_ => Asset("qld-events-2026-08-17.json"));
        var feed = new QldTrafficFeed(new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions()), Substitute.For<ILogger<QldTrafficFeed>>());

        var result = await feed.GetEventsAsync(Cairns);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldNotBeEmpty();
        result.Value.Any(e => e.StartTime.HasValue).ShouldBeTrue();
        result.Value.Any(e => e.EndTime.HasValue).ShouldBeTrue();
        result.Value.Any(e => e.PublishedTime.HasValue).ShouldBeTrue();
        result.Value.Any(e => !string.IsNullOrWhiteSpace(e.Direction)).ShouldBeTrue();
    }


    [Fact]
    public async Task QldFeed_ParsesCapturedEvents_RoadworksAreNotIncidentClass()
    {
        var handler = new RoutedHandler(_ => Asset("qld-events-2026-08-17.json"));
        var feed = new QldTrafficFeed(new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions()), Substitute.For<ILogger<QldTrafficFeed>>());

        var result = await feed.GetEventsAsync(Cairns);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldNotBeEmpty();
        result.Value.Where(e => e.Headline.StartsWith("Roadworks"))
            .ShouldAllBe(e => !e.IsIncidentClass);
    }

    [Fact]
    public async Task WaFeed_FetchesBothLayers_MapsMetadata_AndExcludesUnpublishedIncidents()
    {
        var handler = new RoutedHandler(url =>
            url.Contains("/0/query") ? Asset("wa-incident-2026-08-19.json")
            : url.Contains("/1/query") ? Asset("wa-roadwork-2026-08-19.json")
            : throw new InvalidOperationException(url));
        var options = new OfficialFeedsOptions { WaTravelMapUrl = "https://wa.test/TravelInformation/MapServer" };
        var feed = new MainRoadsWaFeed(new HttpClient(handler), Options.Create(options),
            Substitute.For<ILogger<MainRoadsWaFeed>>());

        var result = await feed.GetEventsAsync(Perth);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldNotBeEmpty();
        result.Value.ShouldContain(e => e.IsIncidentClass);
        result.Value.ShouldContain(e => !e.IsIncidentClass);
        result.Value.ShouldNotContain(e => e.Id == "270002");
        result.Value.ShouldAllBe(e => !string.IsNullOrWhiteSpace(e.Id));
        var incident = result.Value.Single(e => e.IsIncidentClass);
        incident.AuthorityPriority.ShouldBe("Level 2");
        incident.Category.ShouldBe("Road Crash");
        incident.SubCategory.ShouldBe("Lane closure");
        incident.ImpactType.ShouldBe("Left Lane Blocked");
        incident.Detail!.ShouldContain("Left lane blocked");
        incident.PublishedTime.ShouldNotBeNull();
        incident.LastUpdated.ShouldNotBeNull();
        var roadwork = result.Value.Single(e => !e.IsIncidentClass);
        roadwork.Category.ShouldBe("Road resurfacing");
        roadwork.SubCategory.ShouldBe("In progress");
        roadwork.ImpactType.ShouldBe("Reduced speed limit");
        roadwork.Detail!.ShouldContain("Night works");
        roadwork.StartTime.ShouldNotBeNull();
        roadwork.EndTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task WaFeed_WhenOneLayerFails_ReturnsTheOtherLayer()
    {
        var handler = new RoutedHandler(url =>
            url.Contains("/0/query")
                ? throw new HttpRequestException("incident layer unavailable")
                : Asset("wa-roadwork-2026-08-19.json"));
        var feed = new MainRoadsWaFeed(
            new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions { WaTravelMapUrl = "https://wa.test/TravelInformation/MapServer" }),
            Substitute.For<ILogger<MainRoadsWaFeed>>());

        var result = await feed.GetEventsAsync(Perth);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.ShouldNotBeEmpty();
        result.Value.ShouldAllBe(e => !e.IsIncidentClass);
    }

    [Fact]
    public async Task SaFeed_FiltersCurrentActive_AndMapsCoordinatesAndLaneMetadata()
    {
        var handler = new RoutedHandler(_ => Asset("sa-traffic-2026-08-19.json"));
        var feed = new TrafficSaFeed(new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions { SaTrafficUrl = "https://sa.test/query" }),
            Substitute.For<ILogger<TrafficSaFeed>>());

        var result = await feed.GetEventsAsync(Adelaide);

        var parsed = result.Value.Single();
        parsed.Id.ShouldBe("1001");
        parsed.Category.ShouldBe("INCIDENT");
        parsed.Detail.ShouldBe("Crash blocking lanes");
        parsed.LanesClosed.ShouldBe(2);
        parsed.SpeedLimitKph.ShouldBe(60);
        parsed.Direction.ShouldBe("NORTHBOUND and SOUTHBOUND");
        parsed.RoadName.ShouldBe("Main North Road");
        parsed.CrossStreet.ShouldBe("Regency Road");
        parsed.StartTime.ShouldNotBeNull();
        parsed.EndTime.ShouldNotBeNull();
        parsed.Shape.ShouldContain(new CoveragePoint(-34.8850, 138.6000));
    }

    [Fact]
    public async Task SaFeed_UsesNestedPositiveDirectionalLaneCount_WhenTopLevelIsUnknown()
    {
        string payload = Asset("sa-traffic-2026-08-19.json")
            .Replace("\\\"lanes_closed\\\":2", "\\\"lanes_closed\\\":1", StringComparison.Ordinal)
            .Replace("\"no_lanes_closed\": \"2\"", "\"no_lanes_closed\": \"0\"", StringComparison.Ordinal);
        var handler = new RoutedHandler(_ => payload);
        var feed = new TrafficSaFeed(new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions { SaTrafficUrl = "https://sa.test/query" }),
            Substitute.For<ILogger<TrafficSaFeed>>());

        var result = await feed.GetEventsAsync(Adelaide);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        result.Value.Single().LanesClosed.ShouldBe(1);
    }

    [Fact]
    public async Task CoverageFiltering_ExcludesEventsOutsideTheArea()
    {
        // The Cairns circle must not contain Brisbane-area QLD events and vice versa.
        var handler = new RoutedHandler(_ => Asset("qld-events-2026-08-17.json"));
        var feed = new QldTrafficFeed(new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions()), Substitute.For<ILogger<QldTrafficFeed>>());

        var cairns = (await feed.GetEventsAsync(Cairns)).Value;
        var brisbane = (await feed.GetEventsAsync(CoverageArea.Create(-27.4698, 153.0251, 25.0).Value)).Value;

        cairns.ShouldNotBeEmpty();
        brisbane.ShouldNotBeEmpty();
        // Disjoint geographies: no event geometry can sit in both circles (they are ~1400 km apart).
        var cairnsShapes = cairns.Select(e => e.Shape[0]).ToList();
        brisbane.Select(e => e.Shape[0])
            .ShouldAllBe(p => cairnsShapes.All(c => Math.Abs(c.Lat - p.Lat) > 0.5));
    }

    private sealed class RoutedHandler(Func<string, string> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(route(request.RequestUri!.ToString()))
            });
    }
}

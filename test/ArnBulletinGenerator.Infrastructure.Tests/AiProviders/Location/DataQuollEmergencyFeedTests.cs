using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public sealed class DataQuollEmergencyFeedTests
{
    private static CoverageArea Sydney => CoverageArea.Create(-33.8688, 151.2093, 20).Value;

    [Fact]
    public async Task DataQuoll_RevisionsCollapseToStableIdAndKeepNewestRevision()
    {
        string baseUrl = TestBaseUrl();
        string payload = FeatureCollection(
            Feature("nsw-rfs-cap-2026-08-31T12:45:00.0000000:673492", "old", "2026-08-31T12:45:00Z"),
            Feature("nsw-rfs-cap-2026-08-31T12:53:00.0000000:673492", "middle", "2026-08-31T12:53:00Z"),
            Feature("nsw-rfs-cap-2026-08-31T13:03:00.0000000:673492", "newest", "2026-08-31T13:03:00Z"));
        var feed = CreateFeed(baseUrl, request =>
            request.RequestUri!.AbsolutePath.EndsWith("/incidents", StringComparison.Ordinal)
                ? JsonResponse(payload)
                : JsonResponse(Attribution(("nsw", "RFS", "CC BY 4.0", true))));

        Result<IReadOnlyList<EmergencyEvent>> result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.Count.ShouldBe(1);
        result.Value[0].Id.ShouldBe("673492");
        result.Value[0].Title.ShouldBe("newest");
        result.Value[0].UpdatedUtc.ShouldBe(DateTimeOffset.Parse("2026-08-31T13:03:00Z"));
    }

    [Fact]
    public async Task DataQuoll_UnknownWarningLevelFailsClosedToNone()
    {
        string baseUrl = TestBaseUrl();
        var feed = CreateFeed(baseUrl, request =>
            request.RequestUri!.AbsolutePath.EndsWith("/incidents", StringComparison.Ordinal)
                ? JsonResponse(FeatureCollection(Feature("unknown-warning", "unknown", "2026-08-31T13:03:00Z", warningLevel: "catastrophic")))
                : JsonResponse(Attribution(("nsw", "RFS", "CC BY 4.0", true))));

        Result<IReadOnlyList<EmergencyEvent>> result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.Single().WarningLevel.ShouldBe(EmergencyWarningLevel.None);
    }

    [Fact]
    public async Task DataQuoll_IneligibleLicencesAndCategoriesRemainVisibleButCannotAir()
    {
        string baseUrl = TestBaseUrl();
        string payload = FeatureCollection(
            Feature("nc", "non-commercial", "2026-08-31T13:03:00Z", category: "fire", agency: "CFS"),
            Feature("nd", "no-derivatives", "2026-08-31T13:03:00Z", category: "fire", agency: "DFES"),
            Feature("other-license", "other licence", "2026-08-31T13:03:00Z", category: "fire", agency: "EMQ"),
            Feature("medical", "medical", "2026-08-31T13:03:00Z", category: "medical", agency: "RFS"));
        var feed = CreateFeed(baseUrl, request =>
            request.RequestUri!.AbsolutePath.EndsWith("/incidents", StringComparison.Ordinal)
                ? JsonResponse(payload)
                : JsonResponse(Attribution(
                    ("nsw", "RFS", "CC BY 4.0", true),
                    ("nsw", "CFS", "CC BY-NC-ND 4.0", true),
                    ("nsw", "DFES", "CC BY-ND 4.0", true),
                    ("nsw", "EMQ", "All rights reserved", true))));

        Result<IReadOnlyList<EmergencyEvent>> result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.Count.ShouldBe(4);
        result.Value.ShouldAllBe(item => !item.AirEligible);
        result.Value.ShouldContain(item => item.Id == "medical" && item.Licence == "CC BY 4.0");
    }

    [Fact]
    public async Task DataQuoll_BlankApiKeyReturnsReadableFailureWithoutThrowing()
    {
        var feed = new DataQuollEmergencyFeed(
            new HttpClient(new StubHandler(_ => throw new InvalidOperationException("HTTP must not be called"))),
            Options.Create(new EmergencyFeedOptions { Enabled = true, ApiKey = "" }),
            Substitute.For<ILogger<DataQuollEmergencyFeed>>(),
            new MemoryCache(new MemoryCacheOptions()));

        Result<IReadOnlyList<EmergencyEvent>> result = await feed.GetEventsAsync(Sydney);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("no API key");
    }

    [Fact]
    public async Task DataQuoll_PointOutsideCircleIsDropped()
    {
        string baseUrl = TestBaseUrl();
        var feed = CreateFeed(baseUrl, request =>
            request.RequestUri!.AbsolutePath.EndsWith("/incidents", StringComparison.Ordinal)
                ? JsonResponse(FeatureCollection(Feature("far-away", "outside", "2026-08-31T13:03:00Z", latitude: -34.4, longitude: 151.2)))
                : JsonResponse(Attribution(("nsw", "RFS", "CC BY 4.0", true))));

        Result<IReadOnlyList<EmergencyEvent>> result = await feed.GetEventsAsync(Sydney);

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not-json")]
    public async Task DataQuoll_MalformedOrEmptyJsonReturnsFailure(string body)
    {
        string baseUrl = TestBaseUrl();
        var feed = CreateFeed(baseUrl, request =>
            request.RequestUri!.AbsolutePath.EndsWith("/incidents", StringComparison.Ordinal)
                ? JsonResponse(body)
                : JsonResponse(Attribution(("nsw", "RFS", "CC BY 4.0", true))));

        Result<IReadOnlyList<EmergencyEvent>> result = await feed.GetEventsAsync(Sydney);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("DataQuoll");
    }

    [Fact]
    public void DataQuoll_LicencePredicateRejectsCommercialRestrictions()
    {
        DataQuollEmergencyFeed.IsCommerciallyReusableLicence("CC BY 4.0").ShouldBeTrue();
        DataQuollEmergencyFeed.IsCommerciallyReusableLicence("CC BY-NC-ND 4.0").ShouldBeFalse();
        DataQuollEmergencyFeed.IsCommerciallyReusableLicence("CC BY-ND 4.0").ShouldBeFalse();
        DataQuollEmergencyFeed.IsCommerciallyReusableLicence("All rights reserved").ShouldBeFalse();
        DataQuollEmergencyFeed.IsAirableCategory("rescue").ShouldBeTrue();
        DataQuollEmergencyFeed.IsAirableCategory("medical").ShouldBeFalse();
    }

    [Fact]
    public async Task DataQuoll_SecondCallInsideTtlDoesNotIssueAnotherIncidentRequest()
    {
        string baseUrl = TestBaseUrl();
        int requestCount = 0;
        var feed = CreateFeed(baseUrl, request =>
        {
            requestCount++;
            return request.RequestUri!.AbsolutePath.EndsWith("/incidents", StringComparison.Ordinal)
                ? JsonResponse(FeatureCollection(Feature("cached", "cached", "2026-08-31T13:03:00Z")))
                : JsonResponse(Attribution(("nsw", "RFS", "CC BY 4.0", true)));
        }, cacheTtlSeconds: 300);

        (await feed.GetEventsAsync(Sydney)).IsSuccess.ShouldBeTrue();
        (await feed.GetEventsAsync(Sydney)).IsSuccess.ShouldBeTrue();

        requestCount.ShouldBe(2, "the first incident and attribution requests are cached for the second call");
    }

    private static DataQuollEmergencyFeed CreateFeed(
        string baseUrl, Func<HttpRequestMessage, HttpResponseMessage> responder, int cacheTtlSeconds = 0) =>
        new(
            new HttpClient(new StubHandler(responder)),
            Options.Create(new EmergencyFeedOptions
            {
                Enabled = true,
                BaseUrl = baseUrl,
                ApiKey = "test-key",
                CacheTtlSeconds = cacheTtlSeconds
            }),
            Substitute.For<ILogger<DataQuollEmergencyFeed>>(),
            new MemoryCache(new MemoryCacheOptions()));

    private static string FeatureCollection(params object[] features) =>
        JsonSerializer.Serialize(new { type = "FeatureCollection", features });

    private static object Feature(
        string id,
        string title,
        string updated,
        string warningLevel = "advice",
        string category = "fire",
        string agency = "RFS",
        double latitude = -33.8688,
        double longitude = 151.2093) => new
        {
            type = "Feature",
            id,
            geometry = new { type = "Point", coordinates = new[] { longitude, latitude } },
            properties = new
            {
                source = new { state = "nsw", agency, feedId = "nsw-rfs-cap" },
                title,
                eventType = "bushfire",
                eventCategory = category,
                warningLevel,
                location = new { suburb = "Sydney", address = "Test Road", state = "nsw" },
                timestamps = new { reported = updated, updated },
                details = new { description = "detail", source_url = "https://example.test/source" }
            }
        };

    private static string Attribution(params (string State, string Agency, string Licence, bool Official)[] sources) =>
        JsonSerializer.Serialize(new
        {
            count = sources.Length,
            sources = sources.Select(source => new
            {
                state = source.State,
                agency = source.Agency,
                name = source.Agency,
                licence = source.Licence,
                url = "https://example.test",
                official = source.Official
            })
        });

    private static HttpResponseMessage JsonResponse(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static string TestBaseUrl() => $"https://dataquoll.test/{Guid.NewGuid():N}";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}

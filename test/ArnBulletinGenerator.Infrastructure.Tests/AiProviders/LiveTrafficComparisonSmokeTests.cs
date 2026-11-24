using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public class LiveTrafficComparisonSmokeTests
{
    private readonly ITestOutputHelper _output;
    private static readonly CoverageArea SydneyArea = CoverageArea.Create(-33.8688, 151.2093, 35.0).Value;

    public LiveTrafficComparisonSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Live verification: requires real HERE + Live Traffic NSW credentials; run manually")]
    public async Task Compare_RealLiveApis_BeforeAndAfterCorroboration()
    {
        // 1. Setup real live HERE traffic provider using dev credentials
        string localSettingsPath = Path.Combine(AppContext.BaseDirectory, "../../../../../src/ArnBulletinGenerator.Functions/local.settings.json");
        if (!File.Exists(localSettingsPath))
            localSettingsPath = Path.GetFullPath("src/ArnBulletinGenerator.Functions/local.settings.json");

        string settingsJson = await File.ReadAllTextAsync(localSettingsPath);
        using var settingsDoc = JsonDocument.Parse(settingsJson);
        var values = settingsDoc.RootElement.GetProperty("Values");

        string hereApiKey = values.GetProperty("Here__ApiKey").GetString()!;

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ArnBulletinGenerator/1.0");

        var hereOptions = Options.Create(new HereOptions
        {
            ApiKey = hereApiKey,
            TrafficBaseUrl = "https://data.traffic.hereapi.com/v7",
            GeocodingBaseUrl = "https://geocode.search.hereapi.com/v1",
            RoutingBaseUrl = "https://router.hereapi.com/v8"
        });

        var broadcastOptions = Options.Create(new BroadcastOptions
        {
            DefaultTimezone = "Australia/Sydney"
        });

        var geocodingProvider = new HereGeocodingProvider(
            httpClient,
            hereOptions,
            NullLogger<HereGeocodingProvider>.Instance);

        var routingProvider = new HereRoutingProvider(
            httpClient,
            hereOptions,
            NullLogger<HereRoutingProvider>.Instance);

        var hereProvider = new HereTrafficProvider(
            httpClient,
            hereOptions,
            geocodingProvider,
            routingProvider,
            broadcastOptions,
            NullLogger<HereTrafficProvider>.Instance);

        // Fetch live corridors from real HERE Traffic API
        var hereResult = await hereProvider.GetCorridorsAsync(SydneyArea, maxCorridors: 10);
        hereResult.IsSuccess.ShouldBeTrue();
        var rawHereCorridors = hereResult.Value.ToList();

        // 2. Fetch live events from Live Traffic NSW open endpoint
        var nswFeed = new LiveTrafficNswFeed(
            httpClient,
            Options.Create(new OfficialFeedsOptions()),
            NullLogger<LiveTrafficNswFeed>.Instance);

        var nswResult = await nswFeed.GetEventsAsync(SydneyArea);
        nswResult.IsSuccess.ShouldBeTrue();
        var officialEvents = nswResult.Value;

        // 3. Payload BEFORE (Vanilla HERE only)
        var contextBefore = new BroadcastWithTraffic
        {
            JobId = "live-sydney-before",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Traffic = "Traffic for Sydney",
            Incidents = rawHereCorridors.SelectMany(c => c.Incidents).ToList(),
            Corridors = rawHereCorridors,
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false),
            FillOrder = []
        };

        string summaryBefore = TrafficNarrativeSummarizer.Summarize(contextBefore);

        // 4. Payload AFTER (Corroborated with Live Traffic NSW feeds)
        var corroborationOptions = new CorroborationOptions(
            Enabled: true,
            CorroborateIncidents: true,
            InjectMissingOfficialIncidents: true,
            MaxCorroborationDistanceMeters: 500.0);

        var corroboratedCorridors = TrafficEvidenceFusion.Fuse(
            rawHereCorridors,
            officialEvents,
            corroborationOptions,
            DateTimeOffset.UtcNow);

        var contextAfter = new BroadcastWithTraffic
        {
            JobId = "live-sydney-after",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Traffic = "Traffic for Sydney",
            Incidents = corroboratedCorridors.SelectMany(c => c.Incidents).ToList(),
            Corridors = corroboratedCorridors,
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false),
            FillOrder = []
        };

        string summaryAfter = TrafficNarrativeSummarizer.Summarize(contextAfter);

        _output.WriteLine("===============================================================================");
        _output.WriteLine($"PROMPT PAYLOAD SENT TO DRAFT AGENT — BEFORE (VANILLA HERE: {rawHereCorridors.Count} CORRIDORS)");
        _output.WriteLine("===============================================================================");
        _output.WriteLine(summaryBefore);
        _output.WriteLine("");
        _output.WriteLine("===============================================================================");
        _output.WriteLine($"PROMPT PAYLOAD SENT TO DRAFT AGENT — AFTER (CORROBORATED WITH {officialEvents.Count} LIVE NSW EVENTS)");
        _output.WriteLine("===============================================================================");
        _output.WriteLine(summaryAfter);
        _output.WriteLine("===============================================================================");
    }
}

using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Operator-run acquisition of a real HERE API corpus, persisted under <c>test/corpus/here</c>.
///     <para>
///         Captures by driving the production providers (<see cref="HereTrafficProvider" /> and friends)
///         against the live API with a recording handler underneath, so every reverse-geocode and routing
///         response corresponds to the exact incidents snapshot that produced it — coherent, replayable scenarios.
///     </para>
///     <para>
///         Excluded from CI via <c>Category=Integration</c>. To run:
///         <c>ARN_HERE_CAPTURE=1 HERE_API_KEY=&lt;key&gt; dotnet test \
///         --filter "FullyQualifiedName~HereCorpusCapture"</c>.
///         Re-running accumulates timestamped sessions (capture at different times of day for temporal variety).
///     </para>
/// </summary>
public sealed class HereCorpusCaptureTests(ITestOutputHelper output)
{
    private static readonly Market[] Markets =
    [
        new("sydney", -33.8688, 151.2093, 25, "2GB"),
        new("melbourne", -37.8136, 144.9631, 25, "3AW"),
        new("brisbane", -27.4698, 153.0251, 25, "4BC"),
        new("perth", -31.9505, 115.8605, 25, "6PR"),
        new("adelaide", -34.9285, 138.6007, 25, "5AA"),
        new("goldcoast", -28.0167, 153.4000, 20, "Gold FM")
    ];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CaptureHereCorpus_AllMarkets()
    {
        if (Environment.GetEnvironmentVariable("ARN_HERE_CAPTURE") != "1")
        {
            output.WriteLine(
                "Skipped: set ARN_HERE_CAPTURE=1 and HERE_API_KEY to capture the live HERE corpus.");
            return;
        }

        string? hereApiKey = TestConfigurationHelper.BuildConfiguration()["Here:ApiKey"];
        if (string.IsNullOrWhiteSpace(hereApiKey) || hereApiKey == "test-key")
            Assert.Fail("ARN_HERE_CAPTURE=1 but no real HERE key resolved. Set HERE_API_KEY.");

        var writer = new HereCorpusWriter(CorpusPaths.HereCorpusRoot());

        await CaptureMarketCascadesAsync(writer);
        await CaptureRoutingAsync(writer);
        await CaptureBatchReverseGeocodeProbeAsync(writer);

        await writer.FlushManifestAsync(CancellationToken.None);

        output.WriteLine($"Captured {writer.TotalRecorded} HERE exchanges → {CorpusPaths.HereCorpusRoot()}");
        Assert.True(writer.TotalRecorded > 0, "No HERE exchanges were captured.");
    }

    /// <summary>
    ///     Per market: incidents at two radii (distinct snapshots) plus the reverse-geocode and routing
    ///     cascades they trigger, and forward geocode of the market name and station.
    /// </summary>
    private async Task CaptureMarketCascadesAsync(HereCorpusWriter writer)
    {
        using var provider = BuildProvider(writer, useBatchGeocoding: false);
        using var scope = provider.CreateScope();
        var traffic = scope.ServiceProvider.GetRequiredService<ITrafficProvider>();
        var geocoding = scope.ServiceProvider.GetRequiredService<IGeocodingProvider>();
        var cancellationToken = CancellationToken.None;

        foreach (var market in Markets)
        {
            writer.CurrentMarket = market.Name;

            double[] radii = [market.RadiusKm, Math.Max(8, Math.Round(market.RadiusKm / 2))];
            foreach (double radiusKm in radii)
            {
                var area = CoverageArea.Create(market.Lat, market.Lng, radiusKm);
                if (area.IsFailure)
                {
                    output.WriteLine($"{market.Name} r{radiusKm}km: invalid coverage — {area.Error}");
                    continue;
                }

                writer.CurrentSessionId = SessionId($"r{(int)radiusKm}");
                var corridors = await traffic.GetCorridorsAsync(area.Value, 5, cancellationToken);
                output.WriteLine(corridors.IsSuccess
                    ? $"{market.Name} r{radiusKm}km: {corridors.Value.Count()} corridors"
                    : $"{market.Name} r{radiusKm}km: FAILED — {corridors.Error}");
            }

            writer.CurrentSessionId = SessionId("geocode");
            await geocoding.GeocodeAsync($"{market.Name}, Australia", 5.0, cancellationToken);
            await geocoding.GeocodeAsync(market.Station, 5.0, cancellationToken);
        }
    }

    /// <summary>
    ///     Guarantees routing coverage per market by routing between real incident coordinates
    ///     (the provider's own corridor routing fires only incidentally). Also accrues another
    ///     incidents snapshot and its reverse-geocode cascade.
    /// </summary>
    private async Task CaptureRoutingAsync(HereCorpusWriter writer)
    {
        using var provider = BuildProvider(writer, useBatchGeocoding: false);
        using var scope = provider.CreateScope();
        var traffic = scope.ServiceProvider.GetRequiredService<ITrafficProvider>();
        var routing = scope.ServiceProvider.GetRequiredService<IRoutingProvider>();

        foreach (var market in Markets)
        {
            writer.CurrentMarket = market.Name;
            writer.CurrentSessionId = SessionId("routing");

            var area = CoverageArea.Create(market.Lat, market.Lng, market.RadiusKm);
            if (area.IsFailure)
                continue;

            var incidents = await traffic.GetIncidentsAsync(area.Value, CancellationToken.None);
            if (incidents.IsFailure)
            {
                output.WriteLine($"{market.Name} routing: incidents fetch failed — {incidents.Error}");
                continue;
            }

            var points = incidents.Value
                .Where(i => i.Latitude.HasValue && i.Longitude.HasValue)
                .Select(i => (i.Latitude.Value, i.Longitude.Value))
                .Distinct()
                .Take(3)
                .ToList();

            if (points.Count < 2)
            {
                output.WriteLine($"{market.Name} routing: fewer than 2 located incidents — skipped");
                continue;
            }

            await routing.GetDriveTimeAsync(points[0], points[1], CancellationToken.None);
            if (points.Count >= 3)
                await routing.GetDriveTimeAsync(points[0], points[2], CancellationToken.None);

            output.WriteLine($"{market.Name} routing: captured {(points.Count >= 3 ? 2 : 1)} route(s)");
        }
    }

    /// <summary>
    ///     Best-effort probe of the premium multi-revgeocode endpoint. On a freemium plan this records
    ///     the real error response, which is itself useful for replay-based error-path tests.
    /// </summary>
    private async Task CaptureBatchReverseGeocodeProbeAsync(HereCorpusWriter writer)
    {
        using var provider = BuildProvider(writer, useBatchGeocoding: true);
        using var scope = provider.CreateScope();
        var geocoding = scope.ServiceProvider.GetRequiredService<IGeocodingProvider>();

        writer.CurrentMarket = "_batch";
        writer.CurrentSessionId = SessionId("batch");

        var coordinates = Markets.Select(m => (m.Lat, m.Lng));
        var result = await geocoding.BatchReverseGeocodeAsync(coordinates, CancellationToken.None);
        output.WriteLine(result.IsSuccess
            ? $"batch revgeocode: {result.Value.Count} resolved"
            : $"batch revgeocode: not available on this plan — {result.Error}");
    }

    private static ServiceProvider BuildProvider(HereCorpusWriter writer, bool useBatchGeocoding)
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Here:UseBatchGeocoding"] = useBatchGeocoding ? "true" : "false"
        };
        var configuration = TestConfigurationHelper.BuildConfiguration(overrides);

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        services.AddInfrastructure(configuration);
        services.AddSingleton(writer);

        // Append the recorder beneath the existing per-client Polly handler.
        services.AddHttpClient<HereTrafficProvider>()
            .AddHttpMessageHandler(sp => new HereRecordingHandler(sp.GetRequiredService<HereCorpusWriter>()));
        services.AddHttpClient<HereGeocodingProvider>()
            .AddHttpMessageHandler(sp => new HereRecordingHandler(sp.GetRequiredService<HereCorpusWriter>()));
        services.AddHttpClient<HereRoutingProvider>()
            .AddHttpMessageHandler(sp => new HereRecordingHandler(sp.GetRequiredService<HereCorpusWriter>()));

        return services.BuildServiceProvider();
    }

    private static string SessionId(string suffix) =>
        $"{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmss'Z'}-{suffix}";

    private readonly record struct Market(string Name, double Lat, double Lng, double RadiusKm, string Station);
}

using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     One-time fixture enrichment: dev never persisted the flow pool / good-run roads on the
///     captured FallbackUsed jobs (FlowRoadsJson postdates them), yet good-runs are a material slice
///     of a bulletin's word count — without them replay scripts run systematically shorter than dev
///     did. This capture fetches the LIVE HERE free-flow pool for each fixture's coverage circle
///     (7 unique coverages across 58 fixtures), applies the orchestrator's exact policy chain
///     (<see cref="GoodRunPolicy.ExcludeCorridorRoads" /> → pool cap → selection for generation),
///     and rewrites the fixtures. Approximation is documented per fixture: flowing-road NAMES are
///     stable arterials, but jam factors reflect capture time, not the original job's clock.
///     <para>Gated by <c>ARN_REPLAY_CAPTURE_FLOW=1</c>; needs the HERE key
///     (<c>local.settings.json</c>). Excluded from CI.</para>
/// </summary>
public sealed class FlowPoolCaptureTests(ITestOutputHelper output)
{
    private const int GoodRunScriptCap = 6; // BroadcastOrchestrator.GoodRunScriptCap
    private const int FlowPoolCap = 20; // BroadcastOrchestrator.FlowPoolCap
    private const int FreeFlowFetchCount = 18; // BroadcastOrchestrator.FetchFreeFlowingRoadsSafeAsync

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FallbackCorpus_CaptureFlowPools_EnrichesFixtures()
    {
        if (Environment.GetEnvironmentVariable("ARN_REPLAY_CAPTURE_FLOW") != "1")
        {
            output.WriteLine("Skipped: set ARN_REPLAY_CAPTURE_FLOW=1 + HERE credentials to capture flow pools.");
            return;
        }

        var fixtures = FallbackReplayFixture.LoadAll();
        fixtures.ShouldNotBeEmpty();

        var overrides = new Dictionary<string, string?>();
        FallbackReplayTests.ApplyLocalSettingsCredentials(overrides);
        var configuration = TestConfigurationHelper.BuildConfiguration(
            TestConfigurationHelper.DeployedPipeline(overrides));
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddLogging();
        services.AddMemoryCache();
        services.AddInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<Infrastructure.AiProviders.Location.HereTrafficProvider>();
        services.AddHttpClient<Infrastructure.AiProviders.Location.HereGeocodingProvider>();
        services.AddHttpClient<Infrastructure.AiProviders.Location.HereRoutingProvider>();
        await using var provider = services.BuildServiceProvider();
        var traffic = provider.GetRequiredService<ITrafficProvider>();

        // One live fetch per unique coverage circle.
        var pools = new Dictionary<(double, double, double), IReadOnlyList<FlowRoad>>();
        int enriched = 0, skipped = 0;
        foreach (var fixture in fixtures)
        {
            if (fixture.Coverage is not { } cov || cov.RadiusKm is not { } radius)
            {
                skipped++;
                continue;
            }

            var key = (Math.Round(cov.Latitude, 4), Math.Round(cov.Longitude, 4), Math.Round(radius, 1));
            if (!pools.TryGetValue(key, out var pool))
            {
                var area = CoverageArea.Create(cov.Latitude, cov.Longitude, radius);
                if (area.IsFailure)
                {
                    output.WriteLine($"{fixture.JobId[..10]}: invalid coverage — {area.Error}");
                    skipped++;
                    continue;
                }

                var fetched = await traffic.GetFreeFlowingRoadsAsync(area.Value, FreeFlowFetchCount);
                pool = fetched.IsSuccess ? fetched.Value : [];
                pools[key] = pool;
                output.WriteLine($"coverage ({key.Item1}, {key.Item2}, {key.Item3}km): {pool.Count} flowing road(s)");
            }

            // Orchestrator policy chain (automatic path, BroadcastOrchestrator line ~134): corridor
            // roads never double as good-runs; incidents and flowing roads then COMPETE for the slot
            // via BulletinContentSelector (incl. the airtime backfill for suppressed minors) — the
            // same rule that gave dev bulletins their good-run lines alongside a full corridor set.
            var eligible = GoodRunPolicy.ExcludeCorridorRoads(pool, fixture.Snapshot.Corridors)
                .Take(FlowPoolCap).ToList();
            int slotCapacity = Math.Clamp((int)(fixture.Snapshot.TargetDurationSeconds / 5.0), 6, 15);
            var selection = BulletinContentSelector.Select(
                fixture.Snapshot.Corridors, eligible, slotCapacity, GoodRunScriptCap);
            var goodRuns = selection.GoodRuns.Select(f => f.RoadName).ToList();

            var updated = fixture with
            {
                Snapshot = fixture.Snapshot with { FlowPool = eligible, GoodRunRoads = goodRuns },
                Fidelity = fixture.Fidelity with { GoodRunsAndFlowPoolLost = false }
            };
            updated.Save();
            enriched++;
        }

        output.WriteLine($"Enriched {enriched} fixture(s); skipped {skipped}; live fetches: {pools.Count}.");
        enriched.ShouldBeGreaterThan(0);
    }
}

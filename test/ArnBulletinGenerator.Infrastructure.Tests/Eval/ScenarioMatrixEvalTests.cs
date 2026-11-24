using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Pilot-station condition matrix (2026-08-21): one shipped bulletin per common scenario for
///     Sydney KIIS 90s (busy metro) and Cairns Star 102.7 (thin regional), on LIVE HERE data through
///     the real pipeline (draft -> review -> refine -> slot-fit). Corridor subsets are derived by a
///     filtering decorator around the one live fetch per market, so each condition is exercised on
///     real evidence rather than hand-authored data. Scaffolding for inspection — non-deterministic,
///     live calls. Run with ARN_SCENARIO_MATRIX=1 + HERE/Azure OpenAI creds.
/// </summary>
public sealed class ScenarioMatrixEvalTests(ITestOutputHelper output)
{
    private const double SlotSeconds = 22;

    private static readonly (string Label, BulletinScenario Scenario, Func<IReadOnlyList<TrafficCorridor>, IReadOnlyList<TrafficCorridor>>? Select, bool WeatherAlert)[] Scenarios =
    [
        // ── Sydney (busy metro): weather required, no fuel ──────────────────────────────
        ("SYD — lots of data (evening peak, all types)",
            Syd(), null, false),
        ("SYD — congestion only (delays, no works/closures)",
            Syd(), cs => cs.Where(c => c.PrimaryType == "congestion").ToList(), false),
        ("SYD — roadworks only (standing works, no live congestion)",
            Syd(), cs => cs.Where(c => c.PrimaryType is "construction" or "roadworks").ToList(), false),
        ("SYD — little data (two minor spots)",
            Syd(), cs => cs.Take(2).ToList(), false),
        ("SYD — all clear (no corridors; good runs + weather)",
            Syd(), _ => [], false),

        // ── Cairns (thin regional): weather + fuel required ─────────────────────────────
        ("CAI — little data (thin regional read, as fetched)",
            Cai(), null, false),
        ("CAI — incidents only (crashes/hazards/closures, no works)",
            Cai(), cs => cs.Where(c => c.PrimaryType is not ("construction" or "roadworks")).ToList(), false),
        ("CAI — all clear (good runs + weather + fuel)",
            Cai(), _ => [], false),
        ("CAI — weather alert (synthetic alert layered on live fetch)",
            Cai(), null, true),
    ];

    private static BulletinScenario Syd() => new(
        $"sydney-{Guid.NewGuid():N}"[..20], "Sydney", -33.8688, 151.2093, 30, SlotSeconds, MaxCorridors: 12,
        Weather: true, WeatherRequired: true,
        FillOrder: [FillSource.Weather, FillSource.MinorTraffic, FillSource.Filler]);

    private static BulletinScenario Cai() => new(
        $"cairns-{Guid.NewGuid():N}"[..20], "Cairns", -16.92, 145.77, 32, SlotSeconds, MaxCorridors: 12,
        Weather: true, Fuel: true, WeatherRequired: true, FuelRequired: true,
        FillOrder: [FillSource.Fuel, FillSource.Weather, FillSource.MinorTraffic, FillSource.Filler]);

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GenerateScenarioMatrix_LiveHere_FullPipeline_WritesMarkdown()
    {
        if (Environment.GetEnvironmentVariable("ARN_SCENARIO_MATRIX") != "1")
        {
            output.WriteLine("Skipped: set ARN_SCENARIO_MATRIX=1 and live credentials to run the scenario matrix.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Pilot-station condition matrix — live HERE, full pipeline (2026-08-21)");
        sb.AppendLine();
        sb.AppendLine("22s slots. Sydney: weather required, no fuel. Cairns: weather + fuel required.");
        sb.AppendLine("All traffic content from one live HERE fetch per market; scenario subsets are");
        sb.AppendLine("derived by filtering that fetch. The weather-alert row layers a synthetic alert");
        sb.AppendLine("onto live weather (flagged synthetic).");
        sb.AppendLine();

        int i = 0;
        foreach (var (label, scenario, select, weatherAlert) in Scenarios)
        {
            i++;
            await using var provider = BuildProvider(select, weatherAlert);
            output.WriteLine($"--- {i}. {label} ---");
            var score = await BulletinEval.EvaluateAsync(
                provider, scenario, CancellationToken.None, measureDuration: false, fullPipeline: true);
            output.WriteLine($"corridors: {score.CorridorCount}");
            output.WriteLine(score.Script);
            output.WriteLine("");

            sb.AppendLine($"## {i}. {label}");
            sb.AppendLine();
            sb.AppendLine($"corridors returned: {score.CorridorCount}");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(score.Script);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        string path = Path.Combine("output", "eval", "scenario-matrix.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString());
        output.WriteLine($"Scenario matrix written: {Path.GetFullPath(path)}");
    }

    private static ServiceProvider BuildProvider(
        Func<IReadOnlyList<TrafficCorridor>, IReadOnlyList<TrafficCorridor>>? select,
        bool weatherAlert)
    {
        // See TestConfigurationHelper.DeployedPipeline — one definition of "the pipeline that airs".
        var overrides = TestConfigurationHelper.DeployedPipeline();
        var configuration = TestConfigurationHelper.BuildConfiguration(overrides);
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(configuration);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<HereTrafficProvider>();
        services.AddHttpClient<HereGeocodingProvider>();
        services.AddHttpClient<HereRoutingProvider>();

        if (select is not null || weatherAlert)
        {
            var inner = services.BuildServiceProvider();
            if (select is not null)
                services.AddSingleton<ITrafficProvider>(sp =>
                    new FilteringTrafficProvider(inner.GetRequiredService<ITrafficProvider>(), select));
            if (weatherAlert)
                services.AddSingleton<IWeatherProvider>(sp =>
                    new AlertInjectingWeatherProvider(inner.GetRequiredService<IWeatherProvider>()));
        }
        return services.BuildServiceProvider();
    }

    /// <summary>Subsets the corridor list of the one live fetch to derive a condition.</summary>
    private sealed class FilteringTrafficProvider(
        ITrafficProvider inner,
        Func<IReadOnlyList<TrafficCorridor>, IReadOnlyList<TrafficCorridor>> select)
        : ITrafficProvider
    {
        public async Task<Result<IEnumerable<TrafficIncident>>> GetIncidentsAsync(
            CoverageArea area, CancellationToken cancellationToken = default) =>
            await inner.GetIncidentsAsync(area, cancellationToken);

        public async Task<Result<IEnumerable<TrafficCorridor>>> GetCorridorsAsync(
            CoverageArea area, int maxCorridors = 5, CancellationToken cancellationToken = default,
            IncidentExclusions? exclusions = null)
        {
            var result = await inner.GetCorridorsAsync(area, maxCorridors, cancellationToken, exclusions);
            if (result.IsFailure)
                return result;
            return Result<IEnumerable<TrafficCorridor>>.Success(select(result.Value!.ToList()));
        }

        public async Task<Result<IReadOnlyList<string>>> GetIncidentRoadNamesAsync(
            CoverageArea area, int maxCorridors = 5, CancellationToken cancellationToken = default) =>
            await inner.GetIncidentRoadNamesAsync(area, maxCorridors, cancellationToken);

        public async Task<Result<IReadOnlyList<FlowRoad>>> GetFreeFlowingRoadsAsync(
            CoverageArea area,
            int max = 5,
            CancellationToken cancellationToken = default,
            IReadOnlyList<string>? excludedRoadNames = null) =>
            await inner.GetFreeFlowingRoadsAsync(area, max, cancellationToken, excludedRoadNames);
    }

    /// <summary>Layers a synthetic weather alert onto the live weather (HERE cannot supply one on demand).</summary>
    private sealed class AlertInjectingWeatherProvider(IWeatherProvider inner) : IWeatherProvider
    {
        public async Task<Result<WeatherReport>> GetWeatherAsync(
            CoverageArea area, CancellationToken cancellationToken = default)
        {
            var result = await inner.GetWeatherAsync(area, cancellationToken);
            if (result.IsFailure)
                return result;
            return Result<WeatherReport>.Success(result.Value! with
            {
                Alerts = ["Severe thunderstorm warning for heavy rainfall and damaging winds"]
            });
        }
    }
}

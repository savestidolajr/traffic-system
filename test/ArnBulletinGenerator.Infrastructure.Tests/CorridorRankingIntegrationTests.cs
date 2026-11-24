using System.Diagnostics;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Integration tests that hit the real HERE Traffic API and output
///     both raw response stats and our processed corridor rankings.
///     Used to validate and iterate on the scoring/prioritization algorithm.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CorridorRankingIntegrationTests(ITestOutputHelper output)
{
    [Fact(Skip = "Requires live HERE API credentials")]
    public async Task Sydney_CorridorRanking_OutputRawAndProcessed()
    {
        var configOverrides = new Dictionary<string, string?>
        {
            ["Here:ApiKey"] = "YOUR_HERE_API_KEY"
        };

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));

        var configuration = TestConfigurationHelper.BuildConfiguration(configOverrides);
        services.AddInfrastructure(configuration);
        services.AddApplication();

        // In-memory repos (not needed but required by DI)
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();

        using var sp = services.BuildServiceProvider();

        var trafficProvider = sp.GetRequiredService<ITrafficProvider>();
        var coverage = CoverageArea.Create(-33.8688, 151.2093, 50.0).Value; // Sydney, 50km

        // ── Phase 1: Raw incidents ──
        output.WriteLine("═══════════════════════════════════════════════════════════════════════════");
        output.WriteLine("  CORRIDOR RANKING ANALYSIS — Sydney (50km radius)");
        output.WriteLine("═══════════════════════════════════════════════════════════════════════════");
        output.WriteLine("");

        var rawResult = await trafficProvider.GetIncidentsAsync(coverage);
        rawResult.IsSuccess.ShouldBeTrue($"HERE API should return incidents, got: {rawResult.Error}");

        var rawIncidents = rawResult.Value.ToList();
        output.WriteLine($"  ── RAW INCIDENTS ({rawIncidents.Count} total) ────────────────────────────────");
        output.WriteLine("");

        // Severity distribution
        var sevGroups = rawIncidents.GroupBy(i => i.Severity).OrderBy(g => g.Key);
        output.WriteLine("  Severity distribution:");
        foreach (var g in sevGroups)
        {
            string label = g.Key switch
            {
                1 => "Critical", 2 => "Major", 3 => "Minor", 4 => "Low", _ => $"Unknown({g.Key})"
            };
            output.WriteLine($"    {label} (sev={g.Key}): {g.Count()}");
        }

        // Type distribution
        output.WriteLine("");
        output.WriteLine("  Type distribution:");
        foreach (var g in rawIncidents.GroupBy(i => i.Type).OrderByDescending(g => g.Count()))
            output.WriteLine($"    {g.Key}: {g.Count()}");

        // FC distribution
        output.WriteLine("");
        output.WriteLine("  Functional class distribution:");
        foreach (var g in rawIncidents.GroupBy(i => i.FunctionalClass).OrderBy(g => g.Key))
        {
            string label = g.Key switch
            {
                1 => "Freeway", 2 => "Highway", 3 => "Arterial", 4 => "Collector", 5 => "Local", _ => $"FC{g.Key}"
            };
            output.WriteLine($"    FC{g.Key} ({label}): {g.Count()}");
        }

        // All raw incidents
        output.WriteLine("");
        output.WriteLine("  All raw incidents:");
        output.WriteLine($"  {"#",-4} {"Sev",-5} {"FC",-4} {"Type",-18} {"Len",-10} {"Location"}");
        output.WriteLine($"  {"─",-4} {"───",-5} {"──",-4} {"────",-18} {"───",-10} {"────────"}");
        int rank = 1;
        foreach (var i in rawIncidents.OrderBy(i => i.Severity).ThenBy(i => i.FunctionalClass)
                     .ThenByDescending(i => i.LengthMeters))
        {
            output.WriteLine(
                $"  {rank,-4} {i.Severity,-5} {i.FunctionalClass,-4} {i.Type,-18} {i.LengthMeters / 1000.0:F1} km     {i.Location}");
            rank++;
        }

        // ── Phase 2: Processed Corridors ──
        output.WriteLine("");
        output.WriteLine("  ── PROCESSED CORRIDORS ────────────────────────────────────────────────");
        output.WriteLine("");

        var sw = Stopwatch.StartNew();
        var corridorsResult = await trafficProvider.GetCorridorsAsync(coverage, 20);
        sw.Stop();

        corridorsResult.IsSuccess.ShouldBeTrue($"Corridor aggregation should succeed, got: {corridorsResult.Error}");

        var corridors = corridorsResult.Value.ToList();
        output.WriteLine($"  {corridors.Count} corridors from GetCorridorsAsync (took {sw.ElapsedMilliseconds}ms)");
        output.WriteLine("");

        output.WriteLine(
            $"  {"Rank",-5} {"Score",-7} {"Sev",-5} {"FC",-4} {"Type",-16} {"#Inc",-5} {"Len",-9} {"Delay",-7} {"Road",-25} {"Dir",-12} {"Suburb"}");
        output.WriteLine(
            $"  {"────",-5} {"─────",-7} {"───",-5} {"──",-4} {"────",-16} {"────",-5} {"───",-9} {"─────",-7} {"────",-25} {"───",-12} {"──────"}");

        for (int i = 0; i < corridors.Count; i++)
        {
            var c = corridors[i];
            string delay = c.DelayMinutes.Match(d => $"+{d}min", () => "—");
            string dir = c.Direction.Match(d => d, () => "—");
            string suburb = c.Suburb.Match(s => s, () => "—");

            output.WriteLine(
                $"  #{i + 1,-4} {c.ImpactScore,-7:F0} {c.Severity,-5} {c.FunctionalClass,-4} {c.PrimaryType,-16} {c.Incidents.Count,-5} {c.TotalLengthMeters / 1000.0:F1} km   {delay,-7} {c.RoadName,-25} {dir,-12} {suburb}");
        }

        // ── Phase 3: Issue Detection ──
        output.WriteLine("");
        output.WriteLine("  ── ISSUE DETECTION ───────────────────────────────────────────────────");
        output.WriteLine("");

        // Check for all-same severity
        int uniqueSeverities = corridors.Select(c => c.Severity).Distinct().Count();
        if (uniqueSeverities == 1)
            output.WriteLine(
                $"  ⚠ All {corridors.Count} corridors have the same severity ({corridors[0].Severity}) — severity column adds no value");

        // Check for "Unidentified Road" corridors
        var unidentified = corridors.Where(c => c.RoadName is "Unidentified Road" or "Unknown Location").ToList();
        if (unidentified.Count > 0)
            output.WriteLine(
                $"  ⚠ {unidentified.Count} corridor(s) named 'Unidentified Road' with {unidentified.Sum(c => c.Incidents.Count)} total incidents");

        // Check for misclassified major roads
        var suspectLocal = corridors.Where(c =>
            c.FunctionalClass >= 4 &&
            (c.RoadName.StartsWith("M", StringComparison.OrdinalIgnoreCase) ||
             c.RoadName.Contains("Highway", StringComparison.OrdinalIgnoreCase) ||
             c.RoadName.Contains("Freeway", StringComparison.OrdinalIgnoreCase) ||
             c.RoadName.Contains("Motorway", StringComparison.OrdinalIgnoreCase))).ToList();

        foreach (var s in suspectLocal)
            output.WriteLine($"  ⚠ '{s.RoadName}' classified as FC{s.FunctionalClass} but name suggests major road");

        // Check for no delay data
        int noDelay = corridors.Count(c => c.DelayMinutes.IsNone);
        if (noDelay > corridors.Count * 0.8)
            output.WriteLine(
                $"  ⚠ {noDelay}/{corridors.Count} corridors have no delay data — Delay column mostly empty");

        // Check for single type
        int uniqueTypes = corridors.Select(c => c.PrimaryType).Distinct().Count();
        if (uniqueTypes == 1)
            output.WriteLine(
                $"  ⚠ All corridors have the same type ({corridors[0].PrimaryType}) — Type column adds no value");

        // Score distribution
        if (corridors.Count >= 2)
        {
            double maxScore = corridors.Max(c => c.ImpactScore);
            double minScore = corridors.Min(c => c.ImpactScore);
            double range = maxScore - minScore;
            output.WriteLine("");
            output.WriteLine($"  Score range: {minScore:F0} – {maxScore:F0} (spread: {range:F0})");
            if (range < 200)
                output.WriteLine(
                    $"  ⚠ Score spread is narrow ({range:F0}) — ranking may not meaningfully differentiate corridors");
        }

        output.WriteLine("");
        output.WriteLine("═══════════════════════════════════════════════════════════════════════════");
    }
}

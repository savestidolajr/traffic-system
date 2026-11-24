using System.Globalization;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Infrastructure.Tests.Replay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Regression;

/// <summary>
///     Same-data regression guard for the 2026-08-28 pace-magnitude work, across several markets.
///     <para>
///         The input is a set of REAL HERE flow readings captured live inside one 90-second window
///         (<c>test/corpus/pace-verification/</c>, every market's <c>sourceUpdated</c> between
///         13:58:42Z and 13:59:47Z on 2026-08-28), replayed through the REAL
///         <see cref="CorridorAggregator.BuildCongestionCorridors" /> and the REAL
///         <see cref="TrafficNarrativeSummarizer.BuildEvidenceUnits" />. The configured drive-time
///         headline threshold may control how much monitored-route prose is exposed, but it must
///         never erase a measured delay from a flow corridor.
///     </para>
///     <para>
///         Disclosed stand-in: road identity here comes from HERE's <c>location.description</c>
///         label, where production resolves it from the row's own segment references through the Map
///         Attributes API. That affects which NAME a row is voiced under, never the magnitude
///         arithmetic this comparison is measuring.
///     </para>
/// </summary>
public sealed class PaceMagnitudeComparison(ITestOutputHelper output)
{
    private const int RankedCorridorCap = 6;
    private const double CongestionJamFactorThreshold = 5.0;

    private sealed record SegmentRow(
        string RoadName, double JamFactor, double LengthMeters, double SpeedMps, double FreeFlowMps,
        double Confidence, double MaxSubSegmentJamFactor, int HereFunctionalClass,
        double JamFactorMeters, double StandstillMeters,
        double StartLat, double StartLng, double EndLat, double EndLng);

    private sealed record MarketFixture(
        string Market, string? SourceUpdated, int TargetSeconds, List<SegmentRow> Segments);

    [Fact]
    public async Task PaceMagnitude_SameData_BeforeAfterAcrossMarkets()
    {
        bool live = Environment.GetEnvironmentVariable("ARN_EVAL_LIVE") == "1";
        string root = FindCorpusRoot();
        var report = new StringBuilder();
        report.AppendLine("# Pace-magnitude change — same-data before/after");
        report.AppendLine();

        foreach (string path in Directory.GetFiles(root, "*.json").OrderBy(p => p))
        {
            if (Path.GetFileName(path) == "manifest.json")
                continue;

            var fixture = JsonSerializer.Deserialize<MarketFixture>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

            var segments = fixture.Segments.Select(s => new CongestionSegment(
                s.RoadName, s.JamFactor, s.LengthMeters, s.SpeedMps, s.FreeFlowMps,
                Confidence: s.Confidence,
                MaxSubSegmentJamFactor: s.MaxSubSegmentJamFactor,
                HereFunctionalClass: s.HereFunctionalClass,
                ResolvedRoadName: s.RoadName,
                Verdict: IdentityVerdict.Unambiguous,
                JamFactorMeters: s.JamFactorMeters,
                StandstillMeters: s.StandstillMeters,
                StartPoint: (s.StartLat, s.StartLng),
                EndPoint: (s.EndLat, s.EndLng))).ToList();

            var corridors = CorridorAggregator
                .BuildCongestionCorridors(segments, [], CongestionJamFactorThreshold)
                .OrderByDescending(c => CorridorEditorialRank.Of(c))
                .Take(RankedCorridorCap)
                .ToList();

            report.AppendLine(
                $"## {fixture.Market} — {fixture.TargetSeconds}s slot · {fixture.Segments.Count} flow rows " +
                $"· HERE sourceUpdated {fixture.SourceUpdated}");
            report.AppendLine();

            if (corridors.Count == 0)
            {
                report.AppendLine("No congestion corridor qualified. Nothing to compare — this market");
                report.AppendLine("had no road at or above the admission threshold in this snapshot.");
                report.AppendLine();
                continue;
            }

            var beforeByRoad = VoicedByRoad(corridors, thresholdMinutes: 1);
            var afterByRoad = VoicedByRoad(corridors, thresholdMinutes: 5);

            report.AppendLine("| Road | pace | jam | queue | delay | BEFORE voices | AFTER voices |");
            report.AppendLine("|---|---:|---:|---:|---:|---|---|");
            foreach (var c in corridors)
            {
                string before = beforeByRoad.GetValueOrDefault(c.RoadName, "_(not in read)_");
                string after = afterByRoad.GetValueOrDefault(c.RoadName, "_(not in read)_");

                // A later production correction established that a measured delay remains evidence
                // even below the drive-time headline threshold. The threshold controls how much
                // monitored-route prose is exposed; it must not make a slow flow corridor look clear.
                int delay = c.DelayMinutes.GetValueOr(0);
                if (delay > 0)
                {
                    before.ShouldContain("delayMinutes");
                    after.ShouldContain("delayMinutes");
                }

                // A severely slow corridor must also retain at least one magnitude signal in the
                // default-threshold read. This guards the failure mode where only an extent survived.
                if (c.PaceRatio.HasValue && c.PaceRatio.Value <= 0.55)
                {
                    bool containsMagnitude = after.Contains("pace: ", StringComparison.OrdinalIgnoreCase)
                                             || after.Contains("delayMinutes", StringComparison.OrdinalIgnoreCase);
                    containsMagnitude.ShouldBeTrue(
                        $"Expected {c.RoadName} to retain pace or measured-delay magnitude.");
                }
            }

            report.AppendLine();
            report.AppendLine($"Derived description handed to the operator console (never to the writer): " +
                              $"`{corridors[0].Description}`");
            report.AppendLine();

            if (!live)
                continue;

            // Same corridors, same slot, same everything — only the rule set differs. Two real
            // generator runs so the comparison is the SCRIPT, not just the payload.
            string beforeScript = await GenerateAsync(fixture, corridors, thresholdMinutes: 1);
            string afterScript = await GenerateAsync(fixture, corridors, thresholdMinutes: 5);
            report.AppendLine("**LOW THRESHOLD (full monitored-route detail from one minute):**");
            report.AppendLine();
            report.AppendLine("> " + beforeScript.Replace("\n", " "));
            report.AppendLine();
            report.AppendLine("**DEFAULT THRESHOLD (flow-corridor delay evidence retained):**");
            report.AppendLine();
            report.AppendLine("> " + afterScript.Replace("\n", " "));
            report.AppendLine();
        }

        // A snapshot where no market produced a corridor would make the whole comparison vacuous.
        report.ToString().ShouldContain("| Road | pace |");

        output.WriteLine(report.ToString());

        string outPath = Path.Combine(AppContext.BaseDirectory, "pace-magnitude-comparison.md");
        File.WriteAllText(outPath, report.ToString());
        output.WriteLine($"[report] {outPath}");
    }

    /// <summary>
    ///     One real generator run over the supplied corridors at the supplied threshold. The service
    ///     provider is the production DI graph with credentials from <c>local.settings.json</c>, so
    ///     this is the shipped writer, not a stub.
    /// </summary>
    private static async Task<string> GenerateAsync(
        MarketFixture fixture, IReadOnlyList<TrafficCorridor> corridors, int thresholdMinutes)
    {
        var overrides = new Dictionary<string, string?>();
        FallbackReplayTests.ApplyLocalSettingsCredentials(overrides);
        await using var provider = TestConfigurationHelper.BuildRealServiceProvider(
            overrides, minimumLogLevel: LogLevel.Warning);

        using var scope = provider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IScriptGenerator>();

        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = fixture.TargetSeconds,
            JobId = $"pace-{fixture.Market}-{thresholdMinutes}",
            Location = fixture.Market,
            MarketId = Option<string>.Some(fixture.Market),
            Traffic = "unused",
            Corridors = corridors,
            AllCorridors = corridors,
            Incidents = [],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false),
            EditorialSettings = new EditorialSettings(
                EditorialScope.Global, string.Empty,
                DriveTimeHeadlineThresholdMinutes: thresholdMinutes)
        };

        var result = await generator.GenerateScriptAsync(context, CancellationToken.None);
        return result.IsSuccess && !string.IsNullOrWhiteSpace(result.Value.Text)
            ? result.Value.Text.Trim()
            : $"_(generation failed: {result.Error})_";
    }

    /// <summary>
    ///     The magnitude fields the writer actually receives for each corridor at a given monitored-
    ///     route threshold. Built from one read containing every corridor, never corridor-by-corridor,
    ///     because pace figures are rationed by a read-level budget.
    /// </summary>
    private static Dictionary<string, string> VoicedByRoad(
        IReadOnlyList<TrafficCorridor> corridors, int thresholdMinutes)
    {
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "pace-compare",
            Location = "compare",
            Traffic = "unused",
            Corridors = [.. corridors],
            Incidents = [],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false),
            EditorialSettings = new EditorialSettings(
                EditorialScope.Global, string.Empty,
                DriveTimeHeadlineThresholdMinutes: thresholdMinutes)
        };

        // Units are id-prefixed per corridor ("r3:pace"), and each corridor also emits a "road" unit
        // under the same prefix, so the prefix maps back to a road name without assuming the
        // discriminator order matches the table order.
        var byPrefix = TrafficNarrativeSummarizer
            .BuildEvidenceUnits(context, mirrorProseSelection: false)
            .GroupBy(u => u.Id.Split(':')[0], StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in byPrefix)
        {
            string? road = group.FirstOrDefault(u => u.Field == "road")?.Value;
            if (road is null)
                continue;

            var magnitude = group
                .Where(u => u.Field is "delayMinutes" or "pace" or "road feel" or "extentLength")
                .Select(u => $"{u.Field}: {u.Value}")
                .ToList();

            result[road] = magnitude.Count == 0 ? "_(no magnitude)_" : string.Join("<br>", magnitude);
        }

        return result;
    }

    private static string Pct(Option<double> ratio) =>
        ratio.HasValue
            ? ((int)Math.Round(ratio.Value * 100)).ToString(CultureInfo.InvariantCulture) + "%"
            : "n/a";

    private static string FindCorpusRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "test", "corpus", "pace-verification");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("test/corpus/pace-verification not found");
    }
}

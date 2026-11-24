using System.Text.Json;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Calibration;
using ArnBulletinGenerator.Infrastructure.Serialization;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit.Abstractions;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     One-off local reproduction of deployed job <c>24c74dca5ca644c0b00e2d338c50bf95</c>
///     (brisbane / KIIS 97.3, FallbackUsed, chronic streak 3) — investigates why the review-refine
///     loop never converges on a script voicing the two separate Gateway Motorway roadworks spots
///     as separate items, and why 4 full generation attempts are needed despite a rich (20-corridor,
///     10-incident) data set. No LLM captures existed for this job (outside the rolling capture
///     window), so the writer input below is reconstructed from the job's persisted
///     <c>CorridorsJson</c>/<c>AllIncidentsJson</c> (real HERE incident text, cross-checked word for
///     word against the reviewer's own quoted source text in <c>LogsJson</c> — e.g. "the source
///     gives unleaded 91 cheapest at $1.59 in Chermside West" and "Between Sandgate Road and Bruce
///     Highway") plus the honest per-job "20 corridor(s), 10 incident(s)" fetch line. Three
///     flow-derived congestion corridors (WYNNUM ROAD, Springfield Parkway, Miles Platting Road)
///     had no discrete HERE incident record (<c>IncidentCount=0</c>) and are reconstructed from
///     their own corridor <c>Description</c> ("at a standstill") — the exact phrase the reviewer
///     confirms is the correct source wording. Bruce Highway's 2 incident keys are absent from
///     <c>AllIncidentsJson</c> (a fallback-copy-chain gap) and are reconstructed from the corridor
///     summary only, noted inline. Unlike the other 9 corridors, Bruce Highway is NOT central to
///     the investigated defect (never named in the reviewer's feedback), so this gap does not
///     affect the finding.
///     <para>Run: <c>ARN_REPLAY_FALLBACK=1 dotnet test test/ArnBulletinGenerator.Infrastructure.Tests
///     --filter Brisbane_Investigate_DraftReviewConvergence -l "console;verbosity=detailed"</c>.
///     Captures every real Draft/Review/Refine/Safety call's exact rendered prompt + raw response
///     to <c>output/brisbane-investigation/llm_calls/</c> (mirrors the <c>Debug:CaptureLlmCalls</c>
///     blob format, kept local — never written to shared dev storage).</para>
/// </summary>
public sealed class BrisbaneRootCauseTests(ITestOutputHelper output)
{
    private const string JobId = "24c74dca5ca644c0b00e2d338c50bf95-local";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Brisbane_Investigate_DraftReviewConvergence()
    {
        if (Environment.GetEnvironmentVariable("ARN_REPLAY_FALLBACK") != "1")
        {
            output.WriteLine("Skipped: set ARN_REPLAY_FALLBACK=1 (+ Azure OpenAI creds) to run this investigation.");
            return;
        }

        // ARN_INVESTIGATE_REASONING: "none" | "minimal" | "low" | "medium" | "high" — overrides
        // AiSettings:AzureAI:ReasoningEffort for EVERY stage (Draft/Review/Refine/Safety/Attribute
        // share one client). Unset = deployed default (ResponsesTransport.ResolveReasoningEffort:
        // "none" for gpt-5.x with no producer feedback). Used to A/B whether raising the reviewer's
        // reasoning budget reduces hallucinated fabrication claims and review-refine churn.
        string? reasoningOverride = Environment.GetEnvironmentVariable("ARN_INVESTIGATE_REASONING");
        string runTag = string.IsNullOrWhiteSpace(reasoningOverride) ? "default" : reasoningOverride;
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string outDir = Path.Combine("output", "brisbane-investigation", $"{runTag}-{stamp}");
        string callsDir = Path.Combine(outDir, "llm_calls");
        Directory.CreateDirectory(callsDir);
        string historyWorkPath = Path.Combine(outDir, "synthesis-history.work.json");
        File.Copy(FallbackReplayFixture.SynthesisHistorySeedPath(), historyWorkPath, true);

        var capture = new LocalFileLlmCallCapture(callsDir);
        await using var provider = BuildProvider(historyWorkPath, capture, reasoningOverride);

        var jobs = provider.GetRequiredService<IJobRepository>();
        var job = Job.Create(JobId, "brisbane", "KIIS 97.3", 30, DateTime.UtcNow).Value;
        await jobs.SaveJobAsync(job);

        var context = BuildSnapshot().ToContext(JobId) with
        {
            ScriptOnly = false,
            VoiceId = Option<string>.Some("9KesAUJoZzq3yyT2bJ3G")
        };

        var orchestrator = provider.GetRequiredService<IBroadcastOrchestrator>();
        var result = await orchestrator.OrchestrateAsync(context);

        var finalJob = (await jobs.GetJobAsync(JobId)).Value;
        string scriptText = finalJob?.ScriptText.Match(s => s, () => "(none)") ?? "(job not found)";
        int scriptWords = finalJob?.ScriptText.Match(
            s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, () => 0) ?? 0;
        output.WriteLine($"Pipeline result: {(result.IsFailure ? $"FAILURE: {result.Error}" : "success")}");
        output.WriteLine($"Final status: {finalJob?.Status}");
        output.WriteLine($"Final script ({scriptWords} words): {scriptText}");
        output.WriteLine("");
        output.WriteLine("=== Full LogsJson-equivalent timeline ===");
        foreach (var log in finalJob?.Logs ?? [])
            output.WriteLine($"{log.At:HH:mm:ss.fffffff} | {log.Message}");

        string logPath = Path.Combine(outDir, "logs_timeline.txt");
        File.WriteAllLines(logPath,
            (finalJob?.Logs ?? []).Select(l => $"{l.At:HH:mm:ss.fffffff} | {l.Message}"));
        output.WriteLine("");
        output.WriteLine($"Full timeline written to {Path.GetFullPath(logPath)}");
        output.WriteLine($"Captured LLM calls written to {Path.GetFullPath(callsDir)}");
    }

    // ── Writer input reconstruction (see class doc for provenance of every field) ──────────────

    private static GeneratorInputSnapshot BuildSnapshot()
    {
        // NOTE: incident key 3580718921341326740 for Bruce Highway not present in AllIncidentsJson (fallback-copy gap) — reconstructed from the corridor summary only.
        var i1 = new TrafficIncident(
            Id: "3580718921341326740", Description: "roadworks citybound", Severity: 1,
            Location: "Bruce Highway", Type: "construction", FunctionalClass: 1);
        // NOTE: incident key 4252568569751917526 for Bruce Highway not present in AllIncidentsJson (fallback-copy gap) — reconstructed from the corridor summary only.
        var i2 = new TrafficIncident(
            Id: "4252568569751917526", Description: "roadworks citybound", Severity: 1,
            Location: "Bruce Highway", Type: "construction", FunctionalClass: 1);

        // Real HERE data (AllIncidentsJson) — matches the reviewer's own quoted source text exactly.
        var i3 = new TrafficIncident(
            Id: "3179114667743551042", Description: "Between Sandgate Road and Bruce Highway - Road construction", Severity: 1,
            Location: "Gateway Mwy", Type: "construction", LengthMeters: 4810,
            FunctionalClass: 1);
        var i4 = new TrafficIncident(
            Id: "2503237784937245931", Description: "Between Gateway Motorway and Dohles Rocks Road - Road construction", Severity: 1,
            Location: "Gateway Mwy", Type: "construction", LengthMeters: 1419,
            FunctionalClass: 1);

        var i5 = new TrafficIncident(
            Id: "2543287847163343322", Description: "At Beenleigh - Redland Bay Road - Road construction", Severity: 1,
            Location: "Pacific Mwy", Type: "construction", LengthMeters: 130,
            FunctionalClass: 1);

        var i6 = new TrafficIncident(
            Id: "1445149215322968910", Description: "Between Ipswich Motorway and River Road - Road construction", Severity: 1,
            Location: "M2", Type: "construction", LengthMeters: 885,
            FunctionalClass: 1);
        var i7 = new TrafficIncident(
            Id: "867731840632095691", Description: "Ramp to Logan Motorway (Eastbound) - Entry frontage road closed", Severity: 1,
            Location: "M2", Type: "construction", LengthMeters: 449,
            FunctionalClass: 1);

        var i8 = new TrafficIncident(
            Id: "2983454572527730160", Description: "Between River Road and Mt Crosby Road - Road construction", Severity: 1,
            Location: "Warrego Hwy", Type: "construction", LengthMeters: 3759,
            FunctionalClass: 1);

        var i9 = new TrafficIncident(
            Id: "3276848777331685567", Description: "At Baudesert Road - Broken-down vehicle", Severity: 1,
            Location: "Logan Mwy", Type: "disabledVehicle", LengthMeters: 0,
            FunctionalClass: 1);

        var i10 = new TrafficIncident(
            Id: "4102809099995603426", Description: "At Webster Road - Road construction", Severity: 1,
            Location: "Gympie Rd", Type: "construction", LengthMeters: 68,
            FunctionalClass: 2);


        List<TrafficCorridor> corridors =
        [
            new("Bruce Highway", 1, 1, 7300.0, 254.8, "construction", "roadworks citybound",
                [i1, i2], Direction: Option<string>.Some("citybound"), PriorityBoosted: true),
            // The corridor at the centre of the investigation: TWO genuinely separate HERE
            // incidents (Sandgate Rd<->Bruce Hwy, and Gateway Motorway<->Dohles Rocks Rd) folded
            // by CorridorAggregator into ONE TrafficCorridor with a single "Gateway Motorway
            // (westbound)" header and 2 "Details:" bullets underneath (see CorridorFormatter).
            new("Gateway Motorway", 1, 1, 6200.0, 254.8, "construction", "roadworks westbound",
                [i3, i4], Direction: Option<string>.Some("westbound"), PriorityBoosted: true),
            new("Pacific Motorway", 1, 1, 100.0, 244.99250543199554, "construction", "roadworks",
                [i5], PriorityBoosted: true),
            new("M2", 1, 1, 1300.0, 196, "construction", "roadworks westbound",
                [i6, i7], Direction: Option<string>.Some("westbound"), Suburb: Option<string>.Some("Riverview")),
            new("Warrego Highway", 1, 1, 3800.0, 190, "construction", "roadworks westbound",
                [i8], Direction: Option<string>.Some("westbound")),
            new("Logan Motorway", 1, 1, 0, 180, "disabledVehicle", "breakdown", [i9]),
            new("Gympie Road", 1, 2, 100.0, 167.33003565082495, "construction", "roadworks", [i10]),
            new("WYNNUM ROAD", 1, 3, 3000, 165, "congestion", "at a standstill", []),
            new("Springfield Parkway", 1, 3, 5200.0, 165, "congestion", "at a standstill", []),
            new("Miles Platting Road", 1, 3, 4800.0, 165, "congestion", "at a standstill", [])
        ];

        return new GeneratorInputSnapshot
        {
            TargetDurationSeconds = 30,
            Location = "brisbane",
            City = Option<string>.Some("Brisbane"),
            MarketId = Option<string>.Some("brisbane"),
            Content = new BulletinContentSelection(true, true, true),
            Corridors = corridors,
            // Story Bridge and Ipswich Motorway are named as "priority roads flowing well" across
            // every attempt's review feedback for this job — not present in CorridorsJson (only
            // incident-bearing/congested roads are), so sourced from the reviewer's own quotes.
            GoodRunRoads = ["Story Bridge", "Ipswich Motorway"],
            // Weather from the job's own LogsJson ("Weather fetched: Sunny. Mild., 19°C").
            Weather = Option<WeatherReport>.Some(new WeatherReport("Sunny. Mild.", 19, Option<string>.None, 0, [])),
            // Fuel prices quoted verbatim by the reviewer as the correct source values (attempt 2,
            // round 1 feedback): "the source gives unleaded 91 cheapest at $1.59 in Chermside West
            // and diesel cheapest at $1.83 in Kedron".
            Fuel = Option<FuelPriceReport>.Some(new FuelPriceReport(
            [
                new FuelPriceSummary("Unleaded 91", 1.59, 1.59, 1, CheapestStationSuburb: "Chermside West"),
                new FuelPriceSummary("Diesel", 1.83, 1.83, 1, CheapestStationSuburb: "Kedron")
            ]))
        };
    }

    // ── Minimal DI (adapted from FallbackReplayTests.BuildProvider, + local LLM-call capture) ──

    private static ServiceProvider BuildProvider(string historyWorkPath, ILlmCallCapture capture, string? reasoningOverride)
    {
        var overrides = new Dictionary<string, string?> { ["Providers:OutputProvider"] = "Console" };
        FallbackReplayTests.ApplyLocalSettingsCredentials(overrides);
        if (!string.IsNullOrWhiteSpace(reasoningOverride))
            overrides["AiSettings:AzureAI:ReasoningEffort"] = reasoningOverride;
        var configuration = TestConfigurationHelper.BuildConfiguration(
            TestConfigurationHelper.DeployedPipeline(overrides));

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddInfrastructure(configuration);
        services.AddApplication();
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<HereTrafficProvider>();
        services.AddHttpClient<HereGeocodingProvider>();
        services.AddHttpClient<HereRoutingProvider>();

        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();
        services.AddSingleton<IVoiceCalibrationRepository, InMemoryVoiceCalibrationRepository>();
        services.AddSingleton<ISynthesisHistoryRepository>(sp =>
            new InMemorySynthesisHistoryRepository(historyWorkPath,
                sp.GetRequiredService<ILogger<InMemorySynthesisHistoryRepository>>()));
        services.AddSingleton<IAudioSynthesizer>(sp => new SimulatedElevenLabsSynthesizer(
            FallbackReplayFixture.SynthesisHistorySeedPath(),
            sp.GetRequiredService<ILogger<SimulatedElevenLabsSynthesizer>>()));

        // Real BlobLlmCallCapture needs Azure Storage; substitute a local-file writer instead so
        // every real Draft/Review/Refine/Safety call's exact rendered prompt + raw response lands
        // on disk for inspection without touching shared dev infra.
        services.AddSingleton(capture);

        services.AddSingleton<RecordingFallbackService>();
        services.AddSingleton<IBulletinFallbackService>(sp => sp.GetRequiredService<RecordingFallbackService>());

        var notifier = Substitute.For<INotificationService>();
        notifier.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        services.AddSingleton(notifier);

        var contentSafety = Substitute.For<IContentSafetyFilter>();
        contentSafety.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        services.AddSingleton(contentSafety);

        var director = Substitute.For<IScriptDirector>();
        director.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<string>.Success((string)ci[0]));
        services.AddSingleton(director);


        return services.BuildServiceProvider();
    }
}

/// <summary>Writes every captured LLM call to a local JSON file, mirroring BlobLlmCallCapture's
/// naming (<c>{time}-{stage}-attempt{n}.json</c>) without any Azure Storage dependency.</summary>
internal sealed class LocalFileLlmCallCapture(string directory) : ILlmCallCapture
{
    public Task CaptureAsync(LlmCallRecord record, CancellationToken cancellationToken = default)
    {
        string name = $"{record.TimestampUtc:HHmmss.fff}-{record.Stage}-attempt{record.Attempt}.json";
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions(JsonDefaults.Storage) { WriteIndented = true }));
        return Task.CompletedTask;
    }
}

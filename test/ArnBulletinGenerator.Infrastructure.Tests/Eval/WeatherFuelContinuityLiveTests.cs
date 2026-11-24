using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit.Abstractions;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     THROWAWAY local verification (delete after running). Exercises the full BroadcastOrchestrator
///     against REAL Azure OpenAI (draft/review/refine) with deterministic stubbed traffic/weather/fuel
///     and a controlled clock, to prove two fixes end-to-end:
///     (1) a SELECTED weather + fuel source appears even on a BUSY bulletin (sufficient incidents);
///     (2) same-day continuity links a second bulletin to the first across the UTC/Sydney day boundary
///         (prior at 2026-06-29 23:56Z = 2026-06-30 09:56 Sydney; second at 2026-06-30 00:08Z).
///     Run with: ARN_EVAL_LIVE=1 + AZURE_OPENAI_ENDPOINT/API_KEY/MODEL set.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WeatherFuelContinuityLiveTests(ITestOutputHelper output)
{
    private const string Market = "Melbourne Drive Traffic";
    private const string SkipReason =
        "Live verification: requires real Azure OpenAI credentials + ARN_EVAL_LIVE=1; run manually by removing Skip.";

    [Fact(Skip = SkipReason)]
    public async Task WeatherFuelIncluded_AndContinuity_AcrossUtcBoundary()
    {
        var fakeTime = new ManualClock();
        await using var sp = BuildProvider(fakeTime);

        var orchestrator = sp.GetRequiredService<IBroadcastOrchestrator>();
        var jobRepo = sp.GetRequiredService<IJobRepository>();
        var coverage = CoverageArea.Create(-37.8136, 144.9631, 25).Value;
        var content = new BulletinContentSelection(Traffic: true, Weather: true, Fuel: true);

        // ── Bulletin 1: first of day. 2026-06-29 23:56Z = 09:56 Sydney (30 Jun). ──
        fakeTime.SetUtcNow(new DateTime(2026, 6, 29, 23, 56, 0, DateTimeKind.Utc));
        await CreateJobAsync(jobRepo, "live-b1", fakeTime);
        var r1 = await orchestrator.OrchestrateAsync(MakeRequest("live-b1", coverage, content), Phase.FetchAndGenerate, CancellationToken.None);
        r1.IsSuccess.ShouldBeTrue($"Bulletin 1 orchestration failed: {r1.Error}");
        var job1 = (await jobRepo.GetJobAsync("live-b1")).Value;
        string script1 = job1.ScriptText.GetValueOr("");

        // ── Bulletin 2: same Sydney day, NEXT UTC day. 2026-06-30 00:08Z = 10:08 Sydney. ──
        fakeTime.SetUtcNow(new DateTime(2026, 6, 30, 0, 8, 0, DateTimeKind.Utc));
        await CreateJobAsync(jobRepo, "live-b2", fakeTime);
        var r2 = await orchestrator.OrchestrateAsync(MakeRequest("live-b2", coverage, content), Phase.FetchAndGenerate, CancellationToken.None);
        r2.IsSuccess.ShouldBeTrue($"Bulletin 2 orchestration failed: {r2.Error}");
        var job2 = (await jobRepo.GetJobAsync("live-b2")).Value;
        string script2 = job2.ScriptText.GetValueOr("");

        // ── Evidence ──
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Weather/Fuel + Continuity live verification\n");
        sb.AppendLine($"Bulletin 1 created (UTC): {job1.CreatedAt:o}  |  IncidentChangeSummary: " +
                      Describe(job1.IncidentChangeSummary));
        sb.AppendLine($"\n## Bulletin 1 (first of day)\n\n{script1}\n");
        sb.AppendLine($"Bulletin 2 created (UTC): {job2.CreatedAt:o}  |  IncidentChangeSummary: " +
                      Describe(job2.IncidentChangeSummary));
        sb.AppendLine($"\n## Bulletin 2 (same Sydney day, next UTC day)\n\n{script2}\n");
        string artifact = Path.Combine(AppContext.BaseDirectory, "weather-fuel-continuity-live.md");
        await File.WriteAllTextAsync(artifact, sb.ToString());
        output.WriteLine(sb.ToString());
        output.WriteLine($"Artifact: {artifact}");

        // ── (1) Weather + fuel appear in BOTH bulletins despite busy traffic ──
        foreach (var (label, script) in new[] { ("1", script1), ("2", script2) })
        {
            string lower = script.ToLowerInvariant();
            (lower.Contains("rain") || lower.Contains("degrees") || lower.Contains("wet") ||
             lower.Contains("cool") || lower.Contains("wind") || lower.Contains("14"))
                .ShouldBeTrue($"Bulletin {label} is missing the selected WEATHER content:\n{script}");
            script.Contains('$').ShouldBeTrue($"Bulletin {label} is missing the selected FUEL price:\n{script}");
        }

        // ── (2a) Continuity MECHANISM (deterministic): the manual second bulletin computed an
        //         incident delta against the first's persisted state. On the pre-fix code the
        //         manual trigger carried no timezone, so the delta returned None (summary absent).
        job1.IncidentChangeSummary.HasValue.ShouldBeTrue("Bulletin 1 incident delta did not run.");
        job1.IncidentChangeSummary.Value.NewCount.ShouldBeGreaterThan(0, "Bulletin 1 should see all incidents as NEW.");
        job2.IncidentChangeSummary.HasValue.ShouldBeTrue(
            "Bulletin 2 incident delta did not run — the manual trigger's timezone did not resolve (the bug).");
        job2.IncidentChangeSummary.Value.NewCount.ShouldBe(0,
            "Bulletin 2 should recognise the first bulletin's incidents as ongoing (NewCount=0), proving the " +
            "same-day window spanned the UTC boundary.");

        // ── (2b) Continuity END-TO-END: the second script references the earlier read. ──
        string[] markers = ["still", "continues", "continuing", "remains", "earlier", "ongoing", "yet to clear"];
        markers.Any(m => script2.ToLowerInvariant().Contains(m))
            .ShouldBeTrue($"Bulletin 2 did not reference the earlier bulletin:\n{script2}");
    }

    private static BroadcastRequest MakeRequest(string jobId, CoverageArea coverage, BulletinContentSelection content)
    {
        // NOTE: no TimezoneId — exercises the manual-trigger path (DefaultTimezone fallback).
        return new BroadcastRequest
        {
            JobId = jobId,
            Location = Market,
            TargetDurationSeconds = 30,
            Coverage = coverage,
            Content = content,
            ScriptOnly = true // testing script content (weather/fuel/continuity), not audio
        };
    }

    private static async Task CreateJobAsync(IJobRepository repo, string jobId, ManualClock clock)
    {
        var job = Job.Create(jobId, Market, Market, 30, clock.GetUtcNow().UtcDateTime).Value;
        await repo.SaveJobAsync(job);
    }

    private static string Describe(Option<IncidentChangeSummary> s) =>
        s.Match(v => $"New={v.NewCount}, Updated={v.UpdatedCount}, Cleared={v.ClearedCount}", () => "(none)");

    private static ServiceProvider BuildProvider(TimeProvider clock)
    {
        var config = TestConfigurationHelper.BuildConfiguration(
            TestConfigurationHelper.DeployedPipeline(new Dictionary<string, string?>
            {
                ["Providers:OutputProvider"] = "Console"
            }));

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddInfrastructure(config);
        services.AddApplication();

        // Shared in-memory state across both runs.
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();

        // Deterministic clock (overrides AddApplication's TimeProvider.System).
        services.AddSingleton(clock);

        // Busy, deterministic traffic — 7 corridors, all with incidents.
        var traffic = Substitute.For<ITrafficProvider>();
        traffic.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(BusyCorridors()));
        services.AddSingleton<ITrafficProvider>(traffic);

        // Weather present (drizzle, 14°, rain about) — selected, must surface even when busy.
        var weather = Substitute.For<IWeatherProvider>();
        weather.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Success(new WeatherReport(
                "Light rain. Overcast. Cool.", 14.5, Option<string>.Some("fresh north wind"),
                82, [], Option<string>.None)));
        services.AddSingleton<IWeatherProvider>(weather);

        // Fuel present (cheapest unleaded $1.59).
        var fuel = Substitute.For<IFuelPriceProvider>();
        fuel.GetFuelPricesAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<FuelPriceReport>.Success(new FuelPriceReport(
            [
                new FuelPriceSummary("unleaded", 1.59, 1.68, 12),
                new FuelPriceSummary("diesel", 1.78, 1.85, 12)
            ])));
        services.AddSingleton<IFuelPriceProvider>(fuel);

        // Audio: no ElevenLabs — echo the script, fixed duration.
        var audio = Substitute.For<IAudioPipelineService>();
        audio.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(),
                Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>())
            .Returns(ci => Result<AudioSynthesisResult>.Success(new AudioSynthesisResult([], (string)ci[0], 30.0)));
        audio.FineTuneToTargetDuration(Arg.Any<byte[]>(), Arg.Any<double>()).Returns(ci => (byte[])ci[0]);
        services.AddSingleton<IAudioPipelineService>(audio);

        var duration = Substitute.For<IDurationEstimator>();
        duration.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        duration.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        services.AddSingleton<IDurationEstimator>(duration);

        var calibration = Substitute.For<IVoiceCalibrationService>();
        calibration.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new CalibrationMetrics(150, 5.8, 2.3));
        services.AddSingleton<IVoiceCalibrationService>(calibration);

        var notifier = Substitute.For<INotificationService>();
        notifier.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        services.AddSingleton<INotificationService>(notifier);

        // Safety: allow (avoid Azure Content Safety dependency; not under test).
        var contentSafety = Substitute.For<IContentSafetyFilter>();
        contentSafety.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        services.AddSingleton<IContentSafetyFilter>(contentSafety);

        var scriptSafety = Substitute.For<IScriptSafetyReviewer>();
        scriptSafety.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));
        services.AddSingleton<IScriptSafetyReviewer>(scriptSafety);

        // Director passthrough (not under test; avoid extra LLM calls).
        var director = Substitute.For<IScriptDirector>();
        director.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<string>.Success((string)ci[0]));
        services.AddSingleton<IScriptDirector>(director);


        return services.BuildServiceProvider();
    }

    private static List<TrafficCorridor> BusyCorridors() =>
    [
        new("West Gate Freeway", 1, 1, 4000, 95, "congestion", "Heavy congestion citybound",
            [new TrafficIncident("wg1", "Heavy congestion", 2, "West Gate Freeway at Williamstown Road", "congestion")],
            Direction: Option<string>.Some("citybound"), DriveTimeMinutes: 28, NormalDriveTimeMinutes: 14),
        new("Monash Freeway", 1, 1, 3500, 90, "accident", "Crash blocking a lane",
            [new TrafficIncident("mon1", "Multi-vehicle crash blocking the right lane", 1, "Monash Freeway at Toorak Road", "accident")],
            Direction: Option<string>.Some("inbound")),
        new("Eastern Freeway", 2, 1, 3000, 80, "construction", "Roadworks reducing lanes",
            [new TrafficIncident("east1", "Roadworks, one lane closed", 3, "Eastern Freeway at Bulleen Road", "construction")]),
        new("Tullamarine Freeway", 2, 1, 2500, 75, "congestion", "Slow outbound",
            [new TrafficIncident("tul1", "Slow traffic", 3, "Tullamarine Freeway at Bell Street", "congestion")],
            Direction: Option<string>.Some("outbound")),
        new("Princes Highway", 2, 3, 2000, 60, "accident", "Breakdown in the left lane",
            [new TrafficIncident("pri1", "Breakdown blocking the left lane", 2, "Princes Highway at Warrigal Road", "accident")]),
        new("Hoddle Street", 3, 3, 1500, 55, "congestion", "Backed up",
            [new TrafficIncident("hod1", "Heavy congestion", 3, "Hoddle Street at Johnston Street", "congestion")]),
        new("Punt Road", 3, 3, 1200, 50, "congestion", "Slow southbound",
            [new TrafficIncident("punt1", "Slow traffic", 3, "Punt Road at Swan Street", "congestion")],
            Direction: Option<string>.Some("southbound"))
    ];

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public void SetUtcNow(DateTime utc) => _now = new DateTimeOffset(utc, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }
}

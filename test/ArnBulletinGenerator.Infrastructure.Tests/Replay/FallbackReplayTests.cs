using System.Text.Json;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Calibration;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit.Abstractions;
using ArnBulletinGenerator.Application.Broadcasting;

using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Infrastructure.Tests.Replay;

/// <summary>
///     Replays captured dev FallbackUsed scenarios (<c>test/corpus/fallback-replay/</c>) through the
///     REAL orchestrator — real LLM generation/review/refine agents, real word-target math, real
///     audio pipeline and slot gate — with ElevenLabs replaced by
///     <see cref="SimulatedElevenLabsSynthesizer" /> (durations fitted from 850 real dev reads).
///     This reproduces the full retry/agent flow locally with zero TTS cost so the content-limited
///     fallback dynamics can be studied and any fix measured against the exact scenarios that
///     failed in dev.
///     <para>Excluded from CI (<c>Category=Integration</c>). Requires Azure OpenAI credentials
///     (taken from <c>src/ArnBulletinGenerator.Functions/local.settings.json</c> when not in env)
///     and <c>ARN_REPLAY_FALLBACK=1</c>.</para>
///     <para>Knobs: <c>ARN_REPLAY_JOB</c> (job-id prefix filter), <c>ARN_REPLAY_LIMIT</c> (max
///     fixtures), <c>ARN_REPLAY_REPEATS</c> (runs per fixture; default 1), <c>ARN_REPLAY_RETRY_BIAS</c> /
///     <c>ARN_REPLAY_WORD_OVERSHOOT</c> (override Broadcast options to replay under historical config,
///     e.g. bias 1.0 = pre-2026-07-06), <c>ARN_REPLAY_NOISE_SIGMA</c> (Monte Carlo duration jitter,
///     seconds).</para>
/// </summary>
public sealed class FallbackReplayTests(ITestOutputHelper output)
{
    private static readonly Regex GenAttempt = new(@"^Generation attempt (\d+)/(\d+): target (\d+) words", RegexOptions.Compiled);
    private static readonly Regex ShortBulletin = new(@"^Short bulletin: only ([\d.]+)s of natural speech", RegexOptions.Compiled);
    private static readonly Regex Synthesizing = new(@"^Synthesizing audio \((\d+) words", RegexOptions.Compiled);
    private static readonly Regex FilledToSlot = new(@"^Audio filled to slot: ([\d.]+)s natural read", RegexOptions.Compiled);

    // BroadcastOrchestrator.GoodRunScriptCap — the automatic-path good-run cap.
    private const int GoodRunScriptCap = 6;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FallbackCorpus_Replay_ThroughRealRetryLoop()
    {
        if (Environment.GetEnvironmentVariable("ARN_REPLAY_FALLBACK") != "1")
        {
            output.WriteLine("Skipped: set ARN_REPLAY_FALLBACK=1 (+ Azure OpenAI creds) to replay the fallback corpus.");
            return;
        }

        var fixtures = FallbackReplayFixture.LoadAll();
        fixtures.ShouldNotBeEmpty("No fixtures under test/corpus/fallback-replay/.");

        // ARN_REPLAY_JOB: comma-separated job-id prefixes, or the literal "panel" for the
        // committed stratified iteration panel — 10 fixtures spanning near-miss / moderate /
        // deep shortfall bands, TEST STATION + real markets, and the 15s slot. A census of all
        // 58 fixtures adds mostly LLM noise, not signal (50 TEST STATION fixtures share one
        // coverage/voice); the panel is the fast A/B unit (~8 min per config arm).
        string? jobFilter = Environment.GetEnvironmentVariable("ARN_REPLAY_JOB");
        if (string.Equals(jobFilter, "panel", StringComparison.OrdinalIgnoreCase))
            jobFilter = string.Join(',', IterationPanel);
        if (!string.IsNullOrWhiteSpace(jobFilter))
        {
            string[] prefixes = jobFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            fixtures = fixtures
                .Where(f => prefixes.Any(p => f.JobId.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("ARN_REPLAY_LIMIT"), out int limit) && limit > 0)
            fixtures = fixtures.Take(limit).ToList();

        fixtures.ShouldNotBeEmpty("Job filter/limit removed every fixture.");
        output.WriteLine($"Replaying {fixtures.Count} fallback scenario(s) through the real retry loop...");

        string outDir = Path.Combine("output", "replay");
        Directory.CreateDirectory(outDir);
        // Working copy: InMemorySynthesisHistoryRepository persists on Record — never write back
        // into the committed seed.
        string historyWorkPath = Path.Combine(outDir, "synthesis-history.work.json");
        File.Copy(FallbackReplayFixture.SynthesisHistorySeedPath(), historyWorkPath, true);

        // ITERATION SPEED IS A FEATURE OF THIS HARNESS (2026-08-23). A blanket 20s sleep between
        // every run used to serialise the panel: 10 fixtures spent 200s asleep, and ARN_REPLAY_REPEATS=3
        // spent TEN MINUTES asleep, on top of real agent latency. The sleep was guarding an AOAI rate
        // limit on the draft call, but a rate limit is a concurrency problem, not a delay problem —
        // so bound the concurrency and stagger the launches instead. A harness nobody can afford to
        // run is a harness whose verdicts go unchecked, which is how the config drift this corpus
        // exists to catch survived for months.
        int concurrency = int.TryParse(Environment.GetEnvironmentVariable("ARN_REPLAY_CONCURRENCY"), out int c)
            ? Math.Max(1, c)
            : 4;
        // Stagger only the STARTS, so the draft calls of a wave do not land together. At
        // concurrency=1 the old sequential-with-pause behaviour is still available for a
        // rate-limit-sensitive arm via ARN_REPLAY_PAUSE.
        int staggerSeconds = int.TryParse(Environment.GetEnvironmentVariable("ARN_REPLAY_STAGGER"), out int st)
            ? Math.Max(0, st)
            : 2;
        int pauseSeconds = int.TryParse(Environment.GetEnvironmentVariable("ARN_REPLAY_PAUSE"), out int p)
            ? Math.Max(0, p)
            : concurrency > 1 ? 0 : 20;

        int repeats = int.TryParse(Environment.GetEnvironmentVariable("ARN_REPLAY_REPEATS"), out int configuredRepeats)
            ? Math.Max(1, configuredRepeats)
            : 1;
        output.WriteLine($"concurrency={concurrency} stagger={staggerSeconds}s pause={pauseSeconds}s repeats={repeats}");

        using var gate = new SemaphoreSlim(concurrency);
        var ordered = fixtures.Select((fixture, index) => (fixture, index)).ToList();
        var completed = new ReplayBatch?[ordered.Count];
        // Per-stage model calls across the whole panel. Reported because meanAttempts counts OUTER
        // retries only: it read 1.4-1.8 all through 2026-08-25 while the same runs spent 7-9 LLM
        // calls per bulletin, and the harness had no way to show the difference.
        var llmCalls = new System.Collections.Concurrent.ConcurrentDictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        await Task.WhenAll(ordered.Select(async entry =>
        {
            await gate.WaitAsync();
            try
            {
                if (staggerSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(staggerSeconds * (entry.index % concurrency)));

                // Per-fixture working copy of the synthesis history. Concurrent runs sharing one file
                // would race on Record and silently cross-contaminate calibration between markets —
                // the exact confound ARN_REPLAY_REPEATS exists to measure.
                string fixtureHistoryPath = Path.Combine(
                    outDir, $"synthesis-history.work.{entry.fixture.JobId[..10]}.json");
                File.Copy(FallbackReplayFixture.SynthesisHistorySeedPath(), fixtureHistoryPath, true);
                byte[] baseline = await File.ReadAllBytesAsync(fixtureHistoryPath);

                var runs = new List<ReplayResult>(repeats);
                for (int repeat = 0; repeat < repeats; repeat++)
                {
                    if (repeat > 0 && pauseSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(pauseSeconds));
                    // Repeated runs must start from the same history snapshot; otherwise the first
                    // run's newly recorded synthesis changes calibration inputs for the next and
                    // hides the agent wobble this knob is intended to measure.
                    if (repeats > 1)
                        await File.WriteAllBytesAsync(fixtureHistoryPath, baseline);
                    await using var provider = BuildProvider(fixtureHistoryPath);
                    runs.Add(await ReplayOneAsync(provider, entry.fixture));

                    // The provider is per-fixture, so the counter has to be drained here. This is
                    // the number meanAttempts was standing in for and getting wrong by ~5x.
                    var counted = provider.GetRequiredService<CountingLlmCallCapture>();
                    foreach (string stage in new[] { "Draft", "Review", "RefineSentence", "Safety" })
                        llmCalls.AddOrUpdate(stage, counted.For(stage), (_, n) => n + counted.For(stage));
                }

                completed[entry.index] = new ReplayBatch(entry.fixture, runs);
            }
            finally
            {
                gate.Release();
            }
        }));

        // Emit in fixture order, never completion order, so two runs of the same panel diff cleanly.
        var batches = new List<ReplayBatch>(ordered.Count);
        foreach (var batch in completed)
        {
            if (batch is null)
                continue;
            batches.Add(batch);
            ReplayResult first = batch.Runs[0];
            string flap = repeats > 1 && batch.IsFlapped
                ? $" | FLAPPED {batch.AiredCount}/{repeats} aired"
                : string.Empty;
            output.WriteLine(
                $"{batch.Fixture.JobId[..10]} {batch.Fixture.Market,-22} dev: {batch.Fixture.DevOutcome.Attempts} att, best {batch.Fixture.DevOutcome.BestNaturalSec?.ToString("F1") ?? "-"}s, {batch.Fixture.DevOutcome.Status}" +
                $" | replay: {first.Attempts.Count} att, best {first.BestNaturalSec?.ToString("F1") ?? "-"}s, {first.EndState}{flap}");
        }

        // TWO DIFFERENT QUESTIONS, REPORTED SEPARATELY (2026-08-23, corrected same day).
        //
        // Leading with slots FILLED misleads: a harness wired to a padding-prone path, or handed a
        // generous synthetic filler set, scores a HIGHER fill precisely because it fabricates, and
        // reads as an improvement. Both happened here in one day.
        //
        // But a single "agreement with dev" number is just as wrong, and was the first attempt at
        // this. The corpus spans six weeks of pipeline change: the negatives were captured 2-4 Jul,
        // the positives 10-13 Aug. Today's code SHOULD air a July failure — punishing it for that
        // measures nothing but the age of the fixture. So:
        //
        //   airedWhenDevAired    — THE fidelity gate. Dev shipped it, so the replay must too.
        //                          A miss is a real regression or a harness that is too harsh, and
        //                          this signal did not exist at all until the corpus gained
        //                          positives, because every fixture was a failure.
        //   airedWhenDevFellBack — improvement CANDIDATES, deliberately not scored. Dev could not
        //                          air these; the replay can. That is either six weeks of genuine
        //                          progress or over-generosity, and only reading the script tells
        //                          you which. Never treat it as pass/fail in either direction.
        var allResults = batches.SelectMany(b => b.Runs).ToList();
        int fixtureCount = batches.Count;
        int filled = allResults.Count(IsReplayAired);
        int devAiredCount = batches.Count(b => DevAired(b.Fixture));
        int devFellBackCount = fixtureCount - devAiredCount;
        int airedWhenDevAired = batches.Count(b => DevAired(b.Fixture) && IsReplayAired(b.Runs[0]));
        int airedWhenDevFellBack = batches.Count(b => !DevAired(b.Fixture) && IsReplayAired(b.Runs[0]));
        var filledByRun = Enumerable.Range(0, repeats)
            .Select(index => batches.Count(b => IsReplayAired(b.Runs[index])))
            .ToList();
        var fidelityByRun = Enumerable.Range(0, repeats)
            .Select(index => batches.Count(b => DevAired(b.Fixture) && IsReplayAired(b.Runs[index])))
            .ToList();
        var fills = allResults
            .Where(r => r.BestNaturalSec.HasValue)
            .Select(r => r.BestNaturalSec!.Value / r.Fixture.Snapshot.TargetDurationSeconds)
            .ToList();
        string summaryMetrics = repeats == 1
            ? $"airedWhenDevAired={airedWhenDevAired}/{devAiredCount} (FIDELITY GATE) " +
              $"airedWhenDevFellBack={airedWhenDevFellBack}/{devFellBackCount} (improvement candidates, unscored) " +
              $"filled={filled} ({100.0 * filled / fixtureCount:F0}%) "
            : $"airedWhenDevAired={FormatMetricBand(fidelityByRun, devAiredCount, repeats)} (FIDELITY GATE) " +
              $"filled={FormatMetricBand(filledByRun, fixtureCount, repeats, includeDenominator: false)} ";
        string callMix = string.Join(" ", new[] { "Draft", "Review", "RefineSentence", "Safety" }
            .Where(s => llmCalls.GetValueOrDefault(s) > 0)
            .Select(s => $"{s.ToLowerInvariant()}={llmCalls.GetValueOrDefault(s)}"));
        output.WriteLine(
            $"ARM SUMMARY | fixtures={fixtureCount} " +
            summaryMetrics +
            $"meanBestFill={(fills.Count > 0 ? fills.Average() : 0):P0} " +
            $"meanAttempts={allResults.Average(r => r.Attempts.Count):F1} " +
            $"llmCallsPerBulletin={llmCalls.Values.Sum() / (double)Math.Max(1, fixtureCount * repeats):F1} " +
            (callMix.Length > 0 ? $"({callMix}) " : "") +
            $"| overshoot={Environment.GetEnvironmentVariable("ARN_REPLAY_WORD_OVERSHOOT") ?? "(appsettings)"} " +
            $"bias={Environment.GetEnvironmentVariable("ARN_REPLAY_RETRY_BIAS") ?? "(appsettings)"} " +
            $"capacity={Environment.GetEnvironmentVariable("ARN_REPLAY_CAPACITY") ?? "(fixture)"} " +
            $"extend={Environment.GetEnvironmentVariable("ARN_REPLAY_EXTEND") ?? "off"}");


        string stamp = $"{DateTime.UtcNow:yyyyMMddTHHmmssZ}";
        string mdPath = Path.Combine(outDir, $"fallback-replay-{stamp}.md");
        string jsonPath = Path.Combine(outDir, $"fallback-replay-{stamp}.json");
        File.WriteAllText(mdPath, RenderPack(batches));
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(
            batches.SelectMany(b => b.Runs.Select((r, index) => r.ToReportRow(repeats == 1 ? null : index + 1))),
            new JsonSerializerOptions { WriteIndented = true }));
        output.WriteLine($"Replay pack written: {Path.GetFullPath(mdPath)}");

        batches.SelectMany(b => b.Runs).ShouldAllBe(r => r.Completed, "Every replay should run the pipeline to a terminal decision.");
    }

    /// <summary>
    ///     Mixed-polarity stratified panel: five jobs DEV aired and five ended in FallbackUsed,
    ///     spanning near-miss/moderate/deep reads plus 15s, 22s and 30s slots. The mix matters:
    ///     with an all-negative panel, devAgreement is arithmetically <c>10 - filled</c> and
    ///     measures nothing that the fill count did not already say.
    /// </summary>
    private static readonly string[] IterationPanel =
    [
        // DEV fallback (negative): near-miss, moderate, deep, and short-slot coverage.
        "9ef6501ce98e468ea048eb1b798abd70", // TEST STATION, 30s
        "2ab24e833b1d49de96303438d21db9f1", // portlincoln, 30s
        "1d438b96ab3a4b2e83d84be9c5453b1d", // Gold Coast, 30s
        "11eee9bf4360405db0e4a12920133ba8", // sydney, 30s
        "0871f23415164164a31ed6093eb65c3b", // TEST STATION, 15s
        // DEV aired (positive): same slot spread with different markets.
        "16f5f75a28d1455bb187bf95376d85d7", // portlincoln, 15s
        "c5b3bf0ad6874bb8ac3df785dbae81e3", // sydney, 22s
        "bb0489ba2d284d22a788ea59f8d857c9", // cairns-4ca, 22s
        "132924f589854e91a62d2deb9114d89d", // brisbane, 30s
        "a41ed02d02384766a3ec07c4300336f9", // adelaide, 30s
    ];

    // ── Provider ─────────────────────────────────────────────────────

    private static ServiceProvider BuildProvider(string historyWorkPath)
    {
        // The replay's whole claim is "this is what production would have done", so it runs
        // production's pipeline: structured matrix + the live drift-checked Foundry agents.
        var overrides = TestConfigurationHelper.DeployedPipeline(new Dictionary<string, string?>
        {
            ["Providers:OutputProvider"] = "Console"
        });
        ApplyLocalSettingsCredentials(overrides);

        string? slowdown = Environment.GetEnvironmentVariable("ARN_REPLAY_SLOWDOWN");
        if (!string.IsNullOrWhiteSpace(slowdown))
            overrides["SpeechDuration:MaxSlowDownFactor"] = slowdown;
        string? pad = Environment.GetEnvironmentVariable("ARN_REPLAY_PAD");
        if (!string.IsNullOrWhiteSpace(pad))
            overrides["SpeechDuration:MaxSilencePadSeconds"] = pad;
        string? bed = Environment.GetEnvironmentVariable("ARN_REPLAY_BED");
        if (!string.IsNullOrWhiteSpace(bed))
            overrides["SpeechDuration:UseTrimmedBedCloser"] = bed == "1" ? "true" : bed;
        string? dynSlot = Environment.GetEnvironmentVariable("ARN_REPLAY_DYNAMIC_SLOT");
        if (!string.IsNullOrWhiteSpace(dynSlot))
            overrides["SpeechDuration:UseDynamicSlot"] = dynSlot == "1" ? "true" : dynSlot;
        string? maxReviewRefine = Environment.GetEnvironmentVariable("ARN_REPLAY_MAX_REVIEW_REFINE");
        if (!string.IsNullOrWhiteSpace(maxReviewRefine))
            overrides["Broadcast:MaxReviewRefineRetries"] = maxReviewRefine;
        string? draftAgent = Environment.GetEnvironmentVariable("ARN_REPLAY_DRAFT_AGENT");
        if (!string.IsNullOrWhiteSpace(draftAgent))
            overrides["AiSettings:AzureAI:DraftAgentName"] = draftAgent;
        string? foundryEndpoint = Environment.GetEnvironmentVariable("ARN_REPLAY_FOUNDRY_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(foundryEndpoint))
            overrides["AiSettings:AzureAI:FoundryProjectEndpoint"] = foundryEndpoint;

        var configuration = TestConfigurationHelper.BuildConfiguration(overrides);

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

        // In-memory state; no Azure table/blob dependencies.
        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();
        services.AddSingleton<IVoiceCalibrationRepository, InMemoryVoiceCalibrationRepository>();
        services.AddSingleton<ISynthesisHistoryRepository>(sp =>
            new InMemorySynthesisHistoryRepository(historyWorkPath,
                sp.GetRequiredService<ILogger<InMemorySynthesisHistoryRepository>>()));

        // ElevenLabs replaced by the history-fitted simulator; calibration flows through it too,
        // so calibrated WPM/SPS is self-consistent with simulated durations (mirrors how dev
        // calibrated against real ElevenLabs). The simulator reads the COMMITTED seed, never the
        // work copy: replay-recorded rows must not drift the duration model mid-run (determinism).
        services.AddSingleton<IAudioSynthesizer>(sp => new SimulatedElevenLabsSynthesizer(
            FallbackReplayFixture.SynthesisHistorySeedPath(),
            sp.GetRequiredService<ILogger<SimulatedElevenLabsSynthesizer>>()));

        // Counts what a bulletin actually costs in model calls. meanAttempts counts OUTER retries,
        // so it read 1.4-1.8 while the same runs were spending 7-9 LLM calls per bulletin — see
        // CountingLlmCallCapture. Registered as a singleton so one panel run accumulates across
        // every fixture, and overrides the infrastructure's scoped Blob capture.
        services.AddSingleton<CountingLlmCallCapture>();
        services.AddSingleton<ILlmCallCapture>(sp => sp.GetRequiredService<CountingLlmCallCapture>());

        // Terminal fallback recorder (the decision is the signal under test).
        services.AddSingleton<RecordingFallbackService>();
        services.AddSingleton<IBulletinFallbackService>(sp => sp.GetRequiredService<RecordingFallbackService>());

        // Out-of-scope externals.
        var notifier = Substitute.For<INotificationService>();
        notifier.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        services.AddSingleton(notifier);

        var contentSafety = Substitute.For<IContentSafetyFilter>();
        contentSafety.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        services.AddSingleton(contentSafety);

        // Delivery styles were resolved per-station in dev and are not part of the retry dynamics;
        // pass through to avoid extra LLM calls (dev logs show tags rarely applied anyway).
        var director = Substitute.For<IScriptDirector>();
        director.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<string>.Success((string)ci[0]));
        services.AddSingleton(director);


        // Station configuration must default to production semantics — but "production semantics"
        // means the station's OWN config, not a generous stand-in for it. The synthetic set below
        // answers a different question ("what could filler do if a station had a rich set?") and
        // making it the default converted that experiment into a false baseline: every panel fixture
        // filled its slot (10/10, meanBestFill 99%) and devAgreement collapsed to 5/10, because the
        // harness aired all five jobs dev could not. Dev's markets each carry 4-8 REAL statements
        // (verified 2026-08-23 via GET /api/config/export), so the faithful source is the fixture's
        // captured config, applied in ReplayOneAsync. This registration now only supplies the
        // synthetic set when explicitly asked for it.
        //
        // ARN_REPLAY_FILLER: unset/absent = fixture's own captured config (production semantics);
        // "graded" = the synthetic escalating set, for capacity experiments; "0" = force no filler.
        string fillerMode = Environment.GetEnvironmentVariable("ARN_REPLAY_FILLER") ?? string.Empty;
        if (fillerMode == "graded" || fillerMode == "1")
        {
            var fillerRepo = Substitute.For<IFillerStatementsRepository>();
            IReadOnlyList<FillerStatement> graded =
            [
                new("f5", "Take it easy out there."),
                new("f9", "Remember to keep a safe distance in the wet."),
                new("f14", "School zones are back in force this week, so please slow right down near the schools."),
                new("f19", "If you are heading out later, allow yourself a few extra minutes and check your route before you leave home."),
                new("f25", "Roadworks continue across parts of the network this month, so expect changed conditions, reduced speed limits, and the odd lane closure in a few spots, so plan ahead where you can."),
            ];
            fillerRepo.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Result<IReadOnlyList<FillerStatement>>.Success(graded));
            services.RemoveAll<IFillerStatementsRepository>();
            services.AddSingleton(fillerRepo);
        }

        if (fillerMode != "0")
        {
            // The REAL IFillerAudioCacheService needs Table+Blob Storage this in-memory harness
            // has none of — every ResolveAsync call would fail against the dummy "test" account,
            // silently leaving BroadcastOrchestrator's resolvedFiller empty and filler top-up a
            // no-op even when statements resolve fine. Substitute a fake that reuses the
            // already-registered simulated synthesizer so filler duration is estimated on the
            // SAME fitted model as traffic audio. Registered regardless of where the STATEMENTS
            // come from, because without it captured config would be silently inert too.
            services.RemoveAll<IFillerAudioCacheService>();
            services.AddSingleton<IFillerAudioCacheService, SimulatedFillerAudioCacheService>();
        }

        return services.BuildServiceProvider();

    }

    /// <summary>
    ///     Azure OpenAI + HERE credentials from the Functions local.settings.json when not already
    ///     present in the environment — makes the replay self-sufficient on a dev machine.
    /// </summary>
    internal static void ApplyLocalSettingsCredentials(Dictionary<string, string?> overrides)
    {

        string path = Path.Combine(Corpus.CorpusPaths.RepoRoot(),
            "src", "ArnBulletinGenerator.Functions", "local.settings.json");
        if (!File.Exists(path))
            return;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("Values", out var values))
            return;
        foreach (var prop in values.EnumerateObject())
        {
            bool wanted = prop.Name.StartsWith("AiSettings__AzureAI__", StringComparison.Ordinal)
                          || prop.Name.StartsWith("Here__", StringComparison.Ordinal);
            if (!wanted)
                continue;
            string key = prop.Name.Replace("__", ":");

            // The deployed snapshot wins. local.settings.json exists here to supply the things the
            // snapshot deliberately excludes — secrets — and nothing else. Letting it also carry
            // behaviour (a model name, an endpoint, a per-role override) reintroduces exactly the
            // problem the snapshot solves: a harness quietly running something production is not.
            if (overrides.ContainsKey(key))
                continue;

            if (prop.Name.StartsWith("AiSettings__AzureAI__", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")))
                continue; // env-mapped creds win for AOAI
            overrides[key] = prop.Value.GetString();
        }
    }

    // ── Replay ───────────────────────────────────────────────────────

    private async Task<ReplayResult> ReplayOneAsync(ServiceProvider provider, FallbackReplayFixture fixture)
    {
        var jobs = provider.GetRequiredService<IJobRepository>();
        var fallbacks = provider.GetRequiredService<RecordingFallbackService>();


        var job = Job.Create(fixture.JobId, fixture.Market, fixture.Station ?? fixture.Market,
            fixture.Snapshot.TargetDurationSeconds, DateTime.UtcNow).Value;
        await jobs.SaveJobAsync(job);

        // ARN_REPLAY_PREWARM=1: seed calibration to the voice's TRUE simulated pace at the job's
        // exact model+settings BEFORE the measured run — isolates "cold-start (150wpm seed) vs
        // already-calibrated" as the causal variable. Without it the in-memory calibration repo is
        // empty, so every replay runs cold and the orchestrator falls back to Broadcast:WordsPerMinute
        // (150). Seeded from traffic-density prose (not the generic reference phrase) so the SPW/WPM
        // reflects real bulletin syllable density.
        if (Environment.GetEnvironmentVariable("ARN_REPLAY_PREWARM") == "1"
            && !string.IsNullOrWhiteSpace(fixture.VoiceConfig.VoiceId))
        {
            var synth = provider.GetRequiredService<IAudioSynthesizer>();
            var estimator = provider.GetRequiredService<IDurationEstimator>();
            var calibRepo = provider.GetRequiredService<IVoiceCalibrationRepository>();
            var vs = fixture.VoiceConfig.Settings ?? VoiceSettings.Default;
            const string sample =
                "Warringah Freeway northbound is heavy from the harbour bridge with delays through " +
                "the lane closures near Falcon Street. The M4 motorway is congested citybound past " +
                "Parramatta, and the Gore Hill Freeway is slow approaching the tunnel with queues " +
                "building on the approaches to the Anzac Bridge this morning.";
            var seedSynth = await synth.SynthesizeAsync(sample, Option<VoiceSettings>.Some(vs),
                Option<string>.Some(fixture.VoiceConfig.VoiceId),
                string.IsNullOrWhiteSpace(fixture.VoiceConfig.Model)
                    ? Option<string>.None : Option<string>.Some(fixture.VoiceConfig.Model!));
            double seedDur = estimator.GetDuration(seedSynth.Value.Audio);
            int seedWords = WordCounter.Count(sample);
            int seedSyll = SyllableCounter.CountSyllables(sample);
            var seed = new VoiceCalibration(vs.GetConfigurationKey(fixture.VoiceConfig.VoiceId),
                fixture.VoiceConfig.VoiceId, vs)
            {
                Provider = "ElevenLabs",
                RunCount = 20,
                SyllablesPerSecond = seedSyll / seedDur,
                SyllablesPerMinute = seedSyll / seedDur * 60.0,
                SyllablesPerWord = (double)seedSyll / seedWords,
                WordsPerMinute = seedWords / seedDur * 60.0,
                CalibratedAt = DateTime.UtcNow,
                ValidFor = TimeSpan.FromDays(30)
            };
            await calibRepo.SaveCalibrationAsync(seed, CancellationToken.None);
        }

        // ARN_REPLAY_CAPACITY=<N>: re-run rundown selection from the fixture's FULL fetched
        // corridor pool at slot capacity N (BulletinContentSelector — the orchestrator's automatic
        // path), instead of replaying dev's pinned selection (capacity 6 for a 30s slot). This is
        // the slot-capacity experiment: dev's CalculateMaxCorridors assumes 5s per rundown item;
        // measured speech is ~3s/item, so a 30s slot plausibly holds ~9-10 items.
        var snapshot = fixture.Snapshot;
        if (int.TryParse(Environment.GetEnvironmentVariable("ARN_REPLAY_CAPACITY"), out int capacity)
            && capacity > 0 && fixture.FullCorridorPool is { Count: > 0 } pool)
        {
            var selection = BulletinContentSelector.Select(pool, snapshot.FlowPool, capacity, GoodRunScriptCap);
            snapshot = snapshot with
            {
                Corridors = selection.Corridors,
                GoodRunRoads = selection.GoodRuns.Select(f => f.RoadName).ToList()
            };
        }

        // ARN_REPLAY_WEATHER=1: force Content.Weather on for this replay — most real markets run
        // with weather enabled; some captured fixtures happened to have it off at capture time
        // (schedule-level toggle), which understates what a typical production read looks like.
        // If the fixture has no captured WeatherReport at all, inject a plausible synthetic one
        // (same spirit as the corpus's existing "fuel=synthetic-typical-prices" fixtures) so the
        // {{#if weather}} block in draft.md actually renders something to write from.
        if (Environment.GetEnvironmentVariable("ARN_REPLAY_WEATHER") == "1")
        {
            var weather = snapshot.Weather.HasValue
                ? snapshot.Weather
                : Option<WeatherReport>.Some(new WeatherReport("Partly cloudy", 18, Option<string>.None, 10, []));
            snapshot = snapshot with { Content = snapshot.Content with { Weather = true }, Weather = weather };
            output.WriteLine($"[ARN_REPLAY_WEATHER] {fixture.JobId[..10]} weather forced on" +
                              (snapshot.Weather.HasValue && !fixture.Snapshot.Weather.HasValue ? " (synthetic — fixture had none)" : " (fixture's own captured data)"));
        }

        // Station config defaults to the FIXTURE'S OWN captured config, which is what production
        // semantics actually means. Falling back to the registered repository is only meaningful
        // when something was deliberately substituted for it (ARN_REPLAY_FILLER=graded): against
        // this harness's dummy "test" storage account the real repository just fails, and reporting
        // that as station config would overstate the run. Labels must stay honest here, because the
        // whole point of the 2026-08-23 work is that a report which misdescribes its own inputs is
        // worse than no report.
        string fillerMode = Environment.GetEnvironmentVariable("ARN_REPLAY_FILLER") ?? string.Empty;
        var fillerStatements = Array.Empty<FillerStatement>() as IReadOnlyList<FillerStatement>;
        string stationConfigSource = "none";
        if (fillerMode != "0")
        {
            if (snapshot.FillerStatements.Count > 0)
            {
                fillerStatements = snapshot.FillerStatements;
                stationConfigSource = fixture.Fidelity.StationConfigSource is "live-station-config"
                    ? "live-station-config"
                    : "job-snapshot";
                output.WriteLine($"[filler] {fixture.JobId[..10]} {fillerStatements.Count} statement(s) from fixture ({stationConfigSource})");
            }
            else
            {
                var fillerResult = await provider.GetRequiredService<IFillerStatementsRepository>()
                    .GetStatementsAsync(fixture.Market);
                fillerStatements = fillerResult.IsSuccess ? fillerResult.Value : [];
                if (fillerStatements.Count > 0)
                    stationConfigSource = fillerMode is "graded" or "1" ? "graded-synthetic" : "repository";
                output.WriteLine($"[filler] {fixture.JobId[..10]} {fillerStatements.Count} statement(s) from {(fillerStatements.Count > 0 ? stationConfigSource : "nothing resolvable")}");
            }
        }

        // Bookends follow the same precedence as filler, and must NOT clobber the provenance the
        // filler branch already established. An earlier version assigned "job-snapshot" here
        // unconditionally, so cairns-4ca reported its live-captured config as though it had come out
        // of the job record — the console said live-station-config and the written pack said
        // job-snapshot for the same run. A provenance label that disagrees with itself is worse than
        // none, because the pack is what gets read weeks later.
        var bookends = snapshot.Bookends;
        bool bookendsResolved = bookends.HasValue;
        if (bookendsResolved)
        {
            if (stationConfigSource is "none")
                stationConfigSource = fixture.Fidelity.StationConfigSource is "live-station-config"
                    ? "live-station-config"
                    : "job-snapshot";
        }
        else
        {
            var scheduleResult = await provider.GetRequiredService<IScheduleRepository>()
                .GetScheduleAsync(fixture.Market);
            if (scheduleResult.IsSuccess)
            {
                // IAdHocStationConfigResolver delegates bookend resolution to the same
                // StationConfigResolver used by SchedulePollerService; using it here keeps
                // rotation, token substitution and fail-soft archive handling identical.
                var stationConfig = await provider.GetRequiredService<IAdHocStationConfigResolver>()
                    .ResolveAsync(fixture.Market, fixture.VoiceConfig.Model, CancellationToken.None);
                bookends = Option.OfNullable(stationConfig.Bookends);
                bookendsResolved = bookends.HasValue;
                if (bookendsResolved || stationConfigSource is "none")
                    stationConfigSource = "live-station-config";
                if (!bookendsResolved)
                    output.WriteLine($"[BOOKENDS] {fixture.JobId[..10]} station config resolved no bookends; leaving None");
            }
            else
            {
                output.WriteLine($"[BOOKENDS] {fixture.JobId[..10]} no schedule to resolve current station bookends: {scheduleResult.Error}; leaving None");
            }
        }
        output.WriteLine($"[BOOKENDS] {fixture.JobId[..10]} source={stationConfigSource}, resolved={(bookendsResolved ? "yes" : "no")}");

        var context = snapshot.ToContext(fixture.JobId) with
        {
            ScriptOnly = false,
            VoiceId = string.IsNullOrWhiteSpace(fixture.VoiceConfig.VoiceId)
                ? Option<string>.None
                : Option<string>.Some(fixture.VoiceConfig.VoiceId),
            Model = string.IsNullOrWhiteSpace(fixture.VoiceConfig.Model)
                ? Option<string>.None
                : Option<string>.Some(fixture.VoiceConfig.Model!),
            VoiceSettings = fixture.VoiceConfig.Settings is null
                ? Option<VoiceSettings>.None
                : Option<VoiceSettings>.Some(fixture.VoiceConfig.Settings),
            PaceBoostWpm = fixture.VoiceConfig.PaceBoostWpm,
            StationId = string.IsNullOrWhiteSpace(fixture.Station)
                ? Option<string>.None
                : Option<string>.Some(fixture.Station!),
            FillerStatements = fillerStatements,
            Bookends = bookends
        };

        var orchestrator = provider.GetRequiredService<IBroadcastOrchestrator>();
        string? pipelineError = null;
        try
        {
            var result = await orchestrator.OrchestrateAsync(context);
            if (result.IsFailure)
                pipelineError = result.Error;
        }
        catch (Exception ex)
        {
            pipelineError = $"EXCEPTION: {ex.Message}";
        }

        var finalJob = (await jobs.GetJobAsync(fixture.JobId)).Value;
        var logs = finalJob?.Logs.Select(l => l.Message).ToList() ?? [];
        return ParseReplay(fixture, finalJob, logs, fallbacks, pipelineError,
            stationConfigSource, bookendsResolved, fillerStatements.Count);
    }
    private static ReplayResult ParseReplay(FallbackReplayFixture fixture, Job? job,
        IReadOnlyList<string> logs, RecordingFallbackService fallbacks, string? pipelineError,
        string stationConfigSource, bool bookendsResolved, int fillerStatementCount)
    {
        var attempts = new List<ReplayAttempt>();
        ReplayAttempt? current = null;
        double? filledNatural = null;
        foreach (string message in logs)
        {
            if (GenAttempt.Match(message) is { Success: true } g)
            {
                current = new ReplayAttempt(int.Parse(g.Groups[1].Value), int.Parse(g.Groups[3].Value));
                attempts.Add(current);
            }
            else if (Synthesizing.Match(message) is { Success: true } s && current is not null)
            {
                current.SynthWords = int.Parse(s.Groups[1].Value);
            }
            else if (ShortBulletin.Match(message) is { Success: true } sb && current is not null)
            {
                current.NaturalSec = double.Parse(sb.Groups[1].Value);
            }
            else if (FilledToSlot.Match(message) is { Success: true } f)
            {
                filledNatural = double.Parse(f.Groups[1].Value);
                if (current is not null && current.NaturalSec is null)
                    current.NaturalSec = filledNatural;
            }
        }

        bool fellBack = fallbacks.Decisions.Any(d => d.JobId == fixture.JobId);
        string endState = job?.Status.ToString() ?? "unknown";
        if (fellBack)
            endState = "FallbackUsed (content-limited)";
        else if (filledNatural.HasValue)
            endState = $"{endState} (slot filled, natural {filledNatural:F1}s)";

        string? finalScript = job is { } j && j.ScriptText.HasValue ? j.ScriptText.Value : null;
        return new ReplayResult(fixture, attempts, endState, fellBack, logs, pipelineError,
            Completed: job is not null && (fellBack || filledNatural.HasValue || pipelineError is not null
                                           || job.Status is JobStatus.WaitingApproval or JobStatus.Failed),
            FinalScript: finalScript,
            StationConfigSource: stationConfigSource,
            BookendsResolved: bookendsResolved,
            FillerStatementCount: fillerStatementCount);
    }

    // ── Reporting ────────────────────────────────────────────────────

    private static bool IsReplayAired(ReplayResult result) =>
        !result.FellBack && result.PipelineError is null && result.EndState.Contains("slot filled");

    /// <summary>
    ///     Whether DEV actually put this fixture's job to air. The discriminator is the terminal
    ///     status: <c>FallbackUsed</c> means dev gave up and the filler cart covered the slot.
    /// </summary>
    /// <remarks>
    ///     Deliberately not paired with a single "agreement" helper. Whether a replay SHOULD match
    ///     dev depends on which side of the fixture it is: a job dev aired must air (fidelity), a job
    ///     dev failed six weeks ago may legitimately air now (improvement). Collapsing both into one
    ///     equality made a pipeline improvement register as a fidelity failure.
    /// </remarks>
    private static bool DevAired(FallbackReplayFixture fixture) =>
        !string.Equals(fixture.DevOutcome.Status, "FallbackUsed", StringComparison.OrdinalIgnoreCase);

    private static string FormatMetricBand(
        IReadOnlyList<int> values, int denominator, int repeats, bool includeDenominator = true)
    {
        // Median is intentional: these are small integer counts, and a single stochastic run can
        // move one fixture across the gate. A mean would manufacture fractional precision around
        // exactly the +/-1 wobble observed with identical config (filled=2, then filled=3,
        // 2026-08-23).
        int[] ordered = values.OrderBy(v => v).ToArray();
        double median = ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2.0;
        string centre = Math.Abs(median - Math.Round(median)) < 0.001
            ? ((int)Math.Round(median)).ToString()
            : median.ToString("F1");
        string count = includeDenominator ? $"{centre}/{denominator}" : centre;
        return $"{count} ({100.0 * median / denominator:F0}%) [{ordered[0]}-{ordered[^1]} over {repeats} runs]";
    }

    private static string FormatBestFillBand(ReplayBatch batch)
    {
        double slot = batch.Fixture.Snapshot.TargetDurationSeconds;
        var values = batch.Runs
            .Where(r => r.BestNaturalSec.HasValue)
            .Select(r => r.BestNaturalSec!.Value / slot)
            .OrderBy(v => v)
            .ToArray();
        if (values.Length == 0)
            return "-";
        if (values.Length == 1)
            return $"{values[0]:P0}";
        double median = values.Length % 2 == 1
            ? values[values.Length / 2]
            : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2.0;
        return $"{median:P0} [{values[0]:P0}-{values[^1]:P0}]";
    }

    private static string RenderPack(IReadOnlyList<ReplayBatch> batches)
    {
        int repeats = batches.Count == 0 ? 1 : batches[0].Runs.Count;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Fallback corpus replay — dev vs local (simulated ElevenLabs, real agents)");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:u}. Repeats: {repeats}. Config knobs: " +
                      $"RetryBias={Environment.GetEnvironmentVariable("ARN_REPLAY_RETRY_BIAS") ?? "(appsettings)"}, " +
                      $"Overshoot={Environment.GetEnvironmentVariable("ARN_REPLAY_WORD_OVERSHOOT") ?? "(appsettings)"}, " +
                      $"NoiseSigma={Environment.GetEnvironmentVariable("ARN_REPLAY_NOISE_SIGMA") ?? "0"}");
        sb.AppendLine("Caveat: identical config has produced filled=2 then filled=3 (2026-08-23); " +
                      "with one run, a one-fixture comparison is within the known +/-1 agent wobble and is not significant.");
        sb.AppendLine();
        sb.AppendLine("| Job | Market | Dev attempts | Dev best fill | Dev end | Replay attempts | Replay best fill | Replay end |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (ReplayBatch batch in batches)
        {
            ReplayResult r = batch.Runs[0];
            double slot = r.Fixture.Snapshot.TargetDurationSeconds;
            string devFill = r.Fixture.DevOutcome.BestNaturalSec is { } d ? $"{d / slot:P0}" : "-";
            string replayEnd = r.EndState;
            if (batch.IsFlapped)
                replayEnd += $" — FLAPPED {batch.AiredCount}/{repeats} aired";
            sb.AppendLine(
                $"| {r.Fixture.JobId[..10]} | {r.Fixture.Market} | {r.Fixture.DevOutcome.Attempts} | {devFill} | {r.Fixture.DevOutcome.Status} " +
                $"| {r.Attempts.Count} | {FormatBestFillBand(batch)} | {replayEnd} |");
        }

        foreach (ReplayBatch batch in batches)
        {
            ReplayResult first = batch.Runs[0];
            sb.AppendLine();
            sb.AppendLine($"## {first.Fixture.JobId} — {first.Fixture.Market} ({first.Fixture.Snapshot.TargetDurationSeconds:F0}s slot)");
            sb.AppendLine($"Fidelity: incidents {first.Fixture.Fidelity.IncidentsRecovered} recovered / " +
                          $"{first.Fixture.Fidelity.IncidentsSynthesized} synthesized; weather={first.Fixture.Fidelity.WeatherSource}; " +
                          $"fuel={first.Fixture.Fidelity.FuelSource}; goodRuns/flowPool lost={first.Fixture.Fidelity.GoodRunsAndFlowPoolLost}; " +
                          $"stationConfig={first.StationConfigSource}; bookends={(first.BookendsResolved ? "yes" : "no")}; " +
                          $"filler={first.FillerStatementCount}");
            sb.AppendLine($"Dev:    targets [{string.Join(", ", first.Fixture.DevOutcome.TargetWordsSeq)}], " +
                          $"words [{string.Join(", ", first.Fixture.DevOutcome.SynthWordsSeq.Select(w => w?.ToString() ?? "-"))}], " +
                          $"natural [{string.Join(", ", first.Fixture.DevOutcome.NaturalSecSeq.Select(n => n?.ToString("F1") ?? "-"))}] → {first.Fixture.DevOutcome.Status}");
            if (batch.IsFlapped)
                sb.AppendLine($"**FLAPPED {batch.AiredCount}/{repeats} aired** — this fixture can produce a spurious A/B verdict.");
            foreach (var item in batch.Runs.Select((r, index) => new { Result = r, Index = index + 1 }))
            {
                ReplayResult r = item.Result;
                int index = item.Index;
                string runPrefix = repeats > 1 ? $"Run {index}: " : string.Empty;
                sb.AppendLine($"{runPrefix}Replay: targets [{string.Join(", ", r.Attempts.Select(a => a.TargetWords))}], " +
                              $"words [{string.Join(", ", r.Attempts.Select(a => a.SynthWords?.ToString() ?? "-"))}], " +
                              $"natural [{string.Join(", ", r.Attempts.Select(a => a.NaturalSec?.ToString("F1") ?? "-"))}] → {r.EndState}");
                if (r.PipelineError is not null)
                    sb.AppendLine($"Pipeline error: {r.PipelineError}");
                sb.AppendLine();
                sb.AppendLine($"<details><summary>{runPrefix}Replay log</summary>");
                sb.AppendLine();
                foreach (string message in r.Logs)
                    sb.AppendLine($"- {message}");
                sb.AppendLine();
                sb.AppendLine("</details>");
            }
        }

        return sb.ToString();
    }

    private sealed class ReplayAttempt(int number, int targetWords)
    {
        public int Number { get; } = number;
        public int TargetWords { get; } = targetWords;
        public int? SynthWords { get; set; }
        public double? NaturalSec { get; set; }
    }

    private sealed record ReplayBatch(
        FallbackReplayFixture Fixture,
        IReadOnlyList<ReplayResult> Runs)
    {
        public int AiredCount => Runs.Count(IsReplayAired);
        public bool IsFlapped => AiredCount > 0 && AiredCount < Runs.Count;
    }

    private sealed record ReplayResult(
        FallbackReplayFixture Fixture,
        IReadOnlyList<ReplayAttempt> Attempts,
        string EndState,
        bool FellBack,
        IReadOnlyList<string> Logs,
        string? PipelineError,
        bool Completed,
        string StationConfigSource = "none",
        bool BookendsResolved = false,
        int FillerStatementCount = 0,
        string? FinalScript = null)
    {
        public double? BestNaturalSec => Attempts.Where(a => a.NaturalSec.HasValue)
            .Select(a => a.NaturalSec).DefaultIfEmpty(null).Max();

        public object ToReportRow(int? repeat = null)
        {
            object replay = new
            {
                Attempts = Attempts.Select(a => new { a.Number, a.TargetWords, a.SynthWords, a.NaturalSec }),
                EndState,
                FellBack,
                PipelineError,
                StationConfigSource,
                BookendsResolved,
                FillerStatementCount,
                Script = FinalScript
            };
            return repeat is { } index
                ? new { Repeat = index, Fixture.JobId, Fixture.Market, Slot = Fixture.Snapshot.TargetDurationSeconds, Dev = Fixture.DevOutcome, Replay = replay }
                : new { Fixture.JobId, Fixture.Market, Slot = Fixture.Snapshot.TargetDurationSeconds, Dev = Fixture.DevOutcome, Replay = replay };
        }
    }
}

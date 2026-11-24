using System.Diagnostics;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit.Abstractions;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Integration profiling tests that hit real external APIs (HERE, Azure OpenAI, ElevenLabs)
///     and measure per-phase latencies across multiple iterations.
/// </summary>
public sealed class RealServiceProfilingTests(ITestOutputHelper output)
{
    [Fact(Skip = "Requires real HERE, Azure OpenAI, and ElevenLabs credentials")]
    public async Task RealPipeline_FullOrchestration_ProfileAllPhases()
    {
        const int iterations = 3;
        var iterationResults =
            new List<(int Iter, bool Success, string? Error, long TotalMs, long TrafficMs, long ScriptMs, long AudioMs
                )>();

        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine($"  REAL SERVICE INTEGRATION PROFILING — {iterations} Iterations");
        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine("");

        var locations = new[]
        {
            ("Melbourne, VIC, Australia", -37.8136, 144.9631),
            ("Sydney, NSW, Australia", -33.8688, 151.2093),
            ("Brisbane, QLD, Australia", -27.4698, 153.0251)
        };

        for (int i = 0; i < iterations; i++)
        {
            (string location, double lat, double lon) = locations[i % locations.Length];
            string iterMsg = $"── Iteration {i + 1}: {location} ──────────────────────────";
            output.WriteLine(iterMsg);
            Console.WriteLine(iterMsg);

            // Build configuration with overrides
            var configOverrides = new Dictionary<string, string?>
            {
                ["Providers:OutputProvider"] = "Console"
            };

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddProvider(new XunitLoggerProvider(output));
            });

            var configuration = TestConfigurationHelper.BuildConfiguration(configOverrides);
            services.AddInfrastructure(configuration);

            // In-memory repositories only
            services.AddSingleton<IJobRepository, InMemoryJobRepository>();
            services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
            services.AddSingleton(Substitute.For<IVoiceCalibrationRepository>());
            services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();
            services.AddApplication();
            services.Configure<IncidentStateOptions>(
                configuration.GetSection(IncidentStateOptions.SectionName).Bind);

            using var sp = services.BuildServiceProvider();

            var jobRepo = sp.GetRequiredService<IJobRepository>();

            // Create a job first
            var job = Job.Create($"profile-{i + 1}-{Guid.NewGuid():N}", "test-station", location, 30.0, DateTime.UtcNow).Value;
            await jobRepo.SaveJobAsync(job);

            var request = new BroadcastRequest
            {
                JobId = job.JobId,
                Location = location,
                Coverage = CoverageArea.Create(lat, lon, 25.0).Value,
                TargetDurationSeconds = 30
            };

            var totalSw = Stopwatch.StartNew();
            long trafficMs = 0, scriptMs = 0, audioMs = 0;

            try
            {
                // Phase 1: Traffic fetch (instrumented inside orchestrator)
                var trafficSw = Stopwatch.StartNew();
                var trafficProvider = sp.GetRequiredService<ITrafficProvider>();
                var corridorsResult = await trafficProvider.GetCorridorsAsync(request.Coverage);
                trafficSw.Stop();
                trafficMs = trafficSw.ElapsedMilliseconds;

                string trafficLine = $"  [Traffic/HERE]     {trafficMs,6}ms  " +
                                     $"{(corridorsResult.IsSuccess ? $"OK — {corridorsResult.Value.Count()} corridors" : $"FAIL: {corridorsResult.Error}")}";
                output.WriteLine(trafficLine);
                Console.WriteLine(trafficLine);

                if (corridorsResult.IsFailure)
                {
                    totalSw.Stop();
                    iterationResults.Add((i + 1, false, $"Traffic: {corridorsResult.Error}",
                        totalSw.ElapsedMilliseconds, trafficMs, 0, 0));
                    output.WriteLine("");
                    Console.WriteLine("");
                    continue;
                }

                var corridors = corridorsResult.Value.ToList();
                var incidents = corridors.SelectMany(c => c.Incidents).ToList();
                output.WriteLine($"                     Incidents: {incidents.Count}");

                // Phase 2: Script generation (Azure OpenAI)
                var scriptGen = sp.GetRequiredService<IScriptGenerator>();
                var scriptSw = Stopwatch.StartNew();
                var scriptResult = await scriptGen.GenerateScriptAsync(
                    new BroadcastWithTraffic
                    {
                        Traffic = string.Join("; ", incidents.Take(5).Select(inc => inc.Description)),
                        TargetDurationSeconds = 27.5, // buffer for padding
                        Location = location,
                        JobId = job.JobId,
                        Incidents = incidents
                    },
                    4.6,
                    VoiceSettings.Default);
                scriptSw.Stop();
                scriptMs = scriptSw.ElapsedMilliseconds;

                string scriptLine = $"  [Script/AzureOAI]  {scriptMs,6}ms  " +
                                    $"{(scriptResult.IsSuccess ? $"OK — {scriptResult.Value.Text.Length} chars" : $"FAIL: {scriptResult.Error}")}";
                output.WriteLine(scriptLine);
                Console.WriteLine(scriptLine);

                if (scriptResult.IsFailure)
                {
                    totalSw.Stop();
                    iterationResults.Add((i + 1, false, $"Script: {scriptResult.Error}", totalSw.ElapsedMilliseconds,
                        trafficMs, scriptMs, 0));
                    output.WriteLine("");
                    Console.WriteLine("");
                    continue;
                }

                output.WriteLine(
                    $"                     Script preview: \"{scriptResult.Value.Text[..Math.Min(100, scriptResult.Value.Text.Length)]}...\"");

                // Phase 3: Audio synthesis (ElevenLabs)
                var audioPipeline = sp.GetRequiredService<IAudioPipelineService>();
                var durationEstimator = sp.GetRequiredService<IDurationEstimator>();
                var synthSw = Stopwatch.StartNew();
                var audioResult = await audioPipeline.SynthesizeWithDurationTargetAsync(
                    scriptResult.Value.Text, 30.0);
                synthSw.Stop();
                audioMs = synthSw.ElapsedMilliseconds;

                if (audioResult.IsSuccess)
                {
                    double duration = durationEstimator.GetDuration(audioResult.Value.Audio);
                    string audioLine =
                        $"  [Audio/ElevenLabs] {audioMs,6}ms  OK — {audioResult.Value.Audio.Length / 1024}KB, {duration:F1}s";
                    output.WriteLine(audioLine);
                    Console.WriteLine(audioLine);
                }
                else
                {
                    string audioLine = $"  [Audio/ElevenLabs] {audioMs,6}ms  FAIL: {audioResult.Error}";
                    output.WriteLine(audioLine);
                    Console.WriteLine(audioLine);
                }

                totalSw.Stop();
                bool success = audioResult.IsSuccess;
                iterationResults.Add((i + 1, success, success ? null : audioResult.Error, totalSw.ElapsedMilliseconds,
                    trafficMs, scriptMs, audioMs));
            }
            catch (Exception ex)
            {
                totalSw.Stop();
                output.WriteLine($"  EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                iterationResults.Add((i + 1, false, $"{ex.GetType().Name}: {ex.Message}", totalSw.ElapsedMilliseconds,
                    trafficMs, scriptMs, audioMs));
            }

            output.WriteLine($"  [TOTAL]            {totalSw.ElapsedMilliseconds,6}ms");
            output.WriteLine("");
        }

        // ── Summary Report ───────────────────────────────────────────────
        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine("  SUMMARY");
        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine($"  Iterations: {iterations}");
        output.WriteLine($"  Successes:  {iterationResults.Count(r => r.Success)}");
        output.WriteLine($"  Failures:   {iterationResults.Count(r => !r.Success)}");
        output.WriteLine("");

        var successful = iterationResults.Where(r => r.Success).ToList();
        if (successful.Count > 0)
        {
            output.WriteLine("  Phase Latencies (successful runs only):");
            output.WriteLine($"  {"Phase",-22} {"Min",8} {"Avg",8} {"Max",8} {"% of Total",12}");
            output.WriteLine($"  {"─────",-22} {"───",8} {"───",8} {"───",8} {"──────────",12}");

            ReportPhase("Traffic/HERE", successful.Select(r => r.TrafficMs),
                successful.Select(r => r.TotalMs).Average());
            ReportPhase("Script/AzureOpenAI", successful.Select(r => r.ScriptMs),
                successful.Select(r => r.TotalMs).Average());
            ReportPhase("Audio/ElevenLabs", successful.Select(r => r.AudioMs),
                successful.Select(r => r.TotalMs).Average());
            ReportPhase("TOTAL", successful.Select(r => r.TotalMs), successful.Select(r => r.TotalMs).Average());
        }

        if (iterationResults.Any(r => !r.Success))
        {
            output.WriteLine("");
            output.WriteLine("  ERRORS:");
            foreach (var r in iterationResults.Where(r => !r.Success))
                output.WriteLine($"    Iteration {r.Iter}: {r.Error}");
        }

        output.WriteLine("");
        output.WriteLine("  BOTTLENECK ANALYSIS:");
        if (successful.Count > 0)
        {
            double avgTraffic = successful.Average(r => r.TrafficMs);
            double avgScript = successful.Average(r => r.ScriptMs);
            double avgAudio = successful.Average(r => r.AudioMs);
            double avgTotal = successful.Average(r => r.TotalMs);
            var bottleneck = new[]
                    { ("Traffic/HERE", avgTraffic), ("Script/AzureOpenAI", avgScript), ("Audio/ElevenLabs", avgAudio) }
                .OrderByDescending(x => x.Item2).First();
            output.WriteLine(
                $"    Bottleneck: {bottleneck.Item1} at {bottleneck.Item2:F0}ms avg ({bottleneck.Item2 / avgTotal * 100:F1}% of total)");
        }
    }

    [Fact(Skip = "Requires real HERE, Azure OpenAI, and ElevenLabs credentials")]
    public async Task RealPipeline_FullOrchestrator_EndToEnd()
    {
        if (!TestConfigurationHelper.HasRealServiceCredentials())
        {
            output.WriteLine("SKIPPED: Real service credentials not available.");
            return;
        }

        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine("  FULL ORCHESTRATOR E2E — Single Run with Phase Logging");
        output.WriteLine("═══════════════════════════════════════════════════════════════");
        output.WriteLine("");

        using var sp = TestConfigurationHelper.BuildRealServiceProvider(
            minimumLogLevel: LogLevel.Warning);

        var orchestrator = sp.GetRequiredService<BroadcastOrchestrator>();
        var jobRepo = sp.GetRequiredService<IJobRepository>();

        var job = Job.Create($"e2e-real-{Guid.NewGuid():N}", "test-station", "Melbourne, VIC, Australia", 30.0, DateTime.UtcNow).Value;
        await jobRepo.SaveJobAsync(job);

        var request = new BroadcastRequest
        {
            JobId = job.JobId,
            Location = "Melbourne, VIC, Australia",
            Coverage = CoverageArea.Create(-37.8136, 144.9631, 25.0).Value,
            TargetDurationSeconds = 30
        };

        var totalSw = Stopwatch.StartNew();
        var result = await orchestrator.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);
        totalSw.Stop();

        output.WriteLine($"  Result:   {(result.IsSuccess ? "SUCCESS" : $"FAIL: {result.Error}")}");
        output.WriteLine($"  Total:    {totalSw.ElapsedMilliseconds}ms");

        if (result.IsSuccess)
        {
            output.WriteLine($"  File:     {result.Value.FileName}");
            output.WriteLine($"  Duration: {result.Value.DebugInfo.FinalDuration:F1}s");
            output.WriteLine($"  Script:   {result.Value.DebugInfo.ScriptGenerator}");
            output.WriteLine($"  Audio:    {result.Value.DebugInfo.AudioSynthesizer}");
        }

        // Check job state
        var finalJob = (await jobRepo.GetJobAsync(job.JobId)).Value;
        output.WriteLine($"  Status:   {finalJob.Status}");
        output.WriteLine($"  Attempts: {finalJob.AttemptCount}");

        if (finalJob.AttemptHistory.Count > 0)
        {
            output.WriteLine("");
            output.WriteLine("  Attempt History:");
            foreach (var attempt in finalJob.AttemptHistory)
                output.WriteLine($"    #{attempt.AttemptNumber}: Rate={attempt.SyllablesPerSecond:F2} " +
                                 $"Est={attempt.EstimatedDurationSec.Value:F1}s " +
                                 $"Act={attempt.ActualDurationSec} " +
                                 $"Success={attempt.Succeeded}");
        }
    }

    private void ReportPhase(string name, IEnumerable<long> timingsMs, double avgTotal)
    {
        var list = timingsMs.ToList();
        if (list.Count == 0) return;
        long min = list.Min();
        long max = list.Max();
        double avg = list.Average();
        double pct = avgTotal > 0 ? avg / avgTotal * 100 : 0;
        output.WriteLine($"  {name,-22} {min,7}ms {avg,7:F0}ms {max,7}ms {pct,10:F1}%");
    }

    // ── Phase-capturing logger ───────────────────────────────────────────

    private sealed class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new XunitLogger(output, categoryName);
        }

        public void Dispose()
        {
        }

        private sealed class XunitLogger(ITestOutputHelper output, string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                try
                {
                    string msg = formatter(state, exception);
                    string outputMsg = $"[{logLevel}] {categoryName}: {msg}";
                    output.WriteLine(outputMsg);
                    // Also write to Console so we can see it in redirected background logs
                    Console.WriteLine(outputMsg);
                }
                catch
                {
                    // Ignore output errors during shutdown
                }
            }
        }
    }
}

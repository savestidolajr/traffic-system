using System.Collections.Concurrent;
using System.Diagnostics;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit.Abstractions;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Performance profiling tests that execute the integration pipeline multiple times,
///     measure per-step timings, capture errors, and report bottlenecks.
/// </summary>
public sealed class PerformanceProfilingTests(ITestOutputHelper output)
{
    private const int DefaultIterations = 10;

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ServiceProvider BuildServiceProvider()
    {
        return TestConfigurationHelper.BuildTestServiceProvider(
            minimumLogLevel: LogLevel.Warning);
    }

    private static BroadcastWithTraffic CreateContext(int iteration)
    {
        return new BroadcastWithTraffic
        {
            Traffic = "Heavy traffic on I-5",
            TargetDurationSeconds = 30,
            Location = "Seattle",
            JobId = $"perf-test-{iteration:D4}-{Guid.NewGuid():N}",
            Incidents =

            [
                new TrafficIncident("1", "Heavy traffic on I-5", 3, "I-5 North", "congestion")
            ]
        };
    }

    // ── Tests ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Runs the full orchestration pipeline <see cref="DefaultIterations" /> times sequentially,
    ///     capturing detailed timings for each step. Reports min/max/avg/p95 per phase.
    /// </summary>
    [Fact(Skip = "Needs mock coverage for full orchestrator flow (in-memory job persistence edge cases)")]
    public async Task OrchestrateAsync_SequentialRuns_ProfileTimings()
    {
        // ── Arrange ──────────────────────────────────────────────────────
        var results = new List<IterationResult>(DefaultIterations);
        var wallClockSw = Stopwatch.StartNew();

        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine($"  PERFORMANCE PROFILE — {DefaultIterations} Sequential Iterations");
        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine("");

        // ── Act ──────────────────────────────────────────────────────────
        for (int i = 1; i <= DefaultIterations; i++)
        {
            var iterSw = Stopwatch.StartNew();
            string? error = null;
            bool success = false;

            // Phase 1: Service resolution
            var resolveSw = Stopwatch.StartNew();
            using var sp = BuildServiceProvider();
            var orchestrator = sp.GetRequiredService<IBroadcastOrchestrator>();
            resolveSw.Stop();

            // Phase 2: Orchestration
            var orchSw = Stopwatch.StartNew();
            try
            {
                var context = CreateContext(i);
                var result = await orchestrator.OrchestrateAsync(context);
                orchSw.Stop();

                success = result.IsSuccess;
                if (!success)
                    error = result.Error;
            }
            catch (Exception ex)
            {
                orchSw.Stop();
                error = $"{ex.GetType().Name}: {ex.Message}";
            }

            iterSw.Stop();

            var iterResult = new IterationResult(
                i, success, error, iterSw.Elapsed, resolveSw.Elapsed, orchSw.Elapsed);
            results.Add(iterResult);

            output.WriteLine($"  [{i:D2}] {(success ? "OK" : "FAIL")} " +
                             $"Total={iterSw.ElapsedMilliseconds,6}ms  " +
                             $"Resolve={resolveSw.ElapsedMilliseconds,4}ms  " +
                             $"Orchestrate={orchSw.ElapsedMilliseconds,5}ms" +
                             $"{(error is not null ? $"  ERR: {error[..Math.Min(error.Length, 80)]}" : "")}");
        }

        wallClockSw.Stop();

        // ── Assert & Report ──────────────────────────────────────────────
        output.WriteLine("");
        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine("  SUMMARY");
        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine($"  Wall clock:       {wallClockSw.ElapsedMilliseconds}ms");
        output.WriteLine($"  Iterations:       {DefaultIterations}");
        output.WriteLine($"  Successes:        {results.Count(r => r.Success)}");
        output.WriteLine($"  Failures:         {results.Count(r => !r.Success)}");
        output.WriteLine("");

        ReportPhase("Total", results.Select(r => r.TotalElapsed));
        ReportPhase("ServiceResolution", results.Select(r => r.ServiceResolution));
        ReportPhase("Orchestration", results.Select(r => r.Orchestration));

        ReportErrors(results);
        ReportBottlenecks(results);

        // At least 80% should succeed (mock providers rarely fail)
        (results.Count(r => r.Success) >= (int)(DefaultIterations * 0.8)).ShouldBeTrue(
            $"Expected at least {(int)(DefaultIterations * 0.8)} successes but got {results.Count(r => r.Success)}");
    }

    /// <summary>
    ///     Runs orchestration in parallel batches to stress-test thread safety and
    ///     measure throughput under contention.
    /// </summary>
    [Fact(Skip = "Needs mock coverage for full orchestrator flow")]
    public async Task OrchestrateAsync_ParallelBatch_ProfileThroughput()
    {
        const int batchSize = 5;
        const int batches = 3;
        var allResults = new ConcurrentBag<IterationResult>();

        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine($"  PARALLEL THROUGHPUT — {batches} Batches × {batchSize} Parallel");
        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine("");

        var globalSw = Stopwatch.StartNew();

        for (int batch = 1; batch <= batches; batch++)
        {
            var batchSw = Stopwatch.StartNew();
            var tasks = Enumerable.Range(1, batchSize).Select(async idx =>
            {
                var iterSw = Stopwatch.StartNew();
                string? error = null;
                bool success = false;

                var resolveSw = Stopwatch.StartNew();
                using var sp = BuildServiceProvider();
                var orchestrator = sp.GetRequiredService<IBroadcastOrchestrator>();
                resolveSw.Stop();

                var orchSw = Stopwatch.StartNew();
                try
                {
                    var context = CreateContext(batch * 100 + idx);
                    var result = await orchestrator.OrchestrateAsync(context);
                    orchSw.Stop();
                    success = result.IsSuccess;
                    if (!success) error = result.Error;
                }
                catch (Exception ex)
                {
                    orchSw.Stop();
                    error = $"{ex.GetType().Name}: {ex.Message}";
                }

                iterSw.Stop();
                allResults.Add(new IterationResult(
                    batch * 100 + idx, success, error,
                    iterSw.Elapsed, resolveSw.Elapsed, orchSw.Elapsed));
            });

            await Task.WhenAll(tasks);
            batchSw.Stop();
            output.WriteLine($"  Batch {batch}: {batchSw.ElapsedMilliseconds}ms ({batchSize} parallel tasks)");
        }

        globalSw.Stop();

        var resultsList = allResults.ToList();

        output.WriteLine("");
        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine("  PARALLEL SUMMARY");
        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine($"  Wall clock:       {globalSw.ElapsedMilliseconds}ms");
        output.WriteLine($"  Total runs:       {resultsList.Count}");
        output.WriteLine($"  Successes:        {resultsList.Count(r => r.Success)}");
        output.WriteLine($"  Failures:         {resultsList.Count(r => !r.Success)}");
        output.WriteLine($"  Throughput:       {resultsList.Count / globalSw.Elapsed.TotalSeconds:F1} ops/sec");
        output.WriteLine("");

        ReportPhase("Total", resultsList.Select(r => r.TotalElapsed));
        ReportPhase("ServiceResolution", resultsList.Select(r => r.ServiceResolution));
        ReportPhase("Orchestration", resultsList.Select(r => r.Orchestration));
        ReportErrors(resultsList);

        (resultsList.Count(r => r.Success) >= (int)(resultsList.Count * 0.8)).ShouldBeTrue(
            $"Expected at least {(int)(resultsList.Count * 0.8)} successes but got {resultsList.Count(r => r.Success)}");
    }

    /// <summary>
    ///     Measures individual mock component latency in isolation to identify which
    ///     components dominate orchestration time.
    /// </summary>
    [Fact(Skip = "Needs mock coverage for full orchestrator flow")]
    public async Task ComponentIsolation_ProfileIndividualMocks()
    {
        const int iterations = 50;

        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine($"  COMPONENT ISOLATION — {iterations} Iterations Each");
        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine("");

        using var sp = BuildServiceProvider();

        // ── Script Generator ─────────────────────────────────────────────
        var scriptGen = sp.GetRequiredService<IScriptGenerator>();
        var scriptTimings = new List<TimeSpan>(iterations);
        int scriptErrors = 0;
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await scriptGen.GenerateScriptAsync(CreateContext(i), 4.6, VoiceSettings.Default);
                sw.Stop();
                if (!result.IsSuccess) scriptErrors++;
            }
            catch
            {
                sw.Stop();
                scriptErrors++;
            }

            scriptTimings.Add(sw.Elapsed);
        }

        ReportPhase("MockScriptGenerator", scriptTimings);
        output.WriteLine($"  Errors: {scriptErrors}");
        output.WriteLine("");

        // ── Audio Pipeline (Synthesize) ──────────────────────────────────
        var audioPipeline = sp.GetRequiredService<IAudioPipelineService>();
        var synthTimings = new List<TimeSpan>(iterations);
        int synthErrors = 0;
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await audioPipeline.SynthesizeWithDurationTargetAsync(
                    "Mock script for performance test at 4.6 syl/sec", 30.0);
                sw.Stop();
                if (!result.IsSuccess) synthErrors++;
            }
            catch
            {
                sw.Stop();
                synthErrors++;
            }

            synthTimings.Add(sw.Elapsed);
        }

        ReportPhase("MockAudioSynthesis", synthTimings);
        output.WriteLine($"  Errors: {synthErrors}");
        output.WriteLine("");

        // ── Traffic Provider ─────────────────────────────────────────────
        var trafficProvider = sp.GetRequiredService<ITrafficProvider>();
        var trafficTimings = new List<TimeSpan>(iterations);
        int trafficErrors = 0;
        var area = CoverageArea.Create(47.6, -122.3, 25.0).Value;
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await trafficProvider.GetCorridorsAsync(area);
                sw.Stop();
                if (!result.IsSuccess) trafficErrors++;
            }
            catch
            {
                sw.Stop();
                trafficErrors++;
            }

            trafficTimings.Add(sw.Elapsed);
        }

        ReportPhase("MockTrafficProvider", trafficTimings);
        output.WriteLine($"  Errors: {trafficErrors}");
        output.WriteLine("");

        // ── Job Repository ───────────────────────────────────────────────
        var jobRepo = sp.GetRequiredService<IJobRepository>();
        var repoTimings = new List<TimeSpan>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            string jobId = $"perf-repo-{i}";
            var job = Job.Create(jobId, "test-station", "seattle", 30.0, DateTime.UtcNow).Value;
            await jobRepo.SaveJobAsync(job);

            var sw = Stopwatch.StartNew();
            await jobRepo.GetJobAsync(jobId);
            sw.Stop();
            repoTimings.Add(sw.Elapsed);
        }

        ReportPhase("InMemoryJobRepo.Get", repoTimings);
        output.WriteLine("");

        // ── Duration Estimator ───────────────────────────────────────────
        var durationEstimator = sp.GetRequiredService<IDurationEstimator>();
        var estimateTimings = new List<TimeSpan>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            durationEstimator.EstimateDuration(
                "Good morning, here's your traffic update. Heavy traffic on I-5 heading northbound near the downtown exit.");
            sw.Stop();
            estimateTimings.Add(sw.Elapsed);
        }

        ReportPhase("DurationEstimation", estimateTimings);
        output.WriteLine("");

        // ── FineTune ─────────────────────────────────────────────────────
        byte[] dummyAudio = new byte[960044]; // ~30s of WAV at 16kHz
        var fineTuneTimings = new List<TimeSpan>(iterations);
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            audioPipeline.FineTuneToTargetDuration(dummyAudio, 30.0);
            sw.Stop();
            fineTuneTimings.Add(sw.Elapsed);
        }

        ReportPhase("FineTuneToTarget", fineTuneTimings);
    }

    /// <summary>
    ///     Measures GC pressure and memory allocation during orchestration.
    /// </summary>
    [Fact(Skip = "Needs mock coverage for full orchestrator flow")]
    public async Task OrchestrateAsync_MemoryPressure_MeasureAllocations()
    {
        const int iterations = 10;

        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine($"  MEMORY PRESSURE — {iterations} Iterations");
        output.WriteLine("═══════════════════════════════════════════════════════");
        output.WriteLine("");

        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long memBefore = GC.GetTotalMemory(true);

        using var sp = BuildServiceProvider();
        for (int i = 1; i <= iterations; i++)
        {
            var orchestrator = sp.GetRequiredService<IBroadcastOrchestrator>();
            var context = CreateContext(i);
            await orchestrator.OrchestrateAsync(context);
        }

        long memAfter = GC.GetTotalMemory(false);
        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);

        output.WriteLine($"  Memory before:    {memBefore / 1024.0:F0} KB");
        output.WriteLine($"  Memory after:     {memAfter / 1024.0:F0} KB");
        output.WriteLine(
            $"  Delta:            {(memAfter - memBefore) / 1024.0:F0} KB ({(memAfter - memBefore) / 1024.0 / iterations:F1} KB/iter)");
        output.WriteLine($"  GC Gen0 delta:    {gen0After - gen0Before}");
        output.WriteLine($"  GC Gen1 delta:    {gen1After - gen1Before}");
        output.WriteLine($"  GC Gen2 delta:    {gen2After - gen2Before}");

        // Sanity: ensure no unreasonable memory growth
        double perIterKb = (memAfter - memBefore) / 1024.0 / iterations;
        output.WriteLine("");
        output.WriteLine(perIterKb > 5000
            ? "  ⚠ WARNING: >5MB per iteration — possible memory leak"
            : "  ✓ Memory growth within acceptable bounds");
    }

    // ── Reporting helpers ────────────────────────────────────────────────

    private void ReportPhase(string name, IEnumerable<TimeSpan> timings)
    {
        var sorted = timings.OrderBy(t => t).ToList();
        if (sorted.Count == 0)
        {
            output.WriteLine($"  {name,-25} (no data)");
            return;
        }

        double min = sorted[0].TotalMilliseconds;
        double max = sorted[^1].TotalMilliseconds;
        double avg = sorted.Average(t => t.TotalMilliseconds);
        double median = sorted[sorted.Count / 2].TotalMilliseconds;
        double p95 = sorted[(int)(sorted.Count * 0.95)].TotalMilliseconds;
        double stdDev = Math.Sqrt(sorted.Average(t => Math.Pow(t.TotalMilliseconds - avg, 2)));

        output.WriteLine(
            $"  {name,-25} Min={min,8:F2}ms  Avg={avg,8:F2}ms  Med={median,8:F2}ms  P95={p95,8:F2}ms  Max={max,8:F2}ms  σ={stdDev:F2}ms");
    }

    private void ReportErrors(List<IterationResult> results)
    {
        var failures = results.Where(r => !r.Success).ToList();
        if (failures.Count == 0) return;

        output.WriteLine($"  ─── ERRORS ({failures.Count}) ───────────────────────────────");
        foreach (var f in failures)
            output.WriteLine($"    [{f.Iteration:D2}] {f.Error}");
        output.WriteLine("");
    }

    private void ReportBottlenecks(List<IterationResult> results)
    {
        var successful = results.Where(r => r.Success).ToList();
        if (successful.Count == 0) return;

        output.WriteLine("  ─── BOTTLENECK ANALYSIS ───────────────────────────");

        double avgResolve = successful.Average(r => r.ServiceResolution.TotalMilliseconds);
        double avgOrch = successful.Average(r => r.Orchestration.TotalMilliseconds);
        double avgTotal = successful.Average(r => r.TotalElapsed.TotalMilliseconds);

        output.WriteLine($"    Avg Service Resolution: {avgResolve:F2}ms ({avgResolve / avgTotal * 100:F1}% of total)");
        output.WriteLine($"    Avg Orchestration:      {avgOrch:F2}ms ({avgOrch / avgTotal * 100:F1}% of total)");

        // Identify the slowest phase
        double overhead = avgTotal - avgResolve - avgOrch;
        output.WriteLine($"    Overhead (measure):     {overhead:F2}ms ({overhead / avgTotal * 100:F1}% of total)");

        // Check for outliers (any iteration > 2× average)
        double outlierThreshold = avgTotal * 2;
        var outliers = successful.Where(r => r.TotalElapsed.TotalMilliseconds > outlierThreshold).ToList();
        if (outliers.Count > 0)
        {
            output.WriteLine($"    ⚠ Outliers (>2× avg): {outliers.Count}");
            foreach (var o in outliers)
                output.WriteLine($"      [{o.Iteration:D2}] {o.TotalElapsed.TotalMilliseconds:F2}ms");
        }

        // Check warm-up effect (first run vs subsequent)
        if (successful.Count >= 3)
        {
            double firstRun = successful[0].TotalElapsed.TotalMilliseconds;
            double warmAvg = successful.Skip(1).Average(r => r.TotalElapsed.TotalMilliseconds);
            double warmupRatio = firstRun / warmAvg;
            output.WriteLine(
                $"    Warm-up effect:         {warmupRatio:F1}× (first={firstRun:F2}ms vs rest avg={warmAvg:F2}ms)");
            if (warmupRatio > 3.0)
                output.WriteLine("    ⚠ Significant JIT warm-up detected");
        }

        output.WriteLine("");
    }

    // ── Data structures ──────────────────────────────────────────────────

    private sealed record IterationResult(
        int Iteration,
        bool Success,
        string? Error,
        TimeSpan TotalElapsed,
        TimeSpan ServiceResolution,
        TimeSpan Orchestration);
}

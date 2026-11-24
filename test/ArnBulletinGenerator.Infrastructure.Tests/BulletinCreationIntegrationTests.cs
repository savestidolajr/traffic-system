using System.Diagnostics;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit.Abstractions;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Integration tests that exercise the full bulletin creation pipeline:
///     Job creation → queue message mapping → orchestration (traffic + script generation + audio).
///     Uses mock providers so tests are runnable locally without external dependencies.
/// </summary>
public sealed class BulletinCreationIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly Stopwatch _totalStopwatch = new();

    public BulletinCreationIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _serviceProvider = TestConfigurationHelper.BuildTestServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private T Resolve<T>() where T : notnull
    {
        return _serviceProvider.GetRequiredService<T>();
    }

    private void Log(string message)
    {
        _output.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] {message}");
    }

    private void LogPhase(string phase, Stopwatch sw)
    {
        _output.WriteLine($"  ⏱  {phase}: {sw.ElapsedMilliseconds}ms");
    }

    private async Task<(Job Job, BroadcastRequest Request)> CreateJobAndBuildRequest(
        string location, double targetDuration, double lat, double lon,
        double radiusKm = 25, bool scriptOnly = false)
    {
        var jobService = Resolve<IJobService>();
        var createCommand = new CreateJobCommand(location, targetDuration,
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));
        var jobResult = await jobService.CreateJobAsync(createCommand);
        jobResult.IsSuccess.ShouldBeTrue($"job creation failed: {jobResult.Error}");

        var job = jobResult.Value;
        Log($"Created Job: {job.JobId}");

        var queueMessage = new BroadcastQueueMessage(
            job.JobId, location, location,
            (int)targetDuration, lat, lon,
            radiusKm,
            VoiceId: job.VoiceConfig.Value.VoiceId,
            Model: job.VoiceConfig.Value.Model,
            ScriptOnly: scriptOnly);

        return (job, queueMessage.ToBroadcastRequest().Value);
    }

    // ── Tests ────────────────────────────────────────────────────────────

    [Fact(Skip = "Needs full mock coverage for orchestrator job persistence flow")]
    public async Task FullBulletinCreation_WithScriptGeneration_ShouldSucceed()
    {
        _totalStopwatch.Start();
        var orchestrator = Resolve<BroadcastOrchestrator>();
        var jobRepository = Resolve<IJobRepository>();

        // Phase 1: Create Job + build request
        var createSw = Stopwatch.StartNew();
        var (job, broadcastRequest) = await CreateJobAndBuildRequest(
            "Melbourne, VIC, Australia", 30, -37.8136, 144.9631);
        createSw.Stop();
        LogPhase("Job Creation", createSw);

        // Verify persistence
        var fetchResult = await jobRepository.GetJobAsync(job.JobId);
        fetchResult.IsSuccess.ShouldBeTrue("job should be retrievable after creation");
        fetchResult.Value.Status.ShouldBe(JobStatus.Requested);
        broadcastRequest.Coverage.Latitude.ShouldBe(-37.8136, 0.001);

        // Phase 2: Run orchestration
        var orchSw = Stopwatch.StartNew();
        var orchResult = await orchestrator.OrchestrateAsync(broadcastRequest, Phase.FetchAndGenerate, CancellationToken.None);
        orchSw.Stop();
        LogPhase("Orchestration (traffic + script + audio)", orchSw);

        orchResult.IsSuccess.ShouldBeTrue($"orchestration failed: {orchResult.Error}");
        orchResult.Value.Message.ShouldBe(SuccessMessages.BroadcastGenerated);
        orchResult.Value.FileName.ShouldNotBeNullOrWhiteSpace();

        // Phase 3: Verify final job state
        var finalJob = (await jobRepository.GetJobAsync(job.JobId)).Value;
        finalJob.Status.ShouldBe(JobStatus.WaitingApproval);
        finalJob.ScriptText.HasValue.ShouldBeTrue();
        finalJob.AudioUrl.HasValue.ShouldBeTrue();
        finalJob.MeasuredDurationSec.HasValue.ShouldBeTrue();

        Log(
            $"Final: status={finalJob.Status}, script={finalJob.ScriptText.Value.Length} chars, duration={finalJob.MeasuredDurationSec.Value:F1}s");
        _totalStopwatch.Stop();
        LogPhase("TOTAL end-to-end", _totalStopwatch);
    }

    [Fact(Skip = "Needs full mock coverage for orchestrator job persistence flow")]
    public async Task FullBulletinCreation_ScriptOnly_ShouldSkipAudioSynthesis()
    {
        _totalStopwatch.Start();
        var orchestrator = Resolve<BroadcastOrchestrator>();
        var jobRepository = Resolve<IJobRepository>();

        var (job, broadcastRequest) = await CreateJobAndBuildRequest(
            "Sydney, NSW, Australia", 25, -33.8688, 151.2093, 20, true);
        broadcastRequest.ScriptOnly.ShouldBeTrue();

        var orchSw = Stopwatch.StartNew();
        var orchResult = await orchestrator.OrchestrateAsync(broadcastRequest, Phase.FetchAndGenerate, CancellationToken.None);
        orchSw.Stop();
        LogPhase("Orchestration (ScriptOnly)", orchSw);

        orchResult.IsSuccess.ShouldBeTrue($"ScriptOnly orchestration failed: {orchResult.Error}");
        orchResult.Value.FileName.ShouldBeEmpty("ScriptOnly should not produce a file");
        orchResult.Value.DebugInfo.AudioSynthesizer.ShouldContain("ScriptOnly");

        var finalJob = (await jobRepository.GetJobAsync(job.JobId)).Value;
        finalJob.Status.ShouldBe(JobStatus.Approved, "ScriptOnly should auto-approve");
        finalJob.ScriptText.HasValue.ShouldBeTrue();

        _totalStopwatch.Stop();
        LogPhase("TOTAL end-to-end (ScriptOnly)", _totalStopwatch);
    }

    [Fact(Skip = "Needs full mock coverage for orchestrator job persistence flow")]
    public async Task FullBulletinCreation_WithPreFetchedTraffic_ShouldSucceed()
    {
        _totalStopwatch.Start();
        var orchestrator = Resolve<BroadcastOrchestrator>();

        var (job, _) = await CreateJobAndBuildRequest(
            "Brisbane, QLD, Australia", 30, -27.4698, 153.0251);

        var context = new BroadcastWithTraffic
        {
            Traffic = "Heavy congestion on Pacific Motorway southbound between Eight Mile Plains and Springwood.",
            TargetDurationSeconds = 30,
            Location = "Brisbane",
            JobId = job.JobId,
            Incidents =
            [
                new TrafficIncident("inc-1", "Pacific Motorway congestion", 3, "Pacific Motorway S/B", "congestion"),
                new TrafficIncident("inc-2", "Minor delay on Story Bridge", 1, "Story Bridge", "delay")
            ]
        };

        var orchSw = Stopwatch.StartNew();
        var result = await orchestrator.OrchestrateAsync(context);
        orchSw.Stop();
        LogPhase("Orchestration (pre-fetched traffic)", orchSw);

        result.IsSuccess.ShouldBeTrue($"orchestration failed: {result.Error}");
        result.Value.Message.ShouldBe(SuccessMessages.BroadcastGenerated);

        _totalStopwatch.Stop();
        LogPhase("TOTAL end-to-end (pre-fetched)", _totalStopwatch);
    }

    [Fact(Skip = "Needs full mock coverage for orchestrator job persistence flow")]
    public async Task BulletinCreation_ExecutionTime_ShouldBeReasonable()
    {
        var orchestrator = Resolve<BroadcastOrchestrator>();

        var (_, broadcastRequest) = await CreateJobAndBuildRequest(
            "Perth, WA, Australia", 30, -31.9505, 115.8605);

        var sw = Stopwatch.StartNew();
        var result = await orchestrator.OrchestrateAsync(broadcastRequest, Phase.FetchAndGenerate, CancellationToken.None);
        sw.Stop();

        Log($"Execution time: {sw.ElapsedMilliseconds}ms");
        result.IsSuccess.ShouldBeTrue($"orchestration failed: {result.Error}");
        sw.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30),
            "mock-backed bulletin creation should complete within 30 seconds");
    }
}

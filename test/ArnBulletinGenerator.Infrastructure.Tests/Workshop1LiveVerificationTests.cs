using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit.Abstractions;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Live end-to-end verification of the Workshop-1-driven backend changes against the REAL
///     HERE Traffic API and Azure OpenAI (ScriptOnly mode — no audio synthesis). Run manually
///     with env vars HERE_API_KEY, AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_MODEL.
///     Generated bulletins are written to output/live-verify.txt for inspection.
/// </summary>
public sealed class Workshop1LiveVerificationTests(ITestOutputHelper output)
{
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "output", "live-verify.txt");

    private static ServiceProvider BuildProvider(Dictionary<string, string?>? overrides = null)
    {
        var configuration = TestConfigurationHelper.BuildConfiguration(overrides);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddMemoryCache();
        services.AddInfrastructure(configuration);

        // Azure OpenAI auth uses the real DefaultAzureCredential (registered by AddInfrastructure).
        // In this sandbox the signed-in principal lacks the OpenAI role, so the AAD token is rejected
        // and the configured API-key transport handles the structured-matrix request.

        services.AddSingleton<IJobRepository, InMemoryJobRepository>();
        services.AddSingleton<IScheduleRepository, InMemoryScheduleRepository>();
        services.AddSingleton<IIncidentStateRepository, InMemoryIncidentStateRepository>();
        services.AddSingleton(Substitute.For<IVoiceCalibrationRepository>());
        services.AddApplication();
        services.Configure<IncidentStateOptions>(configuration.GetSection(IncidentStateOptions.SectionName).Bind);

        return services.BuildServiceProvider();
    }

    private void Record(string header, string body)
    {
        var block = $"\n===== {header} =====\n{body}\n";
        output.WriteLine(block);
        try { File.AppendAllText(LogPath, block); } catch { /* best effort */ }
    }

    private static int WordCount(string s) =>
        s.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;

    private async Task<(bool Success, string Error, Job Job)> RunScriptOnlyAsync(
        ServiceProvider sp, string location, double lat, double lon, double radiusKm)
    {
        var orchestrator = sp.GetRequiredService<IBroadcastOrchestrator>();
        var jobRepo = sp.GetRequiredService<IJobRepository>();

        var job = Job.Create($"live-{Guid.NewGuid():N}", "live-station", location, 30.0, DateTime.UtcNow).Value;
        await jobRepo.SaveJobAsync(job);

        var request = new BroadcastRequest
        {
            JobId = job.JobId,
            Location = location,
            Coverage = CoverageArea.Create(lat, lon, radiusKm).Value,
            TargetDurationSeconds = 30,
            ScriptOnly = true
        };

        var result = await orchestrator.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);
        var finalJob = (await jobRepo.GetJobAsync(job.JobId)).Value;
        return (result.IsSuccess, result.IsSuccess ? string.Empty : result.Error, finalJob);
    }

    [Fact(Skip = "Live verification: requires real HERE + Azure OpenAI credentials; run manually with HERE_API_KEY and AZURE_OPENAI_* env vars.")]
    public async Task GoldCoast_RealHereAndOpenAi_GeneratesBulletin()
    {
        using var sp = BuildProvider();

        var (success, error, job) = await RunScriptOnlyAsync(
            sp, "Gold Coast, QLD, Australia", -28.0167, 153.4, 20);

        success.ShouldBeTrue($"orchestration failed: {error}");
        job.Status.ShouldBe(JobStatus.Approved);
        job.ScriptText.HasValue.ShouldBeTrue();
        job.ScriptText.Value.ShouldNotBeNullOrWhiteSpace();
        Record($"Gold Coast (words={WordCount(job.ScriptText.Value)})", job.ScriptText.Value);
    }

    [Fact(Skip = "Live verification: requires real HERE + Azure OpenAI credentials; run manually with HERE_API_KEY and AZURE_OPENAI_* env vars.")]
    public async Task QuietMarket_ZeroIncidents_GeneratesValidBulletin()
    {
        using var sp = BuildProvider();

        // Scottsdale TAS returns 0 HERE incidents (verified live) — the low/no-traffic regional case.
        var (success, error, job) = await RunScriptOnlyAsync(
            sp, "Scottsdale, TAS, Australia", -41.1667, 147.5167, 15);

        success.ShouldBeTrue($"orchestration failed: {error}");
        job.Status.ShouldBe(JobStatus.Approved);
        job.ScriptText.HasValue.ShouldBeTrue();

        Record($"Scottsdale quiet (words={WordCount(job.ScriptText.Value)})", job.ScriptText.Value);

        // The zero-incident path must produce a valid bulletin without crashing and without
        // fabricating incidents (the logged script describes clear roads, not invented closures).
        job.ScriptText.Value.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact(Skip = "Live verification: requires real HERE + Azure OpenAI credentials; run manually with HERE_API_KEY and AZURE_OPENAI_* env vars.")]
    public async Task ContentSafety_BannedWord_BlocksBulletin()
    {
        var overrides = new Dictionary<string, string?>
        {
            ["Broadcast:MaxGenerationRetries"] = "1",
            ["ContentSafety:BannedWords:0"] = "the",
            ["ContentSafety:BannedWords:1"] = "traffic",
            ["ContentSafety:BannedWords:2"] = "road"
        };
        using var sp = BuildProvider(overrides);

        var (success, error, _) = await RunScriptOnlyAsync(
            sp, "Gold Coast, QLD, Australia", -28.0167, 153.4, 20);

        Record("Content safety (expected block)", $"success={success} error={error}");
        success.ShouldBeFalse("a script containing common banned words should be blocked");
        error.ShouldContain("Content safety", Case.Insensitive);
    }

}

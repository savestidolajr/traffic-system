using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Infrastructure.Tests;

public class EndToEndTests
{
    [Fact(Skip = "Needs full mock coverage for orchestrator job persistence flow")]
    public async Task Orchestrate_WithMockProvider_ShouldHandleMockWav()
    {
        using var serviceProvider = TestConfigurationHelper.BuildTestServiceProvider();
        var orchestrator = serviceProvider.GetRequiredService<IBroadcastOrchestrator>();

        var context = new BroadcastWithTraffic
        {
            Traffic = "Heavy traffic on I-5",
            TargetDurationSeconds = 30,
            Location = "Seattle",
            JobId = "test-e2e-job",
            Incidents = [new TrafficIncident("1", "Heavy traffic on I-5", 3, "I-5 North", "congestion")]
        };

        var result = await orchestrator.OrchestrateAsync(context);

        result.IsSuccess.ShouldBeTrue($"Orchestration failed with error: {result.Error}");
        result.Value.ShouldNotBeNull();
        result.Value.Message.ShouldBe(SuccessMessages.BroadcastGenerated);
    }
}

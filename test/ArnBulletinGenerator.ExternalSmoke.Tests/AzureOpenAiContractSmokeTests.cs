using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ArnBulletinGenerator.ExternalSmoke.Tests;

[Collection(ExternalSmokeCollection.Name)]
[Trait("Category", "ExternalSmoke")]
public sealed class AzureOpenAiContractSmokeTests(ExternalSmokeFixture fixture)
{
    [Fact]
    public async Task ScriptGenerator_ReturnsNonEmptyScript()
    {
        if (!fixture.Enabled) return;
        var generator = fixture.Services.GetRequiredService<IScriptGenerator>();
        var incident = new TrafficIncident("smoke-1", "Heavy traffic near Smith Street Motorway", 2,
            "M1 Pacific Motorway", "congestion");
        var corridor = new TrafficCorridor("M1 Pacific Motorway", 2, 1, 1200, 50, "congestion",
            "Heavy traffic near Smith Street Motorway", [incident]);
        var context = new BroadcastWithTraffic
        {
            JobId = "external-smoke",
            Location = "Gold Coast",
            TargetDurationSeconds = 30,
            Traffic = "M1 Pacific Motorway is heavy near Smith Street Motorway.",
            Incidents = [incident],
            Corridors = [corridor],
            VoiceSettings = VoiceSettings.Default
        };

        var script = await generator.GenerateScriptAsync(context);
        script.IsSuccess.ShouldBeTrue(script.Error);
        script.Value.Text.ShouldNotBeNullOrWhiteSpace();
    }
}

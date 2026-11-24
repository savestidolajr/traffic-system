using ArnBulletinGenerator.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Live verification of the LLM script-safety agent (SoW: AI-based pre-review of script
///     content prior to human review) against the REAL Azure OpenAI deployment.
///     Proves the prompt blocks Mindframe/broadcast-standards violations while passing
///     legitimate traffic copy (including standard "fatal crash" phrasing).
///     Run manually with AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_MODEL set.
/// </summary>
public sealed class ScriptSafetyLiveVerificationTests(ITestOutputHelper output)
{
    private const string SkipReason =
        "Live verification: requires real Azure OpenAI credentials; run manually with AZURE_OPENAI_* env vars.";

    [Fact(Skip = SkipReason)]
    public async Task ViolatingScript_SelfHarmAndProfanity_IsBlocked()
    {
        await using var sp = TestConfigurationHelper.BuildRealServiceProvider(
            minimumLogLevel: LogLevel.Information);
        var reviewer = sp.GetRequiredService<IScriptSafetyReviewer>();

        const string script =
            "Traffic is stopped citybound on the Westgate Bridge after a man jumped from the railing. " +
            "Police are describing bloody scenes. Expect a bloody long delay, so get off your arse and leave early.";

        var result = await reviewer.ReviewScriptSafetyAsync(script);

        result.IsSuccess.ShouldBeTrue($"Reviewer failed: {result.Error}");
        output.WriteLine($"Safe: {result.Value.IsSafe}");
        foreach (string v in result.Value.Violations) output.WriteLine($"  - {v}");

        result.Value.IsSafe.ShouldBeFalse("Self-harm description must be blocked (Mindframe).");
        result.Value.Violations.ShouldNotBeEmpty();
    }

    [Fact(Skip = SkipReason)]
    public async Task LegitimateScript_FatalCrashMention_Passes()
    {
        await using var sp = TestConfigurationHelper.BuildRealServiceProvider(
            minimumLogLevel: LogLevel.Information);
        var reviewer = sp.GetRequiredService<IScriptSafetyReviewer>();

        const string script =
            "Heavy delays on the M1 at Parramatta this morning after an earlier fatal crash, " +
            "with traffic queued back to Penrith. Emergency services remain on scene and one " +
            "citybound lane is closed. A police operation on the Harbour Bridge has now wrapped up, " +
            "and trains are replacing buses on the T3 line.";

        var result = await reviewer.ReviewScriptSafetyAsync(script);

        result.IsSuccess.ShouldBeTrue($"Reviewer failed: {result.Error}");
        output.WriteLine($"Safe: {result.Value.IsSafe}");
        foreach (string v in result.Value.Violations) output.WriteLine($"  - {v}");

        result.Value.IsSafe.ShouldBeTrue(
            "Standard broadcast phrasing (fatal crash, police operation) must NOT be over-blocked.");
    }
}

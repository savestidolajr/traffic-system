using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Tests.Eval;
using Microsoft.Extensions.DependencyInjection;

namespace ArnBulletinGenerator.Infrastructure.Tests.Regression;

/// <summary>A regression fixture paired with the score of its current-pipeline replay.</summary>
internal sealed record RegressionResult(RegressionFixture Fixture, ScenarioScore Score);

/// <summary>
///     Replays a captured regression fixture through the LIVE generator + the full
///     review→refine→compliance pipeline (the shipped path, shared with the scenario eval via
///     <see cref="BulletinEval.ReviewRefineAndComplyAsync" />). The writer input is held constant from
///     the snapshot, so the only variable is the prompt/code — re-run after a change and compare the
///     replay script + scores to the flagged original.
/// </summary>
internal static class RegressionEval
{
    public static async Task<RegressionResult> ReplayAsync(
        IServiceProvider provider, RegressionFixture fixture, CancellationToken cancellationToken)
    {
        using var scope = provider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IScriptGenerator>();
        var safety = scope.ServiceProvider.GetRequiredService<IContentSafetyFilter>();

        var context = fixture.Snapshot.ToContext($"regression-{fixture.Label}");
        double targetSeconds = context.TargetDurationSeconds;

        var draft = await generator.GenerateScriptAsync(context, cancellationToken);
        string script = draft.IsSuccess ? draft.Value.Text : string.Empty;
        script = await BulletinEval.ReviewRefineAndComplyAsync(
            scope.ServiceProvider, context, script, targetSeconds, cancellationToken);

        double wps = await BulletinEval.ResolveWordsPerSecondAsync(scope.ServiceProvider, cancellationToken);
        var score = new ScenarioScore(
            context.MarketId.GetValueOr(fixture.Label),
            context.Corridors.Count,
            FabricationScorer.Evaluate(script, context.Corridors, context.MonitoredRoutes, context.GoodRunRoads),
            DurationScorer.Evaluate(script, targetSeconds),
            StyleScorer.Evaluate(script),
            await SafetyScorer.EvaluateAsync(safety, script),
            script,
            null,
            ScriptSanityScorer.Evaluate(script),
            TargetShapeScorer.Evaluate(script, context.Corridors, targetSeconds, wps),
            BulletinDataComposer.ComposeAllData(context));
        return new RegressionResult(fixture, score);
    }
}

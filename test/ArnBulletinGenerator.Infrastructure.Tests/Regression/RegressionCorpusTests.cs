using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Regression;

/// <summary>
///     Replays every committed regression fixture (<c>test/corpus/regressions/</c>) through the LIVE
///     generator + full pipeline and writes a before/after pack to <c>output/eval/</c>. This is the
///     iteration loop for bulletins we were unhappy with: capture the data once (the fixture), change a
///     prompt or code path, re-run this, and read the pack to see whether the flagged script improved —
///     with the writer input held constant so the change is the only variable.
///     <para>Excluded from CI (<c>Category=Integration</c>); needs Azure OpenAI creds + <c>ARN_EVAL_LIVE=1</c>.</para>
/// </summary>
public sealed class RegressionCorpusTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task RegressionCorpus_Replay_RegeneratesAndScores()
    {
        if (Environment.GetEnvironmentVariable("ARN_EVAL_LIVE") != "1")
        {
            output.WriteLine("Skipped: set ARN_EVAL_LIVE=1 + Azure OpenAI credentials to replay the regression corpus.");
            return;
        }

        var fixtures = RegressionFixture.LoadAll();
        fixtures.ShouldNotBeEmpty("No regression fixtures under test/corpus/regressions/.");

        var results = new List<RegressionResult>();
        foreach (var fixture in fixtures)
        {
            using var provider = HereLive.BuildProvider();
            var result = await RegressionEval.ReplayAsync(provider, fixture, CancellationToken.None);
            results.Add(result);
            output.WriteLine($"--- {fixture.Label} (flagged: {fixture.Reason}) ---");
            output.WriteLine($"ORIGINAL: {fixture.OriginalScript}");
            output.WriteLine($"REPLAY:   {result.Score.Script}");
        }

        string label = $"regression-{DateTime.UtcNow:yyyyMMddTHHmmssZ}";
        string path = Path.Combine("output", "eval", $"{label}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RegressionPack.Render(results, label));
        output.WriteLine($"Regression pack written: {Path.GetFullPath(path)}");

        results.ShouldAllBe(r => r.Score.Script.Length > 0);
    }
}

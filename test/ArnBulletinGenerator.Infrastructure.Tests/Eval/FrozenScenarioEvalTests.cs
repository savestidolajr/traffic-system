using System.Text;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Frozen-fetch prompt-iteration harness (2026-07-02 session scaffolding — see the session
///     decision log entry before deleting).
///     <para>
///         One live HERE pass is recorded across all five typed clients (<c>mode=record</c>), then
///         every prompt iteration replays that IDENTICAL data (<c>mode=replay</c>) so script changes
///         are attributable to the prompt, never to live-data drift. The LLM is live in both modes —
///         it is the system under test. Combine with <c>ARN_PROMPTS_DIR</c> to point the generator at
///         an experimental prompt set without touching the repo's <c>prompts/</c> tree.
///     </para>
///     <para>
///         Run (record, once):  <c>ARN_FROZEN_EVAL=record</c> + HERE + Azure OpenAI credentials.
///         Run (replay, iterate): <c>ARN_FROZEN_EVAL=replay ARN_PROMPTS_DIR=/abs/path/to/prompt-set</c>
///         + Azure OpenAI credentials. Optional <c>ARN_FROZEN_EVAL_LABEL</c> names the output file.
///     </para>
/// </summary>
public sealed class FrozenScenarioEvalTests(ITestOutputHelper output)
{
    /// <summary>Shared with the pipeline experiment harness (<c>PipelineExperimentTests</c>).</summary>
    private static (string Label, BulletinScenario Scenario)[] Scenarios => EvalScenarios.Matrix;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FrozenScenarioEval_RunsMatrix_WritesReviewPack()
    {
        string? mode = Environment.GetEnvironmentVariable("ARN_FROZEN_EVAL");
        if (mode is not ("record" or "replay"))
        {
            output.WriteLine("Skipped: set ARN_FROZEN_EVAL=record (freeze one live fetch) or =replay (iterate on frozen data).");
            return;
        }

        string label = Environment.GetEnvironmentVariable("ARN_FROZEN_EVAL_LABEL")
                       ?? $"{mode}-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmss'Z'}";

        HereCorpusWriter? writer = mode == "record" ? new HereCorpusWriter(HereFrozen.CorpusRoot()) : null;
        using var provider = writer is not null
            ? HereFrozen.BuildRecordingProvider(writer)
            : HereFrozen.BuildReplayProvider(HereCorpusReplay.Load(corpusRoot: HereFrozen.CorpusRoot()));

        var scores = new List<(string Label, ScenarioScore Score)>();
        foreach (var (scenarioLabel, scenario) in Scenarios)
        {
            if (writer is not null)
            {
                writer.CurrentMarket = scenario.Market;
                writer.CurrentSessionId = "frozen";
            }

            var score = await BulletinEval.EvaluateAsync(
                provider, scenario, CancellationToken.None, measureDuration: false, fullPipeline: true);
            scores.Add((scenarioLabel, score));
            output.WriteLine($"--- {scores.Count}. {scenarioLabel} ({scenario.City}, corridors:{score.CorridorCount}) ---");
            output.WriteLine(score.Script);
            output.WriteLine("");
        }

        if (writer is not null)
        {
            await writer.FlushManifestAsync(CancellationToken.None);
            output.WriteLine($"Frozen corpus recorded: {writer.TotalRecorded} exchanges under {HereFrozen.CorpusRoot()}");
        }

        string packPath = Path.Combine(CorpusPaths.RepoRoot(), "output", "eval", $"frozen-pack-{label}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(packPath)!);
        await File.WriteAllTextAsync(packPath, RenderBfnPack(scores, label, mode));
        output.WriteLine($"Review pack written: {packPath}");
    }

    /// <summary>
    ///     Review pack keyed to the 1-Jul ARN editorial standard (BFN-001..016) rather than the
    ///     legacy ATN-idiom rubric: objective/factual read, no editorializing or vague colloquialism,
    ///     concrete verbs, no redundant/duplicate anchors, structural variety, rounding + fuel-suburb
    ///     data rules, honest fill, clean close, absolute fidelity to the frozen source data.
    /// </summary>
    private static string RenderBfnPack(
        IReadOnlyList<(string Label, ScenarioScore Score)> scores, string label, string mode)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Frozen-fetch review pack — {label} (mode: {mode})");
        sb.AppendLine();
        sb.AppendLine("Judged against the 1-Jul ARN editorial feedback (BFN-001..016): state what happened,");
        sb.AppendLine("don't react to it. Score 0–1 per dimension; verdict is would-air / borderline / send-back.");
        sb.AppendLine();

        int i = 0;
        foreach (var (scenarioLabel, score) in scores)
        {
            i++;
            sb.AppendLine($"## {i}. {scenarioLabel} — {score.Market} ({score.CorridorCount} corridors)");
            sb.AppendLine();
            sb.AppendLine("### Source data the writer was given");
            sb.AppendLine("```");
            sb.AppendLine(score.SourceData.Trim().Length > 0 ? score.SourceData.Trim() : "(none)");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("### Generated script");
            sb.AppendLine("> " + score.Script.Trim().Replace("\n", "\n> "));
            sb.AppendLine();
            sb.AppendLine("### Deterministic scores");
            sb.AppendLine($"- fabrication: {(score.Fabrication.Passed ? "ok" : string.Join(", ", score.Fabrication.UnsupportedMentions))}");
            sb.AppendLine($"- style: {(score.Style.Passed ? "ok" : string.Join("; ", score.Style.Issues))}");
            sb.AppendLine($"- safety: {(score.Safety.Passed ? "ok" : string.Join("; ", score.Safety.Violations))}");
            if (score.TargetShape is { } ts)
            {
                sb.AppendLine($"- target shape: {ts.Percent:F0}%");
                foreach (var d in ts.Dimensions.Where(d => d.Score is < 1.0))
                    sb.AppendLine($"  - {d.Name}: {d.Score!.Value:0.0} ({d.Detail})");
            }
            sb.AppendLine();
            sb.AppendLine("### BFN judgement (agent fills)");
            sb.AppendLine("| dimension | 0–1 | note |");
            sb.AppendLine("|---|---|---|");
            foreach (var dim in new[]
                     {
                         "objectivity", "concreteness", "redundancy", "anchoring_clarity",
                         "variety", "data_rules", "honest_fill", "clean_close", "fidelity"
                     })
                sb.AppendLine($"| {dim} |  |  |");
            sb.AppendLine();
            sb.AppendLine("- **Verdict:** ");
            sb.AppendLine("- **Biggest gap:** ");
            sb.AppendLine("- **Layer to change:** ");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

using System.Text;
using ArnBulletinGenerator.Infrastructure.Tests.Eval;

namespace ArnBulletinGenerator.Infrastructure.Tests.Regression;

/// <summary>
///     Renders a before/after pack for a regression replay: the captured source data, the flagged
///     ORIGINAL script, the current-pipeline REPLAY script, and the deterministic scores — so the judge
///     can decide whether a change actually fixed the flagged issue. Pairing the original with the
///     replay (on identical data) is the point: it isolates the prompt/code change as the only variable.
/// </summary>
internal static class RegressionPack
{
    public static string Render(IReadOnlyList<RegressionResult> results, string label)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Regression replay — {label}");
        sb.AppendLine();
        sb.AppendLine("Each case replays a flagged bulletin through the current pipeline on its captured data.");
        sb.AppendLine("Compare REPLAY to ORIGINAL; judge against `docs/reference/atn-bulletin-style-samples.md`.");
        sb.AppendLine();

        foreach (var (fixture, score) in results)
        {
            sb.AppendLine($"## {fixture.Label} — flagged: {fixture.Reason}");
            sb.AppendLine($"Source job `{fixture.SourceJobId}` · captured {fixture.CapturedAtUtc:u} · {score.CorridorCount} corridors");
            sb.AppendLine();
            sb.AppendLine("### Source data the writer was given");
            sb.AppendLine("```");
            sb.AppendLine(score.SourceData.Trim().Length > 0 ? score.SourceData.Trim() : "(none)");
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine("### ORIGINAL (flagged as unsatisfactory)");
            sb.AppendLine("> " + fixture.OriginalScript.Trim().Replace("\n", "\n> "));
            sb.AppendLine();
            sb.AppendLine("### REPLAY (current pipeline)");
            sb.AppendLine("> " + score.Script.Trim().Replace("\n", "\n> "));
            sb.AppendLine();
            sb.AppendLine("### Deterministic scores (replay)");
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
            sb.AppendLine("### Manual judgement (agent fills) — did the replay fix the flagged issue?");
            sb.AppendLine("| dimension | 0–1 | note |");
            sb.AppendLine("|---|---|---|");
            foreach (var dim in new[] { "anchoring", "direction", "drive_time_headline", "idiom", "flow", "clean_close", "fidelity" })
                sb.AppendLine($"| {dim} |  |  |");
            sb.AppendLine();
            sb.AppendLine("- **Improved vs original?** ");
            sb.AppendLine("- **Remaining gap / layer to change:** ");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

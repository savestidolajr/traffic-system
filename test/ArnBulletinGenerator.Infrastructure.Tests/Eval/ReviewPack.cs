using System.Text;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Renders a human-judge review pack: for each scenario it pairs the SOURCE DATA the writer was
///     given with the generated script and the deterministic scores, plus a blank ATN-rubric template.
///     The agent (not an API model) reads this artifact, scores each bulletin against the real ATN
///     reference samples, names the weakest pipeline layer, then tweaks that layer and re-runs.
///     Pairing source data with script is the point — it is what lets the judge check fidelity
///     (did the script claim anything the data does not contain?).
/// </summary>
internal static class ReviewPack
{
    public static string Render(IReadOnlyList<ScenarioScore> scores, string label)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Bulletin review pack — {label}");
        sb.AppendLine();
        sb.AppendLine("Judge each bulletin against `docs/reference/atn-bulletin-style-samples.md`. Score every");
        sb.AppendLine("dimension 0–1, name the single biggest gap, and the pipeline layer to change");
        sb.AppendLine("(system / draft / review / refine / summarizer).");
        sb.AppendLine();

        foreach (var score in scores)
        {
            sb.AppendLine($"## {score.Market} ({score.CorridorCount} corridors)");
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
            sb.AppendLine("### Manual judgement (agent fills)");
            sb.AppendLine("| dimension | 0–1 | note |");
            sb.AppendLine("|---|---|---|");
            foreach (var dim in new[] { "anchoring", "direction", "drive_time_headline", "idiom", "flow", "clean_close", "fidelity" })
                sb.AppendLine($"| {dim} |  |  |");
            sb.AppendLine();
            sb.AppendLine("- **Biggest gap:** ");
            sb.AppendLine("- **Layer to change:** ");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

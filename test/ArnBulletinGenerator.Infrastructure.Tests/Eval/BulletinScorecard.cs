using System.Text;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>Scores for one evaluated bulletin scenario.</summary>
internal sealed record ScenarioScore(
    string Market,
    int CorridorCount,
    FabricationResult Fabrication,
    DurationResult Duration,
    StyleResult Style,
    SafetyResult Safety,
    string Script,
    PronunciationResult? Pronunciation = null,
    SanityResult? Sanity = null,
    TargetShapeResult? TargetShape = null,
    string SourceData = "")
{
    /// <summary>Hard gates: fabrication, sanity, style, safety, and slot-fill (underfill = can't air).</summary>
    public bool Passed => Fabrication.Passed && (Sanity?.Passed ?? true) && Style.Passed && Safety.Passed && Duration.Passed;
}

/// <summary>Renders a set of scenario scores as a readable scorecard.</summary>
internal static class BulletinScorecard
{
    public static string Render(IReadOnlyList<ScenarioScore> scores)
    {
        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine("market       corridors  fabrication  duration             pronunciation  style  safety  target  overall");
        builder.AppendLine("-----------  ---------  -----------  -------------------  -------------  -----  ------  ------  -------");

        foreach (var score in scores)
        {
            string fabrication = score.Fabrication.Passed
                ? "ok"
                : $"{score.Fabrication.UnsupportedMentions.Count} bad";
            string fillStatus = score.Duration.IsUnderfilled ? "UNDERFILL"
                : score.Duration.WithinTolerance(0.25) ? "ok" : "off";
            string duration =
                $"{score.Duration.Seconds:F0}/{score.Duration.TargetSeconds:F0}s " +
                $"{(score.Duration.IsMeasured ? "meas" : "est")} {fillStatus}";

            string pronunciation = score.Pronunciation is { } p
                ? $"{p.TermsMatched}/{p.TermsChecked} {(p.Passed ? "ok" : "susp")}"
                : "—";
            string target = score.TargetShape is { } ts ? $"{ts.Percent:F0}%" : "—";

            builder.AppendLine(
                $"{score.Market,-11}  {score.CorridorCount,9}  {fabrication,-11}  {duration,-19}  " +
                $"{pronunciation,-13}  {(score.Style.Passed ? "ok" : "x"),-5}  " +
                $"{(score.Safety.Passed ? "ok" : "x"),-6}  {target,-6}  {(score.Passed ? "PASS" : "FAIL")}");

            if (score.Fabrication.UnsupportedMentions.Count > 0)
                builder.AppendLine($"               fabricated → {string.Join(", ", score.Fabrication.UnsupportedMentions)}");
            if (score.Sanity is { JunkTokens.Count: > 0 } sanity)
                builder.AppendLine($"               junk → {string.Join(", ", sanity.JunkTokens)}");
            if (!score.Style.Passed)
                builder.AppendLine($"               style → {string.Join("; ", score.Style.Issues)}");
            if (!score.Safety.Passed)
                builder.AppendLine($"               safety → {string.Join("; ", score.Safety.Violations)}");
            if (score.Pronunciation is { SuspectTerms.Count: > 0 } pron)
                builder.AppendLine($"               pronunciation → {string.Join(", ", pron.SuspectTerms)}");
            if (score.TargetShape is { } shape)
            {
                var weak = shape.Dimensions
                    .Where(d => d.Score is < 1.0)
                    .Select(d => $"{d.Name} {d.Score!.Value:0.0} ({d.Detail})");
                if (weak.Any())
                    builder.AppendLine($"               target → {string.Join("; ", weak)}");
            }
        }

        var shaped = scores.Where(s => s.TargetShape is not null).Select(s => s.TargetShape!.Percent).ToList();
        if (shaped.Count > 0)
            builder.AppendLine($"overall target shape: {shaped.Average():F0}% (n={shaped.Count})");

        return builder.ToString();
    }
}

using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Audio;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

/// <summary>
///     Accuracy suite for the shipped spoken-duration calibration, scored against every measured
///     (script -> natural seconds) observation scraped out of the deployed job records
///     (<c>test/corpus/duration/dataset-v2.json</c>, built by <c>scripts/build-duration-dataset.py</c>).
///     <para>
///         Why this exists: the v1 calibration was fitted through a corpus that
///         <c>Job.ApplyFallback</c> had mislabelled — it overwrites <c>ScriptText</c> with the
///         fallback SOURCE bulletin's text, so 151 dev jobs contributed a pair whose words came from
///         one bulletin and whose duration came from another. The resulting model ran ~17% slow for
///         ARN Sarah, and because the word ceiling trims on its prediction, that pessimism silently
///         shed real content from bulletins (dev job 8b895081: 12 words of slot spent on
///         pace-stretching instead of traffic).
///     </para>
///     <para>
///         BIAS is the assertion that matters. A model that is wrong symmetrically costs nothing —
///         the measured audio fitter corrects it downstream. A model that is wrong in one direction
///         moves the content/length trade every single time.
///     </para>
/// </summary>
public sealed class SpokenDurationCalibrationAccuracyTests(ITestOutputHelper output)
{
    /// <summary>
    ///     In-sample regression gates against dataset-v2.json, not a held-out accuracy estimate.
    /// </summary>
    private const double MaeBudgetSeconds = 1.45;
    private const double BiasBudgetSeconds = 0.35;
    private const double FleetMaeBudgetSeconds = 1.05;
    private const double FleetBiasBudgetSeconds = 0.20;

    private sealed record Observation(
        string JobId, string Market, int Words, double NaturalSeconds, string Text, VoiceRow Voice);

    private sealed record VoiceRow(string VoiceId, string VoiceName, string ModelId, double Speed, double PaceBoostWpm);

    [Fact]
    public void ShippedCalibration_IsUnbiasedAndInsideBudget_ForEveryCoveredVoice()
    {
        var observations = LoadObservations();
        observations.Count.ShouldBeGreaterThan(100, "the scraped corpus should carry the whole dev history");

        var model = new TokenAdditiveDurationModel(
            EmbeddedSpokenDurationCalibration.Load(),
            new SpokenDurationModelOptions { Enabled = true });

        var byVoice = observations
            .GroupBy(o => (o.Voice.VoiceId, o.Voice.ModelId, o.Voice.Speed, o.Voice.PaceBoostWpm))
            .ToList();

        var failures = new List<string>();
        int covered = 0, scored = 0;
        output.WriteLine($"{"voice",-42} {"n",4} {"MAE",7} {"bias",7} {"p90",7} {"<=1s",6}");
        foreach (var group in byVoice.OrderByDescending(g => g.Count()))
        {
            var errors = new List<double>();
            foreach (var o in group)
            {
                var predicted = model.Predict(
                    o.Text,
                    new DurationVoiceKey(o.Voice.VoiceId, o.Voice.ModelId, o.Voice.Speed, o.Voice.PaceBoostWpm));
                if (!predicted.HasValue)
                    continue;              // voice not covered by the calibration; reported below
                errors.Add(predicted.Value.Seconds - o.NaturalSeconds);
            }

            if (errors.Count == 0)
            {
                output.WriteLine($"{Label(group.Key),-42} {group.Count(),4} {"(no calibration row)",-30}");
                continue;
            }

            covered++;
            scored += errors.Count;
            double mae = errors.Average(Math.Abs);
            double bias = errors.Average();
            var sorted = errors.Select(Math.Abs).OrderBy(e => e).ToList();
            double p90 = sorted[(int)(sorted.Count * 0.9)];
            double within = sorted.Count(e => e <= 1.0) / (double)sorted.Count;
            output.WriteLine($"{Label(group.Key),-42} {errors.Count,4} {mae,7:F2} {bias,7:F2} {p90,7:F2} {within,6:P0}");

            // Thin rows are reported but not gated: 8 observations cannot certify a voice.
            if (errors.Count < 8)
                continue;
            if (mae > MaeBudgetSeconds)
                failures.Add($"{Label(group.Key)}: MAE {mae:F2}s > {MaeBudgetSeconds:F1}s budget (n={errors.Count})");
            if (Math.Abs(bias) > BiasBudgetSeconds)
                failures.Add($"{Label(group.Key)}: bias {bias:+0.00;-0.00}s exceeds ±{BiasBudgetSeconds:F1}s (n={errors.Count})");
        }

        output.WriteLine($"\ncovered {covered} voice configurations over {scored} measured bulletins");
        covered.ShouldBeGreaterThanOrEqualTo(5, "the calibration should cover the fleet's working voices");
        failures.ShouldBeEmpty(string.Join("\n", failures));
    }

    /// <summary>
    ///     The fleet-wide check: across every covered voice at once the model must not lean. A
    ///     per-voice pass with a consistent one-way lean would still move every content decision.
    /// </summary>
    [Fact]
    public void ShippedCalibration_HasNoFleetWideLean()
    {
        var model = new TokenAdditiveDurationModel(
            EmbeddedSpokenDurationCalibration.Load(),
            new SpokenDurationModelOptions { Enabled = true });

        var errors = new List<double>();
        foreach (var o in LoadObservations())
        {
            var predicted = model.Predict(
                o.Text, new DurationVoiceKey(o.Voice.VoiceId, o.Voice.ModelId, o.Voice.Speed, o.Voice.PaceBoostWpm));
            if (predicted.HasValue)
                errors.Add(predicted.Value.Seconds - o.NaturalSeconds);
        }

        errors.Count.ShouldBeGreaterThan(80);
        double bias = errors.Average();
        double mae = errors.Average(Math.Abs);
        output.WriteLine($"fleet: n={errors.Count} MAE={mae:F2}s bias={bias:+0.00;-0.00}s");
        Math.Abs(bias).ShouldBeLessThanOrEqualTo(FleetBiasBudgetSeconds,
            $"fleet-wide lean of {bias:F2}s moves every length decision");
        mae.ShouldBeLessThanOrEqualTo(FleetMaeBudgetSeconds);
    }

    private static string Label((string VoiceId, string ModelId, double Speed, double Pace) key) =>
        $"{key.VoiceId[..Math.Min(10, key.VoiceId.Length)]} {key.ModelId} sp{key.Speed:G} pb{key.Pace:G}";

    private static List<Observation> LoadObservations()
    {
        string path = Path.Combine(RepoRoot(), "test", "corpus", "duration", "dataset-v2.json");
        File.Exists(path).ShouldBeTrue($"expected the scraped duration dataset at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var result = new List<Observation>();
        foreach (var element in doc.RootElement.GetProperty("observations").EnumerateArray())
        {
            if (!element.TryGetProperty("text", out var textElement))
                continue;                  // pace-only observation: no recoverable script
            var voice = element.GetProperty("voice");
            result.Add(new Observation(
                element.GetProperty("jobId").GetString() ?? "",
                element.TryGetProperty("market", out var m) ? m.GetString() ?? "" : "",
                element.GetProperty("words").GetInt32(),
                element.GetProperty("naturalSeconds").GetDouble(),
                textElement.GetString() ?? "",
                new VoiceRow(
                    voice.GetProperty("voiceId").GetString() ?? "",
                    voice.TryGetProperty("voiceName", out var n) ? n.GetString() ?? "" : "",
                    voice.GetProperty("modelId").GetString() ?? "",
                    voice.GetProperty("speed").GetDouble(),
                    voice.GetProperty("paceBoostWpm").GetDouble())));
        }

        return result;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArnBulletinGenerator.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

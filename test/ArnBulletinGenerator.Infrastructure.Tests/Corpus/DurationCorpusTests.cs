using ArnBulletinGenerator.Domain.Services;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Offline integrity, feature-parity, and model-measurement gates for the committed duration corpus.
///     The tests deliberately use the recorded natural (pre-stretch, pre-trim) duration and never call
///     ElevenLabs or any other external service.
/// </summary>
public sealed class DurationCorpusTests(ITestOutputHelper output)
{
    private const string FillerRule = "filler-stitched";
    private const string TrimmedRule = "trimmed-during-synthesis";

    [Fact]
    [Trait("Category", "Corpus")]
    public void DurationCorpus_Integrity()
    {
        if (!DurationCorpusLoader.TryLoad(out DurationCorpus? corpus, out string reason))
        {
            output.WriteLine($"Skipped: {reason}");
            return;
        }

        DurationCorpusManifest manifest = corpus!.Manifest;
        Assert.Equal(3, manifest.SchemaVersion);
        Assert.True(
            manifest.IrreducibleNoise.HistoricalCrossJobVariance.PooledCoefficientOfVariation
                >= 2 * manifest.IrreducibleNoise.SameInputTtsVariance.Unseeded.Cv,
            "historical cross-job variance should be at least twice controlled same-input TTS variance");
        Assert.Equal(manifest.Voices.Count, manifest.Counts.Fingerprints);

        int total = 0;
        int clean = 0;
        int tokens = 0;
        var cleanRates = new List<double>();
        var contaminatedRates = new List<double>();

        foreach (DurationVoiceSummary summary in manifest.Voices)
        {
            DurationVoiceFile voice = corpus.Voices.Single(v => v.File == summary.File);
            Assert.Equal(summary.Total, voice.Bulletins.Count);
            Assert.Equal(summary.Clean, voice.Bulletins.Count(b => b.Clean));
            Assert.Equal(summary.Tokens, voice.Bulletins.Sum(b => b.Tokens.Count));
            Assert.Equal(summary.VoiceId, voice.VoiceId);
            Assert.Equal(summary.ModelId, voice.ModelId);
            Assert.Equal(summary.Speed, voice.Speed, 10);

            total += voice.Bulletins.Count;
            clean += voice.Bulletins.Count(b => b.Clean);
            tokens += voice.Bulletins.Sum(b => b.Tokens.Count);
            foreach (DurationBulletin bulletin in voice.Bulletins)
            {
                Assert.False(string.IsNullOrWhiteSpace(bulletin.SpokenText), bulletin.JobId);
                Assert.True(bulletin.NaturalSec > 0, bulletin.JobId);
                Assert.True(bulletin.FittedSec > 0, bulletin.JobId);
                Assert.InRange(bulletin.Stretch, 0.5 + double.Epsilon, 2.0 - double.Epsilon);
                Assert.True(bulletin.TargetSec >= 0, bulletin.JobId);
                Assert.NotEmpty(bulletin.Tokens);
                Assert.Equal(bulletin.ExcludedBecause.Count == 0, bulletin.Clean);
                Assert.All(bulletin.ExcludedBecause,
                    rule => Assert.True(rule is FillerRule or TrimmedRule, $"{bulletin.JobId}: {rule}"));

                double previousStart = double.NegativeInfinity;
                foreach (DurationToken token in bulletin.Tokens)
                {
                    Assert.True(token.S >= previousStart, bulletin.JobId);
                    Assert.True(token.E >= token.S, $"{bulletin.JobId}: {token.W}");
                    previousStart = token.S;
                }

                double rate = SpokenDurationFeatures.Extract(bulletin.SpokenText).Syl / bulletin.NaturalSec;
                (bulletin.Clean ? cleanRates : contaminatedRates).Add(rate);
            }
        }

        Assert.Equal(manifest.Counts.Total, total);
        Assert.Equal(manifest.Counts.Clean, clean);
        Assert.Equal(manifest.Counts.Tokens, tokens);
        Assert.Equal(manifest.Counts.Fingerprints, corpus.Voices.Count);

        // A filler-stitch example leaves an observed -8.9s residual; a pre-trim duration example
        // implies an impossible 2.8 syllables/second. Both contamination rules must remain visible.
        Assert.NotEmpty(contaminatedRates);
        Assert.True(StandardDeviation(cleanRates) < StandardDeviation(contaminatedRates),
            $"clean syllable-rate SD {StandardDeviation(cleanRates):F3} was not below contaminated "
            + $"SD {StandardDeviation(contaminatedRates):F3}");
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void DurationCorpus_FeaturesMatchCanonicalExtractor()
    {
        if (!DurationCorpusLoader.TryLoad(out DurationCorpus? corpus, out string reason))
        {
            output.WriteLine($"Skipped: {reason}");
            return;
        }

        var mismatches = new List<string>();
        foreach (DurationVoiceFile voice in corpus!.Voices)
            foreach (DurationBulletin bulletin in voice.Bulletins)
            {
                SpokenDurationFeatures.Features actual = SpokenDurationFeatures.Extract(bulletin.SpokenText);
                DurationFeaturesJson expected = bulletin.Features;
                if (actual.Syl != expected.Syl || actual.Words != expected.Words ||
                    actual.Ellipsis != expected.Ellipsis || actual.Period != expected.Period ||
                    actual.Comma != expected.Comma || actual.Dash != expected.Dash ||
                    actual.CostlyTags != expected.CostlyTags || actual.Tags != expected.Tags)
                {
                    mismatches.Add($"{voice.File}/{bulletin.JobId}: "
                        + $"expected ({expected.Syl},{expected.Words},{expected.Ellipsis},{expected.Period},"
                        + $"{expected.Comma},{expected.Dash},{expected.CostlyTags},{expected.Tags}), "
                        + $"actual ({actual.Syl},{actual.Words},{actual.Ellipsis},{actual.Period},"
                        + $"{actual.Comma},{actual.Dash},{actual.CostlyTags},{actual.Tags})");
                }
            }

        Assert.True(mismatches.Count == 0,
            "Python/C# feature parity mismatches (do not relax this gate):\n" + string.Join('\n', mismatches));
    }

    [Fact]
    [Trait("Category", "Corpus")]
    public void DurationCorpus_AccuracyCharacterisation()
    {
        if (!DurationCorpusLoader.TryLoad(out DurationCorpus? corpus, out string reason))
        {
            output.WriteLine($"Skipped: {reason}");
            return;
        }
        // Match the benchmark harness: static A has no fitted parameters and scores every clean
        // row (142); per-voice fitted D/J require at least eight clean rows (127).
        var allRows = corpus!.Voices
            .SelectMany(v => v.Bulletins.Where(b => b.Clean).Select(b => new DurationRow(v, b)))
            .ToList();
        var eligibleRows = allRows.Where(r => r.Voice.Bulletins.Count(b => b.Clean) >= 8).ToList();
        var allGroups = allRows.GroupBy(r => r.Voice.File).ToList();
        var eligibleGroups = eligibleRows.GroupBy(r => r.Voice.File).ToList();

        RegressionResult a = Evaluate(allGroups, ModelKind.StaticWpm);
        RegressionResult d = Evaluate(eligibleGroups, ModelKind.SyllableIntercept);
        RegressionResult j = Evaluate(eligibleGroups, ModelKind.TokenAffine);
        output.WriteLine($"C# measured: A MAE={a.Mae:F3}s bias={a.Bias:F3}s within15={a.Within15:P1}; "
            + $"D MAE={d.Mae:F3}s bias={d.Bias:F3}s within15={d.Within15:P1}; "
            + $"J MAE={j.Mae:F3}s bias={j.Bias:F3}s within15={j.Within15:P1} max={j.Max:F3}s");
        output.WriteLine("Python reference: A 2.09s, D 1.13s, E 1.23s, J 1.12s MAE; J max 5.43s; noise floor 0.17s.");

        // Reference A MAE 2.09s: the static ceiling-rate model must remain materially worse than
        // per-voice D and J, rather than silently becoming the benchmark again.
        Assert.True(a.Mae > d.Mae, $"A MAE {a.Mae:F3}s <= D MAE {d.Mae:F3}s");
        Assert.True(a.Mae > j.Mae, $"A MAE {a.Mae:F3}s <= J MAE {j.Mae:F3}s");
        // J measured 1.125s (rounded 1.12s), and the controlled same-input floor is now 0.17s;
        // keep meaningful headroom while rejecting both a broken model and accidental leakage.
        // Seed finding: pinning a seed shifts the mean 9.3% without reliably reducing variance,
        // so it is not an accuracy fix to smuggle into this gate.
        Assert.InRange(j.Mae, corpus.Manifest.IrreducibleNoise.SameInputTtsVariance.BestAchievableMaeAt22s + 0.001, 1.40);
        Assert.InRange(j.Bias, -0.25, 0.25);
        // Reference J is 96% within 15% and has 5.43s max error; 90% is the durable gate.
        Assert.True(j.Within15 >= 0.90, $"J within-15% rate was {j.Within15:P1}");
    }

    private static RegressionResult Evaluate(IReadOnlyList<IGrouping<string, DurationRow>> groups, ModelKind model)
    {
        var errors = new List<double>();
        foreach (IGrouping<string, DurationRow> group in groups)
        {
            DurationRow[] all = group.ToArray();
            for (int heldOut = 0; heldOut < all.Length; heldOut++)
            {
                var training = all.Where((_, index) => index != heldOut).ToArray();
                DurationRow test = all[heldOut];
                double predicted = model switch
                {
                    ModelKind.StaticWpm => test.Bulletin.Features.Words * 60.0 / 150.0,
                    ModelKind.SyllableIntercept => PredictSyllableIntercept(training, test),
                    ModelKind.TokenAffine => PredictTokenAffine(training, test),
                    _ => throw new ArgumentOutOfRangeException(nameof(model))
                };
                errors.Add(predicted - test.Observed);
            }
        }

        return new RegressionResult(
            errors.Average(Math.Abs),
            errors.Average(),
            errors.Max(Math.Abs),
            errors.Count(error => Math.Abs(error) <= 22 * 0.15) / (double)errors.Count);
    }

    private static double PredictSyllableIntercept(IReadOnlyList<DurationRow> training, DurationRow test)
    {
        double[][] x = training.Select(row => new[] { (double)row.Bulletin.Features.Syl, 1.0 }).ToArray();
        double[] beta = RidgeFit(x, training.Select(row => row.Observed).ToArray());
        return test.Bulletin.Features.Syl * beta[0] + beta[1];
    }

    private static double PredictTokenAffine(IReadOnlyList<DurationRow> training, DurationRow test)
    {
        var tokenData = training.SelectMany(TokenRows).ToArray();
        double[] tokenBeta = RidgeFit(tokenData.Select(pair => pair.X).ToArray(),
            tokenData.Select(pair => pair.Y).ToArray());
        double[][] shapeX = training.Select(row => new[] { BulletinShape(row.Bulletin, tokenBeta), 1.0 }).ToArray();
        double[] affine = RidgeFit(shapeX, training.Select(row => row.Observed).ToArray());
        return BulletinShape(test.Bulletin, tokenBeta) * affine[0] + affine[1];
    }

    private static IEnumerable<(double[] X, double Y)> TokenRows(DurationRow row)
    {
        for (int i = 0; i < row.Bulletin.Tokens.Count; i++)
        {
            DurationToken token = row.Bulletin.Tokens[i];
            double gap = i + 1 < row.Bulletin.Tokens.Count
                ? Math.Max(0, row.Bulletin.Tokens[i + 1].S - token.E)
                : 0;
            double cost = ((token.E - token.S) + gap) * row.Bulletin.Stretch * row.Voice.Speed;
            if (cost <= 0 || cost >= 5)
                continue;

            int punctuation = (int)SpokenDurationFeatures.ClassifyTokenPunctuation(token.W);
            var x = new double[6];
            x[0] = SyllableCounter.CountSyllables(token.W);
            // Python harness order: syllables, none, comma, period, ellipsis, dash.
            int oneHotIndex = punctuation switch
            {
                0 => 1,
                2 => 2,
                1 => 3,
                4 => 4,
                _ => 5
            };
            x[oneHotIndex] = 1;
            yield return (x, cost);
        }
    }

    private static double BulletinShape(DurationBulletin bulletin, IReadOnlyList<double> tokenBeta)
    {
        double syl = bulletin.Features.Syl;
        double comma = bulletin.Features.Comma;
        double period = bulletin.Features.Period;
        double ellipsis = bulletin.Features.Ellipsis;
        double dash = bulletin.Features.Dash;
        double plainWords = Math.Max(0, bulletin.Features.Words - comma - period - ellipsis - dash);
        return syl * tokenBeta[0] + plainWords * tokenBeta[1] + comma * tokenBeta[2]
            + period * tokenBeta[3] + ellipsis * tokenBeta[4] + dash * tokenBeta[5];
    }

    private static double[] RidgeFit(IReadOnlyList<double[]> x, IReadOnlyList<double> y)
    {
        const double lambda = 0.005;
        int width = x[0].Length;
        var gram = new double[width, width];
        var rhs = new double[width];
        for (int row = 0; row < x.Count; row++)
        {
            for (int i = 0; i < width; i++)
            {
                rhs[i] += x[row][i] * y[row];
                for (int j = 0; j < width; j++)
                    gram[i, j] += x[row][i] * x[row][j];
            }
        }
        for (int i = 0; i < width; i++)
            gram[i, i] += lambda;
        return SolveLinearSystem(gram, rhs);
    }

    private static double[] SolveLinearSystem(double[,] matrix, double[] rhs)
    {
        int n = rhs.Length;
        var augmented = new double[n, n + 1];
        for (int row = 0; row < n; row++)
        {
            for (int column = 0; column < n; column++)
                augmented[row, column] = matrix[row, column];
            augmented[row, n] = rhs[row];
        }

        for (int pivot = 0; pivot < n; pivot++)
        {
            int best = pivot;
            for (int row = pivot + 1; row < n; row++)
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot]))
                    best = row;
            if (best != pivot)
                for (int column = pivot; column <= n; column++)
                    (augmented[pivot, column], augmented[best, column]) =
                        (augmented[best, column], augmented[pivot, column]);

            double divisor = augmented[pivot, pivot];
            for (int column = pivot; column <= n; column++)
                augmented[pivot, column] /= divisor;
            for (int row = 0; row < n; row++)
            {
                if (row == pivot)
                    continue;
                double factor = augmented[row, pivot];
                for (int column = pivot; column <= n; column++)
                    augmented[row, column] -= factor * augmented[pivot, column];
            }
        }

        var result = new double[n];
        for (int i = 0; i < n; i++)
            result[i] = augmented[i, n];
        return result;
    }

    private static double StandardDeviation(IReadOnlyList<double> values)
    {
        double mean = values.Average();
        return Math.Sqrt(values.Sum(value => Math.Pow(value - mean, 2)) / values.Count);
    }

    private enum ModelKind { StaticWpm, SyllableIntercept, TokenAffine }

    private sealed record DurationRow(DurationVoiceFile Voice, DurationBulletin Bulletin)
    {
        public double Observed => Bulletin.NaturalSec * Voice.Speed;
    }

    private readonly record struct RegressionResult(double Mae, double Bias, double Max, double Within15);
}

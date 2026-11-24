namespace ArnBulletinGenerator.Domain.ValueObjects;

/// <summary>
///     Tracks the estimated vs final duration across multiple estimation methods.
/// </summary>
public sealed record DurationWaterfall(
    double TargetSeconds,
    double EstimatedSeconds,
    double WordCountEstimatedSeconds,
    double FinalSeconds,
    int WordCount,
    double EffectiveWpm)
{
    /// <summary>Accuracy of the word-count estimate vs actual (0–100%).</summary>
    public double WordCountAccuracyPercent =>
        FinalSeconds > 0
            ? Math.Max(0, 100.0 - Math.Abs(WordCountEstimatedSeconds - FinalSeconds) / FinalSeconds * 100.0)
            : 0;

    /// <summary>Accuracy of the syllable-based estimate vs actual (0–100%).</summary>
    public double SyllableAccuracyPercent =>
        FinalSeconds > 0
            ? Math.Max(0, 100.0 - Math.Abs(EstimatedSeconds - FinalSeconds) / FinalSeconds * 100.0)
            : 0;
}

using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Selects which <c>IDurationPredictor</c> strategy is active at runtime.
/// </summary>
public enum PredictionMode
{
    /// <summary>
    ///     Uses static syllable-per-second and syllables-per-word defaults
    ///     from <c>BroadcastOptions</c>. No historical data required.
    ///     This is the current production behavior.
    /// </summary>
    StaticRate = 0,

    /// <summary>
    ///     Uses a synthesis history table to interpolate the expected duration
    ///     from past (voice, model, speed, syllableCount) → measured outcomes.
    ///     Falls back to <c>StaticRate</c> when no data is available.
    /// </summary>
    HistoryBased = 1
}

/// <summary>
///     Configuration for duration prediction strategy selection.
/// </summary>
public sealed class DurationPredictionOptions
{
    public const string SectionName = "DurationPrediction";

    /// <summary>Which prediction strategy to use.</summary>
    public PredictionMode Mode { get; set; } = PredictionMode.StaticRate;

    /// <summary>
    ///     Fallback syllables-per-second used when no calibration or history data exists.
    ///     This mirrors <c>BroadcastOptions.SyllablesPerSecond</c> but lives here so the
    ///     predictor is self-contained.
    /// </summary>
    [Range(2.0, 12.0)]
    public double FallbackSyllablesPerSecond { get; set; } = 5.8;

    /// <summary>
    ///     Fallback syllables-per-word used when no calibration or history data exists.
    ///     Australian English averages ~1.4–1.6. Our radio domain (long place names)
    ///     trends higher.
    /// </summary>
    [Range(1.0, 3.0)]
    public double FallbackSyllablesPerWord { get; set; } = 1.5;
}

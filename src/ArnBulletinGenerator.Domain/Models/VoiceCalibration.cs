using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Stores measured speech rate for a specific voice to enable accurate duration estimation.
///     It supports self-tuning by averaging results across multiple runs for a specific configuration.
/// </summary>
public sealed class VoiceCalibration(
    string ConfigurationKey,
    string VoiceId,
    VoiceSettings Settings,
    string ModelId = "unknown")
{
    /// <summary>
    ///     Unique key for this specific voice, settings, and (when available) TTS model.
    ///     Typically: {voiceId}_{speed}_{stability}_{similarity}_{style}_{speakerBoost}_{model}
    /// </summary>
    public string ConfigurationKey { get; init; } = ConfigurationKey;

    /// <summary>TTS model used to produce the measured speech-rate sample.</summary>
    public string ModelId { get; init; } = string.IsNullOrWhiteSpace(ModelId) ? "unknown" : ModelId;

    /// <summary>
    ///     Voice ID for the configured TTS provider.
    /// </summary>
    public string VoiceId { get; init; } = VoiceId;

    /// <summary>
    ///     The specific voice settings this calibration is for.
    /// </summary>
    public VoiceSettings Settings { get; init; } = Settings;

    /// <summary>
    ///     TTS provider name.
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>
    ///     Measured syllables spoken per second (exponential moving average).
    /// </summary>
    public double SyllablesPerSecond { get; set; }

    /// <summary>
    ///     Average syllables per word for this voice and script style profile (EMA).
    /// </summary>
    public double SyllablesPerWord { get; set; }

    /// <summary>
    ///     Measured words per minute (exponential moving average).
    /// </summary>
    public double WordsPerMinute { get; set; }

    /// <summary>
    ///     Measured syllables per minute (exponential moving average).
    /// </summary>
    public double SyllablesPerMinute { get; set; }

    /// <summary>
    ///     Number of times this configuration has been measured.
    /// </summary>
    public int RunCount { get; set; }

    /// <summary>
    ///     Number of syllables in the reference phrase used for calibration.
    /// </summary>
    public int ReferenceSyllableCount { get; set; }

    /// <summary>
    ///     Actual duration in seconds of the synthesized reference phrase during last calibration.
    /// </summary>
    public double ReferenceDurationSeconds { get; set; }

    /// <summary>
    ///     When this calibration was performed.
    /// </summary>
    public DateTime CalibratedAt { get; set; }

    /// <summary>
    ///     How long this calibration is considered valid. Default: 30 days.
    ///     After expiry, recalibration should occur on next use.
    /// </summary>
    public TimeSpan ValidFor { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     Whether this calibration has expired and should be refreshed.
    /// </summary>
    public bool IsExpiredAt(DateTime utcNow) => utcNow > CalibratedAt + ValidFor;

    /// <summary>
    ///     Largest fraction a SINGLE measurement may move an established rate. At α=0.3 the EMA has an
    ///     effective memory of only ~3 measurements, so one out-of-family write moves the row almost a
    ///     third of the way to itself — enough to break downstream slot fitting. Verified live on dev:
    ///     the row for Dolly Male Young went 138.3 → 186.1 wpm (+35%) between two identical triggers
    ///     three minutes apart while every honest measurement in that window read 136-147 wpm, and the
    ///     job that read the spiked value burned 4 eleven_v3 renders (115s) reproducing a failure the
    ///     aim-point fix had already eliminated. A real voice's rate does not move 10% between
    ///     bulletins, so clamping the step costs nothing and bounds the damage of a bad write to one
    ///     step instead of one third.
    /// </summary>
    public const double MaxPerMeasurementDrift = 0.10;

    /// <summary>
    ///     Records a new measurement using exponential moving average (EMA) smoothing, with each rate
    ///     limited to <see cref="MaxPerMeasurementDrift" /> of its previous value.
    ///     Increments <see cref="RunCount" /> and updates all rate metrics.
    /// </summary>
    public void RecordMeasurement(
        double measuredSyllablesPerSecond,
        double measuredSyllablesPerWord,
        DateTime utcNow,
        double emaAlpha = 0.3,
        double? referenceDuration = null,
        int? referenceSyllableCount = null)
    {
        bool isFirstRun = RunCount == 0;
        RunCount++;

        SyllablesPerSecond = RateLimited(SyllablesPerSecond,
            ExponentialMovingAverage.Apply(SyllablesPerSecond, measuredSyllablesPerSecond, isFirstRun, emaAlpha));
        SyllablesPerWord = RateLimited(SyllablesPerWord,
            ExponentialMovingAverage.Apply(SyllablesPerWord, measuredSyllablesPerWord, isFirstRun, emaAlpha));

        double measuredSyllablesPerMinute = measuredSyllablesPerSecond * 60.0;
        SyllablesPerMinute = RateLimited(SyllablesPerMinute,
            ExponentialMovingAverage.Apply(SyllablesPerMinute, measuredSyllablesPerMinute, isFirstRun, emaAlpha));

        if (measuredSyllablesPerWord > 0)
        {
            double measuredWordsPerMinute = measuredSyllablesPerMinute / measuredSyllablesPerWord;
            WordsPerMinute = RateLimited(WordsPerMinute,
                ExponentialMovingAverage.Apply(WordsPerMinute, measuredWordsPerMinute, isFirstRun, emaAlpha));
        }

        CalibratedAt = utcNow;

        if (referenceDuration.HasValue)
            ReferenceDurationSeconds = referenceDuration.Value;

        if (referenceSyllableCount.HasValue)
            ReferenceSyllableCount = referenceSyllableCount.Value;
    }

    /// <summary>Clamps <paramref name="next" /> to within <see cref="MaxPerMeasurementDrift" /> of
    /// <paramref name="previous" />. A cold rate (previous ≤ 0) is adopted verbatim.</summary>
    private static double RateLimited(double previous, double next)
    {
        if (previous <= 0)
            return next;

        return Math.Clamp(next, previous * (1.0 - MaxPerMeasurementDrift), previous * (1.0 + MaxPerMeasurementDrift));
    }
}

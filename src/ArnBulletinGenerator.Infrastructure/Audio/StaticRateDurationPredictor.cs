using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Options;

using static ArnBulletinGenerator.Domain.Services.SyllableCounter;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Predicts spoken duration using a static syllable rate derived from
///     <c>DurationPredictionOptions</c>. This matches the current production behavior:
///     syllable count × (1 / syllablesPerSecond) with a configurable fallback.
///     <para>
///         No historical data required. Use as baseline or fallback when
///         <c>PredictionMode.HistoryBased</c> has insufficient data.
///     </para>
/// </summary>
public sealed class StaticRateDurationPredictor : IDurationPredictor
{
    private readonly DurationPredictionOptions _options;

    public StaticRateDurationPredictor(IOptions<DurationPredictionOptions> options)
    {
        _options = options.Value;
    }

    public PredictionResult PredictDuration(string text, PredictionContext context)
    {
        int syllableCount = CountSyllables(text);
        double sps = GetEffectiveSps(context);
        double speed = context.Speed > 0 ? context.Speed : 1.0;

        double expectedDuration = syllableCount / (sps * speed);

        return new PredictionResult
        {
            ExpectedDuration = expectedDuration,
            ConfidenceLower = expectedDuration * 0.95,
            ConfidenceUpper = expectedDuration * 1.05,
            DataPointsUsed = 0,
            StrategyName = nameof(StaticRateDurationPredictor)
        };
    }

    public PredictionResult GetTargetSyllables(double targetDurationSeconds, PredictionContext context)
    {
        if (targetDurationSeconds <= 0)
        {
            return new PredictionResult
            {
                ExpectedDuration = 0,
                ConfidenceLower = 0,
                ConfidenceUpper = 0,
                DataPointsUsed = 0,
                StrategyName = nameof(StaticRateDurationPredictor)
            };
        }

        double sps = GetEffectiveSps(context);
        double speed = context.Speed > 0 ? context.Speed : 1.0;

        double targetSyllables = targetDurationSeconds * sps * speed;

        return new PredictionResult
        {
            ExpectedDuration = targetDurationSeconds,
            ConfidenceLower = targetSyllables * 0.95,
            ConfidenceUpper = targetSyllables * 1.05,
            DataPointsUsed = 0,
            StrategyName = nameof(StaticRateDurationPredictor)
        };
    }

    private double GetEffectiveSps(PredictionContext context)
    {
        // Use calibrated SPS if available AND positive, otherwise fall back to config default
        double? calibrated = context.CalibratedSyllablesPerSecond;
        return calibrated is > 0 ? calibrated.Value : _options.FallbackSyllablesPerSecond;
    }
}

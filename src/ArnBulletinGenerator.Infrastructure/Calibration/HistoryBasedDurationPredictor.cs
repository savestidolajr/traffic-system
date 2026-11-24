using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Options;

using static ArnBulletinGenerator.Domain.Services.SyllableCounter;

namespace ArnBulletinGenerator.Infrastructure.Calibration;

/// <summary>
///     Predicts spoken duration by interpolating historical synthesis outcomes.
///     Uses weighted-distance interpolation from past (voice, model, speed, syllableCount) tuples.
///     Falls back to static-rate prediction when insufficient history exists.
///     <para>
///         This predictor gets more accurate with every broadcast as the history table grows.
///         Switch to it by setting <c>DurationPredictionOptions.Mode = HistoryBased</c>.
///     </para>
/// </summary>
public sealed class HistoryBasedDurationPredictor : IDurationPredictor
{
    private readonly ISynthesisHistoryRepository _history;
    private readonly IDurationPredictor _fallback;
    private readonly DurationPredictionOptions _options;

    private const int MinDataPointsForPrediction = 2;
    private const int MaxNeighbors = 5;
    private const double ExactMatchThreshold = 0.01;
    // z-score for an 80% interval (p10..p90). Tighter than the 95% band so the Phase 2 predictive
    // fit gate can treat ConfidenceUpper as a genuinely optimistic-but-confident (p90) estimate.
    private const double P10P90Z = 1.2816;

    public HistoryBasedDurationPredictor(
        ISynthesisHistoryRepository history,
        IOptions<DurationPredictionOptions> options,
        IDurationPredictor fallback)
    {
        _history = history;
        _options = options.Value;
        _fallback = fallback;
    }

    public PredictionResult PredictDuration(string text, PredictionContext context)
    {
        int syllableCount = CountSyllables(text);
        double sps = GetEffectiveSps(context);
        double speed = context.Speed > 0 ? context.Speed : 1.0;

        var neighbors = _history.FindClosestBySyllables(
            context.VoiceId, context.ModelId, speed, syllableCount, MaxNeighbors);

        if (neighbors.Count < MinDataPointsForPrediction)
            return _fallback.PredictDuration(text, context);

        return InterpolateDuration(syllableCount, neighbors, sps, speed);
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
                StrategyName = nameof(HistoryBasedDurationPredictor)
            };
        }

        double sps = GetEffectiveSps(context);
        double speed = context.Speed > 0 ? context.Speed : 1.0;

        var neighbors = _history.FindClosestByDuration(
            context.VoiceId, context.ModelId, speed, targetDurationSeconds, MaxNeighbors);

        if (neighbors.Count < MinDataPointsForPrediction)
            return _fallback.GetTargetSyllables(targetDurationSeconds, context);

        return InterpolateSyllableTarget(targetDurationSeconds, neighbors, sps, speed);
    }

    private PredictionResult InterpolateDuration(
        int targetSyllables,
        IReadOnlyList<SynthesisRecord> neighbors,
        double sps,
        double speed)
    {
        double totalWeight = 0;
        double weightedDuration = 0;

        foreach (var record in neighbors)
        {
            double distance = Math.Abs(record.SyllableCount - targetSyllables);
            double weight = 1.0 / (1.0 + distance);

            // Normalize the achieved duration to speed=1.0, then re-apply our speed
            // This handles the case where history has records at different speeds
            double normalizedDuration = record.AchievedDuration / record.Speed * speed;
            weightedDuration += normalizedDuration * weight;
            totalWeight += weight;
        }

        double predictedDuration = totalWeight > 0 ? weightedDuration / totalWeight : targetSyllables / (sps * speed);

        // Calculate confidence interval from the spread of weighted predictions
        double variance = 0;
        foreach (var record in neighbors)
        {
            double normalizedDuration = record.AchievedDuration / record.Speed * speed;
            double diff = normalizedDuration - predictedDuration;
            variance += diff * diff * (1.0 / (1.0 + Math.Abs(record.SyllableCount - targetSyllables)));
        }
        double stdDev = totalWeight > 0 ? Math.Sqrt(variance / totalWeight) : predictedDuration * 0.05;

        return new PredictionResult
        {
            ExpectedDuration = predictedDuration,
            ConfidenceLower = predictedDuration - P10P90Z * stdDev,
            ConfidenceUpper = predictedDuration + P10P90Z * stdDev,
            DataPointsUsed = neighbors.Count,
            StrategyName = nameof(HistoryBasedDurationPredictor)
        };
    }

    private PredictionResult InterpolateSyllableTarget(
        double targetDuration,
        IReadOnlyList<SynthesisRecord> neighbors,
        double sps,
        double speed)
    {
        double totalWeight = 0;
        double weightedSyllables = 0;

        foreach (var record in neighbors)
        {
            double distance = Math.Abs(record.AchievedDuration / record.Speed * speed - targetDuration);
            double weight = 1.0 / (1.0 + distance);

            weightedSyllables += record.SyllableCount * weight;
            totalWeight += weight;
        }

        double predictedSyllables = totalWeight > 0
            ? weightedSyllables / totalWeight
            : targetDuration * sps * speed;

        double variance = 0;
        foreach (var record in neighbors)
        {
            double diff = record.SyllableCount - predictedSyllables;
            variance += diff * diff * (1.0 / (1.0 + Math.Abs(record.AchievedDuration - targetDuration)));
        }
        double stdDev = totalWeight > 0 ? Math.Sqrt(variance / totalWeight) : predictedSyllables * 0.05;

        return new PredictionResult
        {
            ExpectedDuration = targetDuration,
            ConfidenceLower = predictedSyllables - P10P90Z * stdDev,
            ConfidenceUpper = predictedSyllables + P10P90Z * stdDev,
            DataPointsUsed = neighbors.Count,
            StrategyName = nameof(HistoryBasedDurationPredictor)
        };
    }

    private double GetEffectiveSps(PredictionContext context)
    {
        double? calibrated = context.CalibratedSyllablesPerSecond;
        return calibrated is > 0 ? calibrated.Value : _options.FallbackSyllablesPerSecond;
    }
}

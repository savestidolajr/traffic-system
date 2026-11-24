using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Evaluates the pooled token-shape prosody formula over the shared
///     <see cref="SpokenDurationFeatures"/> extractor. The bulletin corpus cannot identify a
///     duration model on its own because every observation is a slot-constrained 45-60 word read,
///     making word count and syllable count collinear and leaving little length variation to fit
///     a rate. The 11 shape coefficients are fitted once across 11,869 measured word alignments
///     spanning 0 to 5+ syllables, while each voice row supplies one fitted scale. Leave-one-out
///     measurement gives MAE 0.857s, p90 1.75s and bias -0.03s, versus 1.094s / 2.15s for the
///     superseded form. Lookup first requires an exact voice/model/settings match, then degrades
///     to the nearest settings row for that voice and model, and finally to the nearest row for
///     that voice across models. The returned tier keeps that evidence visible to callers.
/// </summary>
public sealed class TokenAdditiveDurationModel
    (SpokenDurationCalibration calibration, SpokenDurationModelOptions options) : ISpokenDurationModel
{
    private const double VoiceSettingEpsilon = 1e-6;

    /// <summary>
    ///     Same voice/model but different settings are given a 1.5x residual margin: the fitted
    ///     leave-one-out model has 0.857 seconds MAE, and the nearest setting contributes unmeasured
    ///     setting variance. Crossing model boundaries is given 2.0x for the additional unknown
    ///     model variance, while keeping the tightest 1.12-second calibration residual below the
    ///     approximately 2.5-second safety-margin ceiling observed for a 22-second slot.
    /// </summary>
    private const double VoiceAndModelResidualInflation = 1.5;

    /// <summary>Cross-model fallback inflation; see <see cref="VoiceAndModelResidualInflation"/>.</summary>
    private const double VoiceOnlyResidualInflation = 2.0;

    public Option<PredictedDuration> Predict(string spokenText, DurationVoiceKey voice)
    {
        if (!options.Enabled || string.IsNullOrWhiteSpace(voice.VoiceId) ||
            string.IsNullOrWhiteSpace(voice.ModelId) || voice.Speed <= 0 ||
            double.IsNaN(voice.Speed))
            return Option<PredictedDuration>.None;

        // A missing DTO pace boost arrives as NaN in some trigger paths; the calibration schema
        // stores the absent value as 0.0, so normalise before the exact comparison.
        double requestedPaceBoostWpm = double.IsNaN(voice.PaceBoostWpm) ? 0 : voice.PaceBoostWpm;
        SpokenDurationVoiceCalibration? row = calibration.Voices.FirstOrDefault(candidate =>
            string.Equals(candidate.VoiceId, voice.VoiceId, StringComparison.Ordinal) &&
            string.Equals(candidate.ModelId, voice.ModelId, StringComparison.Ordinal) &&
            Math.Abs(candidate.Speed - voice.Speed) <= VoiceSettingEpsilon &&
            Math.Abs(candidate.PaceBoostWpm - requestedPaceBoostWpm) <= VoiceSettingEpsilon);
        SpokenDurationMatchTier tier = SpokenDurationMatchTier.Exact;

        if (row is null)
        {
            row = FindNearest(
                calibration.Voices.Where(candidate =>
                    string.Equals(candidate.VoiceId, voice.VoiceId, StringComparison.Ordinal) &&
                    string.Equals(candidate.ModelId, voice.ModelId, StringComparison.Ordinal)),
                voice.Speed,
                requestedPaceBoostWpm);
            tier = row is null ? SpokenDurationMatchTier.VoiceOnly : SpokenDurationMatchTier.VoiceAndModel;
        }

        if (row is null)
        {
            row = FindNearest(
                calibration.Voices.Where(candidate =>
                    string.Equals(candidate.VoiceId, voice.VoiceId, StringComparison.Ordinal)),
                voice.Speed,
                requestedPaceBoostWpm);
        }

        if (row is null)
            return Option<PredictedDuration>.None;

        SpokenDurationFeatures.Features features = SpokenDurationFeatures.Extract(spokenText);
        double shapeTotal = calibration.Shape.Total(features);
        double secondsAtSpeedOne = row.Scale * shapeTotal;
        double residual = row.ResidualSdSec * (tier switch
        {
            SpokenDurationMatchTier.VoiceAndModel => VoiceAndModelResidualInflation,
            SpokenDurationMatchTier.VoiceOnly => VoiceOnlyResidualInflation,
            _ => 1
        });

        return Option<PredictedDuration>.Some(
            new PredictedDuration(secondsAtSpeedOne / voice.Speed, residual, tier));
    }
    private static SpokenDurationVoiceCalibration? FindNearest(
        IEnumerable<SpokenDurationVoiceCalibration> candidates,
        double speed,
        double paceBoostWpm)
    {
        SpokenDurationVoiceCalibration? nearest = null;
        double nearestDistance = double.PositiveInfinity;

        foreach (SpokenDurationVoiceCalibration candidate in candidates)
        {
            // Both dimensions are provider settings; pace boost is intentionally not converted to
            // a duration because the selected row's fitted coefficients already encode its rate.
            double distance = Math.Abs(candidate.Speed - speed)
                + Math.Abs(candidate.PaceBoostWpm - paceBoostWpm);
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}

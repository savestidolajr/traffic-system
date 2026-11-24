using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Predicts natural spoken duration from a calibrated voice and the punctuation-aware shape
///     of the read. An absent result is deliberate: an uncalibrated voice must keep the existing
///     duration path rather than silently using a guessed rate.
/// </summary>
public interface ISpokenDurationModel
{
    /// <summary>
    ///     Predicts the natural duration of <paramref name="spokenText"/> for <paramref name="voice"/>.
    ///     Returns <see cref="Option{T}.None"/> when no row for the voice is available.
    /// </summary>
    Option<PredictedDuration> Predict(string spokenText, DurationVoiceKey voice);
}

/// <summary>
///     Natural duration prediction, fitted residual spread, and calibration evidence. The default
///     tier preserves construction compatibility for callers that create this value directly.
/// </summary>
public readonly record struct PredictedDuration(
    double Seconds,
    double ResidualSdSec,
    SpokenDurationMatchTier MatchTier = SpokenDurationMatchTier.Exact);

/// <summary>
///     Identifies one ElevenLabs voice calibration row. Speed and pace boost are part of the key
///     because the same voice/model has separately fitted rows at different synthesis settings.
/// </summary>
public readonly record struct DurationVoiceKey(
    string VoiceId,
    string ModelId,
    double Speed,
    double PaceBoostWpm);

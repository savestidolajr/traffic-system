using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Errors;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Domain model for voice synthesis settings.
///     Stability: voice consistency (0.0–1.0). SimilarityBoost: sample fidelity (0.0–1.0).
///     Style: exaggeration (0.0–1.0). Speed: multiplier (0.7–1.2).
/// </summary>
public sealed record VoiceSettings
{
    [JsonConstructor]
    private VoiceSettings(double stability, double similarityBoost, double style, bool useSpeakerBoost, double speed)
    {
        Stability = stability;
        SimilarityBoost = similarityBoost;
        Style = style;
        UseSpeakerBoost = useSpeakerBoost;
        Speed = speed;
    }

    public double Stability { get; init; }
    public double SimilarityBoost { get; init; }
    public double Style { get; init; }
    public bool UseSpeakerBoost { get; init; }
    public double Speed { get; init; }

    public static VoiceSettings Default { get; } = Create().Value;

    public static Result<VoiceSettings> Create(
        double stability = 0.50,
        double similarityBoost = 0.90,
        double style = 0.0,
        bool useSpeakerBoost = true,
        double speed = 1.0)
    {
        if (stability < 0.0 || stability > 1.0)
            return Result<VoiceSettings>.Failure(ErrorMessages.VoiceSettings.StabilityOutOfRange);
        if (similarityBoost < 0.0 || similarityBoost > 1.0)
            return Result<VoiceSettings>.Failure(ErrorMessages.VoiceSettings.SimilarityBoostOutOfRange);
        if (style < 0.0 || style > 1.0)
            return Result<VoiceSettings>.Failure(ErrorMessages.VoiceSettings.StyleOutOfRange);
        if (speed < 0.7 || speed > 1.2)
            return Result<VoiceSettings>.Failure(ErrorMessages.VoiceSettings.SpeedOutOfRange);

        return new VoiceSettings(stability, similarityBoost, style, useSpeakerBoost, speed);
    }

    /// <summary>
    ///     Generates a unique key for this specific configuration. Used for calibration caching.
    /// </summary>
    public string GetConfigurationKey(string voiceId)
    {
        return $"{voiceId}_{Speed:F2}_{Stability:F2}_{SimilarityBoost:F2}_{Style:F2}_{UseSpeakerBoost}";
    }
    /// <summary>Generates the persisted calibration key including the TTS model identity.</summary>
    public string GetCalibrationConfigurationKey(string voiceId, string modelId) =>
        $"{GetConfigurationKey(voiceId)}_{(string.IsNullOrWhiteSpace(modelId) ? "unknown" : modelId.Trim())}";
}

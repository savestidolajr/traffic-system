namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
/// Identifies one calibration read by the voice, complete settings, and TTS model that produced it.
/// Keeping the model in the key prevents a measurement for one model being reused for another.
/// </summary>
public readonly record struct VoiceCalibrationLookup(
    string VoiceId,
    VoiceSettings Settings,
    string ModelId = "unknown")
{
    public string Key => Settings.GetCalibrationConfigurationKey(VoiceId,
        string.IsNullOrWhiteSpace(ModelId) ? "unknown" : ModelId.Trim());
}

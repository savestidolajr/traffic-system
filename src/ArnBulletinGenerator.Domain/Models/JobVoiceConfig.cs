namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Snapshot of the voice, model, and settings actually used to synthesize a Job's audio, for provenance and reproducibility.</summary>
public sealed record JobVoiceConfig(
    string Provider,
    string VoiceId,
    string VoiceName,
    string Model,
    Option<VoiceSettings> Settings = default,
    double PaceBoostWpm = 0);

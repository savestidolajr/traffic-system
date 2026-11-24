namespace ArnBulletinGenerator.Domain.Contracts;

// =============================================================================
// Audio Stage DTOs
// =============================================================================

/// <summary>
///     Synthesized audio metadata with format and duration information.
/// </summary>
public sealed record AudioDataDto(
    string Format,
    int SampleRateHz,
    double DurationSec,
    string Url);

/// <summary>
///     Voice settings for synthesis quality and pacing.
///     Optimized for Aussie Traffic Presenter v3: Creative stability with moderate audio tags for natural expressiveness.
/// </summary>
public sealed record VoiceSettingsDto(
    double Speed = 1.0,
    double Stability = 0.0,
    double SimilarityBoost = 0.75,
    double Style = 0.15,
    bool UseSpeakerBoost = true);

/// <summary>
///     Voice configuration for audio synthesis.
/// </summary>
public sealed record VoiceConfigDto(
    string Provider,
    string VoiceId,
    string VoiceName,
    string Model,
    VoiceSettingsDto? Settings = null);

/// <summary>
///     DTO for listing available voices.
/// </summary>
public sealed record VoiceDto(
    string VoiceId,
    string Name,
    string Category,
    string Description,
    IReadOnlyDictionary<string, string> Labels)
{
    public IReadOnlyList<string> ModelIds { get; init; } = [];
    public string ResolvedModelId { get; init; } = "";

    /// <summary>
    ///     Whether this voice is eligible for ElevenLabs voice remix. See
    ///     <see cref="ArnBulletinGenerator.Domain.Models.Voice.CanRemix" />.
    /// </summary>
    public bool CanRemix { get; init; } = true;
}

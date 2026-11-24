namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Represents an available TTS voice.
/// </summary>
public sealed record Voice(
    string VoiceId,
    string Name,
    Option<string> Category,
    Option<string> Description,
    IReadOnlyDictionary<string, string> Labels)
{
    public IReadOnlyList<string> ModelIds { get; init; } = [];

    /// <summary>
    ///     Whether ElevenLabs will accept this voice as a remix source. False for Voice
    ///     Library voices copied into the account with a finite notice period — ElevenLabs
    ///     rejects those with "Only your own custom voices can be remixed." True for voices
    ///     owned outright (generated, cloned, or a self-shared professional voice).
    /// </summary>
    public bool CanRemix { get; init; } = true;
}

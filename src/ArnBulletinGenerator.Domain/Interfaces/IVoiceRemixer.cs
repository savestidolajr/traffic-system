using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     A single remix preview: the playable audio of the remixed voice reading the sample
///     script, plus the <c>generated_voice_id</c> needed to mint a permanent voice from it.
/// </summary>
public sealed record VoiceRemixPreview(string GeneratedVoiceId, byte[] Audio, double DurationSeconds);

/// <summary>
///     Text-driven voice remixing: nudge an existing base voice with a natural-language
///     description (e.g. "sound more energetic") and, once a preview is adopted, mint it as a
///     new, reusable voice. Distinct from <see cref="IVoiceProvider" /> (listing) and the TTS
///     synthesis path — remixing creates a new voice rather than retuning one at render time.
/// </summary>
public interface IVoiceRemixer
{
    /// <summary>
    ///     Remixes <paramref name="voiceId" /> with the supplied description and returns one or
    ///     more previews. <paramref name="previewText" /> is the sample script the remixed voice
    ///     reads (the API requires 100–1000 chars). <paramref name="promptStrength" />, when set,
    ///     controls how strongly the description overrides the original (0 subtle … 1 full).
    /// </summary>
    Task<Result<IReadOnlyList<VoiceRemixPreview>>> RemixVoiceAsync(
        string voiceId,
        string voiceDescription,
        string previewText,
        double? promptStrength,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Persists a previously generated remix preview as a permanent voice and returns its
    ///     voice id. Call after the operator adopts a preview from <see cref="RemixVoiceAsync" />.
    /// </summary>
    Task<Result<string>> CreateVoiceFromPreviewAsync(
        string generatedVoiceId,
        string voiceName,
        string voiceDescription,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Permanently deletes a voice minted from a remix preview. Used to roll back an adopted
    ///     remix whose local profile could not be saved, so a failed save cannot leak a billed
    ///     orphan voice. Idempotent: a voice that is already gone (404) is treated as success.
    /// </summary>
    Task<Result> DeleteVoiceAsync(
        string voiceId,
        CancellationToken cancellationToken = default);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Transcribes finished bulletin audio back to text for the pre-approval audio review
///     (the SoW "AI-based pre-review of generated audio"). Implementations MUST be fast
///     enough to sit inline before <c>WaitingApproval</c> (seconds, not minutes) and MUST
///     fail soft — a transcription outage degrades the review to the deterministic checks,
///     never the bulletin.
/// </summary>
public interface IAudioTranscriber
{
    /// <summary>False when no transcription backend is configured — callers skip the review.</summary>
    bool Enabled { get; }

    /// <summary>Transcribes WAV audio to plain text (Australian English).</summary>
    Task<Result<string>> TranscribeAsync(byte[] wavAudio, CancellationToken cancellationToken = default);
}

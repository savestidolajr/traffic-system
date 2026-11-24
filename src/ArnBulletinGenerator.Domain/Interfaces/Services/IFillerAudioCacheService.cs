using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Resolves filler audio for a bulletin: cache lookup, synthesize-and-store on a miss, and
///     cache invalidation when a filler statement's text changes or is removed. The only
///     component with both Table Storage and blob access for filler audio, orchestrating both as
///     one unit.
/// </summary>
public interface IFillerAudioCacheService
{
    Task<Result<ResolvedFillerAudio>> ResolveAsync(FillerStatement statement, string voiceId, VoiceSettings settings, string model, CancellationToken cancellationToken = default);

    /// <summary>Resolves the filler-cart clip for a voice: a cache hit returns the stored clip
    /// (blob URL + measured duration + audio); a miss synthesizes the cart script once with that
    /// voice, uploads it, persists the cache metadata, and returns the clip. The caller applies
    /// the URL to the job like the evergreen cart URL, and uses the measured duration to judge
    /// whether the clip covers the slot — a fallback that airs a 16s clip in a 30s slot leaves
    /// ~14s of dead air (bug-bash 2026-08-05 finding 32).</summary>
    Task<Result<ResolvedCartClip>> ResolveCartAsync(string scriptText, string voiceId, VoiceSettings settings, string model, CancellationToken cancellationToken = default);

    Task<Result> InvalidateAsync(string fillerStatementId, CancellationToken cancellationToken = default);
}

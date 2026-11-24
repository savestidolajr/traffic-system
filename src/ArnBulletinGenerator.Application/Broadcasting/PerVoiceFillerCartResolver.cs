using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Resolves the per-voice filler cart clip for a job's own voice. The cart script is
///     synthesized with that voice and cached per (voice × settings × model × script), exactly
///     like filler statements; callers fall back to the evergreen cart WAV when this returns
///     None (feature off, or the job's voice config cannot be resolved). The clip carries its
///     measured duration so a caller can judge whether it covers the job's slot — a fallback
///     that airs a 16s clip in a 30s slot leaves ~14s of dead air (bug-bash 2026-08-05 finding
///     32).
/// </summary>
public sealed class PerVoiceFillerCartResolver(
    IFillerAudioCacheService fillerAudioCache,
    IOptions<BroadcastOptions> broadcastOptions,
    ILogger<PerVoiceFillerCartResolver> logger)
{
    private readonly BroadcastOptions _options = broadcastOptions.Value;

    /// <summary>Returns the per-voice cart clip for the job's own voice, or None when the
    /// feature is off or the voice cannot be resolved — callers then use the evergreen cart.</summary>
    public async Task<Option<ResolvedCartClip>> ResolveAsync(Job job, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FillerCartScript))
            return Option<ResolvedCartClip>.None;
        if (job.VoiceConfig.IsNone)
            return Option<ResolvedCartClip>.None;
        var vc = job.VoiceConfig.Value;
        if (string.IsNullOrWhiteSpace(vc.VoiceId) || string.IsNullOrWhiteSpace(vc.Model) || vc.Settings.IsNone)
            return Option<ResolvedCartClip>.None;
        var result = await fillerAudioCache.ResolveCartAsync(
            _options.FillerCartScript, vc.VoiceId, vc.Settings.Value, vc.Model, cancellationToken);
        if (result.IsSuccess)
            return Option<ResolvedCartClip>.Some(result.Value);
        logger.LogWarning(
            "Per-voice filler cart could not be resolved for job {JobId} (voice {VoiceId}): {Error}",
            job.JobId, vc.VoiceId, result.Error);
        return Option<ResolvedCartClip>.None;
    }
}

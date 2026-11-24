using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Builds the cache key identifying one (voice × voice settings × TTS model) synthesis
///     configuration for filler audio. Reuses <see cref="VoiceSettings.GetConfigurationKey" /> —
///     the same convention <c>VoiceCalibration</c>/<c>VoiceProfileVersion</c> already use — and
///     appends the model, since that method does not include it.
/// </summary>
public static partial class FillerAudioCacheKey
{
    // "_t2" = audio-processing version: leading-silence trim added 2026-07-20. Bumping the
    // suffix makes every pre-existing cache entry (stored with untrimmed leads) miss naturally,
    // so clips re-synthesize with the new processing on first use; orphaned blobs are harmless.
    public static string Build(string voiceId, VoiceSettings settings, string model) =>
        $"{settings.GetConfigurationKey(voiceId)}_{model}_t2";
}

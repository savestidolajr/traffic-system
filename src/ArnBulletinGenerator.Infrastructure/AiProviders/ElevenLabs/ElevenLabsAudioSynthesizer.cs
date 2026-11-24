using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;

/// <summary>
///     Audio synthesizer using ElevenLabs API.
///     Delegates voice settings resolution and audio generation to the underlying client.
/// </summary>
public sealed class ElevenLabsAudioSynthesizer(
    ElevenLabsClient client,
    WavDurationProcessor wavProcessor,
    IOptions<ElevenLabsOptions> options,
    ILogger<ElevenLabsAudioSynthesizer> logger) : IAudioSynthesizer
{
    private VoiceSettings Defaults
    {
        get
        {
            var v = options.Value.VoiceSettings;
            return VoiceSettings.Create(
                v.Stability,
                v.SimilarityBoost,
                v.Style,
                v.UseSpeakerBoost,
                v.Speed).Value;
        }
    }

    public async Task<Result<SynthesizedAudio>> SynthesizeAsync(
        string text, Option<VoiceSettings> settings = default,
        Option<string> voiceIdOverride = default, Option<string> modelOverride = default,
        CancellationToken cancellationToken = default,
        Option<string> market = default,
        PronunciationStack? pronunciationStack = null)
    {
        if (voiceIdOverride.IsNone)
            return Result<SynthesizedAudio>.Failure("VoiceId is required. Each schedule or job must specify a voice.");

        var s = settings.GetValueOr(Defaults);
        string effectiveModel = modelOverride.GetValueOr(options.Value.ModelId);
        // LAST LINE OF DEFENCE: only eleven_v3 interprets inline audio tags; every other model SPEAKS
        // them, so "[excitedly] The M1 is closed" would air as "excitedly, the M1 is closed". Upstream
        // gates exist per path (director, delivery writer, operator toned read), but this is the single
        // point every path converges on AND the only place the model actually sent is known — the
        // pipeline's context.Model can be absent, in which case the configured default applies. Gating
        // here means a tag can never reach a voice that would read it aloud, including from a future
        // caller that forgets to check. Costs nothing when the text has no tag.
        string tagSafeText = DeliveryTags.ForModel(text, effectiveModel);
        if (!ReferenceEquals(tagSafeText, text))
            logger.LogWarning(
                "[Synthesis] Stripped audio tag(s) before sending to non-v3 model '{Model}' — they would " +
                "have been read aloud. Voice: {Voice}.",
                effectiveModel, voiceIdOverride.GetValueOr("default"));

        // TTS-only speakability pass — see RoadNameFormatter.ExpandRouteCodesForSpeech. Never
        // applied to the stored/displayed script, only to what's actually sent for synthesis.
        string speechText = RoadNameFormatter.ExpandRouteCodesForSpeech(tagSafeText);

        var result = await client.GenerateAudioAsync(
            speechText, s.Speed, s.Stability, s.SimilarityBoost, s.Style, s.UseSpeakerBoost,
            voiceIdOverride.Value, effectiveModel, market.ToNullable(), cancellationToken,
            pronunciationStack);

        if (result.IsFailure)
        {
            logger.LogError("ElevenLabs synthesis failed. Voice: {Voice}, Model: {Model}, Len: {Len}, Error: {Error}",
                voiceIdOverride.GetValueOr("default"),
                effectiveModel,
                text.Length, result.Error);
            // Preserve the typed disposition (InvalidInput for a 4xx provider rejection) so the
            // HTTP edge can map a bad voice/settings to 400 instead of a misleading 500.
            return result.FailureMetadata.HasValue
                ? Result<SynthesizedAudio>.Failure(result.FailureMetadata.Value)
                : Result<SynthesizedAudio>.Failure(
                    result.Error ?? ErrorMessages.AudioSynthesis.UnknownDraftError);
        }

        // Cut anything the model appended after the last spoken sound, HERE — on the raw render,
        // before the duration fitter ever sees it. It cannot be done later: the fine-tune rescales
        // every word timing to the finished file length, so by then the alignment claims the last
        // word ends exactly at the end of the audio and the boundary is gone. See
        // SpeechTailTrimPolicy for the measurements behind the rule.
        var alignment = Option.OfNullable(result.Value!.Alignment);
        byte[] audio = result.Value.AudioData;
        var lastSpoken = SpeechTailTrimPolicy.LastSpokenCharacterEnd(alignment);
        if (lastSpoken.HasValue)
            audio = wavProcessor.TrimAfterSpeech(audio, lastSpoken.Value);

        return new SynthesizedAudio(audio, alignment, effectiveModel);
    }
}

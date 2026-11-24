using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Renders script text to speech via a TTS provider, honouring voice, model, and pronunciation-dictionary overrides for a given market.</summary>
public interface IAudioSynthesizer
{
    Task<Result<SynthesizedAudio>> SynthesizeAsync(
        string text,
        Option<VoiceSettings> settings = default,
        Option<string> voiceIdOverride = default,
        Option<string> modelOverride = default,
        CancellationToken cancellationToken = default,
        Option<string> market = default,
        PronunciationStack? pronunciationStack = null);
}

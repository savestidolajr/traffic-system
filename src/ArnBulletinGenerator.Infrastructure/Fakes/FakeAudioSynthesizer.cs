using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Audio;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for text-to-speech: returns a 220Hz sine-tone WAV whose
///     LENGTH is proportional to the input text at <see cref="WordsPerMinute" />, and logs each call
///     to <see cref="FakeExternalCallLog" />.
///     <para>
///         It used to return a fixed 30 seconds for every input, which is not a plausible speaker and
///         broke any caller that reasons about rate rather than just "some audio came back": the
///         37-word calibration reference phrase came out at 74 wpm and was correctly rejected by
///         <c>VoiceCalibrationService</c>'s plausibility gate, failing the /voices/{id}/calibrate E2E
///         round-trip. Scaling with the text keeps the fake honest — the reference phrase now lands at
///         ~14.8s, within a second of the 14.0s a real voice measured — while an ~80-word bulletin
///         still lands near a 30s slot, and a 60s slot finally gets a proportionally longer read
///         instead of a permanently content-limited one.
///     </para>
/// </summary>
public sealed class FakeAudioSynthesizer(FakeExternalCallLog log) : IAudioSynthesizer
{
    private const int SampleRate = 44_100;
    private const int Channels = 1;
    private const int BitsPerSample = 16;

    // Mid-range of the rates real ARN voices calibrate at (measured 128-181 wpm across the profiles).
    private const double WordsPerMinute = 150.0;
    private const double MinDurationSeconds = 1.0;

    public Task<Result<SynthesizedAudio>> SynthesizeAsync(
        string text,
        Option<VoiceSettings> settings = default,
        Option<string> voiceIdOverride = default,
        Option<string> modelOverride = default,
        CancellationToken cancellationToken = default,
        Option<string> market = default,
        PronunciationStack? pronunciationStack = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The attached layers are logged so an E2E test can assert the station dictionary was
        // emitted BEFORE the platform-wide one — the ordering the whole feature depends on.
        var dictionaries = pronunciationStack is null or { IsEmpty: true }
            ? "none"
            : string.Join(",", pronunciationStack.Locators.Select(l => l.DictionaryId));
        log.Add(
            $"synthesize voice={voiceIdOverride.GetValueOr("default")} chars={text.Length} dictionaries={dictionaries}");

        // Strip delivery tags first: they direct the read, they are not spoken words.
        int wordCount = WordCounter.Count(DeliveryTags.Strip(text));
        double durationSeconds = Math.Max(MinDurationSeconds, wordCount / WordsPerMinute * 60.0);
        var sampleCount = (int)(SampleRate * durationSeconds);
        var pcm = new byte[sampleCount * Channels * (BitsPerSample / 8)];
        const double frequency = 220.0;
        const short amplitude = 900;
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * i / SampleRate) * amplitude);
            var offset = i * 2;
            pcm[offset] = (byte)(sample & 0xff);
            pcm[offset + 1] = (byte)((sample >> 8) & 0xff);
        }

        var wav = WavHeaderBuilder.WrapInWavHeader(pcm, SampleRate, Channels, BitsPerSample);
        return Task.FromResult(Result<SynthesizedAudio>.Success(new SynthesizedAudio(wav)));
    }
}

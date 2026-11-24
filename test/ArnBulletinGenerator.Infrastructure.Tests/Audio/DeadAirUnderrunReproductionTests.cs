using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

/// <summary>
///     Guards the underrun fill contract (jobs fd4c7ff… / e866746b… dead-air defects). ARN requires
///     an exactly-slot-length file with ≤0.3s trailing silence, so a short take is filled by slowing
///     the read (pitch-preserving) toward the slot within <see cref="SpeechDurationOptions.MaxSlowDownFactor" />,
///     then padding any residual. A take that cannot reach the slot within the cap is left with a
///     large residual gap — the orchestrator rejects it (SlotFilled=false) and falls back rather
///     than ship a short or dead-air bulletin.
/// </summary>
[Trait("Category", "Slow")]
public class DeadAirUnderrunReproductionTests
{
    private static byte[] ToneWav(double seconds, short amplitude = 6000)
    {
        int n = (int)Math.Round(44100 * seconds);
        short[] s = new short[n];
        for (int i = 0; i < n; i++)
            s[i] = (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * 200 * i / 44100.0));
        byte[] pcm = new byte[n * 2];
        Buffer.BlockCopy(s, 0, pcm, 0, pcm.Length);
        return WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);
    }

    private static AudioPipelineService CreateSut(WavDurationProcessor processor) =>
        new(
            Substitute.For<IAudioSynthesizer>(),
            processor,
            Substitute.For<ISpeechDurationEstimator>(),
            Substitute.For<ISynthesisHistoryRepository>(),
            [],
            Options.Create(new BroadcastOptions()),
            Options.Create(new SpeechDurationOptions()),
            new WavLoudnessNormalizer(Substitute.For<ILogger<WavLoudnessNormalizer>>()),
            Options.Create(new AudioLoudnessOptions()),
            Substitute.For<ILogger<AudioPipelineService>>());

    [Fact]
    public void Underrun_within_cap_is_slow_filled_to_exact_slot()
    {
        // A read close enough to fill within the slow-down cap (28s for a 30s slot ≈ 7%) is
        // stretched to the slot, leaving negligible trailing silence — the slot is filled.
        const double target = 30.0;
        var processor = new WavDurationProcessor(Substitute.For<ILogger<WavDurationProcessor>>());
        var sut = CreateSut(processor);

        byte[] finalAudio = sut.FineTuneToTargetDuration(ToneWav(28.0), target);

        double finalLength = processor.GetDuration(finalAudio);
        double spokenSpan = processor.GetSpokenDuration(finalAudio);

        finalLength.ShouldBe(target, 0.05);
        // Filled by slowing the read: trailing silence is within the 0.3s pad budget.
        (finalLength - spokenSpan).ShouldBeLessThanOrEqualTo(0.3);
    }

    [Fact]
    public void Underrun_beyond_cap_is_slowed_to_cap_then_left_short_for_fallback()
    {
        // Mirrors the real content-limited job: ~21s of speech for a 30s slot. The read can only be
        // slowed to the 10% cap (~23.1s); the rest is padded. The large residual (> the 0.3s budget)
        // is what the orchestrator detects as SlotFilled=false and fails to the fallback.
        const double target = 30.0;
        const double rawSpokenSeconds = 21.0;
        var processor = new WavDurationProcessor(Substitute.For<ILogger<WavDurationProcessor>>());
        var sut = CreateSut(processor);

        byte[] finalAudio = sut.FineTuneToTargetDuration(ToneWav(rawSpokenSeconds), target);

        double finalLength = processor.GetDuration(finalAudio);
        double spokenSpan = processor.GetSpokenDuration(finalAudio);

        finalLength.ShouldBe(target, 0.05);
        // Slowed only to the cap (21 × 1.10 ≈ 23.1s), not all the way to the slot.
        spokenSpan.ShouldBe(rawSpokenSeconds * 1.10, 0.4);
        (finalLength - spokenSpan).ShouldBeGreaterThan(0.3);
    }
}

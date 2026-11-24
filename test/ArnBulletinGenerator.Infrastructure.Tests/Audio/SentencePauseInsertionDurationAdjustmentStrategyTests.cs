using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Audio.DurationStrategies;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class SentencePauseInsertionDurationAdjustmentStrategyTests
{
    private readonly SentencePauseInsertionDurationAdjustmentStrategy _strategy = new(
        Substitute.For<ILogger<SentencePauseInsertionDurationAdjustmentStrategy>>());

    private static AudioPostSynthesisAdjustmentContext MakeContext(
        string script, byte[] audio, double targetSeconds, double actualSeconds)
    {
        return new AudioPostSynthesisAdjustmentContext(script, audio, targetSeconds, actualSeconds, 1, 3);
    }

    private static byte[] CreateWavSeconds(double seconds, int sampleRate = 44100)
    {
        int numSamples = (int)(seconds * sampleRate);
        return WavTestHelper.CreateMinimalWav(sampleRate: sampleRate, numSamples: numSamples);
    }

    [Fact]
    public void Name_ReturnsSentencePauseInsertion()
    {
        _strategy.Name.ShouldBe("SentencePauseInsertion");
    }

    [Fact]
    public async Task TryAdjustAsync_WithinResidualRange_InsertsPauses()
    {
        // Multi-sentence script with 0.5s underrun (within 0.08–1.20 range)
        string script =
            "Traffic is heavy on the highway. Delays are expected near the bridge. Please plan accordingly.";
        byte[] audio = CreateWavSeconds(2.5);
        var ctx = MakeContext(script, audio, 3.0, 2.5);

        var result = await _strategy.TryAdjustAsync(ctx);

        result.ShouldNotBeNull();
        result!.Audio.Length.ShouldBeGreaterThan(audio.Length);
        result.Reason.ShouldContain("pause");
    }

    [Fact]
    public async Task TryAdjustAsync_ResidualTooSmall_ReturnsNull()
    {
        // Residual < 0.08s
        string script = "First sentence here. Second sentence here.";
        byte[] audio = CreateWavSeconds(2.0);
        var ctx = MakeContext(script, audio, 2.05, 2.0);

        var result = await _strategy.TryAdjustAsync(ctx);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task TryAdjustAsync_ResidualTooLarge_ReturnsNull()
    {
        // Residual > 1.20s
        string script = "First sentence here. Second sentence here.";
        byte[] audio = CreateWavSeconds(1.0);
        var ctx = MakeContext(script, audio, 3.0, 1.0);

        var result = await _strategy.TryAdjustAsync(ctx);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task TryAdjustAsync_SingleSentence_ReturnsNull()
    {
        // Only one sentence — no boundaries to insert pauses at
        string script = "This is just one single sentence with no period boundaries";
        byte[] audio = CreateWavSeconds(2.0);
        var ctx = MakeContext(script, audio, 2.5, 2.0);

        var result = await _strategy.TryAdjustAsync(ctx);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task TryAdjustAsync_EmptyScript_ReturnsNull()
    {
        byte[] audio = CreateWavSeconds(1.0);
        var ctx = MakeContext("", audio, 1.5, 1.0);

        var result = await _strategy.TryAdjustAsync(ctx);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task TryAdjustAsync_InvalidAudio_ReturnsNull()
    {
        string script = "First sentence. Second sentence.";
        var ctx = MakeContext(script, [1, 2, 3], 2.0, 1.5);

        var result = await _strategy.TryAdjustAsync(ctx);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task TryAdjustAsync_AudioAlreadyLongerThanTarget_ReturnsNull()
    {
        // Negative residual — audio is already longer than target
        string script = "First sentence. Second sentence.";
        byte[] audio = CreateWavSeconds(3.0);
        var ctx = MakeContext(script, audio, 2.5, 3.0);

        var result = await _strategy.TryAdjustAsync(ctx);

        result.ShouldBeNull();
    }
}

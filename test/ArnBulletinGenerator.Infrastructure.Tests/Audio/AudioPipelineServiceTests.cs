using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using static ArnBulletinGenerator.Domain.Services.SyllableCounter;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

[Trait("Category", "Slow")]
public class AudioPipelineServiceTests
{
    private readonly ISpeechDurationEstimator _estimator = Substitute.For<ISpeechDurationEstimator>();
    private readonly ILogger<AudioPipelineService> _logger = Substitute.For<ILogger<AudioPipelineService>>();
    private readonly IAudioProcessor _processor = Substitute.For<IAudioProcessor>();
    private readonly IAudioSynthesizer _synthesizer = Substitute.For<IAudioSynthesizer>();
    private readonly ISynthesisHistoryRepository _historyRepo = Substitute.For<ISynthesisHistoryRepository>();

    private AudioPipelineService CreateSut(
        BroadcastOptions? broadcastOptions = null,
        SpeechDurationOptions? durationOptions = null,
        IEnumerable<IAudioPostSynthesisDurationAdjustmentStrategy>? strategies = null,
        AudioLoudnessOptions? loudnessOptions = null)
    {
        return new AudioPipelineService(
            _synthesizer,
            _processor,
            _estimator,
            _historyRepo,
            strategies ?? [],
            Options.Create(broadcastOptions ?? new BroadcastOptions()),
            Options.Create(durationOptions ?? new SpeechDurationOptions()),
            new WavLoudnessNormalizer(Substitute.For<ILogger<WavLoudnessNormalizer>>()),
            Options.Create(loudnessOptions ?? new AudioLoudnessOptions()),
            _logger);
    }

    private (AudioPipelineService Sut, WavDurationProcessor Processor) CreateSutWithRealProcessor(
        SpeechDurationOptions? durationOptions = null,
        AudioLoudnessOptions? loudnessOptions = null,
        IEnumerable<IAudioPostSynthesisDurationAdjustmentStrategy>? strategies = null)
    {
        var processor = new WavDurationProcessor(Substitute.For<ILogger<WavDurationProcessor>>());
        var sut = new AudioPipelineService(
            _synthesizer,
            processor,
            _estimator,
            _historyRepo,
            strategies ?? [],
            Options.Create(new BroadcastOptions()),
            Options.Create(durationOptions ?? new SpeechDurationOptions()),
            new WavLoudnessNormalizer(Substitute.For<ILogger<WavLoudnessNormalizer>>()),
            Options.Create(loudnessOptions ?? new AudioLoudnessOptions()),
            _logger);
        return (sut, processor);
    }

    // ── Loudness normalization wiring ───────────────────────────────────

    private static byte[] QuietToneWav(double peakAmplitude = 2000)
    {
        short[] s = new short[44100];
        for (int i = 0; i < s.Length; i++)
            s[i] = (short)Math.Round(peakAmplitude * Math.Sin(2 * Math.PI * 200 * i / 44100.0));
        byte[] pcm = new byte[s.Length * 2];
        Buffer.BlockCopy(s, 0, pcm, 0, pcm.Length);
        return WavHeaderBuilder.WrapInWavHeader(pcm, 44100, 1, 16);
    }

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

    private static double RmsDbFs(byte[] wav)
    {
        var info = WavInfoParser.GetWavInfo(wav)!;
        int start = (int)info.DataPosition;
        int n = info.DataSize / 2;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            int v = BitConverter.ToInt16(wav, start + (i * 2));
            sum += (double)v * v;
        }
        return 20 * Math.Log10(Math.Sqrt(sum / n) / 32768.0);
    }

    private void StubSynth(byte[] audio)
    {
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audio)));
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_AttemptOverride_CapsProviderCalls()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("transient provider failure"));
        var sut = CreateSut(
            broadcastOptions: new BroadcastOptions { MaxSynthesisAttempts = 3 },
            loudnessOptions: new AudioLoudnessOptions { Enabled = false });

        var result = await sut.SynthesizeWithDurationTargetAsync(
            "retry once only", 30.0, maxSynthesisAttemptsOverride: 1);

        result.IsFailure.ShouldBeTrue();
        await _synthesizer.Received(1).SynthesizeAsync(
            Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_NormalizesLoudness_OnSuccess()
    {
        byte[] quiet = QuietToneWav();
        double inputRms = RmsDbFs(quiet);
        StubSynth(quiet);
        var sut = CreateSut();

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world test script", 30.0);

        result.IsSuccess.ShouldBeTrue();
        double outputRms = RmsDbFs(result.Value.Audio);
        outputRms.ShouldBeGreaterThan(inputRms + 3.0);
        outputRms.ShouldBe(-14.5, 1.0);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_LoudnessDisabled_LeavesAudioRaw()
    {
        byte[] quiet = QuietToneWav();
        double inputRms = RmsDbFs(quiet);
        StubSynth(quiet);
        var sut = CreateSut(loudnessOptions: new AudioLoudnessOptions { Enabled = false });

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world test script", 30.0);

        result.IsSuccess.ShouldBeTrue();
        RmsDbFs(result.Value.Audio).ShouldBe(inputRms, 0.1);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_PersistsEffectiveProviderModel()
    {
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(
                new SynthesizedAudio([0x01], ModelId: "eleven_v3")));
        var sut = CreateSut(loudnessOptions: new AudioLoudnessOptions { Enabled = false });

        var result = await sut.SynthesizeWithDurationTargetAsync(
            "Hello world test script",
            30.0,
            voiceIdOverride: Option<string>.Some("dolly"),
            modelOverride: Option<string>.None);

        result.IsSuccess.ShouldBeTrue();
        _historyRepo.Received(1).Record(Arg.Is<SynthesisRecord>(r =>
            r.VoiceId == "dolly" && r.ModelId == "eleven_v3"));
    }

    // ── Predictor wiring tests ──────────────────────────────────────────

    [Fact]
    public void EstimateDuration_WithPredictor_DelegatesToSpeechDurationEstimator()
    {
        // The orchestrator's EstimateDuration call should still use the estimator
        _estimator.EstimateDuration("hello world").Returns(3.5);
        var sut = CreateSut();

        sut.EstimateDuration("hello world").ShouldBe(3.5);
    }

    // ── Delegation tests ────────────────────────────────────────────────

    [Fact]
    public void EstimateDuration_DelegatesToSpeechDurationEstimator()
    {
        _estimator.EstimateDuration("hello world").Returns(3.5);
        var sut = CreateSut();

        sut.EstimateDuration("hello world").ShouldBe(3.5);
    }

    [Fact]
    public void GetDuration_DelegatesToAudioProcessor()
    {
        byte[] audio = new byte[] { 1, 2, 3 };
        _processor.GetDuration(audio).Returns(12.5);
        var sut = CreateSut();

        sut.GetDuration(audio).ShouldBe(12.5);
    }

    // ── FineTuneToTargetDuration tests ──────────────────────────────────

    [Fact]
    public void FineTuneToTargetDuration_WithinThreshold_ReturnsAudioUnchanged()
    {
        byte[] audio = new byte[] { 1, 2, 3, 4 };
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(30.005);
        var sut = CreateSut();

        byte[] result = sut.FineTuneToTargetDuration(audio, 30.0);

        result.ShouldBeSameAs(audio);
        _processor.DidNotReceive().AdjustSpeed(Arg.Any<byte[]>(), Arg.Any<double>());
    }

    [Fact]
    public void FineTuneToTargetDuration_Underrun_SlowFillsToTarget()
    {
        // ARN requires an exactly-slot-length file: a short read is slow-filled toward the slot
        // (pitch-preserving) within the cap. 28s for a 30s slot is within 10%, so it stretches to 30s.
        byte[] audio = new byte[] { 1, 2, 3 };
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(28.0); // 2s underrun
        var sut = CreateSut();

        _ = sut.FineTuneToTargetDuration(audio, 30.0);

        _processor.Received(1).AdjustSpeed(Arg.Any<byte[]>(), 30.0);
    }

    [Fact]
    public void FineTuneToTargetDuration_UnderrunBeyondExactFitCap_PadsTrailingSilenceToTarget()
    {
        // Underruns are slow-filled toward the slot within the cap, then any residual is padded so
        // the file is exactly target. 1s → 3s exceeds the 10% cap, so it pads after slowing to the cap.
        var (sut, processor) = CreateSutWithRealProcessor();
        byte[] oneSecondTone = QuietToneWav(); // 1s of audible tone

        byte[] result = sut.FineTuneToTargetDuration(oneSecondTone, 3.0); // 2s underrun

        processor.GetDuration(result).ShouldBe(3.0, 0.01);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_Underrun_PadsFileToExactTarget()
    {
        // A synthesis that lands short of the slot is slow-filled within the cap then padded to
        // exactly target and SUCCEEDS at the pipeline level — the orchestrator makes the
        // SlotFilled-based accept/fallback decision, not the pipeline.
        byte[] oneSecondTone = QuietToneWav(); // 1s
        StubSynth(oneSecondTone);
        var (sut, processor) = CreateSutWithRealProcessor();

        var result = await sut.SynthesizeWithDurationTargetAsync("short bulletin script", 5.0);

        result.IsSuccess.ShouldBeTrue();
        processor.GetDuration(result.Value.Audio).ShouldBe(5.0, 0.05);
    }

    // ── The delivered read must never lose its last word to the slot fit ─────
    //
    // Prod job 8d37ced0.../c2894ffb... (2026-08-18, cairns-4ca, 22s slot) aired a bulletin whose
    // final word "Manoora" was guillotined: the file measured exactly 22.000000s and ended on a
    // rising non-zero sample with no fade. Every success path funnels through
    // LogWaterfallAndSucceed's "final sample-level guarantee"
    // (EnsureExactDurationIfPossible), which used to discard whatever sat in the last ≤0.5s.
    // Two distinct routes reach it holding an overrun; both are covered below.

    [Fact]
    public async Task SynthesizeWithDurationTarget_ReadOverrunsByLessThanTolerance_KeepsTheFinalWordInTheDeliveredAudio()
    {
        byte[] voicedTail = WavTestHelper.CreateWavWithVoicedTail(2.12, 0.12);
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(2.0);
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(voicedTail)));

        var (sut, processor) = CreateSutWithRealProcessor(
            // Route 1: the fine-tuner is told the read is already close enough to leave alone
            // (threshold widened to 0.15s), so the +0.12s overrun reaches the final guarantee raw.
            durationOptions: new SpeechDurationOptions { FineTuningThresholdSeconds = 0.15 },
            loudnessOptions: new AudioLoudnessOptions { Enabled = false });

        var result = await sut.SynthesizeWithDurationTargetAsync("The final word is Manoora.", 2.0);

        result.IsSuccess.ShouldBeTrue();
        processor.GetDuration(result.Value.Audio).ShouldBe(2.0, 0.001);
        WavTestHelper.PeakAbsSampleInFinalSeconds(result.Value.Audio, 0.15).ShouldBeGreaterThan(48);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_StrategyProposalSlightlyOverTarget_KeepsTheFinalWordInDeliveredAudio()
    {
        // Route 2, and the one live in production with DEFAULT options: a post-synthesis strategy
        // proposal is adopted at AudioPipelineService.cs:215-237 WITHOUT a re-run of the fine-tuner,
        // so a proposal within EffectiveToleranceSeconds (0.15s) of the slot succeeds straight into
        // the final guarantee still carrying its overrun.
        byte[] initialAudio = WavTestHelper.CreateWavWithVoicedTail(2.5, 0.5);
        byte[] strategyAudio = WavTestHelper.CreateWavWithVoicedTail(2.12, 0.12);
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(2.0);
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(initialAudio)));

        var strategy = Substitute.For<IAudioPostSynthesisDurationAdjustmentStrategy>();
        strategy.Name.Returns("VoicedTailStrategy");
        strategy.TryAdjustAsync(Arg.Any<AudioPostSynthesisAdjustmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new AudioPostSynthesisAdjustmentProposal(strategyAudio, "Keep voiced tail"));

        var (sut, processor) = CreateSutWithRealProcessor(
            loudnessOptions: new AudioLoudnessOptions { Enabled = false },
            strategies: [strategy]);

        var result = await sut.SynthesizeWithDurationTargetAsync("The final word is Manoora.", 2.0);

        result.IsSuccess.ShouldBeTrue();
        await strategy.Received(1)
            .TryAdjustAsync(Arg.Any<AudioPostSynthesisAdjustmentContext>(), Arg.Any<CancellationToken>());
        processor.GetDuration(result.Value.Audio).ShouldBe(2.0, 0.001);
        WavTestHelper.PeakAbsSampleInFinalSeconds(result.Value.Audio, 0.15).ShouldBeGreaterThan(48);
    }

    [Fact]
    public void FineTuneToTargetDuration_OverrunBeyondSpeedUpFactor_SkipsAdjustment()
    {
        // 23.5 vs 20.0 = +3.5s (within the 4.5s seconds guard) but factor 1.175 > 1.15 cap,
        // so it is NOT sped up — the orchestrator regenerates a shorter script instead.
        byte[] audio = new byte[] { 1, 2, 3 };
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(23.5);
        var sut = CreateSut();

        byte[] result = sut.FineTuneToTargetDuration(audio, 20.0);

        result.ShouldBeSameAs(audio);
        _processor.DidNotReceive().AdjustSpeed(Arg.Any<byte[]>(), Arg.Any<double>());
    }

    [Fact]
    public void FineTuneToTargetDuration_SmallOverrun_AdjustsSpeed()
    {
        byte[] audio = new byte[] { 1, 2, 3 };
        byte[] adjusted = new byte[] { 4, 5, 6 };
        // 32.0 vs 30.0 = +2.0 delta, within MaxSafeOverrunTrimSeconds (3.0)
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(32.0);
        _processor.AdjustSpeed(Arg.Any<byte[]>(), 30.0).Returns(adjusted);
        var sut = CreateSut();

        _ = sut.FineTuneToTargetDuration(audio, 30.0);

        _processor.Received(1).AdjustSpeed(Arg.Any<byte[]>(), 30.0);
    }

    [Fact]
    public void FineTuneToTargetDuration_LargeOverrun_SkipsAdjustment()
    {
        byte[] audio = new byte[] { 1, 2, 3 };
        // 35.0 vs 30.0 = +5.0 delta, exceeds MaxSafeOverrunTrimSeconds (3.0)
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(35.0);
        var sut = CreateSut();

        byte[] result = sut.FineTuneToTargetDuration(audio, 30.0);

        result.ShouldBeSameAs(audio);
        _processor.DidNotReceive().AdjustSpeed(Arg.Any<byte[]>(), Arg.Any<double>());
    }

    // ── SynthesizeWithDurationTargetAsync tests ─────────────────────────

    [Fact]
    public async Task SynthesizeWithDurationTarget_ExactHit_ReturnsSuccess()
    {
        byte[] audioBytes = new byte[] { 1, 2, 3, 4, 5 };
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audioBytes)));
        var sut = CreateSut();

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world test script", 30.0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBe(30.0);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_SynthesisFailure_ReturnsError()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("TTS provider unavailable"));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 1 });

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world", 30.0);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("TTS provider unavailable");
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_PermanentError_DoesNotRetry()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("BadRequest: invalid voice ID"));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 3 });

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world", 30.0);

        result.IsFailure.ShouldBeTrue();
        await _synthesizer.Received(1).SynthesizeAsync(
            Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_WithinCandidateTolerance_FineTunesAndSucceeds()
    {
        byte[] rawAudio = new byte[] { 1, 2, 3 };
        byte[] adjustedAudio = new byte[] { 4, 5, 6 };

        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(rawAudio)));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _processor.AdjustSpeed(Arg.Any<byte[]>(), Arg.Any<double>()).Returns(adjustedAudio);
        // Raw audio → 31.0, adjusted audio → 30.0
        _processor.GetDuration(Arg.Any<byte[]>())
            .Returns(call => ReferenceEquals(call.Arg<byte[]>(), adjustedAudio) ? 30.0 : 31.0);

        var sut = CreateSut(new BroadcastOptions());

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world test script", 30.0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBe(30.0);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_AllAttemptsExhausted_ReturnsFailure()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("TTS provider unavailable"));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 2 });

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world", 30.0);

        result.IsFailure.ShouldBeTrue();
        await _synthesizer.Received(2).SynthesizeAsync(
            Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_CancellationToken_IsPassedToSynthesizer()
    {
        using var cts = new CancellationTokenSource();
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("cancelled"));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 1 });

        await sut.SynthesizeWithDurationTargetAsync("Hello", 30.0, cancellationToken: cts.Token);

        await _synthesizer.Received(1).SynthesizeAsync(
            Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), cts.Token);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_VoiceSettingsOverride_AppliedToSynthesis()
    {
        byte[] audioBytes = new byte[] { 1, 2, 3 };
        var customSettings = VoiceSettings.Create(0.7, 0.9, speed: 1.0).Value;
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audioBytes)));
        var sut = CreateSut();

        var result = await sut.SynthesizeWithDurationTargetAsync(
            "Hello world test script", 30.0,
            settings: Option<VoiceSettings>.Some(customSettings));

        result.IsSuccess.ShouldBeTrue();
        await _synthesizer.Received().SynthesizeAsync(
            Arg.Any<string>(),
            Arg.Is<Option<VoiceSettings>>(o =>
                o.HasValue && o.Value.Stability == 0.7 && o.Value.SimilarityBoost == 0.9),
            Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>());
    }

    // ── Post-synthesis strategy interaction ─────────────────────────────

    [Fact]
    public async Task SynthesizeWithDurationTarget_OutsideEffectiveTolerance_InvokesPostSynthesisStrategy()
    {
        byte[] synthesizedAudio = new byte[] { 1, 2, 3 };
        byte[] speedAdjustedAudio = new byte[] { 4, 5, 6 };
        byte[] strategyAudio = new byte[] { 7, 8, 9 };

        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(synthesizedAudio)));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(callInfo =>
        {
            byte[]? audio = callInfo.Arg<byte[]>();
            if (audio.SequenceEqual(strategyAudio)) return 30.0;
            if (audio.SequenceEqual(speedAdjustedAudio)) return 30.3;
            return 30.5; // synthesizedAudio
        });
        _processor.AdjustSpeed(Arg.Any<byte[]>(), 30.0).Returns(speedAdjustedAudio);

        var strategy = Substitute.For<IAudioPostSynthesisDurationAdjustmentStrategy>();
        strategy.Name.Returns("TestStrategy");
        strategy.TryAdjustAsync(Arg.Any<AudioPostSynthesisAdjustmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new AudioPostSynthesisAdjustmentProposal(strategyAudio, "Adjusted duration"));

        var sut = CreateSut(
            new BroadcastOptions { MaxSynthesisAttempts = 1 },
            strategies: [strategy]);

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world test script.", 30.0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBe(30.0);
        await strategy.Received(1).TryAdjustAsync(
            Arg.Is<AudioPostSynthesisAdjustmentContext>(c =>
                c.TargetDurationSeconds == 30.0 && c.ActualDurationSeconds == 30.3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_MultipleStrategies_FirstReturnsNull_SecondApplied()
    {
        byte[] synthesizedAudio = new byte[] { 1, 2, 3 };
        byte[] speedAdjustedAudio = new byte[] { 4, 5, 6 };
        byte[] strategyAudio = new byte[] { 7, 8, 9 };

        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(synthesizedAudio)));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(callInfo =>
        {
            byte[]? audio = callInfo.Arg<byte[]>();
            if (audio.SequenceEqual(strategyAudio)) return 30.0;
            if (audio.SequenceEqual(speedAdjustedAudio)) return 30.3;
            return 30.5;
        });
        _processor.AdjustSpeed(Arg.Any<byte[]>(), 30.0).Returns(speedAdjustedAudio);

        var noopStrategy = Substitute.For<IAudioPostSynthesisDurationAdjustmentStrategy>();
        noopStrategy.Name.Returns("NoopStrategy");
        noopStrategy.TryAdjustAsync(Arg.Any<AudioPostSynthesisAdjustmentContext>(), Arg.Any<CancellationToken>())
            .Returns((AudioPostSynthesisAdjustmentProposal?)null);

        var activeStrategy = Substitute.For<IAudioPostSynthesisDurationAdjustmentStrategy>();
        activeStrategy.Name.Returns("ActiveStrategy");
        activeStrategy.TryAdjustAsync(Arg.Any<AudioPostSynthesisAdjustmentContext>(), Arg.Any<CancellationToken>())
            .Returns(new AudioPostSynthesisAdjustmentProposal(strategyAudio, "Fixed"));

        var sut = CreateSut(
            new BroadcastOptions { MaxSynthesisAttempts = 1 },
            strategies: [noopStrategy, activeStrategy]);

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world test script.", 30.0);

        result.IsSuccess.ShouldBeTrue();
        await noopStrategy.Received(1)
            .TryAdjustAsync(Arg.Any<AudioPostSynthesisAdjustmentContext>(), Arg.Any<CancellationToken>());
        await activeStrategy.Received(1)
            .TryAdjustAsync(Arg.Any<AudioPostSynthesisAdjustmentContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_NoStrategies_SkipsStrategyPhase()
    {
        byte[] audio = new byte[] { 1, 2, 3 };
        byte[] adjusted = new byte[] { 4, 5, 6 };

        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audio)));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        // 30.3s is within candidate tolerance (2.5s) but outside effective tolerance (0.15s)
        // With no strategies, speed adjustment kicks in on next attempt
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(callInfo =>
            callInfo.Arg<byte[]>().SequenceEqual(adjusted) ? 30.0 : 30.3);
        _processor.AdjustSpeed(Arg.Any<byte[]>(), 30.0).Returns(adjusted);

        var sut = CreateSut(
            new BroadcastOptions { MaxSynthesisAttempts = 2 },
            strategies: []);

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world test script.", 30.0);

        // Second attempt should succeed with adjusted speed
        result.IsSuccess.ShouldBeTrue();
    }

    // ── Duration estimation divergence ──────────────────────────────────

    [Fact]
    public async Task SynthesizeWithDurationTarget_EstimationFarFromActual_StillSucceedsOnExactHit()
    {
        byte[] audioBytes = new byte[] { 1, 2, 3 };

        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audioBytes)));
        // Estimator says 25s but actual synthesis produces 30s — pipeline succeeds regardless
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(25.0);
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(30.0);

        var sut = CreateSut();

        var result = await sut.SynthesizeWithDurationTargetAsync("Some script text here.", 30.0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBe(30.0);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_SpeedAdjustmentBetweenAttempts_ConvergesToTarget()
    {
        byte[] audio1 = new byte[] { 1, 2, 3 };
        byte[] audio2 = new byte[] { 4, 5, 6 };
        int callCount = 0;

        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return Result<SynthesizedAudio>.Success(new SynthesizedAudio(callCount == 1 ? audio1 : audio2));
            });
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(callInfo =>
        {
            byte[]? audio = callInfo.Arg<byte[]>();
            // First attempt: 40s — beyond the pitch-preserving stretch ceiling (and with no
            // alignment to trim by) → CASE 3 speed-path re-synthesis.
            // Second attempt: 30s exact hit.
            return audio.SequenceEqual(audio1) ? 40.0 : 30.0;
        });

        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 3 });

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world test script.", 30.0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBe(30.0);
        callCount.ShouldBe(2, "second attempt should hit target");
    }

    private static AudioAlignment UniformAlignment(string script, double secondsPerChar)
    {
        var chars = new string[script.Length];
        var starts = new double[script.Length];
        var ends = new double[script.Length];
        for (int i = 0; i < script.Length; i++)
        {
            chars[i] = script[i].ToString();
            starts[i] = i * secondsPerChar;
            ends[i] = (i + 1) * secondsPerChar;
        }

        return new AudioAlignment(chars, starts, ends);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_LargeOverrun_TrimsTrailingSentencesViaAlignment_AndSucceeds()
    {
        // Measure-then-fit: a take that runs far over the slot is fitted by dropping a sentence
        // (using TTS alignment) and re-synthesizing the trimmed script, not by fighting the speed
        // parameter. WHICH sentence goes is BulletinDurationFitter's contract and is pinned by its own
        // Domain tests (cheapest subset within budget); this test pins only that the pipeline hands
        // the fitter the measured alignment and re-synthesises exactly what it returned.
        const string fullScript = "One two. Four five six seven. Eight nine ten eleven.";
        double overrunAllowance = Math.Max(
            0.0, 30.0 * new SpeechDurationOptions().MaxSpeedUpFactor - 30.0 - 0.75);
        byte[] longAudio = { 1, 2, 3 };
        byte[] trimmedAudio = { 4, 5, 6 };
        var alignment = UniformAlignment(fullScript, 38.0 / fullScript.Length);

        int callCount = 0;
        string? secondCallText = null;
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                if (callCount == 1)
                    return Result<SynthesizedAudio>.Success(
                        new SynthesizedAudio(longAudio, Option<AudioAlignment>.Some(alignment)));
                secondCallText = ci.ArgAt<string>(0);
                return Result<SynthesizedAudio>.Success(new SynthesizedAudio(trimmedAudio));
            });
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        // First take: 38s (8s overrun, beyond candidate tolerance). Trimmed re-synth: 30s.
        _processor.GetDuration(Arg.Any<byte[]>())
            .Returns(ci => ci.Arg<byte[]>().SequenceEqual(longAudio) ? 38.0 : 30.0);

        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 3 });

        var result = await sut.SynthesizeWithDurationTargetAsync(fullScript, 30.0);

        result.IsSuccess.ShouldBeTrue();
        string expectedTrimmed = Domain.Services.BulletinDurationFitter.FitByTrimming(
            fullScript, alignment, 30.0, overrunAllowance)!;
        expectedTrimmed.ShouldNotBe(fullScript);
        result.Value.FinalScript.ShouldBe(expectedTrimmed);
        result.Value.DurationSeconds.ShouldBe(30.0);
        secondCallText.ShouldBe(expectedTrimmed, "the re-synthesis must use the alignment-trimmed script");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_LargeOverrun_BelowNaturalFloor_RendersFullScriptAtSpeed()
    {
        // Equal 14-character sentences make every one-sentence drop land below the 29.7s floor.
        const string fullScript = "One two three. Four five six. Nine ten zero.";
        const double targetDuration = 30.0;
        var alignment = UniformAlignment(fullScript, 35.0 / fullScript.Length);
        byte[] initialAudio = { 1, 2, 3 };
        byte[] reRenderedAudio = { 4, 5, 6 };
        int callCount = 0;
        string? secondCallText = null;

        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                callCount++;
                if (callCount == 1)
                    return Result<SynthesizedAudio>.Success(
                        new SynthesizedAudio(initialAudio, Option<AudioAlignment>.Some(alignment)));
                secondCallText = ci.ArgAt<string>(0);
                return Result<SynthesizedAudio>.Success(new SynthesizedAudio(reRenderedAudio));
            });
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(targetDuration);
        _processor.GetDuration(Arg.Any<byte[]>())
            .Returns(ci => ci.Arg<byte[]>().SequenceEqual(initialAudio) ? 35.0 : targetDuration);

        var sut = CreateSut(
            new BroadcastOptions { MaxSynthesisAttempts = 3 },
            new SpeechDurationOptions { MaxSlowDownFactor = 1.0 });

        var result = await sut.SynthesizeWithDurationTargetAsync(fullScript, targetDuration);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FinalScript.ShouldBe(fullScript);
        result.Value.DurationSeconds.ShouldBe(targetDuration);
        secondCallText.ShouldBe(fullScript, "the speed re-render must keep the full script");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_UnderrunWithinPadBudget_PadsSlotTail_AndSucceeds()
    {
        // An underrun beyond the candidate band is padded to the slot tail and accepted in one
        // pass — never slowed, never looped, never failed.
        byte[] audio = { 7, 8, 9 };
        int callCount = 0;
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                return Result<SynthesizedAudio>.Success(new SynthesizedAudio(audio));
            });
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(26.0); // 4s underrun

        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 3 });

        var result = await sut.SynthesizeWithDurationTargetAsync("Heads up, traffic is light today.", 30.0);

        result.IsSuccess.ShouldBeTrue();
        callCount.ShouldBe(1, "the underrun is padded in one pass, not retried");
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_UnderrunSlowFilled_RescalesWordTimingsToStretchedSpan()
    {
        // Underruns are filled by SLOWING the read toward the slot (within the 10% cap) then padding
        // any residual. The speech is stretched later in time, so the word timings must be rescaled
        // by the slow-fill factor to stay aligned with where the (now slower) speech actually plays —
        // otherwise the karaoke highlight leads the audio. Calibration still keys off the NATURAL
        // pre-fill span, never the stretched/padded file.
        const string script = "alpha bravo";
        // Spoken timescale of the raw take: 'alpha' at ~0-1s, 'bravo' at ~18-20s.
        string[] chars = { "a", "l", "p", "h", "a", " ", "b", "r", "a", "v", "o" };
        double[] starts = { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0, 18.0, 18.4, 18.8, 19.2, 19.6 };
        double[] ends = { 0.2, 0.4, 0.6, 0.8, 1.0, 1.0, 18.4, 18.8, 19.2, 19.6, 20.0 };
        var alignment = new AudioAlignment(chars, starts, ends);

        var (sut, _) = CreateSutWithRealProcessor();
        byte[] rawTake = ToneWav(20.0); // 20s of speech, no trailing silence
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(
                new SynthesizedAudio(rawTake, Option<AudioAlignment>.Some(alignment))));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(20.0);

        var result = await sut.SynthesizeWithDurationTargetAsync(script, 26.0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBe(26.0, 0.05); // slot filled: stretched speech + silent pad
        // Calibration signal is the natural 20s read, NOT the 26s file.
        result.Value.SpokenDurationSeconds.ShouldBe(20.0, 0.3);
        var timings = result.Value.WordTimings;
        timings.Count.ShouldBe(2);
        timings[1].Word.ShouldBe("bravo");
        // 20s read stretched to the 10% cap (~22s): 'bravo' moves from 18.0s to ~18.0 × 1.10 = 19.8s.
        timings[1].StartSeconds.ShouldBe(19.8, 0.6);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_UnderrunBedClosed_WordTimingsStayAtNaturalSpan()
    {
        // UseTrimmedBedCloser fills an underrun by appending a non-silent bed AFTER the speech,
        // leaving the speech itself byte-for-byte untouched (unlike the slow-fill case above). The
        // bed is deliberately loud enough to pass the SlotFilled trailing-silence check, so the
        // amplitude-based "spoken span" measurement used elsewhere counts the bed as voiced too.
        // Word timings must NOT be rescaled using that bed-inclusive span — doing so stretches the
        // karaoke highlight across the bed's dead time, so the audible read finishes noticeably
        // before the served timings claim it does (live regression: job 639f932a, 2026-07-07 —
        // real speech ended at 28.3s, served timings ran to exactly 30.0s).
        const string script = "alpha bravo";
        string[] chars = { "a", "l", "p", "h", "a", " ", "b", "r", "a", "v", "o" };
        double[] starts = { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0, 18.0, 18.4, 18.8, 19.2, 19.6 };
        double[] ends = { 0.2, 0.4, 0.6, 0.8, 1.0, 1.0, 18.4, 18.8, 19.2, 19.6, 20.0 };
        var alignment = new AudioAlignment(chars, starts, ends);

        var (sut, _) = CreateSutWithRealProcessor(new SpeechDurationOptions { UseTrimmedBedCloser = true });
        byte[] rawTake = ToneWav(20.0); // 20s of speech, no trailing silence
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(
                new SynthesizedAudio(rawTake, Option<AudioAlignment>.Some(alignment))));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(20.0);

        var result = await sut.SynthesizeWithDurationTargetAsync(script, 26.0);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBe(26.0, 0.05); // slot filled: speech + trimmed bed
        result.Value.SpokenDurationSeconds.ShouldBe(20.0, 0.3); // calibration keys off the natural read
        var timings = result.Value.WordTimings;
        timings.Count.ShouldBe(2);
        timings[1].Word.ShouldBe("bravo");
        // Speech is untouched by the bed-close — 'bravo' must stay at its natural ~18.0s, not be
        // stretched toward the bed-inclusive 26s span (~18.0 × 26/20 = 23.4s — the pre-fix bug).
        timings[1].StartSeconds.ShouldBe(18.0, 0.3);
    }

    // ── Pace Boost (maxSpeedUpFactorOverride) ──────────────────────────────

    [Fact]
    public void FineTuneToTargetDuration_OverrunBeyondDefaultCap_OverrideEnablesAdjustment()
    {
        // 23.5 vs 20.0 = factor 1.175 > default 1.15 cap → normally left raw.
        // With maxSpeedUpFactorOverride=1.35, the factor is within the widened cap,
        // so AdjustSpeed must be called.
        byte[] audio = new byte[] { 1, 2, 3 };
        byte[] adjusted = new byte[] { 4, 5, 6 };
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(23.5);
        _processor.AdjustSpeed(Arg.Any<byte[]>(), 20.0).Returns(adjusted);
        _processor.GetDuration(adjusted).Returns(20.0);
        var sut = CreateSut();

        byte[] result = sut.FineTuneToTargetDuration(audio, 20.0, maxSpeedUpFactorOverride: 1.35);

        result.ShouldBe(adjusted);
        _processor.Received(1).AdjustSpeed(Arg.Any<byte[]>(), 20.0);
    }

    [Fact]
    public void FineTuneToTargetDuration_OverrunBeyondDefaultCap_NullOverride_LeavesRaw()
    {
        // Regression guard: same overrun (factor 1.175 > 1.15 default cap) with null
        // override must still fall through unchanged — behaviour identical to pre-feature.
        byte[] audio = new byte[] { 1, 2, 3 };
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(23.5);
        var sut = CreateSut();

        byte[] result = sut.FineTuneToTargetDuration(audio, 20.0, maxSpeedUpFactorOverride: null);

        result.ShouldBeSameAs(audio);
        _processor.DidNotReceive().AdjustSpeed(Arg.Any<byte[]>(), Arg.Any<double>());
    }

    [Fact]
    public void FineTuneToTargetDuration_OverrunBeyondSecondsGuard_OverrideWidensWindow()
    {
        // 36.0 vs 30.0 = +6.0s overrun — beyond the default 4.5s MaxSafeOverrunTrimSeconds guard,
        // but a 1.25x override can absorb 30 × 0.25 = 7.5s, so the window widens with the factor
        // and the take is compressed instead of being left for regeneration.
        byte[] audio = new byte[] { 1, 2, 3 };
        byte[] adjusted = new byte[] { 4, 5, 6 };
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(36.0);
        _processor.AdjustSpeed(Arg.Any<byte[]>(), 30.0).Returns(adjusted);
        var sut = CreateSut();

        byte[] result = sut.FineTuneToTargetDuration(audio, 30.0, maxSpeedUpFactorOverride: 1.25);

        result.ShouldBe(adjusted);
        _processor.Received(1).AdjustSpeed(Arg.Any<byte[]>(), 30.0);
    }

    [Fact]
    public void FineTuneToTargetDuration_OverrunBeyondSecondsGuard_NullOverride_LeavesRaw()
    {
        // Regression guard: the widened window is strictly opt-in. The same +6.0s overrun with no
        // override must keep the configured 4.5s guard and fall through for regeneration.
        byte[] audio = new byte[] { 1, 2, 3 };
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(36.0);
        var sut = CreateSut();

        byte[] result = sut.FineTuneToTargetDuration(audio, 30.0, maxSpeedUpFactorOverride: null);

        result.ShouldBeSameAs(audio);
        _processor.DidNotReceive().AdjustSpeed(Arg.Any<byte[]>(), Arg.Any<double>());
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_OverrideWidensSpeedUpCap_OverrunCompressed()
    {
        // Full pipeline: a take that overshoots beyond the default 1.15x cap (34.5s for
        // a 30s slot → factor 1.15, just at the boundary). With a 1.35x override the
        // overrun qualifies for compression and the pipeline succeeds.
        byte[] rawAudio = new byte[] { 1, 2, 3 };
        byte[] adjustedAudio = new byte[] { 4, 5, 6 };
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(rawAudio)));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        // 34.5 / 30 = 1.15x — at the default cap boundary
        _processor.GetDuration(Arg.Any<byte[]>())
            .Returns(call => ReferenceEquals(call.Arg<byte[]>(), adjustedAudio) ? 30.0 : 34.5);
        _processor.AdjustSpeed(Arg.Any<byte[]>(), 30.0).Returns(adjustedAudio);

        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 1 });

        var result = await sut.SynthesizeWithDurationTargetAsync(
            "Test script for override.", 30.0,
            maxSpeedUpFactorOverride: 1.35);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DurationSeconds.ShouldBe(30.0);
        _processor.Received(1).AdjustSpeed(Arg.Any<byte[]>(), 30.0);
    }
    // ── IsSlotFilled ───────────────────────────────────────────────

    [Fact]
    public void IsSlotFilled_AudioWithinToleranceAndNoExcessiveSilence_ReturnsTrue()
    {
        // 30s of audible tone, no trailing silence, target = 30.0s
        var (sut, _) = CreateSutWithRealProcessor();
        byte[] audio = ToneWav(30.0);

        bool filled = sut.IsSlotFilled(audio, 30.0);

        filled.ShouldBeTrue();
    }

    [Fact]
    public void IsSlotFilled_AudioFarOutsideTolerance_ReturnsFalse()
    {
        // 5s of audio against a 30s target — 25s underrun far exceeds 0.15s tolerance
        var (sut, _) = CreateSutWithRealProcessor();
        byte[] audio = ToneWav(5.0);

        bool filled = sut.IsSlotFilled(audio, 30.0);

        filled.ShouldBeFalse();
    }

    [Fact]
    public void IsSlotFilled_RightDurationButExcessiveTrailingSilence_ReturnsFalse()
    {
        // 2s of tone + ~28s of silence = 30s total, but trailing silence ~28s far exceeds MaxSilencePadSeconds (0.3s)
        int toneSamples = (int)Math.Round(44100 * 2.0);
        int totalSamples = (int)Math.Round(44100 * 30.0);
        short[] pcm = new short[totalSamples];
        short amplitude = 6000;
        for (int i = 0; i < toneSamples; i++)
            pcm[i] = (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * 200 * i / 44100.0));
        byte[] bytes = new byte[totalSamples * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        byte[] audio = WavHeaderBuilder.WrapInWavHeader(bytes, 44100, 1, 16);
        var (sut, _) = CreateSutWithRealProcessor();

        bool filled = sut.IsSlotFilled(audio, 30.0);

        filled.ShouldBeFalse();
    }

    [Fact]
    public void IsSlotFilled_ExactlyAtTargetWithZeroPadding_ReturnsTrue()
    {
        var (sut, _) = CreateSutWithRealProcessor();
        byte[] audio = ToneWav(30.0);

        bool filled = sut.IsSlotFilled(audio, 30.0);

        filled.ShouldBeTrue();
    }

    // ── TrimToSpokenDuration ─────────────────────────────────────────────

    [Fact]
    public void TrimToSpokenDuration_ClipWithLargeTrailingSilencePad_TrimsToSpokenLength()
    {
        // Mirrors the real underrun bug: SynthesizeWithDurationTargetAsync pads a content-limited
        // take toward its target with raw silence even when it can't reach it. 2.0s of tone
        // followed by 1.5s of silence — a >0.5s delta that EnsureExactDuration would refuse to
        // correct but TrimToSpokenDuration must strip anyway (2026-07-07 dead-air fix).
        int spokenSamples = (int)Math.Round(44100 * 2.0);
        int totalSamples = (int)Math.Round(44100 * 3.5);
        short[] pcm = new short[totalSamples];
        short amplitude = 6000;
        for (int i = 0; i < spokenSamples; i++)
            pcm[i] = (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * 200 * i / 44100.0));
        byte[] bytes = new byte[totalSamples * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        byte[] audio = WavHeaderBuilder.WrapInWavHeader(bytes, 44100, 1, 16);
        var (sut, processor) = CreateSutWithRealProcessor();

        byte[] result = sut.TrimToSpokenDuration(audio);

        processor.GetDuration(result).ShouldBe(2.0, 0.05);
        processor.GetDuration(result).ShouldBeLessThan(processor.GetDuration(audio) - 1.0);
    }

    [Fact]
    public void TrimToSpokenDuration_NoTrailingSilence_ReturnsUnchangedOrEquivalentDuration()
    {
        var (sut, processor) = CreateSutWithRealProcessor();
        byte[] audio = ToneWav(2.0);

        byte[] result = sut.TrimToSpokenDuration(audio);

        processor.GetDuration(result).ShouldBe(processor.GetDuration(audio), 0.01);
    }

    [Fact]
    public void TrimToSpokenDuration_EmptyOrInvalidAudio_ReturnsOriginal()
    {
        var (sut, _) = CreateSutWithRealProcessor();
        byte[] data = new byte[] { 1, 2, 3 };

        sut.TrimToSpokenDuration(data).ShouldBeSameAs(data);
    }
    // ── OperationalFailure metadata at the provider boundary ────────────

    [Fact]
    public async Task SynthesizeWithDurationTarget_PermanentError_CarriesPermanentMetadata()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("BadRequest: invalid voice ID"));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 3 });

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world", 30.0);

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Permanent);
        result.FailureMetadata.Value.Code.ShouldBe("tts.permanent");
        result.FailureMetadata.Value.Message.ShouldBe("BadRequest: invalid voice ID");
        // Permanent error aborts immediately — only 1 call despite MaxSynthesisAttempts=3
        await _synthesizer.Received(1).SynthesizeAsync(
            Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_TransientError_CarriesRetryableMetadata()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("TTS provider unavailable"));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 2 });

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world", 30.0);

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Retryable);
        result.FailureMetadata.Value.Code.ShouldBe("tts.retryable");
        result.FailureMetadata.Value.Message.ShouldBe("TTS provider unavailable");
        // Transient error retries — both attempts consumed
        await _synthesizer.Received(2).SynthesizeAsync(
            Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_ErrorTextPreserved_ByteForByte()
    {
        const string vendorError = "unauthorized: API key revoked";
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure(vendorError));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 1 });

        var result = await sut.SynthesizeWithDurationTargetAsync("Hello world", 30.0);

        result.Error.ShouldBe(vendorError);
        result.FailureMetadata.Value.Message.ShouldBe(vendorError);
    }

    [Fact]
    public async Task SynthesizeWithDurationTarget_Cancellation_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result<SynthesizedAudio>>(new OperationCanceledException(cts.Token)));
        _estimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        var sut = CreateSut(new BroadcastOptions { MaxSynthesisAttempts = 3 });

        await Should.ThrowAsync<OperationCanceledException>(
            () => sut.SynthesizeWithDurationTargetAsync("Hello", 30.0, cancellationToken: cts.Token));
    }

}

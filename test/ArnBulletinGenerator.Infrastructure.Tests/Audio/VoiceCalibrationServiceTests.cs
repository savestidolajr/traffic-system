using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Audio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public class VoiceCalibrationServiceTests
{
    private readonly IDurationEstimator _durationEstimator = Substitute.For<IDurationEstimator>();
    private readonly ILogger<VoiceCalibrationService> _logger = Substitute.For<ILogger<VoiceCalibrationService>>();
    private readonly IVoiceCalibrationRepository _repository = Substitute.For<IVoiceCalibrationRepository>();
    private readonly IAudioSynthesizer _synthesizer = Substitute.For<IAudioSynthesizer>();

    private VoiceCalibrationService CreateSut(
        BroadcastOptions? options = null,
        TimeProvider? timeProvider = null,
        IAudioSynthesizer? synthesizer = null)
    {
        return new VoiceCalibrationService(
            _repository,
            synthesizer ?? _synthesizer,
            _durationEstimator,
            Options.Create(options ?? new BroadcastOptions()),
            timeProvider ?? TimeProvider.System,
            _logger);
    }

    private static VoiceCalibration CreateFreshCalibration(
        string voiceId = "voice1",
        double syllablesPerSecond = 6.0,
        double syllablesPerWord = 1.5,
        double wordsPerMinute = 160,
        int runCount = 5)
    {
        var settings = VoiceSettings.Default;
        return new VoiceCalibration(settings.GetConfigurationKey(voiceId), voiceId, settings)
        {
            SyllablesPerSecond = syllablesPerSecond,
            SyllablesPerWord = syllablesPerWord,
            WordsPerMinute = wordsPerMinute,
            RunCount = runCount,
            CalibratedAt = DateTime.UtcNow
        };
    }

    // ── GetCalibrationMetricsAsync ──────────────────────────────────────

    [Fact]
    public async Task GetCalibrationMetrics_ExistingCalibration_ReturnsCalibrationValues()
    {
        var calibration = CreateFreshCalibration();
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.Some(calibration)));
        var sut = CreateSut();

        var metrics = await sut.GetCalibrationMetricsAsync("voice1");

        metrics.WordsPerMinute.ShouldBe(160);
        metrics.SyllablesPerSecond.ShouldBe(6.0);
        metrics.SyllablesPerWord.ShouldBe(1.5);
    }

    [Fact]
    public async Task GetCalibrationMetrics_EmptyVoiceId_ReturnsDefaults()
    {
        var opts = new BroadcastOptions { WordsPerMinute = 150, SyllablesPerSecond = 5.8 };
        var sut = CreateSut(opts);

        var metrics = await sut.GetCalibrationMetricsAsync("");

        metrics.WordsPerMinute.ShouldBe(150);
        metrics.SyllablesPerSecond.ShouldBe(5.8);
        metrics.SyllablesPerWord.ShouldBe(CalibrationDefaults.SyllablesPerWord);
    }

    [Fact]
    public async Task GetCalibrationMetrics_NoCalibration_TriggerDisabled_ReturnsDefaults()
    {
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        var opts = new BroadcastOptions { WordsPerMinute = 150, SyllablesPerSecond = 5.8 };
        var sut = CreateSut(opts);

        var metrics = await sut.GetCalibrationMetricsAsync("voice1", triggerCalibrationIfMissing: false);

        metrics.WordsPerMinute.ShouldBe(150);
        metrics.SyllablesPerSecond.ShouldBe(5.8);
    }

    // ── CalibrateVoiceAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CalibrateVoice_ValidSynthesis_SavesCalibration()
    {
        byte[] audioBytes = new byte[] { 1, 2, 3 };
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audioBytes)));
        // 14.0s for the 37-word/60-syllable reference phrase = 158.6 wpm — the value actually
        // measured on dev. A 5.0s render would be 444 wpm and is now correctly rejected as impossible.
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut();

        var result = await sut.CalibrateVoiceAsync("voice1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.RunCount.ShouldBe(1);
        result.Value.SyllablesPerSecond.ShouldBeGreaterThan(0);
        await _repository.Received(1).SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CalibrateVoice_ImpossibleReferenceRate_IsRejected_AndNeverSaved()
    {
        // The live defect: this writer had NO sanity gate while RecordActualDurationAsync did, making
        // it the only path able to put an impossible rate into the row. 60 syllables in 5.0s is
        // 444 wpm; a truncated or failed reference render must not poison slot fitting.
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio([1, 2, 3])));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(5.0);
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        var sut = CreateSut();

        var result = await sut.CalibrateVoiceAsync("voice1");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("out of range");
        await _repository.DidNotReceive().SaveCalibrationAsync(
            Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CalibrateVoice_WarmRow_DoesNotBlendTheSyntheticReferenceIn()
    {
        // The reference phrase reads 158.6 wpm; real bulletin copy on the same voice reads 128-147.
        // Blending the two into one row produced a number describing neither. On a row that already
        // has real measurements the reference render only refreshes the staleness clock.
        var warm = new VoiceCalibration("key-1", "voice1", VoiceSettings.Default)
        {
            RunCount = 200,
            SyllablesPerSecond = 3.55,
            SyllablesPerWord = 1.55,
            SyllablesPerMinute = 213.0,
            WordsPerMinute = 137.4,
            CalibratedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio([1, 2, 3])));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.Some(warm)));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut();

        var result = await sut.CalibrateVoiceAsync("voice1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.WordsPerMinute.ShouldBe(137.4, 0.001);   // untouched
        result.Value.RunCount.ShouldBe(200);                  // not counted as a measurement
        result.Value.CalibratedAt.ShouldBeGreaterThan(warm.CalibratedAt.AddDays(-1)); // clock refreshed
    }

    [Fact]
    public async Task CalibrateVoice_ForwardsTheModelOverrideToSynthesis()
    {
        // Without this the reference render used the configured default model, so a profile pinned to
        // eleven_multilingual_v2 had its rate measured on eleven_v3 — 135.6 vs 159.4 wpm on the same
        // voice reading the same text.
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio([1, 2, 3])));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut();

        await sut.CalibrateVoiceAsync("voice1", modelOverride: Option<string>.Some("eleven_multilingual_v2"));

        await _synthesizer.Received(1).SynthesizeAsync(
            Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
            Arg.Is<Option<string>>(m => m.HasValue && m.Value == "eleven_multilingual_v2"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CalibrateVoice_SynthesisFailure_ReturnsError()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("Provider error"));
        var sut = CreateSut();

        var result = await sut.CalibrateVoiceAsync("voice1");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Provider error");
    }

    [Fact]
    public async Task CalibrateVoice_SynthesisTypedInvalidInput_IsPreservedToTheCaller()
    {
        // Finding 26: an unknown voice is a typed InvalidInput from the synthesizer; the
        // calibration service must preserve the disposition so the trigger can return 400
        // instead of a misleading 500 leaking the provider error.
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure(OperationalFailure.InvalidInput(
                "tts.invalid_request",
                "TTS failed: An invalid ID has been received: 'zzz-nonexistent'. Make sure to provide a correct one.")));
        var sut = CreateSut();

        var result = await sut.CalibrateVoiceAsync("voice1");

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
        result.FailureMetadata.Value.Code.ShouldBe("tts.invalid_request");
    }

    [Fact]
    public async Task CalibrateVoice_ZeroDuration_ReturnsError()
    {
        byte[] audioBytes = new byte[] { 1, 2, 3 };
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audioBytes)));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(0.0);
        var sut = CreateSut();

        var result = await sut.CalibrateVoiceAsync("voice1");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Invalid audio duration");
    }

    [Fact]
    public async Task CalibrateVoice_SaveFailure_ReturnsError()
    {
        byte[] audioBytes = new byte[] { 1, 2, 3 };
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audioBytes)));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Storage error"));
        var sut = CreateSut();

        var result = await sut.CalibrateVoiceAsync("voice1");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Storage error");
    }

    // ── HasValidCalibrationAsync ────────────────────────────────────────

    [Fact]
    public async Task HasValidCalibration_FreshCalibration_ReturnsTrue()
    {
        var calibration = CreateFreshCalibration();
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.Some(calibration)));
        var sut = CreateSut();

        bool result = await sut.HasValidCalibrationAsync("voice1");

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task HasValidCalibration_NoCalibration_ReturnsFalse()
    {
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        var sut = CreateSut();

        bool result = await sut.HasValidCalibrationAsync("voice1");

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task HasValidCalibration_StaleCalibration_ReturnsFalse()
    {
        var calibration = CreateFreshCalibration();
        calibration.CalibratedAt = DateTime.UtcNow.AddHours(-25);
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.Some(calibration)));
        var sut = CreateSut();

        bool result = await sut.HasValidCalibrationAsync("voice1");

        result.ShouldBeFalse();
    }

    // ── RecordActualDurationAsync ───────────────────────────────────────

    [Fact]
    public async Task RecordActualDuration_ValidData_SavesCalibration()
    {
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut();

        // Text with ~14 words, ~25 syllables, duration 5.0s → ~168 WPM, ~5.0 syl/sec
        await sut.RecordActualDurationAsync(
            "voice1", VoiceSettings.Default,
            "Good morning, this is a test sentence for calibration and verification of voice data.",
            5.0);

        await _repository.Received(1).SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordActualDuration_WithModel_SavesIntoModelAwareCalibrationRow()
    {
        _repository.GetCalibrationAsync(
                "voice1", VoiceSettings.Default, Arg.Any<CancellationToken>(), "eleven_v3")
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut();

        await sut.RecordActualDurationAsync(
            "voice1", VoiceSettings.Default,
            "Good morning, this is a test sentence for calibration and verification of voice data.",
            5.0, modelOverride: Option<string>.Some("eleven_v3"));

        await _repository.Received(1).SaveCalibrationAsync(
            Arg.Is<VoiceCalibration>(calibration => calibration.ModelId == "eleven_v3"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordActualDuration_EmptyText_SkipsUpdate()
    {
        var sut = CreateSut();

        await sut.RecordActualDurationAsync("voice1", VoiceSettings.Default, "", 5.0);

        await _repository.DidNotReceive()
            .SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordActualDuration_ZeroDuration_SkipsUpdate()
    {
        var sut = CreateSut();

        await sut.RecordActualDurationAsync("voice1", VoiceSettings.Default, "Some text here", 0.0);

        await _repository.DidNotReceive()
            .SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CalibrateVoice_UsesInjectedClock_NotSystemClock()
    {
        var fixedUtc = new DateTimeOffset(2030, 6, 15, 3, 0, 0, TimeSpan.Zero);
        var fakeClock = new FixedTimeProvider(fixedUtc);
        byte[] audioBytes = [1, 2, 3];
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio(audioBytes)));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut(timeProvider: fakeClock);

        var result = await sut.CalibrateVoiceAsync("voice1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.CalibratedAt.ShouldBe(fixedUtc.UtcDateTime);
    }
    [Fact]
    public async Task CalibrateVoice_SameVoiceSettingsAndModel_SharesOneFlight()
    {
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        var synthesizer = new BlockingSynthesizer();
        var sut = CreateSut(synthesizer: synthesizer);

        var first = sut.CalibrateVoiceAsync(
            "voice1", modelOverride: Option<string>.Some("eleven_v3"));
        await synthesizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = sut.CalibrateVoiceAsync(
            "voice1", modelOverride: Option<string>.Some("eleven_v3"));

        synthesizer.Release.TrySetResult(null);
        var results = await Task.WhenAll(first, second);

        results.ShouldAllBe(result => result.IsSuccess);
        synthesizer.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task CalibrateVoice_DifferentModels_DoNotShareFlight()
    {
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        var synthesizer = new BlockingSynthesizer();
        var sut = CreateSut(synthesizer: synthesizer);

        var first = sut.CalibrateVoiceAsync(
            "voice1", modelOverride: Option<string>.Some("eleven_v3"));
        await synthesizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = sut.CalibrateVoiceAsync(
            "voice1", modelOverride: Option<string>.Some("eleven_multilingual_v2"));

        synthesizer.Calls.ShouldBe(2);
        synthesizer.Release.TrySetResult(null);
        var results = await Task.WhenAll(first, second);

        results.ShouldAllBe(result => result.IsSuccess);
    }

    [Fact]
    public async Task CalibrateVoice_CancellationOnlyReleasesThatWaiter()
    {
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        var synthesizer = new BlockingSynthesizer();
        var sut = CreateSut(synthesizer: synthesizer);
        using var cancelled = new CancellationTokenSource();

        var first = sut.CalibrateVoiceAsync(
            "voice1", cancellationToken: cancelled.Token);
        await synthesizer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = sut.CalibrateVoiceAsync("voice1");
        cancelled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(async () => await first);
        synthesizer.Release.TrySetResult(null);
        var result = await second;

        result.IsSuccess.ShouldBeTrue();
        synthesizer.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task CalibrateVoice_FailureIsNotCached()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<SynthesizedAudio>.Failure("temporary provider error"),
                Result<SynthesizedAudio>.Success(new SynthesizedAudio([1, 2, 3])));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(14.0);
        _repository.GetCalibrationAsync(Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<VoiceCalibration>>.Success(Option<VoiceCalibration>.None));
        _repository.SaveCalibrationAsync(Arg.Any<VoiceCalibration>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var sut = CreateSut();

        var first = await sut.CalibrateVoiceAsync("voice1");
        var second = await sut.CalibrateVoiceAsync("voice1");

        first.IsFailure.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        await _synthesizer.Received(2).SynthesizeAsync(
            Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
            Arg.Any<Option<string>>(), Arg.Any<CancellationToken>());
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class BlockingSynthesizer : IAudioSynthesizer
{
    public TaskCompletionSource<object?> Started { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<object?> Release { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls;

    public async Task<Result<SynthesizedAudio>> SynthesizeAsync(
        string text,
        Option<VoiceSettings> settings = default,
        Option<string> voiceIdOverride = default,
        Option<string> modelOverride = default,
        CancellationToken cancellationToken = default,
        Option<string> market = default,
        PronunciationStack? pronunciationStack = null)
    {
        Interlocked.Increment(ref Calls);
        Started.TrySetResult(null);
        await Release.Task;
        return Result<SynthesizedAudio>.Success(new SynthesizedAudio([1, 2, 3]));
    }
}

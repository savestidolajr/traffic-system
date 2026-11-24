using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Models;
using NSubstitute;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Application.Tests;

public partial class BroadcastOrchestratorTests
{
    [Fact]
    public async Task DurationMissWithComposerThread_ContinuesRevisionWithoutFreshGeneration()
    {
        var sut = CreateSutWithRetries(1);
        byte[] audio = [1, 2, 3];
        _scriptGenerator.GenerateScriptAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])
            {
                ComposerThreadId = "composer-thread-1"
            }));
        _scriptGenerator.ReviseForMeasuredDurationAsync(
                Arg.Any<BroadcastWithTraffic>(), "composer-thread-1", TestScript,
                Arg.Is<double>(seconds => Math.Abs(seconds - 35.2) < 0.001), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])
            {
                ComposerThreadId = "composer-thread-2"
            }));
        SetupDurationMissThenSuccess(audio);

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        await _scriptGenerator.Received(1).GenerateScriptAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
            Arg.Any<CancellationToken>());
        await _scriptGenerator.Received(1).ReviseForMeasuredDurationAsync(
            Arg.Any<BroadcastWithTraffic>(), "composer-thread-1", TestScript,
            Arg.Is<double>(seconds => Math.Abs(seconds - 35.2) < 0.001), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    // Dev daf615b5 (2026-09-02, 22s slot): a 20.9s read was 1.1s SHORT, but the composer thread
    // was handed the full read's seconds against its body-only slot, read it as an overrun and
    // deleted the only traffic sentence. The handoff must subtract the same bookend cost the body
    // slot was derived from, so the composer sees the engine's shortfall, not a phantom overrun.
    [Fact]
    public async Task DurationMissWithBookends_HandsTheComposerBodySecondsNotWholeReadSeconds()
    {
        var sut = CreateSutWithRetries(1);
        byte[] audio = [1, 2, 3];
        // 4-word intro at 150 wpm (0.4 s/word) costs 1.6s of the slot. (The fixture default of
        // 20 wpm would price it at 12s and the slot-fit pre-check would drop the pair entirely.)
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(150, 5.8, 1.45));
        var ctx = Ctx() with
        {
            Bookends = Option<ResolvedBookends>.Some(new ResolvedBookends("Now looking over Sydney.", "", "bk-1", 1))
        };
        double? handedSeconds = null;
        string? handedRead = null;
        _scriptGenerator.GenerateScriptAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])
            {
                ComposerThreadId = "composer-thread-1"
            }));
        _scriptGenerator.ReviseForMeasuredDurationAsync(
                Arg.Any<BroadcastWithTraffic>(), "composer-thread-1",
                Arg.Do<string>(read => handedRead = read),
                Arg.Do<double>(seconds => handedSeconds = seconds), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])
            {
                ComposerThreadId = "composer-thread-2"
            }));
        SetupDurationMissThenSuccess(audio);

        var result = await sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        handedSeconds.ShouldNotBeNull();
        handedSeconds.Value.ShouldBe(35.2 - 1.6, 0.01);
        handedRead.ShouldBe(TestScript);
    }

    [Fact]
    public async Task DurationMissWithTrimmedSpokenTake_ContinuesRevisionWithTrimmedBody()
    {
        var sut = CreateSutWithRetries(1);
        byte[] audio = [1, 2, 3];
        const string firstSentence = "Traffic is moving well across the Gold Coast today.";
        const string secondSentence = "Delays are light through the city centre.";
        const string thirdSentence = "The waterfront roads are also moving slowly this morning.";
        const string requestedScript = firstSentence + " " + secondSentence + " " + thirdSentence;
        const string intro = "Now looking over Gold Coast.";
        const string outro = "That is all for Gold Coast traffic.";
        string trimmedBody = firstSentence + " " + secondSentence;
        string trimmedTake = $"{intro} {trimmedBody} {outro}";
        string? handedRead = null;

        _voiceCalibrationService.GetCalibrationMetricsAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(150, 5.8, 1.45));
        var ctx = Ctx() with
        {
            Bookends = Option<ResolvedBookends>.Some(new ResolvedBookends(intro, outro, "bk-1", 1))
        };
        _scriptGenerator.GenerateScriptAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(requestedScript, [])
            {
                ComposerThreadId = "composer-thread-1"
            }));
        _scriptGenerator.ReviseForMeasuredDurationAsync(
                Arg.Any<BroadcastWithTraffic>(), "composer-thread-1",
                Arg.Do<string>(read => handedRead = read),
                Arg.Any<double>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(requestedScript, [])
            {
                ComposerThreadId = "composer-thread-2"
            }));
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(),
                lockedPrefix: Arg.Any<string?>())
            .Returns(
                Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, trimmedTake, 30.0)
                {
                    SpokenDurationSeconds = 27.8,
                    SlotFilled = false,
                    IsNaturalSynthesis = true
                }),
                Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, requestedScript, 30.0)
                {
                    SpokenDurationSeconds = 30.0,
                    SlotFilled = true,
                    IsNaturalSynthesis = true
                }));
        _durationEstimator.GetDuration(audio).Returns(30.0);

        var result = await sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue(result.Error);
        handedRead.ShouldBe(trimmedBody);
        await _scriptGenerator.Received(1).ReviseForMeasuredDurationAsync(
            Arg.Any<BroadcastWithTraffic>(), "composer-thread-1", trimmedBody,
            Arg.Any<double>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DurationMissWithoutComposerThread_RetriesWithFreshGeneration()
    {
        var sut = CreateSutWithRetries(1);
        byte[] audio = [1, 2, 3];
        _scriptGenerator.GenerateScriptAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
                Arg.Any<CancellationToken>())
            .Returns(
                Result<DraftedScript>.Success(new DraftedScript(TestScript, [])),
                Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        SetupDurationMissThenSuccess(audio);

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        await _scriptGenerator.Received(2).GenerateScriptAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(),
            Arg.Any<CancellationToken>());
        await _scriptGenerator.DidNotReceive().ReviseForMeasuredDurationAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<double>(),
            Arg.Any<double>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    private void SetupDurationMissThenSuccess(byte[] audio)
    {
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(),
                lockedPrefix: Arg.Any<string?>())
            .Returns(
                Result<AudioSynthesisResult>.Failure(
                    OperationalFailure.Retryable("duration.mismatch", "Actual duration 35.2s exceeded target")),
                Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0)
                {
                    SpokenDurationSeconds = 30.0,
                    SlotFilled = true,
                    IsNaturalSynthesis = true
                }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }
}

using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Triggers.Http;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
/// Focused performance-contract tests for synchronous TTS work. These deliberately exercise the
/// application gate rather than a hosted Functions process: a provider call that violates a
/// deadline must be stopped before the output sink is reached, while successful duplicate requests
/// must share the same result.
/// </summary>
public sealed class InteractiveTtsPerformanceTests
{
    [Fact]
    public void DefaultTtsContract_UsesTwoPermitsAndFortyFiveSecondDeadline()
    {
        var options = new InteractiveWorkOptions();

        options.TtsPoolSize.ShouldBe(2);
        options.TtsDeadlineSeconds.ShouldBe(45);
    }

    [Fact]
    public async Task TtsPool_AllowsTwoConcurrentOperations()
    {
        var gate = new InteractiveWorkGate(
            Options.Create(new InteractiveWorkOptions { TtsPoolSize = 2 }));
        var bothEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;

        async Task<Result<int>> Work(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref entered) == 2)
                bothEntered.TrySetResult(true);

            await release.Task.WaitAsync(cancellationToken);
            return Result<int>.Success(entered);
        }

        Task<Result<int>> first = gate.ExecuteTtsAsync(
            Key("first"), Work, bypassCache: true);
        Task<Result<int>> second = gate.ExecuteTtsAsync(
            Key("second"), Work, bypassCache: true);

        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        release.TrySetResult(true);

        (await first).IsSuccess.ShouldBeTrue();
        (await second).IsSuccess.ShouldBeTrue();
        entered.ShouldBe(2);
    }

    [Fact]
    public async Task TtsDeadline_CancelsProviderBeforeOutputSave()
    {
        var gate = new InteractiveWorkGate(
            Options.Create(new InteractiveWorkOptions { TtsDeadlineSeconds = 1 }));
        var output = Substitute.For<IBroadcastOutput>();

        var result = await gate.ExecuteAsync(
            new InteractiveWorkRequest(
                InteractiveWorkPool.Tts,
                Key("deadline"),
                Deadline: TimeSpan.FromMilliseconds(25),
                BypassCache: true),
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await output.SaveAsync([1, 2, 3], "should-not-save", cancellationToken);
                return Result<int>.Success(1);
            });

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("timed out");
        await output.DidNotReceive().SaveAsync(
            Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulDuplicateTtsRequest_ReusesCachedResult()
    {
        var gate = new InteractiveWorkGate(
            Options.Create(new InteractiveWorkOptions { CacheTtlMinutes = 10 }));
        int calls = 0;

        Task<Result<string>> Work(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Result<string>.Success("cached-audio"));
        }

        var key = Key("same-request");
        (await gate.ExecuteTtsAsync(key, Work)).Value.ShouldBe("cached-audio");
        (await gate.ExecuteTtsAsync(key, Work)).Value.ShouldBe("cached-audio");

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Synthesize_PreservesLegacyResponseAndBlobName()
    {
        var pipeline = Substitute.For<IAudioPipelineService>();
        var output = Substitute.For<IBroadcastOutput>();
        var now = new DateTimeOffset(2026, 9, 2, 12, 34, 56, TimeSpan.Zero);
        var trigger = new SynthesizeHttpTrigger(
            pipeline,
            output,
            Options.Create(new ElevenLabsOptions()),
            new FixedTimeProvider(now),
            NullLogger<SynthesizeHttpTrigger>.Instance);

        pipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(),
                lockedSuffix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], "Traffic is moving.", 12.5)));
        output.SaveAsync(
                Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await trigger.Run(
                MockHttpRequestData.CreatePost(
                    new SynthesizeRequestDto("Traffic is moving.", VoiceId: "voice-1"),
                    "http://localhost/api/synthesize"),
                MockHttpRequestData.CreateFunctionContext()));

        status.ShouldBe(HttpStatusCode.OK);
        var payload = JsonDocument.Parse(body).RootElement;
        payload.GetProperty("status").GetString().ShouldBe("success");
        payload.GetProperty("audioUrl").GetString()
            .ShouldBe("synthesize_20260902_123456.wav");
        payload.GetProperty("durationSec").GetDouble().ShouldBe(12.5);
        payload.GetProperty("audioSizeBytes").GetInt32().ShouldBe(3);
        await output.Received(1).SaveAsync(
            Arg.Is<byte[]>(bytes => bytes.SequenceEqual(new byte[] { 1, 2, 3 })),
            "synthesize_20260902_123456",
            Arg.Any<CancellationToken>());
    }

    private static InteractiveWorkKey Key(string input) => new(
        "operator-a", "tts-test", "revision-1", input, "eleven_v3", "default", "", "");

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

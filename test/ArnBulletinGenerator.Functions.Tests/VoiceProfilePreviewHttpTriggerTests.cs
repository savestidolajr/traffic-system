using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Regression tests for bug-bash 2026-08-05 finding 26: previewing a nonexistent/invalid
///     voice used to return HTTP 500 leaking the raw provider error (incl. request_id). The
///     synthesizer now returns a typed InvalidInput with a sanitised message, and the trigger
///     routes it through <c>MapTypedFailureAsync</c> so it becomes a clean 400 — the same shape
///     the remix route already used.
/// </summary>
public sealed class VoiceProfilePreviewHttpTriggerTests
{
    private readonly IAudioSynthesizer _audioSynthesizer = Substitute.For<IAudioSynthesizer>();
    private readonly VoiceProfilePreviewHttpTrigger _sut;

    public VoiceProfilePreviewHttpTriggerTests()
    {
        _sut = new VoiceProfilePreviewHttpTrigger(
            _audioSynthesizer,
            Substitute.For<IAudioProcessor>(),
            Substitute.For<IBroadcastOutput>(),
            Substitute.For<IAudioUrlProvider>(),
            Substitute.For<IVoiceRemixer>(),
            Options.Create(new SpeechDurationOptions()),
            TimeProvider.System,
            Substitute.For<ILogger<VoiceProfilePreviewHttpTrigger>>());
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static PreviewVoiceRequestDto PreviewRequest(string voiceId = "zzz-nonexistent-voice") =>
        new(voiceId, "eleven_v3", new VoiceSettingsDto());

    [Fact]
    public async Task Preview_InvalidVoiceTypedFailure_ReturnsSanitisedBadRequest()
    {
        // A provider 4xx (unknown/ineligible voice) is operator-correctable input → 400 with the
        // sanitised message, and never the raw provider body / request_id.
        _audioSynthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure(OperationalFailure.InvalidInput(
                "tts.invalid_request",
                "TTS failed: An invalid ID has been received: 'zzz-nonexistent-voice'. Make sure to provide a correct one.")));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Preview(MockHttpRequestData.CreatePost(PreviewRequest(), "http://localhost/api/voices/profiles/preview"),
                Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        // The body is JSON and the serializer escapes ' as \u0027, so assert on the decoded
        // Error field, never the raw escaped body.
        string error = JsonDocument.Parse(body).RootElement.GetProperty("Error").GetString()!;
        error.ShouldContain("An invalid ID has been received: 'zzz-nonexistent-voice'");
        error.ShouldNotContain("request_id");
        error.ShouldNotContain("invalid_request");
    }

    [Fact]
    public async Task Preview_UntypedProviderFailure_StaysInternalError()
    {
        // A failure without a typed disposition (provider outage, quota, network) is a server
        // fault, not the caller's input — it must remain a 500.
        _audioSynthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<SynthesizedAudio>.Failure("ElevenLabs unreachable"));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Preview(MockHttpRequestData.CreatePost(PreviewRequest(), "http://localhost/api/voices/profiles/preview"),
                Ctx()));

        status.ShouldBe(HttpStatusCode.InternalServerError);
    }
}

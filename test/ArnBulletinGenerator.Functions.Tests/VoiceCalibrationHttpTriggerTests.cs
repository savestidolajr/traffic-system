using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Regression tests for bug-bash 2026-08-05 finding 26: calibrating a nonexistent voice
///     used to return HTTP 500 leaking the raw provider error (incl. request_id). The
///     calibration service now preserves the synthesizer's typed InvalidInput, and the trigger
///     routes it through <c>MapTypedFailureAsync</c> so it becomes a clean sanitised 400.
/// </summary>
public sealed class VoiceCalibrationHttpTriggerTests
{
    private readonly IVoiceCalibrationService _calibrationService = Substitute.For<IVoiceCalibrationService>();
    private readonly VoiceCalibrationHttpTrigger _sut;

    public VoiceCalibrationHttpTriggerTests()
    {
        _sut = new VoiceCalibrationHttpTrigger(
            _calibrationService,
            Substitute.For<ILogger<VoiceCalibrationHttpTrigger>>());
        _calibrationService.HasValidCalibrationAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    [Fact]
    public async Task Run_InvalidVoiceTypedFailure_ReturnsSanitisedBadRequest()
    {
        _calibrationService.CalibrateVoiceAsync("zzz-nonexistent-voice", Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(Result<VoiceCalibration>.Failure(OperationalFailure.InvalidInput(
                "tts.invalid_request",
                "TTS failed: An invalid ID has been received: 'zzz-nonexistent-voice'. Make sure to provide a correct one.")));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Run(MockHttpRequestData.CreatePostEmpty(
                    "http://localhost/api/voices/zzz-nonexistent-voice/calibrate"),
                "zzz-nonexistent-voice", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        // The body is JSON and the serializer escapes ' as \u0027, so assert on the decoded
        // Error field, never the raw escaped body.
        string error = JsonDocument.Parse(body).RootElement.GetProperty("Error").GetString()!;
        error.ShouldContain("An invalid ID has been received: 'zzz-nonexistent-voice'");
        error.ShouldNotContain("request_id");
    }

    [Fact]
    public async Task Run_UntypedProviderFailure_StaysInternalError()
    {
        _calibrationService.CalibrateVoiceAsync("voice-1", Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(Result<VoiceCalibration>.Failure("ElevenLabs unreachable"));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Run(MockHttpRequestData.CreatePostEmpty(
                    "http://localhost/api/voices/voice-1/calibrate"),
                "voice-1", Ctx()));

        status.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Run_MissingVoiceId_ReturnsBadRequest()
    {
        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Run(MockHttpRequestData.CreatePostEmpty("http://localhost/api/voices//calibrate"),
                "  ", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        await _calibrationService.DidNotReceiveWithAnyArgs().CalibrateVoiceAsync(default!, default, default, default);
    }
}

using ArnBulletinGenerator.Domain.Models;
using System.Net;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Functions.Triggers.Http;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Boundary validation for the ad-hoc synthesis endpoint. Both rules here were live defects:
///     a missing VoiceId surfaced as a 500 (a client input error reported as a server fault), and
///     Script had no length limit at all, so one call could bill an arbitrarily large ElevenLabs
///     job — anonymously reachable on dev.
/// </summary>
public sealed class SynthesizeHttpTriggerTests
{
    private readonly IAudioPipelineService _audioPipeline = Substitute.For<IAudioPipelineService>();

    private readonly SynthesizeHttpTrigger _sut;

    public SynthesizeHttpTriggerTests()
    {
        _sut = new SynthesizeHttpTrigger(
            _audioPipeline,
            Substitute.For<IBroadcastOutput>(),
            Options.Create(new ElevenLabsOptions()),
            TimeProvider.System,
            NullLogger<SynthesizeHttpTrigger>.Instance);
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    [Fact]
    public async Task MissingVoiceId_Returns400_NotAServerError()
    {
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Run(
                MockHttpRequestData.CreatePost(
                    new SynthesizeRequestDto(Script: "Traffic is moving well.", VoiceId: ""),
                    "http://localhost/api/synthesize"),
                Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("VoiceId is required");
        _audioPipeline.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingScript_Returns400()
    {
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Run(
                MockHttpRequestData.CreatePost(
                    new SynthesizeRequestDto(Script: "   ", VoiceId: "voice-1"),
                    "http://localhost/api/synthesize"),
                Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Script text is required");
        _audioPipeline.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ScriptOverTheCap_Returns400_AndNeverReachesTheSynthesiser()
    {
        // A 15,200-character script was accepted on dev 2026-07-28. The synthesiser is the
        // billable step, so the assertion that matters is that it is never called.
        string oversized = new('a', ConfigValidationLimits.MaxSynthesisScriptLength + 1);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Run(
                MockHttpRequestData.CreatePost(
                    new SynthesizeRequestDto(Script: oversized, VoiceId: "voice-1"),
                    "http://localhost/api/synthesize"),
                Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain($"at most {ConfigValidationLimits.MaxSynthesisScriptLength} characters");
        _audioPipeline.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ScriptExactlyAtTheCap_IsAccepted()
    {
        // The cap must be inclusive, or a legitimate long read is refused at the boundary.
        // The pipeline is stubbed to fail so this exercises VALIDATION only: what matters is
        // that the request got past the boundary and reached the synthesiser at all.
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), lockedSuffix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Failure("stubbed: provider not available"));

        string atCap = new('a', ConfigValidationLimits.MaxSynthesisScriptLength);

        await MockHttpRequestData.ReadResponseAsync(
            await _sut.Run(
                MockHttpRequestData.CreatePost(
                    new SynthesizeRequestDto(Script: atCap, VoiceId: "voice-1"),
                    "http://localhost/api/synthesize"),
                Ctx()));

        _audioPipeline.ReceivedCalls().ShouldNotBeEmpty();
    }
}

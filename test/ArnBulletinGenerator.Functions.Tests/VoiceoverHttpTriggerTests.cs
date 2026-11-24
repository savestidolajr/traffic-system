using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     The voiceover render: arbitrary copy (an ad, a promo) on a library voice, returned as a
///     downloadable file. Two properties carry the whole design and are asserted here — the read
///     uses the profile's CURRENT version resolved server-side (never a client snapshot), and the
///     paired GET serves ONLY files this endpoint wrote, because it has no market guard of its own
///     and the broadcasts container it reads from also holds bulletin audio.
/// </summary>
public sealed class VoiceoverHttpTriggerTests
{
    private readonly IVoiceProfileService _profiles = Substitute.For<IVoiceProfileService>();
    private readonly IAudioSynthesizer _synthesizer = Substitute.For<IAudioSynthesizer>();
    private readonly IAudioProcessor _processor = Substitute.For<IAudioProcessor>();
    private readonly IBroadcastOutput _output = Substitute.For<IBroadcastOutput>();
    private readonly IAudioUrlProvider _urls = Substitute.For<IAudioUrlProvider>();
    private readonly VoiceoverHttpTrigger _sut;

    public VoiceoverHttpTriggerTests()
    {
        _sut = new VoiceoverHttpTrigger(
            _profiles, _synthesizer, _processor, _output, _urls,
            TimeProvider.System, NullLogger<VoiceoverHttpTrigger>.Instance);
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static string Url(string id = "vp-1") => $"http://localhost/api/voices/profiles/{id}/voiceover";

    /// <summary>A profile whose CURRENT version differs from its first, so "current" is provable.</summary>
    private static VoiceProfile ProfileWithTwoVersions()
    {
        var profile = VoiceProfile.Create(
            "ARN Hudson", null, "voice-hudson", "Hudson", "eleven_multilingual_v2",
            VoiceSettings.Default, null, null, DateTime.UtcNow, 0, "vp-1").Value;
        profile.ApplyEdit(
            "ARN Hudson", null, "eleven_v3", VoiceSettings.Default with { Stability = 0.42 },
            "Retuned.", null, DateTime.UtcNow);
        return profile;
    }

    private void SynthesisSucceeds()
    {
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio([1, 2, 3])));
        _output.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("saved"));
        _processor.GetDuration(Arg.Any<byte[]>()).Returns(18.4);
    }

    [Fact]
    public async Task Synthesize_UsesTheProfilesCurrentVersion_NotItsFirst()
    {
        // The whole promise of the tab is "this voice as it is configured now". A read on the
        // profile's ORIGINAL settings/model would sound like a voice nobody uses any more.
        _profiles.GetAsync("vp-1", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(ProfileWithTwoVersions()));
        SynthesisSucceeds();

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Synthesize(
                MockHttpRequestData.CreatePost(new VoiceoverRequestDto("Two for one at Bunnings this weekend."), Url()),
                "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        var call = _synthesizer.ReceivedCalls().Single();
        var args = call.GetArguments();
        args[0].ShouldBe("Two for one at Bunnings this weekend.");
        ((Option<VoiceSettings>)args[1]!).Value.Stability.ShouldBe(0.42);
        ((Option<string>)args[2]!).Value.ShouldBe("voice-hudson");
        ((Option<string>)args[3]!).Value.ShouldBe("eleven_v3");

        var payload = JsonDocument.Parse(body).RootElement;
        payload.GetProperty("WordCount").GetInt32().ShouldBe(7);
        payload.GetProperty("DurationSec").GetDouble().ShouldBe(18.4);
        // A relative, same-origin url: the download only works because the bytes come back
        // through our own origin, and the file name is what the browser will save.
        string url = payload.GetProperty("Url").GetString()!;
        url.ShouldStartWith("voices/voiceovers/voiceover_vp1_");
        url.ShouldEndWith(".wav");
        payload.GetProperty("FileName").GetString().ShouldBe(url["voices/voiceovers/".Length..]);
    }

    [Fact]
    public async Task Synthesize_EmptyScript_Returns400_AndNeverSynthesises()
    {
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Synthesize(
                MockHttpRequestData.CreatePost(new VoiceoverRequestDto("   "), Url()), "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("script is required");
        _synthesizer.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Synthesize_ScriptOverTheCap_Returns400_AndNeverSynthesises()
    {
        // The synthesiser is the billable step, so the assertion that matters is that a caller
        // cannot reach it with an arbitrarily long read.
        string oversized = new('a', ConfigValidationLimits.MaxSynthesisScriptLength + 1);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Synthesize(
                MockHttpRequestData.CreatePost(new VoiceoverRequestDto(oversized), Url()), "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain($"at most {ConfigValidationLimits.MaxSynthesisScriptLength} characters");
        _synthesizer.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Synthesize_UnknownProfile_Returns400_NamingIt()
    {
        _profiles.GetAsync("ghost", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("Voice profile 'ghost' not found."));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Synthesize(
                MockHttpRequestData.CreatePost(new VoiceoverRequestDto("Anything."), Url("ghost")), "ghost", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        JsonDocument.Parse(body).RootElement.GetProperty("Error").GetString()!
            .ShouldContain("Voice profile 'ghost' could not be resolved");
        _synthesizer.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Synthesize_ProviderRejectsTheVoice_Returns400_NotAServerError()
    {
        _profiles.GetAsync("vp-1", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(ProfileWithTwoVersions()));
        _synthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(Result<SynthesizedAudio>.Failure(OperationalFailure.InvalidInput(
                "tts.invalid_request", "TTS failed: An invalid ID has been received: 'voice-hudson'.")));

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Synthesize(
                MockHttpRequestData.CreatePost(new VoiceoverRequestDto("Anything."), Url()), "vp-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    // Bulletin audio: a job id blob, exactly what this route must never hand out — its market
    // guard lives on the job routes.
    [InlineData("018661b3e01a4c1396a7ee5df5b105ff.wav")]
    [InlineData("filler-cart.wav")]
    [InlineData("voicepreview_20260812_101500_000.wav")]
    [InlineData("../broadcasts/018661b3.wav")]
    [InlineData("voiceover_vp1_20260812_101500_000.mp3")]
    [InlineData("voiceover_vp1_2026_101500_000.wav")]
    public async Task Get_AnythingButAVoiceoverFileName_Returns400_AndNeverOpensAStream(string fileName)
    {
        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Get(
                MockHttpRequestData.CreateGet($"http://localhost/api/voices/voiceovers/{fileName}"),
                fileName, Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        _urls.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_AVoiceoverFileName_StreamsItAsAnAttachment()
    {
        // The attachment disposition IS the feature: without it the browser plays the file in a
        // media viewer instead of saving it, and the operator has nothing to hand to production.
        const string fileName = "voiceover_vp1_20260812_101500_000.wav";
        _urls.OpenAudioStreamAsync(fileName, null, Arg.Any<CancellationToken>())
            .Returns(Result<AudioStreamHandle>.Success(
                new AudioStreamHandle(new MemoryStream([1, 2, 3]), 0, 3, 3, "audio/wav")));

        var response = await _sut.Get(
            MockHttpRequestData.CreateGet($"http://localhost/api/voices/voiceovers/{fileName}"), fileName, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Content-Disposition").Single()
            .ShouldBe($"attachment; filename=\"{fileName}\"");
        response.Headers.GetValues("Accept-Ranges").Single().ShouldBe("bytes");
    }
}

using System.Net;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using ArnBulletinGenerator.Infrastructure.Audio;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public sealed class ElevenLabsAudioSynthesizerTests
{
    private const string TestVoiceId = "voice-abc";

    private static ElevenLabsOptions DefaultOptions()
    {
        return new ElevenLabsOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech",
            ModelId = "eleven_v3",
            OutputFormat = "mp3_44100_192",
            UseTimestampsEndpoint = false,
            VoicesEndpoint = "voices",
            VoiceSettings = new ElevenLabsVoiceSettings
            {
                Stability = 0.5,
                SimilarityBoost = 0.8,
                Style = 0.2,
                UseSpeakerBoost = true,
                Speed = 1.0
            }
        };
    }

    private static (ElevenLabsAudioSynthesizer Synthesizer, FakeHandler Handler) CreateSut(
        HttpStatusCode statusCode = HttpStatusCode.OK, byte[]? audioBytes = null)
    {
        var opts = DefaultOptions();
        var handler = new FakeHandler(statusCode, audioBytes ?? [0xAA, 0xBB]);
        var httpClient = new HttpClient(handler);
        var client = new ElevenLabsClient(httpClient, Options.Create(opts), NullLogger<ElevenLabsClient>.Instance);
        var synthesizer = new ElevenLabsAudioSynthesizer(client,
            new WavDurationProcessor(NullLogger<WavDurationProcessor>.Instance), Options.Create(opts),
            NullLogger<ElevenLabsAudioSynthesizer>.Instance);
        return (synthesizer, handler);
    }

    [Fact]
    public async Task SynthesizeAsync_DelegatesToClient_ReturnsAudio()
    {
        var (sut, _) = CreateSut();

        var result = await sut.SynthesizeAsync(
            "Hello traffic report",
            voiceIdOverride: Option<string>.Some(TestVoiceId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Audio.ShouldNotBeEmpty();
        result.Value.ModelId.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SynthesizeAsync_ReturnsEffectiveModelOverride()
    {
        var (sut, handler) = CreateSut();

        var result = await sut.SynthesizeAsync(
            "Hello traffic report",
            voiceIdOverride: Option<string>.Some(TestVoiceId),
            modelOverride: Option<string>.Some("eleven_multilingual_v2"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ModelId.ShouldBe("eleven_multilingual_v2");
        handler.LastRequestBody!.ShouldContain("\"model_id\":\"eleven_multilingual_v2\"");
    }

    [Fact]
    public async Task SynthesizeAsync_Fails_WhenVoiceIdNotProvided()
    {
        var (sut, _) = CreateSut();

        var result = await sut.SynthesizeAsync("text");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("VoiceId");
    }

    [Fact]
    public async Task SynthesizeAsync_PropagatesClientFailure()
    {
        var (sut, _) = CreateSut(HttpStatusCode.ServiceUnavailable);

        var result = await sut.SynthesizeAsync(
            "text",
            voiceIdOverride: Option<string>.Some(TestVoiceId));

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task SynthesizeAsync_UsesCustomVoiceSettings()
    {
        var (sut, handler) = CreateSut();
        var customSettings = VoiceSettings.Create(0.9, 0.6, 0.1, false, 0.8).Value;

        await sut.SynthesizeAsync(
            "text",
            Option<VoiceSettings>.Some(customSettings),
            Option<string>.Some(TestVoiceId));

        // v3 clamps stability 0.9 → 1.0 (discrete), ignores speed entirely (omitted), and
        // ignores similarity_boost entirely (zeroed rather than sent misleadingly); style
        // and speaker-boost are likewise v2-family-only. Only stability actually varies here.
        handler.LastRequestBody!.ShouldContain("\"similarity_boost\":0");
        handler.LastRequestBody!.ShouldNotContain("\"speed\"");
    }

    [Fact]
    public async Task SynthesizeAsync_ExpandsRouteCodesForSpeech()
    {
        var (sut, handler) = CreateSut();

        await sut.SynthesizeAsync(
            "A1 is closed at Main North Road",
            voiceIdOverride: Option<string>.Some(TestVoiceId));

        // ExpandRouteCodesForSpeech splits "A1" into "A 1" so ElevenLabs voices the
        // letter and digits as two distinct spoken tokens instead of slurring "AH-ONE".
        handler.LastRequestBody!.ShouldContain("\"text\":\"A 1 is closed at Main North Road\"");
    }

    [Fact]
    public async Task SynthesizeAsync_NoRouteCode_LeavesTextUnchanged()
    {
        var (sut, handler) = CreateSut();
        const string plainText = "Traffic is flowing smoothly on the Pacific Highway";

        await sut.SynthesizeAsync(
            plainText,
            voiceIdOverride: Option<string>.Some(TestVoiceId));

        // Ordinary prose with no route code must not be mutated.
        handler.LastRequestBody!.ShouldContain($"\"text\":\"{plainText}\"");
    }

    [Fact]
    public async Task SynthesizeAsync_SlashSeparatedRoadNames_SendsNoLiteralSlash()
    {
        var (sut, handler) = CreateSut();

        await sut.SynthesizeAsync(
            "A1 is closed between Todd Road / Cook Street",
            voiceIdOverride: Option<string>.Some(TestVoiceId));

        // Job dda2471bce5c4a69addeafbf8e475b4b: HERE's Detail field carried a slash-joined
        // road pair ("Todd Road / Cook Street") straight into the final script, and ElevenLabs
        // read the "/" aloud on air. The TTS boundary must never forward a literal slash, and
        // the unrelated route-code speakability pass ("A1" -> "A 1") must keep running on the
        // same text.
        handler.LastRequestBody.ShouldNotBeNull();
        handler.LastRequestBody!.ShouldNotContain("/");
        handler.LastRequestBody!.ShouldContain("A 1");
    }

    // Only eleven_v3 interprets inline audio tags. Every other model SPEAKS them, so a v2 voice
    // reads "[excitedly] The M1 is closed" out as "excitedly, the M1 is closed" on air. Upstream
    // gates exist per path, but this is the boundary they all converge on and the only place the
    // model actually sent is known - so the guarantee is asserted HERE, on the wire payload.
    [Theory]
    [InlineData("eleven_multilingual_v2")]
    [InlineData("eleven_turbo_v2_5")]
    [InlineData("eleven_flash_v2_5")]
    public async Task SynthesizeAsync_StripsTags_WhenModelCannotInterpretThem(string model)
    {
        var (sut, handler) = CreateSut();

        await sut.SynthesizeAsync(
            "[excitedly] The Monash is closed. [exhales sharply] Burke Road is clear.",
            voiceIdOverride: Option<string>.Some(TestVoiceId),
            modelOverride: Option<string>.Some(model));

        handler.LastRequestBody.ShouldNotBeNull();
        handler.LastRequestBody!.ShouldNotContain("excitedly");
        handler.LastRequestBody!.ShouldNotContain("exhales");
        handler.LastRequestBody!.ShouldNotContain("[");
        // The words still air - stripping a tag must never cost content.
        handler.LastRequestBody!.ShouldContain("The Monash is closed");
        handler.LastRequestBody!.ShouldContain("Burke Road is clear");
    }

    [Fact]
    public async Task SynthesizeAsync_KeepsTags_WhenModelIsV3()
    {
        var (sut, handler) = CreateSut();

        await sut.SynthesizeAsync(
            "[excitedly] The Monash is closed.",
            voiceIdOverride: Option<string>.Some(TestVoiceId),
            modelOverride: Option<string>.Some("eleven_v3"));

        handler.LastRequestBody.ShouldNotBeNull();
        handler.LastRequestBody!.ShouldContain("excitedly");
    }

    // The pipeline's context.Model can be absent (legacy/watchdog requeue), in which case the
    // CONFIGURED default is what actually gets sent. The gate must read that same effective model,
    // not the caller's intent, or a tag reaches a v2 voice through the gap.
    [Fact]
    public async Task SynthesizeAsync_StripsTags_WhenNoOverrideAndConfiguredModelIsNotV3()
    {
        var opts = DefaultOptions();
        opts.ModelId = "eleven_multilingual_v2";
        var handler = new FakeHandler(HttpStatusCode.OK, [0xAA]);
        var client = new ElevenLabsClient(new HttpClient(handler), Options.Create(opts),
            NullLogger<ElevenLabsClient>.Instance);
        var sut = new ElevenLabsAudioSynthesizer(client,
            new WavDurationProcessor(NullLogger<WavDurationProcessor>.Instance), Options.Create(opts),
            NullLogger<ElevenLabsAudioSynthesizer>.Instance);

        await sut.SynthesizeAsync(
            "[brightly] Everything is flowing.",
            voiceIdOverride: Option<string>.Some(TestVoiceId));

        handler.LastRequestBody.ShouldNotBeNull();
        handler.LastRequestBody!.ShouldNotContain("brightly");
        handler.LastRequestBody!.ShouldContain("Everything is flowing");
    }

    private sealed class FakeHandler(HttpStatusCode statusCode, byte[] audioBytes) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(audioBytes) };
        }
    }
}

using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ArnBulletinGenerator.Infrastructure.Tests.WireMock;

/// <summary>
///     WireMock integration tests for the ElevenLabs TTS pipeline.
///     Exercises the real HTTP client for voice listing and audio synthesis
///     with a stubbed ElevenLabs API.
/// </summary>
public sealed class TtsWireMockTests : WireMockTestBase
{
    // 1 second of 16-bit 24000Hz mono PCM = 48000 bytes of silence
    private static readonly byte[] PcmSilence = new byte[48000];
    private static readonly string PcmSilenceBase64 = Convert.ToBase64String(PcmSilence);

    private static readonly string VoicesResponseJson = /* language=json */ """
    {
      "voices": [
        {
          "voice_id": "21m00Tcm4TlvDq8ikWAM",
          "name": "Rachel",
          "category": "premade",
          "description": "Australian radio voice",
          "labels": { "accent": "australian", "gender": "female", "description": "warm", "age": "young" },
          "high_quality_base_model_ids": ["eleven_multilingual_v2", "eleven_turbo_v2_5"]
        },
        {
          "voice_id": "AZnzlk1XvdvUeBnXmlld",
          "name": "Callum",
          "category": "premade",
          "description": "Australian male voice",
          "labels": { "accent": "australian", "gender": "male", "description": "deep", "age": "middle_aged" },
          "high_quality_base_model_ids": ["eleven_multilingual_v2", "eleven_turbo_v2_5"]
        }
      ]
    }
    """;

    private static readonly string TimestampsResponseJson = $$"""
    {
      "audio_base64": "{{PcmSilenceBase64}}",
      "alignment": {
        "characters": ["T", "e", "s", "t"],
        "character_start_times_seconds": [0.0, 0.1, 0.2, 0.3],
        "character_end_times_seconds": [0.1, 0.2, 0.3, 1.0]
      },
      "normalized_alignment": {
        "characters": ["T", "e", "s", "t"],
        "character_start_times_seconds": [0.0, 0.1, 0.2, 0.3],
        "character_end_times_seconds": [0.1, 0.2, 0.3, 1.0]
      }
    }
    """;

    private const string VoiceId = "21m00Tcm4TlvDq8ikWAM";

    protected override Task OnInitializedAsync()
    {
        // Stub: voices listing GET
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher("/v1/voices"))
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(VoicesResponseJson));

        // Stub: TTS with timestamps POST
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher($"/v1/text-to-speech/{VoiceId}/with-timestamps"))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(TimestampsResponseJson));

        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetVoicesAsync_ReturnsVoices()
    {
        var voiceProvider = Services.GetRequiredService<IVoiceProvider>();

        var result = await voiceProvider.GetVoicesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task GetVoicesAsync_IncludesAustralianVoice()
    {
        var voiceProvider = Services.GetRequiredService<IVoiceProvider>();

        var result = await voiceProvider.GetVoicesAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, v =>
            v.Name.Contains("Rachel", StringComparison.OrdinalIgnoreCase) ||
            v.Name.Contains("Callum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SynthesizeAsync_ReturnsAudioBytes()
    {
        var synthesizer = Services.GetRequiredService<IAudioSynthesizer>();

        var result = await synthesizer.SynthesizeAsync(
            "Good morning Sydney, traffic update.",
            voiceIdOverride: Option<string>.Some(VoiceId),
            modelOverride: Option<string>.Some("eleven_v3"),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, $"Expected success but got: {result.Error}");
        Assert.NotNull(result.Value);
        Assert.NotEmpty(result.Value.Audio);
    }

    [Fact]
    public async Task SynthesizeAsync_ReturnsFailure_WhenApiReturns401()
    {
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher($"/v1/text-to-speech/{VoiceId}/with-timestamps"))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(401)
                .WithBody("""{"detail": "Invalid API key"}"""));

        var synthesizer = Services.GetRequiredService<IAudioSynthesizer>();

        var result = await synthesizer.SynthesizeAsync(
            "Test text",
            voiceIdOverride: Option<string>.Some(VoiceId),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsFailure);
        // The raw provider body (incl. request_id) must never surface to a client — only the
        // human-readable detail.message does (finding 26 sanitisation). The old assertion on
        // "Unauthorized" (the interpolated HttpStatusCode name from the pre-sanitisation raw
        // message) encoded exactly the leak the fix removes.
        Assert.Contains("Invalid API key", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("request_id", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"detail\"", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SynthesizeAsync_ReturnsFailure_WhenResponseHasNoAudio()
    {
        var badResponse = /* language=json */ """
        {
          "audio_base64": "",
          "alignment": null,
          "normalized_alignment": null
        }
        """;

        Server.Given(Request.Create()
                .WithPath(new RegexMatcher($"/v1/text-to-speech/{VoiceId}/with-timestamps"))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(badResponse));

        var synthesizer = Services.GetRequiredService<IAudioSynthesizer>();

        var result = await synthesizer.SynthesizeAsync(
            "Test text",
            voiceIdOverride: Option<string>.Some(VoiceId),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SynthesizeAsync_SurvivesMalformedJson()
    {
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher($"/v1/text-to-speech/{VoiceId}/with-timestamps"))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("not valid json {{{"));

        var synthesizer = Services.GetRequiredService<IAudioSynthesizer>();

        var result = await synthesizer.SynthesizeAsync(
            "Test text",
            voiceIdOverride: Option<string>.Some(VoiceId),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // ── Contract Tests ────────────────────────────────────────────────

    [Fact]
    public void VoicesResponse_HasRequiredFields()
    {
        using var doc = JsonDocument.Parse(VoicesResponseJson);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("voices", out var voices), "Missing 'voices'");
        Assert.True(voices.ValueKind == JsonValueKind.Array, "'voices' should be an array");

        foreach (var voice in voices.EnumerateArray())
        {
            // The parser reads voice_id, name, category, labels, high_quality_base_model_ids
            Assert.True(voice.TryGetProperty("voice_id", out var vid), "Missing voice.voice_id");
            Assert.True(vid.ValueKind == JsonValueKind.String, "voice.voice_id should be string");
            Assert.False(string.IsNullOrWhiteSpace(vid.GetString()), "voice.voice_id should not be empty");

            Assert.True(voice.TryGetProperty("name", out var name), "Missing voice.name");
            Assert.True(name.ValueKind == JsonValueKind.String, "voice.name should be string");
        }
    }

    [Fact]
    public void TimestampsResponse_HasRequiredFields()
    {
        using var doc = JsonDocument.Parse(TimestampsResponseJson);
        var root = doc.RootElement;

        // ParseTimestampsResponse reads audio_base64, alignment (fallback), normalized_alignment (preferred)
        Assert.True(root.TryGetProperty("audio_base64", out var audioB64), "Missing 'audio_base64'");
        Assert.True(audioB64.ValueKind == JsonValueKind.String, "'audio_base64' should be string");
        Assert.False(string.IsNullOrWhiteSpace(audioB64.GetString()), "'audio_base64' should be non-empty");

        // normalized_alignment is preferred; alignment is fallback
        Assert.True(root.TryGetProperty("normalized_alignment", out var normAlign),
            "Missing 'normalized_alignment'");
        Assert.True(normAlign.ValueKind == JsonValueKind.Object, "'normalized_alignment' should be object");

        // The parser reads character_end_times_seconds for duration
        Assert.True(normAlign.TryGetProperty("character_end_times_seconds", out var endTimes),
            "Missing normalized_alignment.character_end_times_seconds");
        Assert.True(endTimes.ValueKind == JsonValueKind.Array, "character_end_times_seconds should be array");
        Assert.NotEmpty(endTimes.EnumerateArray());

        // The parser uses the last element as the duration
        var lastTime = endTimes.EnumerateArray().Last();
        Assert.True(lastTime.ValueKind == JsonValueKind.Number, "end times should be numbers");
    }

    [Fact]
    public void TimestampsResponse_HasAlignmentFallback()
    {
        using var doc = JsonDocument.Parse(TimestampsResponseJson);
        var root = doc.RootElement;

        // ParseTimestampsResponse falls back to alignment when normalized_alignment is null
        Assert.True(root.TryGetProperty("alignment", out var alignment),
            "Missing 'alignment'");
        Assert.True(alignment.ValueKind == JsonValueKind.Object, "'alignment' should be object");
        Assert.True(alignment.TryGetProperty("characters", out _), "Missing alignment.characters");
        Assert.True(alignment.TryGetProperty("character_start_times_seconds", out _),
            "Missing alignment.character_start_times_seconds");
        Assert.True(alignment.TryGetProperty("character_end_times_seconds", out _),
            "Missing alignment.character_end_times_seconds");
    }

    [Fact]
    public async Task SynthesizeAsync_Request_IncludesRequiredFields()
    {
        var synthesizer = Services.GetRequiredService<IAudioSynthesizer>();

        var result = await synthesizer.SynthesizeAsync(
            "Good morning Sydney, traffic update.",
            voiceIdOverride: Option<string>.Some(VoiceId),
            modelOverride: Option<string>.Some("eleven_multilingual_v2"),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);

        var requestBody = LastRequestBody();
        using var doc = JsonDocument.Parse(requestBody);
        var root = doc.RootElement;

        // Required ElevenLabs TTS request fields
        Assert.True(root.TryGetProperty("text", out var text), "Missing 'text'");
        Assert.False(string.IsNullOrWhiteSpace(text.GetString()), "'text' should be non-empty");

        Assert.True(root.TryGetProperty("model_id", out var modelId), "Missing 'model_id'");
        Assert.False(string.IsNullOrWhiteSpace(modelId.GetString()), "'model_id' should be non-empty");

        Assert.True(root.TryGetProperty("voice_settings", out var vs), "Missing 'voice_settings'");
        Assert.True(vs.TryGetProperty("stability", out _), "Missing voice_settings.stability");
        Assert.True(vs.TryGetProperty("similarity_boost", out _), "Missing voice_settings.similarity_boost");

        // ElevenLabs honors `speed` ONLY inside voice_settings — a top-level
        // `speed` is silently ignored (verified empirically). For a speed-
        // supporting model (v2-family) it must live in voice_settings.
        Assert.False(root.TryGetProperty("speed", out _), "'speed' must NOT be top-level");
        Assert.True(vs.TryGetProperty("speed", out var speed), "Missing voice_settings.speed");
        Assert.True(speed.ValueKind == JsonValueKind.Number, "'speed' should be a number");
    }

    [Fact]
    public async Task SynthesizeAsync_Request_SendsApiKeyHeader()
    {
        var synthesizer = Services.GetRequiredService<IAudioSynthesizer>();

        var result = await synthesizer.SynthesizeAsync(
            "Test", voiceIdOverride: Option<string>.Some(VoiceId),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);

        var headers = LastRequest().Headers;
        Assert.NotNull(headers);
        Assert.Contains(headers, h =>
            h.Key.Equals("xi-api-key", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SynthesizeAsync_Request_UsesCorrectUrl()
    {
        var synthesizer = Services.GetRequiredService<IAudioSynthesizer>();

        var result = await synthesizer.SynthesizeAsync(
            "Test", voiceIdOverride: Option<string>.Some(VoiceId),
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);

        var url = LastRequest().Url;
        Assert.Contains(VoiceId, url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("with-timestamps", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("output_format", url, StringComparison.OrdinalIgnoreCase);
    }
}

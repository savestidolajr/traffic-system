using System.Net;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public sealed class ElevenLabsClientTests
{
    private const string TestApiKey = "test-api-key";
    private const string TestVoiceId = "voice-123";

    private static ElevenLabsOptions DefaultOptions(string? apiKey = TestApiKey)
    {
        return new ElevenLabsOptions
        {
            ApiKey = apiKey ?? string.Empty,
            BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech",
            ModelId = "eleven_v3",
            OutputFormat = "pcm_24000",
            UseTimestampsEndpoint = false,
            VoicesEndpoint = "voices"
        };
    }

    private static ElevenLabsClient CreateClient(HttpClient httpClient, ElevenLabsOptions? opts = null)
    {
        return new ElevenLabsClient(httpClient, Options.Create(opts ?? DefaultOptions()),
            NullLogger<ElevenLabsClient>.Instance);
    }

    private static HttpClient CreateHttpClient(HttpStatusCode statusCode, string content, byte[]? bytes = null)
    {
        var handler = new FakeHandler(statusCode, content, bytes);
        return new HttpClient(handler);
    }

    // ── GetVoicesAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetVoicesAsync_ReturnsVoices_OnSuccess()
    {
        string json = JsonSerializer.Serialize(new
        {
            voices = new[]
            {
                new
                {
                    voice_id = "v1", name = "Alice", category = (string?)"premade", description = (string?)"Warm voice",
                    labels = (Dictionary<string, string>?)new Dictionary<string, string> { ["accent"] = "australian" },
                    high_quality_base_model_ids = new[] { "eleven_v3" }
                },
                new
                {
                    voice_id = "v2", name = "Bob", category = (string?)null, description = (string?)null,
                    labels = (Dictionary<string, string>?)null, high_quality_base_model_ids = Array.Empty<string>()
                }
            }
        });
        using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
        var client = CreateClient(httpClient);

        var result = await client.GetVoicesAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(2);
        result.Value[0].VoiceId.ShouldBe("v1");
        result.Value[0].Name.ShouldBe("Alice");
        result.Value[1].VoiceId.ShouldBe("v2");
    }
    [Fact]
    public async Task GetVoicesAsync_ComputesCanRemixFromSharingStatus()
    {
        // Cover all sharing permutations in one payload:
        //   null sharing → true, "enabled" → true, "disabled" → true,
        //   "copied" (with 730-day notice) → false, "copied_disabled" → false.
        string json = JsonSerializer.Serialize(new
        {
            voices = new object[]
            {
                new
                {
                    voice_id = "owned-1", name = "Owned", category = (string?)"premade",
                    description = (string?)"No sharing record",
                    labels = (Dictionary<string, string>?)null,
                    high_quality_base_model_ids = new[] { "eleven_v3" }
                    // no "sharing" key at all
                },
                new
                {
                    voice_id = "self-shared", name = "SelfShared", category = (string?)"premade",
                    description = (string?)"Self-shared",
                    labels = (Dictionary<string, string>?)null,
                    high_quality_base_model_ids = Array.Empty<string>(),
                    sharing = new { status = "enabled" }
                },
                new
                {
                    voice_id = "disabled-share", name = "DisabledShare", category = (string?)"premade",
                    description = (string?)"Disabled sharing",
                    labels = (Dictionary<string, string>?)null,
                    high_quality_base_model_ids = Array.Empty<string>(),
                    sharing = new { status = "disabled" }
                },
                new
                {
                    voice_id = "copied-1", name = "Copied", category = (string?)"professional",
                    description = (string?)"Copied Voice Library voice",
                    labels = (Dictionary<string, string>?)null,
                    high_quality_base_model_ids = Array.Empty<string>(),
                    sharing = new { status = "copied", notice_period = 730 }
                },
                new
                {
                    voice_id = "copied-disabled", name = "CopiedDisabled", category = (string?)"professional",
                    description = (string?)"Copied then disabled",
                    labels = (Dictionary<string, string>?)null,
                    high_quality_base_model_ids = Array.Empty<string>(),
                    sharing = new { status = "copied_disabled" }
                }
            }
        });
        using var httpClient = CreateHttpClient(HttpStatusCode.OK, json);
        var client = CreateClient(httpClient);

        var result = await client.GetVoicesAsync();

        result.IsSuccess.ShouldBeTrue();
        var voices = result.Value.ToArray();
        voices.Length.ShouldBe(5);

        // No sharing key → owned → remixable
        voices.First(v => v.VoiceId == "owned-1").CanRemix.ShouldBeTrue();
        // sharing.status = "enabled" → self-shared owner → remixable
        voices.First(v => v.VoiceId == "self-shared").CanRemix.ShouldBeTrue();
        // sharing.status = "disabled" → self-shared owner → remixable
        voices.First(v => v.VoiceId == "disabled-share").CanRemix.ShouldBeTrue();
        // sharing.status = "copied" (even with 730-day notice) → NOT remixable
        voices.First(v => v.VoiceId == "copied-1").CanRemix.ShouldBeFalse();
        // sharing.status = "copied_disabled" → NOT remixable
        voices.First(v => v.VoiceId == "copied-disabled").CanRemix.ShouldBeFalse();
    }

    [Fact]
    public async Task GetVoicesAsync_Fails_WhenApiKeyMissing()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.OK, "{}");
        var client = CreateClient(httpClient, DefaultOptions(""));

        var result = await client.GetVoicesAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("API key");
    }

    [Fact]
    public async Task GetVoicesAsync_Fails_OnHttpError()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.Unauthorized, "invalid key");
        var client = CreateClient(httpClient);

        var result = await client.GetVoicesAsync();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Unauthorized");
    }

    [Fact]
    public async Task GetVoicesAsync_SetsApiKeyHeader()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"voices":[]}""");
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.GetVoicesAsync();

        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest!.Headers.GetValues("xi-api-key").ShouldHaveSingleItem().ShouldBe(TestApiKey);
    }

    // ── GenerateAudioAsync ──────────────────────────────────────────

    [Fact]
    public async Task GenerateAudioAsync_ReturnsAudio_OnSuccess()
    {
        byte[] audioBytes = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 };
        var handler = new FakeHandler(HttpStatusCode.OK, string.Empty, audioBytes);
        using var httpClient = new HttpClient(handler);
        var opts = DefaultOptions();
        opts.UseTimestampsEndpoint = false;
        opts.OutputFormat = "mp3_44100_192";
        var client = CreateClient(httpClient, opts);

        var result = await client.GenerateAudioAsync("Hello world", 1.0, 0.5, 0.8, 0.2, true, TestVoiceId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AudioData.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GenerateAudioAsync_Fails_WhenApiKeyMissing()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.OK, "ok");
        var client = CreateClient(httpClient, DefaultOptions(""));

        var result = await client.GenerateAudioAsync("text", 1.0, 0.5, 0.8, 0.2, true, TestVoiceId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("API key");
    }

    [Fact]
    public async Task GenerateAudioAsync_Fails_WhenVoiceIdEmpty()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.OK, "ok");
        var client = CreateClient(httpClient);

        var result = await client.GenerateAudioAsync("text", 1.0, 0.5, 0.8, 0.2, true, "");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("VoiceId");
    }

    [Fact]
    public async Task GenerateAudioAsync_Fails_OnHttpError()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.InternalServerError, "server error");
        var client = CreateClient(httpClient);

        var result = await client.GenerateAudioAsync("text", 1.0, 0.5, 0.8, 0.2, true, TestVoiceId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("InternalServerError");
    }

    [Fact]
    public async Task GenerateAudioAsync_BadRequest_SanitizesAndTypesInvalidInput()
    {
        // Finding 26: the raw provider body (incl. request_id) must be logged but never
        // returned to a client. A 4xx is operator-correctable input — the failure is typed
        // InvalidInput carrying the human-readable detail.message, so the HTTP edge can 400.
        string rawBody = """
            {"detail":{"type":"invalid_request","code":"bad_request","message":"An invalid ID has been received: 'zzz-nonexistent'. Make sure to provide a correct one.","status":"invalid_uid","request_id":"464acad2e927a148556ab5e0f8d45b7a"}}
            """;
        using var httpClient = CreateHttpClient(HttpStatusCode.BadRequest, rawBody);
        var client = CreateClient(httpClient);

        var result = await client.GenerateAudioAsync("text", 1.0, 0.5, 0.8, 0.2, true, TestVoiceId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("An invalid ID has been received: 'zzz-nonexistent'");
        result.Error.ShouldNotContain("request_id");
        result.Error.ShouldNotContain("464acad2e927a148556ab5e0f8d45b7a");
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.InvalidInput);
    }

    [Fact]
    public async Task GenerateAudioAsync_ServerError_IsTypedRetryable()
    {
        using var httpClient = CreateHttpClient(HttpStatusCode.BadGateway, "upstream exploded");
        var client = CreateClient(httpClient);

        var result = await client.GenerateAudioAsync("text", 1.0, 0.5, 0.8, 0.2, true, TestVoiceId);

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Retryable);
        result.Error.ShouldNotContain("upstream exploded");
    }

    [Fact]
    public async Task GenerateAudioAsync_ClampsSpeed()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, string.Empty, [0x01, 0x02]);
        using var httpClient = new HttpClient(handler);
        var opts = DefaultOptions();
        opts.OutputFormat = "mp3_44100_192";
        var client = CreateClient(httpClient, opts);

        // Speed 2.0 should be clamped to 1.2
        var result = await client.GenerateAudioAsync("text", 2.0, 0.5, 0.8, 0.2, true, TestVoiceId);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task GenerateAudioAsync_UsesModelOverride()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, string.Empty, [0x01, 0x02]);
        using var httpClient = new HttpClient(handler);
        var opts = DefaultOptions();
        opts.OutputFormat = "mp3_44100_192";
        var client = CreateClient(httpClient, opts);

        await client.GenerateAudioAsync("text", 1.0, 0.5, 0.8, 0.2, true, TestVoiceId, "eleven_turbo_v2_5");

        string? body = handler.LastRequestBody;
        body!.ShouldContain("eleven_turbo_v2_5");
    }

    [Fact]
    public async Task GenerateAudioAsync_PcmFormat_WrapsInWav()
    {
        // 4 bytes of PCM data at 24000 Hz
        byte[] pcmData = new byte[] { 0x00, 0x01, 0x00, 0x02 };
        var handler = new FakeHandler(HttpStatusCode.OK, string.Empty, pcmData);
        using var httpClient = new HttpClient(handler);
        var opts = DefaultOptions();
        opts.OutputFormat = "pcm_24000";
        opts.UseTimestampsEndpoint = false;
        var client = CreateClient(httpClient, opts);

        var result = await client.GenerateAudioAsync("test", 1.0, 0.5, 0.8, 0.2, true, TestVoiceId);

        result.IsSuccess.ShouldBeTrue();
        // WAV header is 44 bytes + 4 bytes PCM = 48 bytes
        result.Value.AudioData.Length.ShouldBe(44 + pcmData.Length);
        // RIFF header
        Encoding.ASCII.GetString(result.Value.AudioData, 0, 4).ShouldBe("RIFF");
    }

    // ── FakeHandler ─────────────────────────────────────────────────

    private sealed class FakeHandler(HttpStatusCode statusCode, string content, byte[]? bytes = null)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(statusCode);
            response.Content = bytes is not null
                ? new ByteArrayContent(bytes)
                : new StringContent(content, Encoding.UTF8, "application/json");
            return response;
        }
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

/// <summary>
///     The wire contract for pronunciation dictionaries: which locators reach ElevenLabs, and in
///     which ORDER. Order is precedence — the provider applies locators sequentially and the first
///     to match a word wins it, verified live 2026-07-31 — so a reordering here is a silent
///     production bug where every station-local respelling stops taking effect.
/// </summary>
public sealed class ElevenLabsPronunciationDictionaryTests
{
    private const string TestApiKey = "test-api-key";
    private const string TestVoiceId = "voice-123";
    private const string StationDictId = "station-dict";
    private const string StationVerId = "station-ver";
    private const string GlobalDictId = "global-dict";
    private const string GlobalVerId = "global-ver";

    private static readonly PronunciationLocator Station = new(StationDictId, StationVerId);
    private static readonly PronunciationLocator Global = new(GlobalDictId, GlobalVerId);

    private static ElevenLabsOptions CreateOptions() => new()
    {
        ApiKey = TestApiKey,
        BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech",
        ModelId = "eleven_turbo_v2_5",
        OutputFormat = "mp3_44100_192",
        UseTimestampsEndpoint = false,
        VoicesEndpoint = "voices"
    };

    private static ElevenLabsClient CreateClient(ElevenLabsOptions opts, HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(opts), NullLogger<ElevenLabsClient>.Instance);

    private static async Task<JsonElement> PayloadFor(PronunciationStack? stack)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, string.Empty, [0x01, 0x02]);
        var client = CreateClient(CreateOptions(), handler);

        var result = await client.GenerateAudioAsync(
            "test", 1.0, 0.5, 0.8, 0.2, true, TestVoiceId, market: "sydney",
            pronunciationStack: stack);
        result.IsSuccess.ShouldBeTrue();

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        return doc.RootElement.Clone();
    }

    private static (string DictionaryId, string VersionId)[] LocatorsIn(JsonElement payload)
    {
        payload.TryGetProperty("pronunciation_dictionary_locators", out var locators).ShouldBeTrue();
        return [.. locators.EnumerateArray().Select(l => (
            l.GetProperty("pronunciation_dictionary_id").GetString()!,
            l.GetProperty("version_id").GetString()!))];
    }

    [Fact]
    public async Task NullStack_OmitsLocatorsKeyEntirely()
    {
        var payload = await PayloadFor(null);

        payload.TryGetProperty("pronunciation_dictionary_locators", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task EmptyStack_OmitsLocatorsKeyEntirely()
    {
        // An absent key, not an empty array: the provider rejects an empty locator list.
        var payload = await PayloadFor(PronunciationStack.Empty);

        payload.TryGetProperty("pronunciation_dictionary_locators", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task StationOnly_AttachesJustThatLocator()
    {
        var payload = await PayloadFor(PronunciationStack.Of(Station, null));

        LocatorsIn(payload).ShouldBe([(StationDictId, StationVerId)]);
    }

    [Fact]
    public async Task GlobalOnly_AttachesJustThatLocator()
    {
        // A station with no dictionary of its own still reads with the platform-wide layer.
        var payload = await PayloadFor(PronunciationStack.Of(null, Global));

        LocatorsIn(payload).ShouldBe([(GlobalDictId, GlobalVerId)]);
    }

    [Fact]
    public async Task BothLayers_EmitStationBeforeGlobal()
    {
        // THE load-bearing assertion. Reversed, the platform-wide respelling wins every colliding
        // word and no station override can ever take effect.
        var payload = await PayloadFor(PronunciationStack.Of(Station, Global));

        LocatorsIn(payload).ShouldBe([(StationDictId, StationVerId), (GlobalDictId, GlobalVerId)]);
    }

    [Fact]
    public async Task BothLayers_NeverExceedsTheProviderCap()
    {
        var payload = await PayloadFor(PronunciationStack.Of(Station, Global));

        LocatorsIn(payload).Length.ShouldBeLessThanOrEqualTo(PronunciationStack.MaxLocators);
    }

    // ── FakeHandler ─────────────────────────────────────────────────

    private sealed class FakeHandler(HttpStatusCode statusCode, string content, byte[]? bytes = null)
        : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = bytes is not null
                    ? new ByteArrayContent(bytes)
                    : new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace ArnBulletinGenerator.Infrastructure.Tests.WireMock;

/// <summary>
///     WireMock integration tests for pronunciation dictionary locators, driven through the
///     DI-composed <see cref="IAudioSynthesizer" /> so the whole Infrastructure chain is exercised,
///     not just the payload builder.
///     <para>
///         The resolved <see cref="PronunciationStack" /> is the ONLY source of locators; the PoC's
///         redeploy-only market-config path was removed with the platform-wide layer. What these
///         tests pin is the wire ORDER: the provider applies locators sequentially and the first to
///         match a word wins it (verified live 2026-07-31), so the station layer must land first.
///     </para>
/// </summary>
public sealed class PronunciationDictionaryWireMockTests : WireMockTestBase
{
    // 1 second of 16-bit 24000Hz mono PCM = 48000 bytes of silence
    private static readonly byte[] PcmSilence = new byte[48000];
    private static readonly string PcmSilenceBase64 = Convert.ToBase64String(PcmSilence);

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
    private const string StationDictId = "sydney-dict-002";
    private const string StationVerId = "sydney-ver-002";
    private const string GlobalDictId = "platform-dict-001";
    private const string GlobalVerId = "platform-ver-001";

    private static readonly PronunciationLocator StationLocator = new(StationDictId, StationVerId);
    private static readonly PronunciationLocator GlobalLocator = new(GlobalDictId, GlobalVerId);

    protected override Task OnInitializedAsync()
    {
        // Stub: TTS with timestamps POST (match any voice ID)
        Server.Given(Request.Create()
                .WithPath(new RegexMatcher("/v1/text-to-speech/.+/with-timestamps"))
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(TimestampsResponseJson));

        return Task.CompletedTask;
    }

    private async Task<JsonElement> SynthesizeAndCapture(PronunciationStack? stack)
    {
        var synthesizer = Services.GetRequiredService<IAudioSynthesizer>();

        var result = await synthesizer.SynthesizeAsync(
            "Good morning Sydney.",
            voiceIdOverride: Option<string>.Some(VoiceId),
            modelOverride: Option<string>.Some("eleven_turbo_v2_5"),
            cancellationToken: CancellationToken.None,
            market: Option<string>.Some("sydney"),
            pronunciationStack: stack);

        Assert.True(result.IsSuccess);

        using var doc = JsonDocument.Parse(LastRequestBody());
        return doc.RootElement.Clone();
    }

    private static void AssertLocatorAt(
        JsonElement locators, int index, string expectedDictId, string expectedVerId)
    {
        var locator = locators[index];
        Assert.True(locator.TryGetProperty("pronunciation_dictionary_id", out var dictId));
        Assert.Equal(expectedDictId, dictId.GetString());
        Assert.True(locator.TryGetProperty("version_id", out var verId));
        Assert.Equal(expectedVerId, verId.GetString());
    }

    [Fact]
    public async Task SynthesizeAsync_StationAndGlobal_SendsStationFirst()
    {
        var root = await SynthesizeAndCapture(PronunciationStack.Of(StationLocator, GlobalLocator));

        Assert.True(root.TryGetProperty("pronunciation_dictionary_locators", out var locators),
            "Missing 'pronunciation_dictionary_locators'");
        Assert.Equal(JsonValueKind.Array, locators.ValueKind);
        Assert.Equal(2, locators.GetArrayLength());

        AssertLocatorAt(locators, 0, StationDictId, StationVerId);
        AssertLocatorAt(locators, 1, GlobalDictId, GlobalVerId);
    }

    [Fact]
    public async Task SynthesizeAsync_StationOnly_SendsOneLocator()
    {
        var root = await SynthesizeAndCapture(PronunciationStack.Of(StationLocator, null));

        Assert.True(root.TryGetProperty("pronunciation_dictionary_locators", out var locators));
        Assert.Equal(1, locators.GetArrayLength());
        AssertLocatorAt(locators, 0, StationDictId, StationVerId);
    }

    [Fact]
    public async Task SynthesizeAsync_GlobalOnly_StillAttachesThePlatformLayer()
    {
        // A station with no dictionary of its own now reads with the platform-wide layer.
        var root = await SynthesizeAndCapture(PronunciationStack.Of(null, GlobalLocator));

        Assert.True(root.TryGetProperty("pronunciation_dictionary_locators", out var locators));
        Assert.Equal(1, locators.GetArrayLength());
        AssertLocatorAt(locators, 0, GlobalDictId, GlobalVerId);
    }

    [Fact]
    public async Task SynthesizeAsync_NoStack_OmitsLocatorsEntirely()
    {
        var root = await SynthesizeAndCapture(null);

        Assert.False(root.TryGetProperty("pronunciation_dictionary_locators", out _));
    }
}

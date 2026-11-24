using System.Net.Http.Json;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     Live verification of the region-based pronunciation-dictionary feature against the
///     REAL ElevenLabs API — create a real dictionary, attach it via the per-market locator
///     config, and verify synthesis succeeds.
///     Run manually with the ELEVENLABS_API_KEY environment variable set.
/// </summary>
public sealed class AudioFeaturesLiveVerificationTests(ITestOutputHelper output)
{
    private const string SkipReason =
        "Live verification: requires a real ElevenLabs API key; run manually with ELEVENLABS_API_KEY set.";

    private const string TestVoiceId = "CwhRBWXzGAHq8TQ4Fs17"; // "Roger" — used in sandbox validation

    [Fact(Skip = SkipReason)]
    public async Task PronunciationDictionary_PerMarketLocator_AcceptedByRealApi()
    {
        string apiKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY")
                        ?? throw new InvalidOperationException("ELEVENLABS_API_KEY not set");

        using var http = new HttpClient { BaseAddress = new Uri("https://api.elevenlabs.io/") };
        http.DefaultRequestHeaders.Add("xi-api-key", apiKey);

        // 1. Create a real dictionary with one alias rule.
        var createResponse = await http.PostAsJsonAsync("v1/pronunciation-dictionaries/add-from-rules", new
        {
            name = $"arn-live-verify-{DateTime.UtcNow:yyyyMMddHHmmss}",
            rules = new[]
            {
                new { @string_to_replace = "Bourke", type = "alias", alias = "Burk" }
            }
        });
        string createBody = await createResponse.Content.ReadAsStringAsync();
        createResponse.IsSuccessStatusCode.ShouldBeTrue($"Dictionary creation failed: {createBody}");

        using var doc = JsonDocument.Parse(createBody);
        string dictionaryId = doc.RootElement.GetProperty("id").GetString()!;
        string versionId = doc.RootElement.GetProperty("version_id").GetString()!;
        output.WriteLine($"Created dictionary {dictionaryId} v{versionId}");

        try
        {
            // 2. Synthesize with the resolved locator on a dictionary-supporting model. There is no
            // config-driven dictionary any more: the stack is handed to the synthesizer directly,
            // exactly as the poller does.
            await using var sp = TestConfigurationHelper.BuildRealServiceProvider(
                new Dictionary<string, string?>(), LogLevel.Information);

            var synthesizer = sp.GetRequiredService<IAudioSynthesizer>();
            var stack = PronunciationStack.Of(new PronunciationLocator(dictionaryId, versionId), null);

            var turboResult = await synthesizer.SynthesizeAsync(
                "Delays on Bourke Street in Melbourne.",
                voiceIdOverride: Option<string>.Some(TestVoiceId),
                modelOverride: Option<string>.Some("eleven_turbo_v2_5"),
                market: Option<string>.Some("melbourne"),
                pronunciationStack: stack);
            turboResult.IsSuccess.ShouldBeTrue($"Turbo synthesis with dictionary failed: {turboResult.Error}");
            output.WriteLine($"eleven_turbo_v2_5 + resolved dictionary: OK ({turboResult.Value.Audio.Length} bytes)");

            // 3. Informational: observe v3-family behavior with locators attached.
            var v3Result = await synthesizer.SynthesizeAsync(
                "Delays on Bourke Street in Melbourne.",
                voiceIdOverride: Option<string>.Some(TestVoiceId),
                modelOverride: Option<string>.Some("eleven_v3"),
                market: Option<string>.Some("melbourne"),
                pronunciationStack: stack);
            output.WriteLine(v3Result.IsSuccess
                ? $"eleven_v3 + dictionary locator: accepted ({v3Result.Value.Audio.Length} bytes)"
                : $"eleven_v3 + dictionary locator: REJECTED — {v3Result.Error}");
        }
        finally
        {
            // 4. Best-effort cleanup.
            var delete = await http.DeleteAsync($"v1/pronunciation-dictionaries/{dictionaryId}");
            output.WriteLine($"Dictionary cleanup: {(int)delete.StatusCode} {delete.StatusCode}");
        }
    }
}

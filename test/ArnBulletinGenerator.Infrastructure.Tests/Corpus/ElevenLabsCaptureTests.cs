using System.Text.Json;
using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Operator-run acquisition of a real ElevenLabs audio corpus under <c>test/corpus/elevenlabs</c>.
///     Synthesizes a fixed set of representative bulletins via the production client and persists each as
///     a playable WAV plus metadata carrying the real, alignment-based duration. This gives measured
///     durations (replacing the word-rate estimate) and the audio needed for future pronunciation scoring.
///     <para>
///         Excluded from CI (<c>Category=Integration</c>); needs an ElevenLabs key. Run with:
///         <c>ARN_ELEVEN_CAPTURE=1 ELEVENLABS_API_KEY=… dotnet test \
///         --filter "FullyQualifiedName~ElevenLabsCapture"</c>.
///     </para>
/// </summary>
public sealed class ElevenLabsCaptureTests(ITestOutputHelper output)
{
    private const string DefaultVoiceId = "CwhRBWXzGAHq8TQ4Fs17"; // Roger — local-dev default voice.

    // Fixed, representative AU traffic bulletins using real road names — stable inputs for a stable corpus.
    private static readonly (string Slug, string Script)[] Samples =
    [
        ("sydney-peak",
            "Big one to start — the Warringah Freeway's closed and it's copping heavy delays right through the " +
            "approaches. If you're coming off there, Church Street is crawling with congestion, backed up and slow. " +
            "Walker Street's also shut but only a minor hold-up. Further out, Sommerville Road has a cluster of " +
            "incidents and queues about half a kilometre back, so expect stop-start through that stretch."),
        ("melbourne-peak",
            "Heads up, the biggest problem's the M80 — a southbound closure with a queue around seven hundred metres " +
            "back, so heavy delays there. The Ring Road's crawling too, short queues building through the usual spots. " +
            "If you're on the Princes Freeway it's slow going with congestion the whole way. And the Mordialloc " +
            "Freeway is tied up with a breakdown, so a rough run across the main corridors."),
        ("goldcoast-arvo",
            "Heads up Gold Coast — Smith Street is a mess this arvo, heavy congestion right through the middle. " +
            "Over on the Pacific Motorway there's a closure with about a kilometre of queuing. Morala Avenue is shut " +
            "too, short queues but still slow. And the Coast Highway has a closure as well, only minor delays but " +
            "worth knowing if you're heading that way."),
        ("clear-roads",
            "Good news this morning — roads are running clear right across the network, no crashes or closures to " +
            "report, so an easy run wherever you're headed.")
    ];

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CaptureElevenLabsCorpus()
    {
        if (Environment.GetEnvironmentVariable("ARN_ELEVEN_CAPTURE") != "1")
        {
            output.WriteLine("Skipped: set ARN_ELEVEN_CAPTURE=1 and ELEVENLABS_API_KEY to capture the audio corpus.");
            return;
        }

        var configuration = TestConfigurationHelper.BuildConfiguration();
        string? apiKey = configuration["AiSettings:ElevenLabs:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "test-key")
            Assert.Fail("ARN_ELEVEN_CAPTURE=1 but no real ElevenLabs key resolved. Set ELEVENLABS_API_KEY.");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<ElevenLabsClient>();
        var options = provider.GetRequiredService<IOptions<ElevenLabsOptions>>().Value;
        var voice = options.VoiceSettings;
        string? configuredVoiceId = configuration["AiSettings:ElevenLabs:VoiceId"];
        string voiceId = string.IsNullOrWhiteSpace(configuredVoiceId) ? DefaultVoiceId : configuredVoiceId;

        string directory = Path.Combine(CorpusPaths.ElevenLabsCorpusRoot(), voiceId);
        Directory.CreateDirectory(directory);

        int captured = 0;
        foreach (var (slug, script) in Samples)
        {
            var result = await client.GenerateAudioAsync(
                script, voice.Speed, voice.Stability, voice.SimilarityBoost, voice.Style, voice.UseSpeakerBoost,
                voiceId, modelOverride: null, ct: CancellationToken.None);

            if (result.IsFailure)
            {
                output.WriteLine($"{slug}: FAILED — {result.Error}");
                continue;
            }

            string audioFile = $"{slug}.wav";
            await File.WriteAllBytesAsync(Path.Combine(directory, audioFile), result.Value.AudioData,
                CancellationToken.None);

            var metadata = new
            {
                slug,
                voiceId,
                modelId = options.ModelId,
                outputFormat = options.OutputFormat,
                durationSeconds = Math.Round(result.Value.DurationSeconds, 3),
                charCount = script.Length,
                script,
                audioFile
            };
            await File.WriteAllTextAsync(
                Path.Combine(directory, $"{slug}.json"),
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }),
                CancellationToken.None);

            captured++;
            output.WriteLine($"{slug}: {result.Value.DurationSeconds:F2}s ({result.Value.AudioData.Length} bytes)");
        }

        output.WriteLine($"Captured {captured} ElevenLabs samples → {CorpusPaths.ElevenLabsCorpusRoot()}");
        Assert.True(captured > 0, "No ElevenLabs samples were captured.");
    }
}

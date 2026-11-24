using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit.Abstractions;
using ArnBulletinGenerator.Infrastructure.Audio;

namespace ArnBulletinGenerator.Infrastructure.Tests;

public class TtsIntegrationTests(ITestOutputHelper output)
{
    private static readonly string? ElevenLabsApiKey =
        Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");

    [Fact(Skip = "Quota Exceeded")]
    public async Task ElevenLabs_PreciseSynthesis_ShouldBeCloseTo30s()
    {
        if (string.IsNullOrWhiteSpace(ElevenLabsApiKey))
        {
            output.WriteLine("SKIPPED: Set ELEVENLABS_API_KEY env var to run this test.");
            return;
        }

        var services = new ServiceCollection();
        services.AddHttpClient("ElevenLabsClient");
        using var serviceProvider = services.BuildServiceProvider();
        var httpClient = serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("ElevenLabsClient");

        var elevenLabsOptions = Options.Create(new ElevenLabsOptions
        {
            ApiKey = ElevenLabsApiKey,
            ModelId = "eleven_turbo_v2_5",
            BaseUrl = "https://api.elevenlabs.io/v1/text-to-speech",
            OutputFormat = "pcm_16000"
        });

        var client = new ElevenLabsClient(httpClient, elevenLabsOptions, Substitute.For<ILogger<ElevenLabsClient>>());
        var synthesizer = new ElevenLabsAudioSynthesizer(client,
            new WavDurationProcessor(Substitute.For<ILogger<WavDurationProcessor>>()), elevenLabsOptions,
            Substitute.For<ILogger<ElevenLabsAudioSynthesizer>>());

        const string testText =
            "Good morning, Sunshine City! It's currently eight-fifteen on your morning commute. We're seeing a major slowdown on the Interstate ten eastbound near the fifth street exit due to a multi-vehicle accident. Expect delays of up to twenty minutes as emergency crews work to clear the scene. This traffic update is brought to you by Joe's Coffee\u2014your favorite morning pick-me-up. Stop by Joe's today for a fresh brew and start your day right. Joe's Coffee: keeping you fueled for the road ahead. Stay safe out there, stay alert, and keep it locked here for more updates.";

        var result = await synthesizer.SynthesizeAsync(testText);

        if (result.IsFailure) Assert.Fail(result.Error);

        byte[] audioData = result.Value.Audio;
        double duration = (audioData.Length - 44) / 32000.0; // WAV at 16kHz 16-bit Mono = 32KB/s
        output.WriteLine($"Final Audio Duration: {duration:F2}s");

        Assert.True(Math.Abs(duration - 30.0) <= 2.5, $"Expected ~30s, got {duration:F2}s");
    }
}

using ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit.Abstractions;

namespace ArnBulletinGenerator.Infrastructure.Tests;

/// <summary>
///     E2E tests for duration estimation accuracy and learning loop.
///     These tests call the real ElevenLabs API to validate our calibration and estimation logic.
/// </summary>
public class DurationCalibrationE2ETests(ITestOutputHelper output)
{
    /// <summary>
    ///     Voice ID sourced from config/env (ELEVENLABS_VOICE_ID or AiSettings:ElevenLabs:VoiceId).
    ///     Falls back to a well-known test voice when not configured.
    /// </summary>

    // Reference phrase from VoiceCalibrationService
    private const string ReferencePhrase = @"Traffic is moving smoothly on the main highway. 
A minor delay has been reported near the downtown exit. 
Emergency vehicles responded quickly to the incident.
Construction work continues on the northern bridge.";

    /// <summary>
    ///     Test 1: Validate calibration reference phrase produces expected syllable rate.
    ///     This synthesizes the exact reference phrase and checks the measured rate.
    /// </summary>
    [Fact(Skip = "Requires ElevenLabs API - run manually")]
    public async Task Calibration_ReferencePhraseRate_ShouldBeAccurate()
    {
        // Arrange
        var (synthesizer, durationEstimator, audioProcessor) = CreateServices();

        double estimatedDuration = durationEstimator.EstimateDuration(ReferencePhrase);
        output.WriteLine($"Estimated duration: {estimatedDuration:F2}s");

        // Act - Synthesize the reference phrase
        var result = await synthesizer.SynthesizeAsync(ReferencePhrase);
        result.IsSuccess.ShouldBeTrue($"Synthesis failed: {result.Error}");

        byte[] audioData = result.Value.Audio;
        double actualDuration = audioProcessor.GetDuration(audioData);

        // Assert
        output.WriteLine("Reference phrase:");
        output.WriteLine($"  Estimated Duration: {estimatedDuration:F2}s");
        output.WriteLine($"  Actual Duration: {actualDuration:F2}s");

        double durationError = Math.Abs(estimatedDuration - actualDuration);
        output.WriteLine($"  Prediction error: {durationError:F2}s");

        durationError.ShouldBeLessThan(3.0, $"Prediction error too high: {durationError:F2}s");
    }

    /// <summary>
    ///     Test 2: Validate estimation accuracy for a ~30 second broadcast script.
    ///     This tests if the calibrated rate works for real scripts.
    /// </summary>
    [Fact(Skip = "Requires ElevenLabs API - run manually")]
    public async Task Estimation_BroadcastScript_ShouldMatchActual()
    {
        // Arrange
        var (synthesizer, durationEstimator, audioProcessor) = CreateServices();

        // First, calibrate with the reference phrase
        output.WriteLine("=== STEP 1: CALIBRATION ===");
        var calibrationResult = await synthesizer.SynthesizeAsync(ReferencePhrase);
        calibrationResult.IsSuccess.ShouldBeTrue();

        double refEstimated = durationEstimator.EstimateDuration(ReferencePhrase);
        double refDuration = audioProcessor.GetDuration(calibrationResult.Value.Audio);

        output.WriteLine($"Reference estimated: {refEstimated:F2}s, actual: {refDuration:F2}s");

        // Sample broadcast script (~30s target)
        string testScript =
            @"Good morning, Sunshine City! It's eight-fifteen on your drive to work and here's your traffic update.
We're seeing major congestion on Interstate ten eastbound near the fifth street exit. 
A multi-vehicle accident has traffic backed up for about two miles.
Emergency crews are on scene and expect delays of around twenty minutes.
On the westbound side, things are flowing smoothly all the way to the coast.
If you're heading downtown, consider taking the surface streets through Maple Avenue.
The construction on Highway One is wrapping up so that route should be clear by tomorrow.
This traffic update is brought to you by Joe's Coffee. Joe's Coffee, keeping you fueled for the road ahead.
Stay safe out there and keep it locked here for more updates throughout your morning.";

        output.WriteLine("\n=== STEP 2: ESTIMATION ===");
        double estimatedDuration = durationEstimator.EstimateDuration(testScript);

        output.WriteLine("Test script:");
        output.WriteLine($"  Estimated total duration: {estimatedDuration:F2}s");

        // Act - Synthesize the test script
        output.WriteLine("\n=== STEP 3: SYNTHESIS ===");
        var scriptResult = await synthesizer.SynthesizeAsync(testScript);
        scriptResult.IsSuccess.ShouldBeTrue($"Synthesis failed: {scriptResult.Error}");

        byte[] scriptAudio = scriptResult.Value.Audio;
        double actualDuration = audioProcessor.GetDuration(scriptAudio);

        // Assert
        double estimationError = actualDuration - estimatedDuration;
        double estimationErrorPct = estimationError / actualDuration * 100;

        output.WriteLine($"  Actual duration: {actualDuration:F2}s");
        output.WriteLine($"  Estimation error: {estimationError:F2}s ({estimationErrorPct:F1}%)");

        // The estimation should be within 3 seconds (the tolerance used in the orchestrator)
        Math.Abs(estimationError).ShouldBeLessThan(3.0,
            $"Estimation error too high: {estimationError:F2}s. Expected {estimatedDuration:F2}s, got {actualDuration:F2}s");
    }

    /// <summary>
    ///     Validates that the naive correction formula (actual/target) goes in the wrong direction:
    ///     It reduces syllable targets when the script is already too short.
    /// </summary>
    [Fact]
    public void RateAdjustment_Formula_ShouldCorrectProperly()
    {
        // Scenario: actual=20s, target=30s → factor = 20/30 = 0.67
        double baseRate = 5.0;
        double correctionFactor = 20.0 / 30.0;
        double adjustedRate = baseRate * correctionFactor;

        correctionFactor.ShouldBe(0.67, 0.01);
        adjustedRate.ShouldBe(3.33, 0.01);

        // With this naive formula: 30s * 3.33 = 100 syllables (FEWER, not more)
        // But we need MORE syllables → this formula is backwards
        (30 * adjustedRate).ShouldBeLessThan(30 * baseRate,
            "naive formula reduces syllables when we actually need more");
    }

    /// <summary>
    ///     Validates the correct inverted formula: target/actual increases rate to produce more content.
    /// </summary>
    [Fact]
    public void RateAdjustment_CorrectFormula_ShouldIncreaseRate()
    {
        // Scenario: actual=20s, target=30s → need 50% MORE content
        double baseRate = 5.0;
        double correctionFactor = 30.0 / 20.0; // target / actual (inverted)
        double adjustedRate = baseRate * correctionFactor;

        correctionFactor.ShouldBe(1.5, 0.01);
        adjustedRate.ShouldBe(7.5, 0.01);

        // Verify: more syllables → longer script → closer to target
        (30 * adjustedRate).ShouldBeGreaterThan(30 * baseRate,
            "inverted formula increases syllables to compensate for fast TTS");
    }

    private (ElevenLabsAudioSynthesizer, SpeechDurationEstimator, WavDurationProcessor) CreateServices()
    {
        var config = TestConfigurationHelper.BuildConfiguration();
        var services = new ServiceCollection();
        services.AddHttpClient("ElevenLabsClient");
        var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        string? apiKey = config["AiSettings:ElevenLabs:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "ElevenLabs API key not found. Set ELEVENLABS_API_KEY env var or populate AiSettings:ElevenLabs:ApiKey in appsettings.Development.json.");

        var elevenLabsOptions = Options.Create(new ElevenLabsOptions
        {
            ApiKey = apiKey,
            ModelId = config["AiSettings:ElevenLabs:ModelId"] ?? "eleven_turbo_v2_5",
            BaseUrl = config["AiSettings:ElevenLabs:BaseUrl"] ?? "https://api.elevenlabs.io/v1/text-to-speech",
            OutputFormat = config["AiSettings:ElevenLabs:OutputFormat"] ?? "pcm_24000"
        });

        var clientLogger = Substitute.For<ILogger<ElevenLabsClient>>();
        var synthesizerLogger = Substitute.For<ILogger<ElevenLabsAudioSynthesizer>>();
        var processorLogger = Substitute.For<ILogger<WavDurationProcessor>>();

        var client = new ElevenLabsClient(httpClientFactory.CreateClient("ElevenLabsClient"), elevenLabsOptions,
            clientLogger);
        var synthesizer = new ElevenLabsAudioSynthesizer(client,
            new WavDurationProcessor(processorLogger), elevenLabsOptions, synthesizerLogger);
        var durationEstimator = new SpeechDurationEstimator();
        var audioProcessor = new WavDurationProcessor(processorLogger);

        return (synthesizer, durationEstimator, audioProcessor);
    }
}

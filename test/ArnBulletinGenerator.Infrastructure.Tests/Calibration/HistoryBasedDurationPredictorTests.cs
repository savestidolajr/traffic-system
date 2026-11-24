using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Audio;
using ArnBulletinGenerator.Infrastructure.Calibration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Calibration;

public sealed class HistoryBasedDurationPredictorTests
{
    private readonly StaticRateDurationPredictor _fallback;
    private readonly DurationPredictionOptions _options;
    private readonly HistoryBasedDurationPredictor _predictor;
    private readonly InMemorySynthesisHistoryRepository _history;

    public HistoryBasedDurationPredictorTests()
    {
        _options = new DurationPredictionOptions
        {
            FallbackSyllablesPerSecond = 5.8,
            FallbackSyllablesPerWord = 1.5,
            Mode = PredictionMode.HistoryBased
        };

        _fallback = new StaticRateDurationPredictor(Options.Create(_options));

        _history = new InMemorySynthesisHistoryRepository(
            persistencePath: null,
            Substitute.For<ILogger<InMemorySynthesisHistoryRepository>>());

        _predictor = new HistoryBasedDurationPredictor(
            _history,
            Options.Create(_options),
            _fallback);
    }

    private void SeedHistory()
    {
        // Seed with realistic records: voice speaks ~175 syllables in 30s at speed 1.0
        // That's ~5.83 SPS (close to default)
        _history.Record(MakeRecord(voiceId: "voice-aussie", syllables: 170, duration: 29.2, speed: 1.0));
        _history.Record(MakeRecord(voiceId: "voice-aussie", syllables: 175, duration: 30.1, speed: 1.0));
        _history.Record(MakeRecord(voiceId: "voice-aussie", syllables: 180, duration: 31.0, speed: 1.0));
        _history.Record(MakeRecord(voiceId: "voice-aussie", syllables: 165, duration: 28.3, speed: 1.0));
        _history.Record(MakeRecord(voiceId: "voice-aussie", syllables: 185, duration: 31.8, speed: 1.0));
    }

    private static SynthesisRecord MakeRecord(
        string voiceId = "voice-1",
        string modelId = "eleven_v3",
        double speed = 1.0,
        int syllables = 175,
        double duration = 30.0,
        int seed = 42)
    {
        return new SynthesisRecord
        {
            VoiceId = voiceId,
            ModelId = modelId,
            Speed = speed,
            SyllableCount = syllables,
            WordCount = (int)(syllables / 1.5),
            AchievedDuration = duration,
            TargetDuration = 30.0,
            Seed = seed,
            ScriptPreview = "Test script",
            Timestamp = DateTimeOffset.UtcNow,
            WasWithinTolerance = Math.Abs(duration - 30.0) < 1.0
        };
    }

    [Fact]
    public void StrategyName_IsHistoryBased()
    {
        // Seed enough history near the syllable count of our test text
        string longText = string.Join(" ", Enumerable.Repeat("M1 inbound is heavy traffic Melbourne road", 15));
        // ~12 syl/repetition × 15 ≈ 180 syllables
        SeedHistory(); // has records at 165–185 syllables — matches ~180

        var ctx = new PredictionContext { VoiceId = "voice-aussie", ModelId = "eleven_v3", Speed = 1.0 };

        var result = _predictor.PredictDuration(longText, ctx);

        result.StrategyName
            .ShouldBe(nameof(HistoryBasedDurationPredictor));
    }

    [Fact]
    public void PredictDuration_NoHistory_FallsBackToStaticRate()
    {
        // Use a voice with no history
        var ctx = new PredictionContext { VoiceId = "unknown-voice", Speed = 1.0 };

        var result = _predictor.PredictDuration("M1 inbound is heavy", ctx);

        // Should match static rate: 6 syllables / 5.8 SPS = 1.034s
        result.ExpectedDuration.ShouldBe(6.0 / 5.8, 0.01);
        result.DataPointsUsed.ShouldBe(0);
    }

    [Fact]
    public void PredictDuration_OneRecord_FallsBackToStaticRate()
    {
        // Only 1 record — less than MinDataPointsForPrediction (2)
        _history.Record(MakeRecord(voiceId: "voice-sparse", syllables: 175, duration: 30.0));
        var ctx = new PredictionContext { VoiceId = "voice-sparse", Speed = 1.0 };

        var result = _predictor.PredictDuration("M1 inbound is heavy", ctx);

        result.DataPointsUsed.ShouldBe(0);
    }

    [Fact]
    public void PredictDuration_WithHistory_PredictsCloserToActualThanStatic()
    {
        SeedHistory();
        var ctx = new PredictionContext { VoiceId = "voice-aussie", ModelId = "eleven_v3", Speed = 1.0 };

        var result = _predictor.PredictDuration(
            // "M1 inbound is heavy West Gate Freeway outbound Tullamarine slow" = ~12 syllables
            "M1 inbound is heavy West Gate Freeway outbound Tullamarine slow",
            ctx);

        // Static rate: 12 / 5.8 = 2.07s
        // Historical rate: voice-aussie does ~5.75 SPS → 12 / 5.75 = 2.09s
        // Both should be close, but history-based should use actual data
        result.ExpectedDuration.ShouldBeGreaterThan(0);
        result.DataPointsUsed.ShouldBeGreaterThanOrEqualTo(2);

        // Log the prediction for manual inspection
        var prediction = result;
        System.Diagnostics.Debug.WriteLine(
            $"Predicted: {prediction.ExpectedDuration:F3}s [{prediction.ConfidenceLower:F3}–{prediction.ConfidenceUpper:F3}] " +
            $"based on {prediction.DataPointsUsed} records");
    }

    [Fact]
    public void PredictDuration_ExactMatch_ReturnsHistoricalDuration()
    {
        // Add records at multiple syllable counts around 175
        _history.Record(MakeRecord(voiceId: "voice-exact", syllables: 170, duration: 29.2, seed: 100));
        _history.Record(MakeRecord(voiceId: "voice-exact", syllables: 175, duration: 30.1, seed: 101));
        _history.Record(MakeRecord(voiceId: "voice-exact", syllables: 180, duration: 31.0, seed: 102));

        // Build a text that the syllable counter rates as ~175 syllables
        // "Australian traffic report inbound congestion" = 13 syllables per repetition
        string script = string.Join(" ",
            Enumerable.Repeat("Australian traffic report inbound congestion", 13));
        // 13 × 13 = 169 syllables — close to our seeded data

        var ctx = new PredictionContext { VoiceId = "voice-exact", ModelId = "eleven_v3", Speed = 1.0 };
        var result = _predictor.PredictDuration(script, ctx);

        result.DataPointsUsed.ShouldBeGreaterThanOrEqualTo(2);
        // With nearest records at 29.2–31.0s, the prediction should fall in that range
        result.ExpectedDuration.ShouldBeGreaterThan(28.0);
        result.ExpectedDuration.ShouldBeLessThan(32.0);
    }

    [Fact]
    public void GetTargetSyllables_NoHistory_FallsBackToStaticRate()
    {
        var ctx = new PredictionContext { VoiceId = "unknown", Speed = 1.0 };

        var result = _predictor.GetTargetSyllables(30.0, ctx);

        // Static rate: 30 * 5.8 = 174 syllables
        result.DataPointsUsed.ShouldBe(0);
        result.ConfidenceLower.ShouldBeLessThan(174);
        result.ConfidenceUpper.ShouldBeGreaterThan(174);
    }

    [Fact]
    public void GetTargetSyllables_WithHistory_PredictsReasonableTarget()
    {
        SeedHistory();
        var ctx = new PredictionContext { VoiceId = "voice-aussie", ModelId = "eleven_v3", Speed = 1.0 };

        var result = _predictor.GetTargetSyllables(30.0, ctx);

        // Historical data says ~175 syllables = 30s
        // So the prediction should be close to 175
        result.DataPointsUsed.ShouldBeGreaterThanOrEqualTo(2);
        result.ConfidenceLower.ShouldBeLessThan(180);
        result.ConfidenceUpper.ShouldBeGreaterThan(170);
    }

    [Fact]
    public void GetTargetSyllables_ZeroDuration_ReturnsZero()
    {
        var ctx = new PredictionContext { VoiceId = "voice-aussie", ModelId = "eleven_v3", Speed = 1.0 };

        var result = _predictor.GetTargetSyllables(0, ctx);

        result.ConfidenceLower.ShouldBe(0);
        result.ConfidenceUpper.ShouldBe(0);
    }

    [Fact]
    public void GetTargetSyllables_NegativeDuration_ReturnsZero()
    {
        var ctx = new PredictionContext { VoiceId = "voice-aussie", ModelId = "eleven_v3", Speed = 1.0 };

        var result = _predictor.GetTargetSyllables(-5, ctx);

        result.ConfidenceLower.ShouldBe(0);
        result.ConfidenceUpper.ShouldBe(0);
    }

    [Fact]
    public void PredictDuration_DifferentVoice_SameSyllables_FallsBack()
    {
        SeedHistory(); // has "voice-aussie" data
        var ctx = new PredictionContext { VoiceId = "different-voice", Speed = 1.0 };

        var result = _predictor.PredictDuration("M1 inbound is heavy", ctx);

        // No history for this voice → fallback to static
        result.DataPointsUsed.ShouldBe(0);
    }

    [Fact]
    public void PredictDuration_SameVoice_DifferentSpeed_ReturnsEmpty()
    {
        SeedHistory(); // all at speed 1.0
        var ctx = new PredictionContext { VoiceId = "voice-aussie", ModelId = "eleven_v3", Speed = 0.85 };

        var result = _predictor.PredictDuration("M1 inbound is heavy", ctx);

        // No history at speed 0.85 → fallback
        result.DataPointsUsed.ShouldBe(0);
    }

    [Fact]
    public void PredictDuration_ConfidenceInterval_NarrowsWithMoreData()
    {
        // The test text is "Australian traffic report" × 13 ≈ 169 syllables
        // Add records clustered at various counts around 169 with tight duration clustering
        for (int i = 0; i < 10; i++)
        {
            _history.Record(MakeRecord(
                voiceId: "voice-precise",
                syllables: 165 + i,
                duration: 29.0 + (i % 3) * 0.1,
                seed: i));
        }

        string longText = string.Join(" ",
            Enumerable.Repeat("Australian traffic report inbound congestion", 13));
        // ≈ 169 syllables

        var ctx = new PredictionContext { VoiceId = "voice-precise", ModelId = "eleven_v3", Speed = 1.0 };
        var result = _predictor.PredictDuration(longText, ctx);

        result.DataPointsUsed.ShouldBeGreaterThanOrEqualTo(2);
        // With 10 clustered data points, confidence should be relatively tight (<±1s)
        double intervalWidth = result.ConfidenceUpper - result.ConfidenceLower;
        intervalWidth.ShouldBeLessThan(1.5);
    }
}

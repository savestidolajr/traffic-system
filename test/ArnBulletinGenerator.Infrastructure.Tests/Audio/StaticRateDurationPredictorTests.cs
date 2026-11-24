using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Audio;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Audio;

public sealed class StaticRateDurationPredictorTests
{
    private static StaticRateDurationPredictor CreateSut(double sps = 5.8, double spw = 1.5)
    {
        var options = Options.Create(new DurationPredictionOptions
        {
            FallbackSyllablesPerSecond = sps,
            FallbackSyllablesPerWord = spw,
            Mode = PredictionMode.StaticRate
        });
        return new StaticRateDurationPredictor(options);
    }

    // ── PredictDuration ────────────────────────────────────────────────

    [Fact]
    public void PredictDuration_WithKnownText_ReturnsExpectedDuration()
    {
        var predictor = CreateSut(sps: 5.8);
        // "M1 inbound is heavy" = 6 syllables at 5.8 SPS = ~1.034s
        var context = new PredictionContext { VoiceId = "test", Speed = 1.0 };

        var result = predictor.PredictDuration("M1 inbound is heavy", context);

        result.ExpectedDuration.ShouldBe(6.0 / 5.8, 0.01);
        result.StrategyName.ShouldBe(nameof(StaticRateDurationPredictor));
    }

    [Fact]
    public void PredictDuration_WithSpeed_AdjustsDuration()
    {
        var predictor = CreateSut(sps: 5.8);
        // "West Gate Freeway outbound" = 6 syllables at 5.8 SPS at 0.5 speed = ~2.069s
        var context = new PredictionContext { VoiceId = "test", Speed = 0.5 };

        var result = predictor.PredictDuration("West Gate Freeway outbound", context);

        result.ExpectedDuration.ShouldBe(6.0 / (5.8 * 0.5), 0.01);
    }

    [Fact]
    public void PredictDuration_WithCalibratedSps_UsesCalibratedValue()
    {
        var predictor = CreateSut(sps: 5.8);
        var context = new PredictionContext { VoiceId = "test", Speed = 1.0, CalibratedSyllablesPerSecond = 6.2 };

        var result = predictor.PredictDuration("M1 inbound is heavy", context);

        // Should use 6.2, not the fallback 5.8
        result.ExpectedDuration.ShouldBe(6.0 / 6.2, 0.01);
    }

    [Fact]
    public void PredictDuration_EmptyText_ReturnsZero()
    {
        var predictor = CreateSut();
        var context = new PredictionContext { VoiceId = "test" };

        var result = predictor.PredictDuration("", context);

        result.ExpectedDuration.ShouldBe(0.0);
    }

    // ── GetTargetSyllables ─────────────────────────────────────────────

    [Fact]
    public void GetTargetSyllables_ForGivenDuration_ReturnsCorrectCount()
    {
        var predictor = CreateSut(sps: 5.8);
        var context = new PredictionContext { VoiceId = "test", Speed = 1.0 };

        var result = predictor.GetTargetSyllables(30.0, context);

        // target = 30 * 5.8 * 1.0 = 174 syllables
        result.ExpectedDuration.ShouldBe(30.0);
        result.ConfidenceLower.ShouldBe(174 * 0.95, 0.1);
        result.ConfidenceUpper.ShouldBe(174 * 1.05, 0.1);
        result.StrategyName.ShouldBe(nameof(StaticRateDurationPredictor));
    }

    [Fact]
    public void GetTargetSyllables_WithSpeed_AdjustsTarget()
    {
        var predictor = CreateSut(sps: 5.8);
        var context = new PredictionContext { VoiceId = "test", Speed = 0.85 };

        var result = predictor.GetTargetSyllables(30.0, context);

        // target = 30 * 5.8 * 0.85 = 147.9 syllables (slower speech = fewer syllables needed)
        double expected = 30.0 * 5.8 * 0.85;
        result.ConfidenceLower.ShouldBeLessThan(expected);
        result.ConfidenceUpper.ShouldBeGreaterThan(expected);
    }

    [Fact]
    public void GetTargetSyllables_WithCalibratedSps_UsesCalibrated()
    {
        var predictor = CreateSut(sps: 5.8);
        var context = new PredictionContext
        {
            VoiceId = "test",
            Speed = 1.0,
            CalibratedSyllablesPerSecond = 6.5
        };

        var result = predictor.GetTargetSyllables(30.0, context);

        // target = 30 * 6.5 * 1.0 = 195 syllables (faster speech = more syllables)
        double expected = 30.0 * 6.5 * 1.0;
        result.ConfidenceLower.ShouldBeLessThan(expected);
        result.ConfidenceUpper.ShouldBeGreaterThan(expected);
    }

    // ── Edge cases ─────────────────────────────────────────────────────

    [Fact]
    public void PredictDuration_NullText_ReturnsZero()
    {
        var predictor = CreateSut();
        var context = new PredictionContext { VoiceId = "test" };

        var result = predictor.PredictDuration(null!, context);

        result.ExpectedDuration.ShouldBe(0.0);
    }

    [Fact]
    public void PredictDuration_WhitespaceText_ReturnsZero()
    {
        var predictor = CreateSut();
        var context = new PredictionContext { VoiceId = "test" };

        var result = predictor.PredictDuration("   ", context);

        result.ExpectedDuration.ShouldBe(0.0);
    }

    [Fact]
    public void PredictDuration_SingleCharacter_ReturnsNonZero()
    {
        var predictor = CreateSut(sps: 5.8);
        var context = new PredictionContext { VoiceId = "test", Speed = 1.0 };

        var result = predictor.PredictDuration("A", context);

        // "A" = 1 syllable at 5.8 SPS = ~0.172s
        result.ExpectedDuration.ShouldBe(1.0 / 5.8, 0.01);
        result.ExpectedDuration.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void PredictDuration_VeryLongText_DoesNotOverflow()
    {
        var predictor = CreateSut();
        var longText = string.Join(" ", Enumerable.Repeat("M1 inbound is heavy traffic", 200));
        var context = new PredictionContext { VoiceId = "test", Speed = 1.0 };

        var result = predictor.PredictDuration(longText, context);

        result.ExpectedDuration.ShouldBeGreaterThan(0);
        result.ExpectedDuration.ShouldNotBe(double.PositiveInfinity);
        result.ExpectedDuration.ShouldNotBe(double.NaN);
    }

    [Fact]
    public void PredictDuration_SpeedZero_FallsBackToOne()
    {
        var predictor = CreateSut(sps: 5.8);
        var context = new PredictionContext { VoiceId = "test", Speed = 0 };

        var result = predictor.PredictDuration("M1 inbound is heavy", context);

        // Speed=0 should be treated as 1.0 (defensive)
        result.ExpectedDuration.ShouldBe(6.0 / 5.8, 0.01);
    }

    [Fact]
    public void PredictDuration_VeryHighSpeed_ReturnsSmallPositive()
    {
        var predictor = CreateSut(sps: 5.8);
        var context = new PredictionContext { VoiceId = "test", Speed = 10.0 };

        var result = predictor.PredictDuration("M1 inbound is heavy", context);

        result.ExpectedDuration.ShouldBeGreaterThan(0);
        result.ExpectedDuration.ShouldBeLessThan(6.0 / 5.8); // faster than speed=1.0
    }

    [Fact]
    public void GetTargetSyllables_ZeroDuration_ReturnsZero()
    {
        var predictor = CreateSut();
        var context = new PredictionContext { VoiceId = "test", Speed = 1.0 };

        var result = predictor.GetTargetSyllables(0, context);

        result.ConfidenceLower.ShouldBe(0, 0.01);
        result.ConfidenceUpper.ShouldBe(0, 0.01);
    }

    [Fact]
    public void GetTargetSyllables_NegativeDuration_ReturnsZero()
    {
        var predictor = CreateSut();
        var context = new PredictionContext { VoiceId = "test", Speed = 1.0 };

        var result = predictor.GetTargetSyllables(-10, context);

        result.ConfidenceLower.ShouldBe(0, 0.01);
        result.ConfidenceUpper.ShouldBe(0, 0.01);
    }

    [Fact]
    public void PredictDuration_CalibratedSpsZero_FallsBackToDefault()
    {
        var predictor = CreateSut(sps: 5.8);
        var context = new PredictionContext
        {
            VoiceId = "test",
            Speed = 1.0,
            CalibratedSyllablesPerSecond = 0 // invalid, should use fallback
        };

        var result = predictor.PredictDuration("M1 inbound is heavy", context);

        // Should use fallback 5.8, not 0
        result.ExpectedDuration.ShouldBe(6.0 / 5.8, 0.01);
    }

    // ── Strategy name ──────────────────────────────────────────────────

    [Fact]
    public void StrategyName_IsAlwaysStaticRate()
    {
        var predictor = CreateSut();
        var ctx = new PredictionContext { VoiceId = "test" };

        var durResult = predictor.PredictDuration("test", ctx);
        var sylResult = predictor.GetTargetSyllables(30.0, ctx);

        durResult.StrategyName.ShouldBe(nameof(StaticRateDurationPredictor));
        sylResult.StrategyName.ShouldBe(nameof(StaticRateDurationPredictor));
    }
}

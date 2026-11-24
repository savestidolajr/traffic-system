using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;
using ArnBulletinGenerator.Workflow.Contracts;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Conversational;

/// <summary>
///     The probe guard. <c>WordsForSeconds</c> has no text to price before a read exists, so it
///     prices a fixed 27-word probe through the voice's predictor and divides. That makes the probe
///     the shortest text the predictor ever sees and therefore the first place a modelling error
///     surfaces — and one did: until 2026-08-31 the history predictor answered with the duration of
///     whatever full bulletins sat nearest its syllable count, so the same 30-second slot came back
///     as 25, 26, 36, 66 or 80 words depending only on which (model, speed) bucket matched. A rate
///     outside human news delivery is now refused, and the caller's fallback answers instead.
/// </summary>
public sealed class CalibratedDurationOracleTests
{
    private sealed class FixedPredictor(double seconds) : IDurationPredictor
    {
        public PredictionResult PredictDuration(string text, PredictionContext context) => new()
        {
            ExpectedDuration = seconds,
            ConfidenceLower = seconds,
            ConfidenceUpper = seconds,
            DataPointsUsed = 5,
            StrategyName = nameof(FixedPredictor)
        };

        public PredictionResult GetTargetSyllables(double targetDurationSeconds, PredictionContext context) =>
            new() { ExpectedDuration = targetDurationSeconds, StrategyName = nameof(FixedPredictor) };
    }

    /// <summary>A fallback that is trivially identifiable in an assertion.</summary>
    private sealed class SentinelOracle : IDurationOracle
    {
        public const int Words = 4242;
        public double PredictSeconds(string text) => 1;
        public double SecondsForWords(int words) => 1;
        public int WordsForSeconds(double seconds) => Words;
    }

    private static IDurationOracle Oracle(double probeSeconds) =>
        new CalibratedDurationOracle(
            new FixedPredictor(probeSeconds),
            new PredictionContext { VoiceId = "voice", ModelId = "eleven_v3", Speed = 1.0 },
            new SentinelOracle());

    // 27 probe words in 11.0s is 147 wpm — the measured dev rate, and what the engine sizes against.
    [Fact]
    public void Uses_the_calibrated_rate_when_the_probe_prices_plausibly()
    {
        int words = Oracle(probeSeconds: 11.0).WordsForSeconds(30);

        words.ShouldNotBe(SentinelOracle.Words);
        // 30s at ~147 wpm. The engine independently computes 74 for this slot from 148 wpm.
        words.ShouldBeInRange(70, 78);
    }

    // The exact shape of the defect: the probe handed a full bulletin's duration. 27 words in 30s
    // implies 54 wpm, which no news voice reads at, and it budgeted a 30-second slot at 27 words.
    [Fact]
    public void Refuses_a_rate_below_human_delivery_and_defers_to_the_fallback()
    {
        Oracle(probeSeconds: 30.0).WordsForSeconds(30).ShouldBe(SentinelOracle.Words);
    }

    [Fact]
    public void Refuses_an_implausibly_fast_rate_too()
    {
        // 27 words in 3s is 540 wpm.
        Oracle(probeSeconds: 3.0).WordsForSeconds(30).ShouldBe(SentinelOracle.Words);
    }

    [Fact]
    public void Admits_the_fastest_voice_ARN_has_deployed()
    {
        // Measured deployed reads span 148–190 wpm; 190 wpm is 27 words in 8.5s and must not trip
        // the guard. The band exists to catch a broken derivation, not to second-guess a fast read.
        int words = Oracle(probeSeconds: 8.5).WordsForSeconds(30);

        words.ShouldNotBe(SentinelOracle.Words);
        words.ShouldBeInRange(90, 100);
    }

    [Fact]
    public void SecondsForWords_defers_to_the_fallback_on_an_implausible_rate()
    {
        // Same guard, the other direction: a rejected rate must not leak into the inverse either.
        Oracle(probeSeconds: 30.0).SecondsForWords(70).ShouldBe(1);
    }
}

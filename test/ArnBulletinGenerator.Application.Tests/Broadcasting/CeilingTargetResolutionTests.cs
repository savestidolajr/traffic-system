using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

/// <summary>
///     Guards the ship-time word-ceiling budget derivation.
///     <para>
///         Reference case throughout is the live regression, job 43395fe5: a 22.0s Cairns slot where
///         the old derivation allowed 63 words, real copy of that length costs 25.6s, and the 16.4%
///         overrun fell just outside the 1.15 speed-up absorber. The fitter dropped sentences rather
///         than compressing, the trimmed read then measured under the slot, and the attempt was
///         retried as content-limited — three synthesis rounds and 143 seconds for one bulletin.
///     </para>
///     <para>
///         Calibration figures are measured, not invented: ARN Sarah reads at 6.64 syllables/sec with
///         a 1.55s residual standard deviation, and the model's cross-slot MAE is 1.084s.
///     </para>
/// </summary>
public sealed class CeilingTargetResolutionTests
{
    private const double Slot = 22.0;
    private const double MaxSpeedUp = 1.15;
    private const double LegacySecondsPerWord = 0.4;   // BroadcastOptions.WordsPerMinute = 150
    private const double MaxSilencePad = 0.3;
    private const double MaxSlowDown = 1.0;
    private const double SarahResidualSd = 1.55;

    [Fact]
    public void Uncalibrated_voice_reproduces_the_legacy_150_wpm_budget_exactly()
    {
        // The compatibility path, and the most important assertion here: every voice we have never
        // measured still takes it, so drift would silently change behaviour for unmeasured voices.
        var choice = ScriptReviewLoop.ResolveCeilingTarget(
            explicitTarget: Option<int>.None,
            targetDurationSeconds: Slot,
            maxSpeedUpFactor: MaxSpeedUp,
            maxSilencePadSeconds: MaxSilencePad,
            maxSlowDownFactor: MaxSlowDown,
            legacySecondsPerWord: LegacySecondsPerWord,
            currentWords: 63,
            prediction: Option<PredictedDuration>.None);

        choice.Path.ShouldBe("legacy-150wpm");
        choice.TargetWords.ShouldBe(55);                    // 22.0 / 0.4
        ScriptWordCeiling.For(choice.TargetWords).ShouldBe(63);
        choice.FloorWords.ShouldBe(0);
    }

    [Fact]
    public void Calibrated_script_over_the_allowance_is_trimmed_below_the_old_ceiling()
    {
        // 63 words predicted at 25.6s. Allowance = 22.0 * 1.15 - 1.55 = 23.75s, so it must trim.
        var choice = ScriptReviewLoop.ResolveCeilingTarget(
            Option<int>.None, Slot, MaxSpeedUp, MaxSilencePad, MaxSlowDown, LegacySecondsPerWord,
            currentWords: 63,
            prediction: Option<PredictedDuration>.Some(new PredictedDuration(25.6, SarahResidualSd)));

        choice.Path.ShouldBe("prosody-trim");
        choice.AllowanceSeconds.ShouldBe(23.75, 0.001);
        // The resulting ceiling must be strictly tighter than the 63 words the old path waved through.
        ScriptWordCeiling.For(choice.TargetWords).ShouldBeLessThan(63);
        choice.FloorWords.ShouldBe(54);
        choice.FloorWords.ShouldBe(
            WordTargetCalculator.FloorWords(Slot, MaxSilencePad, MaxSlowDown, 25.6 / 63));
    }

    [Fact]
    public void Calibrated_script_inside_the_allowance_is_left_alone()
    {
        // 56 words predicted at 22.8s, under the 23.75s allowance: a read that legitimately fits must
        // not be trimmed, or we reintroduce the underrun that triggers content-limited retries.
        var choice = ScriptReviewLoop.ResolveCeilingTarget(
            Option<int>.None, Slot, MaxSpeedUp, MaxSilencePad, MaxSlowDown, LegacySecondsPerWord,
            currentWords: 56,
            prediction: Option<PredictedDuration>.Some(new PredictedDuration(22.8, SarahResidualSd)));

        choice.Path.ShouldBe("prosody-fits");
        ScriptWordCeiling.Excess(string.Join(" ", Enumerable.Repeat("word", 56)), choice.TargetWords)
            .ShouldBe(0);
    }

    [Fact]
    public void Attempt_target_is_used_only_when_no_calibrated_prediction_exists()
    {
        // TargetWordCountOverride is NOT an operator instruction — the pipeline sets it every attempt
        // (ScriptGenerationEngine 262/453, WordTargetEnforcer 47). Ranking it above the model made the
        // calibrated path unreachable: live job dfb11ab8 logged `explicit-target` at 47 words, exactly
        // the attempt target, with the prediction never consulted. So the word target is the FALLBACK
        // proxy, used only when no measured prediction is available.
        var withoutModel = ScriptReviewLoop.ResolveCeilingTarget(
            explicitTarget: Option<int>.Some(40),
            targetDurationSeconds: Slot,
            maxSpeedUpFactor: MaxSpeedUp,
            maxSilencePadSeconds: MaxSilencePad,
            maxSlowDownFactor: MaxSlowDown,
            legacySecondsPerWord: LegacySecondsPerWord,
            currentWords: 63,
            prediction: Option<PredictedDuration>.None);

        withoutModel.Path.ShouldBe("attempt-target");
        withoutModel.TargetWords.ShouldBe(40);
        withoutModel.FloorWords.ShouldBe(0);

        // Same attempt target, but now a calibrated prediction says the read is over the slot: the
        // measured duration decides and the read is trimmed.
        var withModel = ScriptReviewLoop.ResolveCeilingTarget(
            explicitTarget: Option<int>.Some(40),
            targetDurationSeconds: Slot,
            maxSpeedUpFactor: MaxSpeedUp,
            maxSilencePadSeconds: MaxSilencePad,
            maxSlowDownFactor: MaxSlowDown,
            legacySecondsPerWord: LegacySecondsPerWord,
            currentWords: 63,
            prediction: Option<PredictedDuration>.Some(new PredictedDuration(25.6, SarahResidualSd)));

        withModel.Path.ShouldBe("prosody-trim");
        ScriptWordCeiling.For(withModel.TargetWords).ShouldBeLessThan(63);
        withModel.FloorWords.ShouldBe(54);
    }

    [Fact]
    public void A_large_residual_cannot_pull_the_allowance_below_the_slot()
    {
        // 22.0 * 1.15 - 5.0 = 20.3s, which is UNDER the slot. Clamping at the bare target keeps a
        // noisy voice from trimming reads that fit; without the clamp a big sigma silently inverts
        // the guard into an under-filling one.
        var choice = ScriptReviewLoop.ResolveCeilingTarget(
            Option<int>.None, Slot, MaxSpeedUp, MaxSilencePad, MaxSlowDown, LegacySecondsPerWord,
            currentWords: 63,
            prediction: Option<PredictedDuration>.Some(new PredictedDuration(21.0, 5.0)));

        choice.AllowanceSeconds.ShouldBe(Slot, 0.001);
        choice.Path.ShouldBe("prosody-fits");   // 21.0s <= 22.0s allowance
    }
}

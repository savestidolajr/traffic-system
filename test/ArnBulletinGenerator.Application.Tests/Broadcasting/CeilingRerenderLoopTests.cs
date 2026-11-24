using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

/// <summary>
///     REPRODUCTION — dev job 6c3ebefb (sydney, 2026-08-24): 95 seconds, four attempts, four
///     ElevenLabs renders, to ship a 22.0s bulletin.
///     <code>
///     attempt 1  predicted 27.7s -> trimmed to 50 words -> measured 19.3s -> content-limited
///     attempt 2  predicted 21.8s                        -> measured 19.3s -> content-limited
///     attempt 3  predicted 26.1s -> trimmed to 52 words -> measured 18.5s -> content-limited
///     attempt 4  predicted 26.7s -> synthesised 58 words -> measured 21.4s -> fits
///     </code>
///     <para>
///         The model claims ~0.48 s/word; this voice reads at ~0.386. The ceiling therefore trims a
///         read that already fits, the shortened read underruns, and the underrun is retried — three
///         renders at ~10s each to arrive at the words attempt 1 was holding. Attempt 2 is the proof
///         that nothing learns: attempt 1 had just MEASURED 19.3s for those exact 50 words, and the
///         next prediction was made as though it had not happened.
///     </para>
/// </summary>
public sealed class CeilingRerenderLoopTests
{
    // Job 6c3ebefb, attempt 1, verbatim from the log line:
    // "predicted 27.7s against a 24.8s allowance for the 22s slot; 50 target words (58-word ceiling)"
    private const double SlotSeconds = 22.0;
    private const double MaxSilencePad = 0.3;
    private const double MaxSlowDown = 1.0;
    private const double MaxSpeedUp = 1.15;
    private const double ResidualSd = 0.5;      // 22 * 1.15 - 0.5 = 24.8s allowance, as logged
    private const int WordsBeforeTrim = 58;
    private const double PredictedSeconds = 27.7;
    private const double MeasuredSeconds = 19.3; // what those words actually read as

    private static Option<PredictedDuration> Prediction(double seconds) =>
        Option<PredictedDuration>.Some(new PredictedDuration(seconds, ResidualSd));

    private static ScriptReviewLoop.CeilingTargetChoice Resolve(double predictedSeconds) =>
        ScriptReviewLoop.ResolveCeilingTarget(
            Option<int>.None,
            SlotSeconds,
            MaxSpeedUp,
            MaxSilencePad,
            MaxSlowDown,
            legacySecondsPerWord: 0.4,
            currentWords: WordsBeforeTrim,
            Prediction(predictedSeconds));

    [Fact]
    public void The_uncorrected_prediction_trims_a_read_that_actually_fits()
    {
        // Documents the live behaviour: this is what cost three renders.
        var choice = Resolve(PredictedSeconds);

        choice.Path.ShouldBe("prosody-trim");
        choice.TargetWords.ShouldBeLessThan(WordsBeforeTrim);

        // And the read it trimmed to could never have filled the slot: at the pace this voice
        // actually reads at, the trimmed word count is seconds short of the 22s it must cover.
        double measuredPace = MeasuredSeconds / 50.0;
        (choice.TargetWords * measuredPace).ShouldBeLessThan(SlotSeconds,
            "the trim guarantees the underrun that is then retried as content-limited");
    }

    [Fact]
    public void Correcting_by_what_the_last_render_measured_lets_the_read_ship_whole()
    {
        double predictedPace = PredictedSeconds / WordsBeforeTrim;
        double measuredPace = MeasuredSeconds / 50.0;
        double factor = SpokenDurationCorrection.From(predictedPace, measuredPace);

        var choice = Resolve(PredictedSeconds * factor);

        choice.Path.ShouldBe("prosody-fits");
        choice.TargetWords.ShouldBe(WordsBeforeTrim,
            "attempt 4 proved these words fit — the corrected prediction reaches that on attempt 2");
    }

    [Fact]
    public void A_genuine_overrun_is_still_trimmed_after_correction()
    {
        // The correction must not become a licence to overrun: a read that is long even at the
        // measured pace still has to come down, or the slot is blown instead of underfilled.
        double factor = SpokenDurationCorrection.From(0.48, 0.386);
        var choice = Resolve(40.0 * factor);

        choice.Path.ShouldBe("prosody-trim");
        choice.TargetWords.ShouldBeLessThan(WordsBeforeTrim);
    }

    // ── the correction itself ──

    [Fact]
    public void An_unmeasured_attempt_leaves_the_model_alone()
    {
        SpokenDurationCorrection.From(0.48, 0).ShouldBe(1.0);
        SpokenDurationCorrection.From(0, 0.386).ShouldBe(1.0);
        SpokenDurationCorrection.From(double.NaN, 0.386).ShouldBe(1.0);
    }

    [Fact]
    public void One_freak_render_cannot_drive_the_next_attempt_off_a_cliff()
    {
        // A render that came back at a third of the expected pace is not evidence, it is a fault.
        SpokenDurationCorrection.From(0.48, 0.05).ShouldBe(SpokenDurationCorrection.MinFactor);
        SpokenDurationCorrection.From(0.48, 5.0).ShouldBe(SpokenDurationCorrection.MaxFactor);
    }

    [Fact]
    public void A_second_observation_refines_the_first_rather_than_replacing_it()
    {
        double first = SpokenDurationCorrection.Fold(1.0, 0.48, 0.386);   // ~0.80
        double second = SpokenDurationCorrection.Fold(first, 0.40, 0.386); // small further trim

        second.ShouldBeLessThan(first);
        second.ShouldBeGreaterThan(SpokenDurationCorrection.MinFactor);
    }
}

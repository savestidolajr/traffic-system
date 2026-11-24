using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Conversational;

/// <summary>
///     Answers the workflow's duration questions with the engine's voice-calibrated predictor
///     instead of a flat words-per-minute rate.
///     <para>
///         <c>WordRateDurationOracle</c> documents itself as a placeholder and names the exact
///         problem: five deployed 30-second reads measured 148, 158, 166, 186 and 190 wpm, so a
///         single global rate cannot serve every market and "the per-market variation is larger
///         than the tolerance the fit loop is chasing".
///     </para>
///     <para>
///         The cost of that gap is not a slightly wrong estimate, it is a re-draft. When the
///         workflow believes a 43-word read fills a 22-second slot and the real voice delivers it
///         in 18.8 seconds, <c>ScriptGenerationEngine</c> declares a content-limited underrun and
///         runs the ENTIRE workflow again — write, compose and edit — for a read that was never
///         wrong, only mismeasured. Observed on 3 of 8 dev jobs 2026-08-26, once three times over.
///     </para>
///     <para>
///         Same predictor the engine will grade the finished read with, so the writer is now aiming
///         at the target it is actually marked against.
///     </para>
/// </summary>
internal sealed class CalibratedDurationOracle(
    IDurationPredictor predictor,
    PredictionContext context,
    IDurationOracle fallback) : IDurationOracle
{
    public double PredictSeconds(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var result = predictor.PredictDuration(text, context);
        return result.ExpectedDuration > 0 ? result.ExpectedDuration : fallback.PredictSeconds(text);
    }

    /// <summary>
    ///     Seconds for a word count, before any prose exists.
    ///     <para>
    ///         The predictor needs text, and a plan has none, so this measures the voice's rate on
    ///         the finished reads it does know about and applies it. Deriving the rate FROM the
    ///         calibrated predictor rather than from a constant is the whole point: a 190 wpm voice
    ///         and a 148 wpm voice must not be handed the same word budget for the same slot.
    ///     </para>
    /// </summary>
    public double SecondsForWords(int words)
    {
        if (words <= 0)
            return 0;

        double perWord = SecondsPerWord();
        return perWord > 0 ? words * perWord : fallback.SecondsForWords(words);
    }

    public int WordsForSeconds(double seconds)
    {
        if (seconds <= 0)
            return 0;

        double perWord = SecondsPerWord();
        return perWord > 0 ? (int)Math.Round(seconds / perWord) : fallback.WordsForSeconds(seconds);
    }

    /// <summary>
    ///     The voice's measured seconds-per-word, taken by pricing a representative probe through
    ///     the same predictor. A probe rather than a constant because the predictor's rate depends
    ///     on the voice, model and speed carried in <see cref="PredictionContext" />, none of which
    ///     this class should be re-deriving for itself.
    /// </summary>
    private double SecondsPerWord()
    {
        var result = predictor.PredictDuration(Probe, context);
        if (result.ExpectedDuration <= 0)
            return 0;

        double wordsPerMinute = ProbeWords / result.ExpectedDuration * 60.0;
        // A malformed or mismatched history prediction must not turn into an absurd word budget.
        // Keep this guard aligned with VoiceCalibrationService's calibration bounds so the
        // predictor used for sizing and the persisted calibration used for grading fail safe in the
        // same way.
        if (wordsPerMinute is < MinWordsPerMinute or > MaxWordsPerMinute
            || double.IsNaN(wordsPerMinute) || double.IsInfinity(wordsPerMinute))
            return 0;

        return result.ExpectedDuration / ProbeWords;
    }

    private const double MinWordsPerMinute = 90.0;
    private const double MaxWordsPerMinute = 240.0;

    /// <summary>
    ///     A plain traffic sentence of known length. Ordinary road-report prose rather than filler
    ///     text, because the predictor's syllable model prices "Parramatta Road" differently from
    ///     an equal count of short words.
    /// </summary>
    private const string Probe =
        "The Western Motorway is heavy through Parramatta this morning with delays of about "
        + "ten minutes and traffic is banked back towards the exit at Church Street tonight.";

    private const int ProbeWords = 27;
}

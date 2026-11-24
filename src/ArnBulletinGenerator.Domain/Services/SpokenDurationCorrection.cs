namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Corrects a duration prediction using what the SAME job already measured.
///     <para>
///         The retry loop predicts a read's length, trims it to fit the slot, synthesises, and then
///         measures the result. When the prediction is high the trim removes words the read did not
///         need to lose, the shortened read underruns, and the attempt is retried as
///         content-limited — buying another ~10s ElevenLabs render to make the same mistake.
///         Job 43395fe5 spent 143s over 3 synthesis rounds this way; the 2026-08-24 fix denominated
///         the allowance in seconds rather than words, which corrected the UNITS but not the source
///         of the error, and job 6c3ebefb then spent 95s over 4 rounds: predicted 27.7s, 26.1s and
///         26.7s for reads that measured 19.3s, 19.3s and 18.5s.
///     </para>
///     <para>
///         Each of those renders is a measurement of this exact voice reading this exact copy, which
///         is strictly better evidence than the pooled model — and the loop threw it away, because
///         the calibrated pace is read once before the first attempt and never revisited. Comparing
///         predicted against measured seconds-per-word (a RATE, so it survives the trim changing the
///         word count) gives a factor the later attempts can apply.
///     </para>
/// </summary>
public static class SpokenDurationCorrection
{
    /// <summary>
    ///     Bounds on a single job's correction. eleven_v3 re-renders the same input within about
    ///     ±12%, so a factor near 1 is noise rather than signal; these bounds let a real 20-40%
    ///     error be corrected while stopping one freak render from driving the next attempt into a
    ///     slot it cannot fill. Errors past the bound are still corrected, just not in one step.
    /// </summary>
    public const double MinFactor = 0.6;

    /// <inheritdoc cref="MinFactor" />
    public const double MaxFactor = 1.4;

    /// <summary>
    ///     How far the last render's measured pace departed from what was predicted for it, as a
    ///     multiplier to apply to the next prediction. Returns 1 (no correction) when either rate is
    ///     missing or non-positive, so an unmeasured attempt leaves the model's own estimate alone.
    /// </summary>
    public static double From(double predictedSecondsPerWord, double measuredSecondsPerWord)
    {
        if (predictedSecondsPerWord <= 0 || measuredSecondsPerWord <= 0
            || double.IsNaN(predictedSecondsPerWord) || double.IsNaN(measuredSecondsPerWord))
            return 1.0;

        return Math.Clamp(measuredSecondsPerWord / predictedSecondsPerWord, MinFactor, MaxFactor);
    }

    /// <summary>
    ///     Folds a fresh observation into the factor already in force for this job, so a second
    ///     render refines the first rather than replacing it. The running factor multiplies through
    ///     because each observation is measured against an ALREADY-corrected prediction.
    /// </summary>
    public static double Fold(double current, double predictedSecondsPerWord, double measuredSecondsPerWord)
    {
        if (current <= 0 || double.IsNaN(current))
            current = 1.0;

        return Math.Clamp(current * From(predictedSecondsPerWord, measuredSecondsPerWord), MinFactor, MaxFactor);
    }
}

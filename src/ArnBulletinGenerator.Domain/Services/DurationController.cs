namespace ArnBulletinGenerator.Domain.Services;

/// <summary>What the duration controller recommends doing with a candidate read before synthesis.</summary>
public enum FitPlan
{
    /// <summary>Predicted duration is (or may be) correctable into the slot — spend the synthesis.</summary>
    Synthesize,

    /// <summary>
    ///     Predicted duration confidently underruns the slot beyond what pitch-preserving stretch can
    ///     rescue, no filler is available, and an extend attempt remains — grow the content BEFORE
    ///     paying for a synthesis that would only be discarded.
    /// </summary>
    SkipUnderrunExtend,

    /// <summary>
    ///     Predicted duration confidently OVERRUNS what pitch-preserving compression can absorb — drop
    ///     trailing content BEFORE paying for a synthesis that would only be measured, trimmed and
    ///     re-synthesized. The mirror of <see cref="SkipUnderrunExtend" />, and the direction a
    ///     pace-boosted voice actually fails in: the boost consumes the compression headroom, so the
    ///     aim sits near the ceiling and ordinary draft/read variance pushes it over. Before this
    ///     existed the upper bound of <see cref="DurationController.CorrectableWindow" /> was computed
    ///     and discarded, so every overrun cost a full render to discover (dev job c48410b6: three
    ///     24-29s eleven_v3 renders to learn what the prediction already implied).
    /// </summary>
    TrimBeforeSynthesis
}

/// <summary>
///     Deterministic pre-synthesis fit planner (Phase 2). Decides, from a duration prediction and the
///     audio pipeline's stretch/compress budget, whether a candidate read can plausibly be corrected
///     into the target slot with a single synthesis — or whether it should be grown first. All
///     arithmetic is external to the model: the LLM is never asked to predict milliseconds.
/// </summary>
public static partial class DurationController
{
    /// <summary>
    ///     The natural-read duration window that can be pulled to <paramref name="targetSeconds"/> using
    ///     pitch-preserving stretch/compress alone: a read of duration d fills the slot when
    ///     d ∈ [target / maxSlowDown, target * maxSpeedUp] (stretch a short read up, compress a long
    ///     read down). Matches the empirically measured correctable band.
    /// </summary>
    public static (double Lower, double Upper) CorrectableWindow(
        double targetSeconds, double maxSlowDownFactor, double maxSpeedUpFactor)
    {
        double slow = maxSlowDownFactor >= 1.0 ? maxSlowDownFactor : 1.0;
        double fast = maxSpeedUpFactor >= 1.0 ? maxSpeedUpFactor : 1.0;
        return (targetSeconds / slow, targetSeconds * fast);
    }

    /// <summary>
    ///     Decide whether to synthesize a read whose predicted natural duration lies in
    ///     [<paramref name="predictedLowerSeconds"/>, <paramref name="predictedUpperSeconds"/>].
    ///     Both skip directions are strictly conservative — they fire only when the ENTIRE confidence
    ///     interval sits outside the correctable window, so the worst error is an unnecessary
    ///     extend/trim, never a shipped underrun or a needlessly shortened bulletin.
    /// </summary>
    public static FitPlan Plan(
        double predictedLowerSeconds,
        double predictedUpperSeconds,
        double targetSeconds,
        double maxSlowDownFactor,
        double maxSpeedUpFactor,
        bool hasFiller,
        bool canExtend,
        bool canTrim = false)
    {
        if (targetSeconds <= 0)
            return FitPlan.Synthesize;

        double upper = predictedUpperSeconds > 0 ? predictedUpperSeconds : predictedLowerSeconds;
        double lower = predictedLowerSeconds > 0 ? predictedLowerSeconds : predictedUpperSeconds;
        if (upper <= 0)
            return FitPlan.Synthesize; // no usable prediction — synthesize and measure

        var (windowLower, windowUpper) = CorrectableWindow(targetSeconds, maxSlowDownFactor, maxSpeedUpFactor);

        // Overrun: even the SHORTEST plausible read is past the compression budget. Filler pre-sizing
        // has already reduced the synth target, so leave that case to the measured path.
        if (canTrim && !hasFiller && lower > windowUpper)
            return FitPlan.TrimBeforeSynthesis;

        // Underrun: even the LONGEST plausible read still falls short (unchanged behaviour).
        if (canExtend && !hasFiller && upper < windowLower)
            return FitPlan.SkipUnderrunExtend;

        return FitPlan.Synthesize;
    }
}

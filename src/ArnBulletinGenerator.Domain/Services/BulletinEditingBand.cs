namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The word-count band a verbatim edit must land in to fit a slot: the target the generator
///     itself aims at, plus the min/max the audio pipeline can absorb by slow-fill / speed-up
///     time-stretch.
/// </summary>
public static partial class BulletinEditingBand
{
    public readonly record struct Band(int TargetWordCount, int MinWordCount, int MaxWordCount);

    /// <summary>
    ///     Computes the editing band from the calibrated read rate and the slot. The floor is the
    ///     shortest read that slow-fill can stretch to the slot (<paramref name="maxSlowDownFactor" />)
    ///     and the ceiling the longest that speed-up can compress into it
    ///     (<paramref name="maxSpeedUpFactor" />); outside the band the take goes content-limited /
    ///     trim → fallback. Both ends widen when <paramref name="paceBoostWpm" /> is set — the ceiling
    ///     by the full boost (<see cref="WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor" />),
    ///     the floor by half of it
    ///     (<see cref="WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor" />).
    ///     <para>
    ///         <see cref="Band.TargetWordCount" /> mirrors what
    ///         <c>ScriptGenerationEngine</c> aims at, so the number an operator is shown is the number
    ///         generation targets: exactly the slot when unboosted, the centre of the band when
    ///         boosted (<see cref="WordTargetCalculator.ComputeBoostedWordTarget" />). The silence pad
    ///         is intentionally ignored to keep the floor conservative.
    ///     </para>
    /// </summary>
    public static Band Compute(double wordsPerMinute, double targetDurationSeconds, double speed,
        double maxSlowDownFactor, double maxSpeedUpFactor, double paceBoostWpm = 0,
        double absolutePaceBoostSpeedUpCeiling = 1.60,
        double absolutePaceBoostSlowDownCeiling = 1.15)
    {
        double effectiveSpeed = speed > 0 ? speed : 1.0;
        double effectiveMaxSpeedUpFactor = paceBoostWpm > 0
            ? WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(
                wordsPerMinute, paceBoostWpm, maxSpeedUpFactor, absolutePaceBoostSpeedUpCeiling)
            : maxSpeedUpFactor;
        double effectiveMaxSlowDownFactor = WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor(
            wordsPerMinute, paceBoostWpm, maxSlowDownFactor, absolutePaceBoostSlowDownCeiling);

        int target = paceBoostWpm > 0
            ? WordTargetCalculator.ComputeBoostedWordTarget(
                wordsPerMinute, targetDurationSeconds, speed, effectiveMaxSpeedUpFactor,
                effectiveMaxSlowDownFactor)
            : WordTargetCalculator.ComputeWordTargetFromWordsPerMinute(
                wordsPerMinute, targetDurationSeconds, speed, paceBoostWpm);

        // The band ends are derived from the SLOT, not from `target`: once the target moves off the
        // slot (boosted mid-band aim) a target-relative floor/ceiling would drift with it and stop
        // describing what the pipeline accepts.
        double wordsPerSecond = wordsPerMinute / 60.0 * effectiveSpeed;
        int min = Math.Max(1, (int)Math.Round(
            targetDurationSeconds / (effectiveMaxSlowDownFactor <= 0 ? 1.0 : effectiveMaxSlowDownFactor)
            * wordsPerSecond));
        int max = (int)Math.Round(
            targetDurationSeconds * (effectiveMaxSpeedUpFactor <= 0 ? 1.0 : effectiveMaxSpeedUpFactor)
            * wordsPerSecond);
        return new Band(target, Math.Min(min, target), Math.Max(max, target));
    }
}

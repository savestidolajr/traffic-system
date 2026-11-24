namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Domain service for computing word targets from speech rate metrics.
/// </summary>
public static partial class WordTargetCalculator
{
    private const int MinimumWordCount = 10;

    /// <summary>
    ///     The words this voice reads in <paramref name="durationSeconds" /> at its calibrated rate:
    ///     the slot itself, aimed at exactly. The read is then fitted by the audio stage's bounded
    ///     speed-up/slow-down. Aiming long "so the read lands on the compressible side" was retired
    ///     on 2026-09-02: a 1.15 overshoot became the workflow's notion of the slot, so a 30-second
    ///     bulletin was composed and word-looped to 81 words while the ship-time ceiling and the
    ///     operator's word band both said 70-80, and the ceiling then cut what the loop had paid for.
    /// </summary>
    public static int ComputeWordTargetFromWordsPerMinute(double wordsPerMinute, double durationSeconds,
        double speed = 1.0, double paceBoostWpm = 0)
    {
        if (durationSeconds <= 0 || wordsPerMinute <= 0)
            return MinimumWordCount;

        double effectiveSpeed = speed > 0 ? speed : 1.0;
        double effectiveWpm = wordsPerMinute + Math.Max(0, paceBoostWpm);
        return Math.Max(MinimumWordCount,
            (int)Math.Round(durationSeconds * effectiveWpm / 60.0 * effectiveSpeed));
    }
    /// <summary>
    ///     The natural-read midpoint of the fitter's unboosted acceptance band. The fitter accepts
    ///     a natural read anywhere in [<paramref name="bodyTargetSeconds" />, slot ×
    ///     <paramref name="maxSpeedUpFactor" />] and compresses transparently; aiming at the bare
    ///     slot puts the floor one prediction error away. The unboosted aim therefore sits at the
    ///     band midpoint (decision 2026-09-03, job e55c3619).
    /// </summary>
    public static double MidpointAimSeconds(double bodyTargetSeconds, double maxSpeedUpFactor) =>
        bodyTargetSeconds * (1 + Math.Max(0, maxSpeedUpFactor - 1) / 2);

    /// <summary>
    ///     The minimum natural-read words that can fill a slot after the fitter's allowed trailing
    ///     silence pad and slow-down. A non-positive <paramref name="secondsPerWord" /> has no
    ///     usable pace evidence, so it returns zero.
    /// </summary>
    public static int FloorWords(double targetSeconds, double maxSilencePadSeconds,
        double maxSlowDownFactor, double secondsPerWord) =>
        secondsPerWord <= 0
            ? 0
            : (int)Math.Ceiling(
                ((targetSeconds - maxSilencePadSeconds) / Math.Max(1.0, maxSlowDownFactor))
                / secondsPerWord);

    /// <summary>
    ///     How hard the post-synthesis time-stretch must compress to realise a voice's
    ///     <paramref name="paceBoostWpm" /> — the ratio between the boosted word target and the
    ///     unboosted one, since both read at the same voice speed. 0 boost → 1.0 (no extra
    ///     headroom needed). Callers widen <see cref="Configuration.SpeechDurationOptions.MaxSpeedUpFactor" />
    ///     to at least this for the request, capped at <paramref name="absoluteCeiling" /> to protect
    ///     intelligibility (WSOLA stays transparent well below the raw 2.0x clamp).
    /// </summary>
    public static double ComputeEffectiveMaxSpeedUpFactor(double wordsPerMinute, double paceBoostWpm,
        double configuredMaxSpeedUpFactor, double absoluteCeiling)
    {
        if (paceBoostWpm <= 0 || wordsPerMinute <= 0)
            return configuredMaxSpeedUpFactor;

        double requiredFactor = (wordsPerMinute + paceBoostWpm) / wordsPerMinute;
        double widened = Math.Max(configuredMaxSpeedUpFactor, requiredFactor);
        return Math.Min(widened, Math.Max(absoluteCeiling, configuredMaxSpeedUpFactor));
    }

    /// <summary>
    ///     The largest boost this voice can actually realise: past it
    ///     <see cref="ComputeEffectiveMaxSpeedUpFactor" /> clamps to
    ///     <paramref name="absoluteCeiling" />, <see cref="ComputeBoostedWordTarget" /> stops
    ///     growing, and every extra wpm an operator dials in is a silent no-op. Callers surface the
    ///     shortfall rather than letting the request quietly evaporate.
    ///     <para>
    ///         A slow voice saturates sooner than a fast one — the ceiling is a RATIO, so the
    ///         absolute boost it buys scales with the voice's own rate (135.8 wpm saturates at
    ///         +47.5, 164.1 wpm at +57.4).
    ///     </para>
    /// </summary>
    public static double SaturatingPaceBoostWpm(double wordsPerMinute, double absoluteCeiling) =>
        wordsPerMinute > 0 && absoluteCeiling > 1.0 ? wordsPerMinute * (absoluteCeiling - 1.0) : 0;

    /// <summary>
    ///     Render-to-render duration slack added on top of the aim ceiling before the fitter is
    ///     told how hard it may compress. <see cref="ComputeEffectiveMaxSpeedUpFactor" /> returns
    ///     EXACTLY the ratio the boost needs, so a take that lands on the aim has zero margin: any
    ///     upward render variance overruns the cap and costs a whole re-render.
    ///     <para>
    ///         Sized from a live A/B on dev job a1d40b4c (2026-08-10): 40 real eleven_v3 renders of
    ///         the same voice/settings measured a ±12% duration spread at a FIXED word count (62
    ///         words rendered 25.20-28.56s). Against the 1.2946 aim ceiling that variance alone
    ///         re-rendered 20% of takes; 5% of extra fitter slack (clamped by the absolute ceiling)
    ///         took every one of the 40 renders inside the cap, worst case 29.20s against 29.70s.
    ///     </para>
    /// </summary>
    private const double FitterVarianceMargin = 1.05;

    /// <summary>
    ///     How hard the post-synthesis time-stretch is ALLOWED to compress — the aim ceiling from
    ///     <see cref="ComputeEffectiveMaxSpeedUpFactor" /> plus <see cref="FitterVarianceMargin" />,
    ///     capped at <paramref name="absoluteCeiling" />. Deliberately separate from the aim: widening
    ///     the aim ceiling would also grow <see cref="ComputeBoostedWordTarget" />'s target and
    ///     over-deliver the boost, so the extra slack is granted to the fitter ONLY, purely to absorb
    ///     render variance around an unchanged target.
    /// </summary>
    public static double ComputeFitterMaxSpeedUpFactor(double wordsPerMinute, double paceBoostWpm,
        double configuredMaxSpeedUpFactor, double absoluteCeiling)
    {
        if (paceBoostWpm <= 0 || wordsPerMinute <= 0)
            return configuredMaxSpeedUpFactor;

        double aim = ComputeEffectiveMaxSpeedUpFactor(
            wordsPerMinute, paceBoostWpm, configuredMaxSpeedUpFactor, absoluteCeiling);
        return Math.Min(
            Math.Max(configuredMaxSpeedUpFactor, aim * FitterVarianceMargin),
            Math.Max(absoluteCeiling, configuredMaxSpeedUpFactor));
    }

    /// <summary>
    ///     How far a read may be SLOWED (pitch-preserving) to fill the slot when a voice's
    ///     <paramref name="paceBoostWpm" /> has widened the speed-up side. A boost turns the
    ///     acceptance band asymmetric — the ceiling moves out to
    ///     <see cref="ComputeEffectiveMaxSpeedUpFactor" /> while the floor stays pinned at the
    ///     configured slow-down — and with sentences worth 4-9s each an 8s-wide band admits at most
    ///     ONE integer sentence count, so the trimmer's only escape from an overrun is to undershoot
    ///     the floor (verified live: job b6e03354 trimmed to 63 words → 29.3s against a 30.0s floor
    ///     → content-limited → a whole second generation pass).
    ///     <para>
    ///         The extra slow-down allowance is EARNED by the boost and granted at half its rate: a
    ///         boost needing 1.20x speed-up permits 1.10x slow-down. Half, not full, because slowing
    ///         a read drags audibly sooner than compressing one tightens. Zero boost returns
    ///         <paramref name="configuredMaxSlowDownFactor" /> unchanged, so unboosted voices keep
    ///         today's behaviour exactly.
    ///     </para>
    ///     <para>
    ///         <paramref name="absoluteSlowDownCeiling" /> is a value in its own right, NOT half the
    ///         speed-up ceiling. It used to be derived, which coupled the two ends: raising the
    ///         speed-up ceiling to let a bigger boost through would have silently licensed a
    ///         proportionally draggier underrun fill. The ends are not symmetric and no longer move
    ///         together.
    ///     </para>
    /// </summary>
    public static double ComputeEffectiveMaxSlowDownFactor(double wordsPerMinute, double paceBoostWpm,
        double configuredMaxSlowDownFactor, double absoluteSlowDownCeiling)
    {
        if (paceBoostWpm <= 0 || wordsPerMinute <= 0)
            return configuredMaxSlowDownFactor;

        double requiredFactor = (wordsPerMinute + paceBoostWpm) / wordsPerMinute;
        double earned = 1.0 + ((requiredFactor - 1.0) / 2.0);
        double cap = Math.Max(absoluteSlowDownCeiling, 1.0);
        return Math.Max(configuredMaxSlowDownFactor, Math.Min(earned, cap));
    }

    /// <summary>
    ///     Fraction held back from the compression ceiling when aiming a pace-boosted script.
    ///     Sized from two measurements on dev: eleven_v3 render-to-render duration variance on
    ///     IDENTICAL input is ±2.3s (~±5% of a 45s read), and the draft undershoots its word target
    ///     by up to 7% (observed 68 words against a 73-word target on job 992209ef). 8% clears both
    ///     with ~3.0s of ceiling headroom on a 30s slot while still delivering ~2/3 of the requested
    ///     boost.
    /// </summary>
    /// <remarks>
    ///     Public because the filler reservation needs the same aim point to predict what rate the
    ///     read will AIR at before it is synthesised (see
    ///     <see cref="FillerPaceMatcher.RetimedSeconds" />) — a second copy of 0.08 elsewhere would
    ///     silently mis-size the reservation the day this one is retuned.
    /// </remarks>
    public const double BoostedCeilingMarginFraction = 0.08;

    /// <summary>
    ///     Words to aim for on a PACE-BOOSTED voice: just inside the compression ceiling, NOT on it
    ///     and NOT at the middle of the band.
    ///     <para>
    ///         <see cref="ComputeWordTargetFromWordsPerMinute" /> aims a boosted script at exactly
    ///         <c>slot × ComputeEffectiveMaxSpeedUpFactor</c> natural seconds — the ceiling itself —
    ///         so it is only ever reachable to the last decimal and integer rounding alone can put it
    ///         out of reach (149.6 wpm + 40 boost on a 30s slot rounds 94.8 → 95 words = 38.10s
    ///         against a 38.02s ceiling), which costs a measure-then-trim re-synthesis.
    ///     </para>
    ///     <para>
    ///         Aiming at the band MIDPOINT was tried first and rejected on evidence: it fixed the
    ///         render count (3 dev bulletins, 1 render each, 0 trims) but silently stopped delivering
    ///         the boost, because the midpoint of <c>[slot / slowDown, slot × speedUp]</c> is by
    ///         construction about half of it. Measured on dev jobs bfcc2f61 / 992209ef / f08e3fb4
    ///         against a `PaceBoostWpm=40` ask: +14.3 / −1.4 / +2.4 wpm delivered (36% / −3% / 6%),
    ///         and 992209ef aired SLOWER than the voice's natural pace because the earned slow-down
    ///         headroom was spent stretching a read that was meant to be compressed.
    ///     </para>
    ///     <para>
    ///         So aim at <c>ceiling × (1 - BoostedCeilingMarginFraction)</c>, floored at the slow-fill
    ///         floor for degenerate configs. The widened floor stays a safety net for draft undershoot
    ///         rather than something the aim point leans on.
    ///     </para>
    /// </summary>
    public static int ComputeBoostedWordTarget(double wordsPerMinute, double durationSeconds, double speed,
        double effectiveMaxSpeedUpFactor, double effectiveMaxSlowDownFactor)
    {
        if (durationSeconds <= 0 || wordsPerMinute <= 0)
            return MinimumWordCount;

        double effectiveSpeed = speed > 0 ? speed : 1.0;
        double floorSeconds = durationSeconds / (effectiveMaxSlowDownFactor > 0 ? effectiveMaxSlowDownFactor : 1.0);
        double ceilingSeconds = durationSeconds * (effectiveMaxSpeedUpFactor > 0 ? effectiveMaxSpeedUpFactor : 1.0);
        double aimSeconds = Math.Max(floorSeconds, ceilingSeconds * (1.0 - BoostedCeilingMarginFraction));
        return Math.Max(MinimumWordCount,
            (int)Math.Round(aimSeconds * wordsPerMinute / 60.0 * effectiveSpeed));
    }
}

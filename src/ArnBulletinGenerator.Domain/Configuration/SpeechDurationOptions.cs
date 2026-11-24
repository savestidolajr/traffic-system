namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Configuration for audio pipeline duration tolerances and thresholds.
///     Controls how aggressively the pipeline fine-tunes audio to match target durations.
/// </summary>
public sealed record SpeechDurationOptions
{
    public const string SectionName = "SpeechDuration";

    /// <summary>
    ///     Minimum delta (seconds) before fine-tuning is attempted.
    ///     Deltas smaller than this are considered "at target".
    /// </summary>
    public double FineTuningThresholdSeconds { get; set; } = 0.01;

    /// <summary>
    ///     Maximum slow-down factor (filled ÷ natural) for underrun fill. 1.10 = the read may be
    ///     stretched up to 10% longer (pitch-preserving) to reach the slot when content alone cannot
    ///     fill it. Bounds audible "drag": a ~10% slow stays acceptable on a punchy traffic read.
    ///     A bulletin that cannot reach the slot within this factor (after content growth) is failed
    ///     to the fallback rather than shipped short — ARN requires an exactly-slot-length file.
    ///     PRODUCTION POLICY: dev's <c>appsettings.json</c> overrides this to <c>1.0</c> (disabled —
    ///     see the 2026-06-25 "never slow the read" decision in docs/canonical/decisions.md); at 1.0
    ///     the audio pipeline's slow-fill is a no-op and every underrun is instead closed by content
    ///     growth then a silence pad. This 1.10 code default is deliberately the "mechanism enabled"
    ///     baseline exercised by <c>DeadAirUnderrunReproductionTests</c>, <c>AudioPipelineServiceTests</c>,
    ///     and <c>SlotFitBench</c> — it is NOT the deployed policy; check the environment's config.
    /// <para>
    ///     This same factor defines the content-limited underrun boundary: a target of <c>T</c>
    ///     can be filled from natural speech of at least <c>T / MaxSlowDownFactor</c>, so the
    ///     retry tolerance is <c>T × (1 − 1 / MaxSlowDownFactor)</c>. That yields 2.0 seconds for
    ///     a 22-second slot and 2.7 seconds for a 30-second slot. A read inside this band can
    ///     physically be stretched to the slot; one outside it cannot. Keep this derived rather
    ///     than introducing a second setting that could drift from the fitter's cap.
    /// </para>
    /// </summary>
    public double MaxSlowDownFactor { get; set; } = 1.10;

    /// <summary>
    ///     Maximum trailing silence (seconds) permitted in a shipped bulletin. After slow-to-fill,
    ///     a residual gap up to this is closed with silence so the file is exactly the slot length;
    ///     a larger residual means the slot could not be filled within <see cref="MaxSlowDownFactor" />
    ///     and the take is rejected (→ fallback).
    /// </summary>
    public double MaxSilencePadSeconds { get; set; } = 0.3;

    /// <summary>
    ///     Maximum overrun (seconds) eligible for a speed-up time-stretch. Secondary "too far to
    ///     fix" guard alongside <see cref="MaxSpeedUpFactor" />; larger overruns are left for the
    ///     orchestrator to fix by regenerating a shorter script rather than over-compressing.
    /// </summary>
    public double MaxSafeOverrunTrimSeconds { get; set; } = 4.5;

    /// <summary>
    ///     Maximum speed-up factor (current ÷ target) for overrun correction. 1.15 = up to 15%
    ///     faster, which stays transparent and suits fast traffic reads. Target-independent, so
    ///     it holds regardless of bulletin length. Overruns needing more are regenerated shorter.
    /// </summary>
    public double MaxSpeedUpFactor { get; set; } = 1.15;

    /// <summary>
    ///     Hard ceiling on the per-request speed-up factor when a voice's
    ///     <see cref="ArnBulletinGenerator.Domain.Models.VoiceProfileVersion.PaceBoostWpm" /> needs
    ///     more headroom than <see cref="MaxSpeedUpFactor" /> to realise (see
    ///     <see cref="ArnBulletinGenerator.Domain.Services.WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor" />).
    ///     Backstop against a pathological request; the raw processor clamp is 2.0x.
    ///     <para>
    ///         1.60 is set so the operator-facing
    ///         <see cref="ArnBulletinGenerator.Domain.Models.VoiceProfile.MaxPaceBoostWpm" /> is the
    ///         ONLY cap an operator can hit: a +60 boost needs 1.60x on a 100 wpm voice and less on
    ///         anything faster, so no configured boost is silently truncated. Two caps in series is
    ///         what produced the earlier bug where +60 quietly read as +48.
    ///     </para>
    ///     <para>
    ///         Raised from 1.35 on evidence (2026-08-10): 162 shipped bulletins showed no transcript
    ///         degradation up to 1.336x, and a controlled sweep of one held-constant read through
    ///         this exact WSOLA path (<c>CompressionCeilingBench</c>) scored a FLAT 9.6-12.3% word
    ///         error rate from 1.00x to 2.00x, with identical mismatches at every factor — machine
    ///         intelligibility does not bind anywhere in this range. What remains unmeasured is how
    ///         a compressed read SOUNDS to a person; the bench writes takes for exactly that check.
    ///     </para>
    /// </summary>
    public double AbsolutePaceBoostSpeedUpCeiling { get; set; } = 1.60;

    /// <summary>
    ///     Hard ceiling on the slow-down a pace boost may EARN (see
    ///     <see cref="ArnBulletinGenerator.Domain.Services.WordTargetCalculator.ComputeEffectiveMaxSlowDownFactor" />).
    ///     Deliberately its own value rather than derived from
    ///     <see cref="AbsolutePaceBoostSpeedUpCeiling" />: the two ends are not symmetric. Slowing a
    ///     read drags audibly far sooner than compressing one tightens, and deriving the floor from
    ///     the ceiling meant raising the ceiling to 1.60 would silently license stretching a boosted
    ///     underrun by 1.30x — a draggy read shipped in pursuit of a faster one.
    /// </summary>
    public double AbsolutePaceBoostSlowDownCeiling { get; set; } = 1.15;

    /// <summary>
    ///     Final tolerance (seconds) for accepting a synthesised candidate.
    ///     Candidates within this delta from the target are considered successful.
    /// </summary>
    public double EffectiveToleranceSeconds { get; set; } = 0.15;

    /// <summary>
    ///     Wider tolerance (seconds) for entering the candidate-selection path.
    ///     Candidates within this delta proceed to fine-tuning; outside it, speed is adjusted.
    /// </summary>
    public double CandidateToleranceSeconds { get; set; } = 2.5;

    /// <summary>
    ///     Threshold (seconds) below which a candidate is treated as an exact hit
    ///     and accepted immediately after fine-tuning.
    /// </summary>
    public double ExactHitThresholdSeconds { get; set; } = 0.05;

    /// <summary>
    ///     When true, an underrun is closed by appending a per-station bed/outro trimmed to the
    ///     EXACT residual, leaving the spoken content (read + any filler) untouched — instead of the
    ///     legacy slow-fill + trailing-silence pad. This makes the final duration land on the slot by
    ///     construction (no dead-zone: a residual larger than <see cref="MaxSilencePadSeconds" /> but
    ///     unreachable within <see cref="MaxSlowDownFactor" /> is closed exactly by the bed, not
    ///     failed), and never time-stretches already-correct speech. Default off (identical behaviour
    ///     to today). Airing requires a real per-station bed/outro asset; until one is configured the
    ///     pipeline generates a neutral low-level bed as a placeholder for the duration mechanism.
    /// </summary>
    public bool UseTrimmedBedCloser { get; set; }

    /// <summary>
    ///     When true, a measured read that underruns the scheduled slot may air a SHORTER standard
    ///     slot (<see cref="StandardSlotSeconds" />) that its honest content justifies — closed by the
    ///     trimmed bed — instead of being padded to the full slot with mostly bed. A read too thin for
    ///     even the smallest slot (bed fraction &gt; <see cref="MaxBedFractionForSlot" />) still falls
    ///     back. Requires <see cref="UseTrimmedBedCloser" /> for the exact close. Default off. Airing a
    ///     shorter slot in a fixed rundown is an ARN editorial decision — this is the mechanism.
    /// </summary>
    public bool UseDynamicSlot { get; set; }

    /// <summary>Allowed standard slot lengths (seconds) dynamic-slot may down-select to, longest last.</summary>
    public double[] StandardSlotSeconds { get; set; } = [15.0, 20.0, 30.0, 60.0];

    /// <summary>
    ///     Maximum fraction of a slot that may be bed for a dynamic-slot fill to count as honest. A
    ///     read whose bed would exceed this even at the smallest slot falls back rather than air
    ///     mostly bed (the filler cart). 0.20 ≈ "the read was already ~80% of the slot".
    /// </summary>
    public double MaxBedFractionForSlot { get; set; } = 0.20;
}

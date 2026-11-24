namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One measured (shape → natural spoken seconds) observation, captured at the moment of
///     synthesis for the voice fingerprint that produced it.
///     <para>
///         WHY THIS EXISTS (2026-08-24). The shipped spoken-duration calibration is fitted OFFLINE by
///         reconstructing the (script, measured duration) pair from job records afterwards, and that
///         reconstruction is lossy by nature: <c>Job.ApplyFallback</c> overwrites <c>ScriptText</c>
///         with the fallback SOURCE bulletin's text, and many jobs retain no script at all. Measured
///         on dev the same day: 775 pace observations harvested, only 180 with a usable script —
///         343 lost to a missing script, 151 to a fallback overwrite, 15 to a word-count mismatch.
///         The starved result put the KIIS 90s afternoon voice on a SIX-sample scale (leave-one-out
///         MAE 1.623s against 0.689s for its 25-sample sibling), which over-predicted, over-trimmed
///         the read, and failed the bulletin content-limited.
///     </para>
///     <para>
///         Capturing here makes that loss impossible by construction: the shape and the duration are
///         both in hand at the same instant and cannot be crossed by anything that happens to the job
///         afterwards. It is a CAPTURE record, not a model — the fit stays where it is.
///     </para>
/// </summary>
public sealed record SpokenDurationSample
{
    /// <summary>TTS voice the read was synthesized with.</summary>
    public required string VoiceId { get; init; }

    /// <summary>TTS model (e.g. "eleven_v3"). Part of the fingerprint: the same voice reads at
    ///     materially different rates per model.</summary>
    public required string ModelId { get; init; }

    /// <summary>Provider speed setting used. Part of the fingerprint.</summary>
    public required double Speed { get; init; }

    /// <summary>
    ///     The voice profile's additive WPM boost. Part of the fingerprint even though it never
    ///     reaches the TTS call — pooling boosted and unboosted reads was measured WORSE under
    ///     leave-one-out (1.623s → 1.743s), and the boosted reads run at 136 wpm against 144.
    /// </summary>
    public required double PaceBoostWpm { get; init; }

    /// <summary>
    ///     The token-additive shape sum for this read, evaluated with the SHIPPED shape
    ///     coefficients. This is the regressor the per-voice scale is fitted against, so storing it
    ///     means the fit needs no text. <see cref="SpokenText" /> is kept alongside so a future
    ///     change to the coefficients can re-derive it.
    /// </summary>
    public required double ShapeTotal { get; init; }

    /// <summary>
    ///     Natural spoken seconds — the raw synthesis span BEFORE any slow-fill, silence pad or
    ///     time-warp. Pairing the shape against a warped duration is what "self-poisons the learned
    ///     read rate" (AudioPipelineService), so only the natural span is ever recorded.
    /// </summary>
    public required double NaturalSeconds { get; init; }

    /// <summary>The slot the read was sized for, kept for diagnostics only — never fitted against.</summary>
    public double TargetSeconds { get; init; }

    public int WordCount { get; init; }

    public int SyllableCount { get; init; }

    /// <summary>The exact read that was measured, tags stripped. Small, and it makes a re-fit possible.</summary>
    public string SpokenText { get; init; } = "";

    /// <summary>Job this read belongs to, for audit and for joining back to the narrative.</summary>
    public string JobId { get; init; } = "";

    public required DateTimeOffset CapturedAt { get; init; }

    /// <summary>
    ///     The calibration fingerprint this sample belongs to, formatted to match the shipped
    ///     calibration's own key so a live scale and an embedded scale can never be compared across
    ///     different groupings.
    /// </summary>
    public string Fingerprint => Format(VoiceId, ModelId, Speed, PaceBoostWpm);

    /// <summary>Shared formatter so writer and reader cannot disagree about the key.</summary>
    public static string Format(string voiceId, string modelId, double speed, double paceBoostWpm) =>
        $"{voiceId}|{modelId}|sp{speed:0.##}|pb{paceBoostWpm:0.##}";

    /// <summary>
    ///     Seconds-at-speed-one per unit of shape — the quantity the per-voice scale estimates.
    ///     Normalised by speed exactly as the model divides by it, so a sample taken at 0.97 and one
    ///     taken at 1.0 are directly comparable.
    /// </summary>
    public double ImpliedScale => ShapeTotal > 0 ? NaturalSeconds * Speed / ShapeTotal : 0;
}

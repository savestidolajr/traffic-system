namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Retimes a cached filler clip so it AIRS at the same speaking rate as the traffic read it is
///     stitched onto.
///     <para>
///         Filler audio is cached once per (statement × voice × settings × model) and stitched onto
///         the finished traffic take without passing through the TTS duration-fit loop — that exact,
///         known duration is what lets the engine reserve slot space for it before synthesis, rescue
///         a short read after synthesis, and pad a filler cart when generation has already failed.
///         The cost is that the clip keeps whatever rate ElevenLabs gave a standalone one-sentence
///         render, while the traffic read around it has been time-compressed to fit the slot.
///     </para>
///     <para>
///         Measured on six aired dev bulletins (2026-08-10): the clip articulates a median 8% faster
///         than the read it follows, worst case 35%. In WORDS per minute the same six look like
///         +26% to +58%, but that is word length, not pace — filler averages 1.18 syllables/word
///         ("stay alert out there") against the read's 1.40-1.73 ("Greensborough", "Metropolitan").
///         Syllables per second is the unit that tracks perceived rate, and the unit
///         <see cref="Models.VoiceCalibration" /> is expressed in.
///     </para>
///     <para>
///         The correction is NOT the compression factor the read received. Applying that to the clip
///         cancels out of the ratio and restores the uncompressed mismatch (+28% on the same six) —
///         the gap is inherent to the cached render, and the read's compression is what accidentally
///         closes most of it. Matching the AIRED rate means retiming against that compression, which
///         is a mild slow-down: median x1.08.
///     </para>
/// </summary>
public static class FillerPaceMatcher
{
    /// <summary>
    ///     Largest retime factor applied in either direction. A cached clip is only a few seconds
    ///     long, but WSOLA drags audibly past roughly a tenth either way and the outlier case wants
    ///     x1.35 — clamping trades an exact rate match on the worst clip (which would sound stretched)
    ///     for a partial one that does not. At x1.20 the measured +35% outlier lands at +13% and the
    ///     four clips already inside 10% are untouched, since their own factor is below the clamp.
    /// </summary>
    public const double MaxRetimeFactor = 1.20;

    /// <summary>
    ///     Seconds the clip should occupy so it airs at <paramref name="readRate" />, given its own
    ///     <paramref name="clipRate" />. Both rates MUST be measured the same way — articulation
    ///     against articulation, or gross against gross. Mixing them silently inverts the
    ///     correction: an articulation-derived ideal compared against a GROSS cached duration
    ///     counts the clip's own lead, trail and internal silence as if it were speech, and asks
    ///     for a compression on a clip that needed stretching (found live on dev job 026b6b1d,
    ///     2026-08-10: 6.76s -> 6.42s where the clip wanted ~7.9s).
    ///     <para>
    ///         Scaling the whole clip by the RATIO also keeps its internal pauses proportional,
    ///         which is what makes the join sound like one continuous read rather than a clip with
    ///         re-cut gaps.
    ///     </para>
    ///     Returns <paramref name="cachedDurationSeconds" /> unchanged when either rate is
    ///     unusable, so a missing calibration, an empty clip or a clip cached without word timings
    ///     degrades to stitch-as-is.
    /// </summary>
    public static double RetimedSeconds(double cachedDurationSeconds, double clipRate, double readRate)
    {
        if (cachedDurationSeconds <= 0 || clipRate <= 0 || readRate <= 0)
            return cachedDurationSeconds;

        double factor = Math.Clamp(clipRate / readRate, 1.0 / MaxRetimeFactor, MaxRetimeFactor);
        return cachedDurationSeconds * factor;
    }

    /// <summary>
    ///     Syllables per second over a clip's whole stored duration, silence included — the only
    ///     rate available before synthesis, where the read side is likewise the voice's gross
    ///     calibrated rate. Used to SIZE the slot reservation; the stitch itself re-measures both
    ///     sides as articulation.
    /// </summary>
    public static double GrossRate(int syllableCount, double seconds) =>
        syllableCount > 0 && seconds > 0 ? syllableCount / seconds : 0;

    /// <summary>
    ///     Syllables per second of a stretch of measured speech, counting only time INSIDE words.
    ///     Inter-word silence is excluded deliberately: a traffic read carries sentence pauses a
    ///     three-second filler clip does not, and including them would understate the read's rate and
    ///     ask for a slow-down the listener never needed.
    /// </summary>
    public static double ArticulationRate(IReadOnlyList<Models.WordTiming> timings)
    {
        if (timings is null || timings.Count == 0)
            return 0;

        int syllables = 0;
        double spoken = 0;
        foreach (var t in timings)
        {
            syllables += SyllableCounter.CountSyllables(t.Word);
            double d = t.EndSeconds - t.StartSeconds;
            if (d > 0)
                spoken += d;
        }

        return spoken > 0 ? syllables / spoken : 0;
    }
}

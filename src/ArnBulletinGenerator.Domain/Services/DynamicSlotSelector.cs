namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Chooses the broadcast slot a measured honest read justifies. After the read is synthesized and
///     measured, a thin read airs a SHORTER standard slot (so a trimmed-bed close stays honest — the
///     bed is a small fraction of the slot) instead of being padded to the full slot with mostly bed
///     or dropped to the fallback cart. Pure and deterministic; the honesty gate (maximum acceptable
///     bed fraction) is applied by the caller from the returned slot.
/// </summary>
public static partial class DynamicSlotSelector
{
    /// <summary>
    ///     The smallest standard slot ≤ <paramref name="originalTargetSeconds" /> that the measured
    ///     read fits (read ≤ slot · <paramref name="maxSpeedUpFactor" />). Returns the original slot
    ///     when the read is at/over it (nothing to shorten) or when no smaller standard slot fits.
    ///     The caller decides fill-vs-fallback from the resulting bed fraction: a read too thin even
    ///     for the smallest slot yields a large fraction and should fall back, not air mostly bed.
    /// </summary>
    /// <param name="readSpokenSeconds">Measured spoken length of the honest read.</param>
    /// <param name="originalTargetSeconds">The originally scheduled slot length.</param>
    /// <param name="standardSlots">Allowed standard slot lengths (e.g. 15/20/30/60s).</param>
    /// <param name="maxSpeedUpFactor">Compression headroom — a read up to this factor over a slot still "fits" it.</param>
    public static double Select(
        double readSpokenSeconds,
        double originalTargetSeconds,
        IReadOnlyList<double> standardSlots,
        double maxSpeedUpFactor = 1.10)
    {
        if (readSpokenSeconds <= 0 || standardSlots is null || standardSlots.Count == 0)
            return originalTargetSeconds;

        // Dynamic slot only shortens an underrun; a read already at/over the slot keeps it (compress).
        if (readSpokenSeconds >= originalTargetSeconds)
            return originalTargetSeconds;

        double chosen = originalTargetSeconds;
        foreach (double slot in standardSlots)
        {
            if (slot <= 0 || slot > originalTargetSeconds + 1e-9)
                continue;
            if (readSpokenSeconds <= slot * maxSpeedUpFactor + 1e-9 && slot < chosen)
                chosen = slot;
        }

        return chosen;
    }
}

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     How many rundown items (incident corridors + good-run roads) a bulletin slot can hold,
///     derived from the configured per-item speech cost
///     (<see cref="Configuration.BroadcastOptions.SlotSecondsPerItem" />).
///     <para>
///     The legacy 5.0 s/item assumption is ~2x the measured on-air cost (~3 s/item at the corpus
///     mean 185 wpm), which starved 30s slots at 6 items while the word budget demanded ~100
///     words — the dominant content-limited fallback cause (2026-07 fallback corpus; replay-panel
///     A/B capacity 6 vs 10: 9/10 fixtures improved, deep-shortfall fixtures gained +3-4s of
///     honest content). The 6-item floor keeps sparse markets viable (and leaves short slots
///     unchanged); the 15-item cap bounds prompt size.
///     </para>
/// </summary>
public static partial class SlotCapacity
{
    public const int MinItems = 6;
    public const int MaxItems = 15;
    public const double LegacySecondsPerItem = 5.0;

    /// <summary>Items the slot can hold; a non-positive <paramref name="secondsPerItem" /> falls back to legacy 5.0.</summary>
    public static int Compute(double targetDurationSeconds, double secondsPerItem)
    {
        double perItem = secondsPerItem > 0 ? secondsPerItem : LegacySecondsPerItem;
        return Math.Clamp((int)(targetDurationSeconds / perItem), MinItems, MaxItems);
    }
}

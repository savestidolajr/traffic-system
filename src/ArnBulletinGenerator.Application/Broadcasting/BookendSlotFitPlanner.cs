using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Decides, BEFORE any drafting or synthesis, whether a station's bookend pair can share a
///     slot with a minimum viable traffic read. The 2026-08-05 bug bash (finding 4) measured the
///     consequence of skipping this: the live 17-word pair on a 15s slot (bookends ≈ 45% of the
///     slot) spiralled through 4 generations and 4+ TTS renders — 14.0 → 17.4 → 13.0 → 11.0s —
///     never converged, and landed on the generic filler cart with <c>ScriptText=null</c>: NEITHER the
///     traffic read NOR the station's top-and-tail aired. The product policy is decided: prefer
///     the traffic content. When the pair cannot fit alongside a minimum viable read, the engine
///     drops the bookend and airs the traffic (see <see cref="ScriptGenerationEngine" />), and the
///     schedule trigger uses this same calculation to warn an admin at assignment time.
///     <para>
///         Pure and deterministic: no I/O, no clock. Callable with just the data a schedule PUT
///         has in hand — slot seconds, the resolved pair's word count, and a pace — so the
///         assignment gate can run it synchronously without a live job or a synthesiser.
///     </para>
/// </summary>
public static class BookendSlotFitPlanner
{
    /// <summary>
    ///     The shortest read this platform will air as a traffic bulletin: a headline sentence
    ///     plus one grounded detail (~21 words, two short sentences). Below this the read is an
    ///     interjection, not a bulletin — and a bookend pair that leaves less than this much room
    ///     cannot be serviced by the duration-fit loop, whose acceptance windows (zero slow-down
    ///     tolerance under the slot, ~2.25s trim allowance over it) are smaller than the natural
    ///     variance of a read squeezed into the remainder.
    ///     <para>
    ///         Calibrated against the measured live failure (job f9907416, 2026-08-05): a 17-word
    ///         pair (7 intro + 10 outro) on a 15s slot at 150 wpm left 8.2s for traffic — the
    ///         duration-fit loop could not converge on that room (4 generations: 14.0 → 17.4 →
    ///         13.0 → 11.0s — never within tolerance) and the bulletin fell to the filler cart.
    ///         8.2s of room was demonstrably NOT enough, so the minimum must be strictly greater:
    ///         21 words = 8.4s at 150 wpm (the smallest whole-word increment that clears the
    ///         measured envelope; 22 words would also clear it but lands the 18-word-pair-on-a-16s-
    ///         slot retain guard exactly on the boundary). Do NOT lower this back to 20 — that
    ///         re-commits the exact failure this planner exists to prevent.
    ///     </para>
    ///     <para>
    ///         This is a WORD count (an editorial-policy judgment), deliberately NOT a seconds
    ///         constant: <see cref="Evaluate" /> converts it to seconds at the caller's
    ///         seconds-per-word — the same pace model the writer's word target is sized with — so
    ///         the seconds threshold moves with the voice (a slow voice needs more seconds of
    ///         traffic than a fast one) and the schedule gate and the engine always agree.
    ///     </para>
    /// </summary>
    public const int MinimumViableTrafficWords = 21;

    /// <summary>Evaluates the resolved pair a schedule/station actually carries.</summary>
    public static BookendFitResult Evaluate(
        Option<ResolvedBookends> bookends,
        double slotSeconds,
        double secondsPerWord) =>
        Evaluate(BulletinBookendComposer.WordCount(bookends), slotSeconds, secondsPerWord);

    /// <summary>Evaluates a pair by its combined intro+outro word count.</summary>
    public static BookendFitResult Evaluate(int bookendWordCount, double slotSeconds, double secondsPerWord)
    {
        // Seconds are rounded to millisecond precision so the reported arithmetic is exact and
        // the verdict always matches the numbers shown to the operator — without it, 17×0.4 + 21×0.4
        // − 15.0 evaluates to 0.1999999999999993 instead of the 0.2s shortfall the verdict reports.
        double bookendSeconds = Math.Round(Math.Max(0, bookendWordCount) * secondsPerWord, 3);
        double minimumViableTrafficSeconds = Math.Round(MinimumViableTrafficWords * secondsPerWord, 3);
        return new BookendFitResult(
            Fits: bookendSeconds + minimumViableTrafficSeconds <= slotSeconds,
            BookendSeconds: bookendSeconds,
            MinimumViableTrafficSeconds: minimumViableTrafficSeconds,
            SlotSeconds: slotSeconds);
    }
}

/// <summary>
///     The outcome of a slot-fit check — richer than a bool so a caller (the engine's pre-draft
///     short-circuit or the schedule trigger's assignment gate) can tell the operator WHY the
///     pair does not fit: how much the bookends claim, what the minimum viable traffic read
///     needs, and how far the two together overshoot the slot.
/// </summary>
public sealed record BookendFitResult(
    bool Fits,
    double BookendSeconds,
    double MinimumViableTrafficSeconds,
    double SlotSeconds)
{
    /// <summary>Bookends plus the minimum viable traffic read, in slot seconds.</summary>
    public double RequiredSeconds => Math.Round(BookendSeconds + MinimumViableTrafficSeconds, 3);

    /// <summary>How far the pair plus a minimum read overshoot the slot; 0 when it fits.</summary>
    public double ShortfallSeconds => Math.Round(Math.Max(0, RequiredSeconds - SlotSeconds), 3);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Selects per-market evergreen filler statements to top up a bulletin whose real traffic,
///     weather, and fuel content cannot fill the slot. Filler is ARN's own editorial content —
///     applied deterministically by the pipeline AFTER generation, review, refine, and safety
///     (BroadcastOrchestrator.GenerateAndSynthesizeAsync), so no LLM call ever sees it: it is
///     never critiqued, reworded, or cut. It closes the slot honestly instead of the writer
///     manufacturing a vague aggregate ("a few more hold-ups around town"). Pure and
///     deterministic: no I/O, no clock. Per-statement availability windows
///     (<see cref="FillerAvailability" />) are deliberately NOT evaluated here — they
///     are filtered at the load boundaries (MarketContentFetcher, FallbackAudioPadder) against
///     the station-local now, so this selector never needs a clock to stay clock-free.
/// </summary>
public static partial class FillerStatementSelector
{
    /// <summary>
    ///     Returns the filler statements the pipeline should append (verbatim, in order) to close
    ///     a slot with <paramref name="estimatedGapSeconds"/> of unfilled time after the real
    ///     content's actual measured/estimated duration — selected to COVER the gap, not merely
    ///     the single best fit. The 1-Jul feedback is explicit that fill is time-based ("filler
    ///     sentence x runs between 12 and 3"): a thin market with one closure leaves a 15–20s hole
    ///     that one 5–8s statement cannot fill, and an under-filled script does not air short — it
    ///     risks the content-limited fallback cart, the exact outcome the filler library exists to
    ///     prevent. Selection is greedy best-fit-decreasing: each round takes the statement whose
    ///     OWN estimated spoken length (<see cref="EstimateSpokenSeconds"/>, word count only — no
    ///     admin-declared range) best covers the remaining gap without exceeding it, subtracts
    ///     that cost, and repeats while meaningful gap remains and room for another candidate
    ///     exists, capped at <paramref name="maxStatements"/> so filler tops up a bulletin and
    ///     never dominates it. A statement qualifies when it is enabled, its own spoken length
    ///     fits within the REMAINING gap, its text was not already voiced in
    ///     <paramref name="previousSameDayScript"/> (per-station same-day anti-repeat), and its
    ///     NORMALISED text was not already picked earlier in this same call — two entries with
    ///     identical wording under different ids must never both air, or the same sentence is read
    ///     twice in one bulletin. Returns an empty list when the slot is already full
    ///     (<paramref name="estimatedGapSeconds"/> ≤ 0) so a full bulletin never appends filler
    ///     it does not need.</summary>
    /// </summary>
    /// <param name="statements">All configured filler statements for the market.</param>
    /// <param name="estimatedGapSeconds">Estimated unfilled slot time after real content.</param>
    /// <param name="previousSameDayScript">This station's previous same-day script, for anti-repeat.</param>
    /// <param name="maxStatements">Hard ceiling on appended filler — top-up, never the bulletin's body.</param>
    /// <param name="wordsPerSecond">
    ///     The SAME rate the caller used to derive <paramref name="estimatedGapSeconds"/> (e.g. the
    ///     voice's calibrated words-per-second). MUST match, or the gap and each candidate's cost are
    ///     computed on different speaking-rate assumptions — a voice calibrated faster than this
    ///     default leaves a real gap the generic rate makes filler look too "expensive" to fit,
    ///     silently excluding filler that would fit at the voice's true pace (observed live: ARN
    ///     Hudson at 2.85 wps vs this 2.5 wps default excluded every configured statement from a
    ///     genuine 3-5s residual gap). Defaults to the historical generic estimate for callers that
    ///     size the gap the same coarse way (the eval harness, and old test expectations).
    /// </param>
    /// <param name="overshootToleranceSeconds">
    ///     When no candidate fits WITHIN the remaining gap, allow closing it with the smallest
    ///     statement that overshoots by at most this many seconds (then stop selecting). 0 keeps
    ///     the strict no-overshoot contract. The post-synthesis rescue pass uses this: a residual
    ///     gap smaller than the shortest configured statement is otherwise unfillable, yet a small
    ///     overrun is safe — the audio pipeline compresses it within the speed-up/trim budget,
    ///     while an underrun larger than the silence pad fails the slot outright.
    /// </param>
    /// <param name="acceptableResidualSeconds">
    ///     How much of the gap may stay uncovered and still count as closed (the caller's silence
    ///     pad). When the greedy pass leaves more than this and no overshoot pick closes the
    ///     remainder, the pass is abandoned in favour of the smallest single statement that covers
    ///     the WHOLE gap inside <paramref name="overshootToleranceSeconds"/> — an overrun the
    ///     pipeline can compress beats a residual it cannot fill. Dev 34402fbb (2026-09-02): a 4.5s
    ///     gap took a 3.2s statement, nothing fit the 1.3s left, and the padded take was rejected.
    /// </param>
    public static IReadOnlyList<FillerStatement> Select(
        IReadOnlyList<FillerStatement> statements,
        double estimatedGapSeconds,
        string? previousSameDayScript = null,
        int maxStatements = 3,
        double wordsPerSecond = WordsPerSecond,
        double overshootToleranceSeconds = 0,
        double acceptableResidualSeconds = 0)
    {
        if (statements.Count == 0 || estimatedGapSeconds <= 0)
            return [];

        string previous = previousSameDayScript is null
            ? string.Empty
            : Normalize(previousSameDayScript);

        var pool = statements
            .Where(s => s.Enabled)
            .Where(s => !string.IsNullOrWhiteSpace(s.Text))
            .Where(s => previous.Length == 0 || !previous.Contains(Normalize(s.Text), StringComparison.Ordinal))
            .ToList();

        var selected = new List<FillerStatement>();
        var selectedTexts = new HashSet<string>(StringComparer.Ordinal);
        double remaining = estimatedGapSeconds;

        while (selected.Count < maxStatements && remaining > 0)
        {
            // A statement whose normalised text matches one already picked this round is
            // ineligible: two entries with identical wording under different ids would otherwise
            // both fit and air the same sentence twice (copy-paste in the composer).
            var eligible = pool
                .Where(s => !selectedTexts.Contains(Normalize(s.Text)))
                .ToList();
            if (eligible.Count == 0)
                break;

            // Best-fit-decreasing for the REMAINING gap: the statement whose own spoken length
            // best covers it without overshooting, so a large gap reaches for a longer statement
            // before a snippet.
            double gap = remaining;
            var next = eligible
                .Select(s => (Statement: s, Cost: EstimateSpokenSeconds(s.Text, wordsPerSecond)))
                .Where(x => x.Cost <= gap)
                .OrderByDescending(x => x.Cost)
                .ThenBy(x => x.Statement.Id, StringComparer.Ordinal)
                .Select(x => x.Statement)
                .FirstOrDefault();
            if (next is null && overshootToleranceSeconds > 0)
            {
                // Nothing fits within the gap; close it with the SMALLEST statement that
                // overshoots inside the tolerance, then stop — the gap is covered.
                next = eligible
                    .Select(s => (Statement: s, Cost: EstimateSpokenSeconds(s.Text, wordsPerSecond)))
                    .Where(x => x.Cost > gap && x.Cost <= gap + overshootToleranceSeconds)
                    .OrderBy(x => x.Cost)
                    .ThenBy(x => x.Statement.Id, StringComparer.Ordinal)
                    .Select(x => x.Statement)
                    .FirstOrDefault();
                if (next is not null)
                {
                    selected.Add(next);
                    return selected;
                }
            }
            if (next is null)
                break;

            selected.Add(next);
            selectedTexts.Add(Normalize(next.Text));
            pool.Remove(next);
            remaining -= EstimateSpokenSeconds(next.Text, wordsPerSecond);
        }

        if (remaining > acceptableResidualSeconds && overshootToleranceSeconds > 0)
        {
            var wholeGapCover = pool
                .Concat(selected)
                .Select(s => (Statement: s, Cost: EstimateSpokenSeconds(s.Text, wordsPerSecond)))
                .Where(x => x.Cost > estimatedGapSeconds
                    && x.Cost <= estimatedGapSeconds + overshootToleranceSeconds)
                .OrderBy(x => x.Cost)
                .ThenBy(x => x.Statement.Id, StringComparer.Ordinal)
                .Select(x => x.Statement)
                .FirstOrDefault();
            if (wholeGapCover is not null)
                return [wholeGapCover];
        }

        return selected;
    }

    /// <summary>
    ///     Coarse spoken-seconds estimate of a statement's text at <paramref name="wordsPerSecond"/> —
    ///     used both to gate eligibility (does this statement's own length fit the remaining
    ///     gap?) and to decrement the remaining gap as statements are picked. Derived from the
    ///     text alone; admins author no duration.
    /// </summary>
    private static double EstimateSpokenSeconds(string text, double wordsPerSecond) =>
        WordCounter.Count(text) / wordsPerSecond;

    /// <summary>Historical generic spoken pace — the default rate when a caller has none calibrated.</summary>
    private const double WordsPerSecond = 2.5;

    /// <summary>
    ///     Punctuation-stripped, lower-invariant, trimmed form of a statement's text — the identity
    ///     used for same-day anti-repeat and for in-bulletin distinctness (two statements differing
    ///     only in punctuation or case would otherwise air as an audible duplicate). Public so the
    ///     generation pipeline's fallback-padding pass and this selector share ONE normaliser
    ///     instead of drifting apart.
    /// </summary>
    public static string Normalize(string text) =>
        new string(text.Where(c => !char.IsPunctuation(c)).ToArray())
            .ToLowerInvariant()
            .Trim();
}

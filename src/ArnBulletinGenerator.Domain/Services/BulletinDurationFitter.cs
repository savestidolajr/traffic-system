using System.Numerics;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     One sentence of a bulletin script mapped onto the time axis of a synthesized
///     take, using the TTS provider's character-level alignment. <see cref="EndSeconds" />
///     is the moment the sentence finishes speaking in that take.
///     <para>
///         <see cref="Text" /> is the SPOKEN sentence as <see cref="ScriptSentenceSplitter" />
///         yields it — inline delivery tags excluded. <see cref="Source" /> is the verbatim script
///         slice it came from, tags INCLUDED, and is what a trim must rejoin: rejoining
///         <see cref="Text" /> deleted every audio tag, so an overrunning read was re-synthesized
///         with no delivery direction at all and the job recorded no delivery provenance (observed
///         live on dev jobs ee6ee37a / db319bcf, 2026-07-31).
///     </para>
/// </summary>
public sealed record SentenceSpan(string Text, double StartSeconds, double EndSeconds, string? Source = null)
{
    /// <summary>The verbatim script slice for this sentence, falling back to the spoken text.</summary>
    public string SourceText => Source ?? Text;
}

/// <summary>
///     Measure-then-fit duration control for bulletins. Rather than predicting a word
///     count and regenerating on a miss, we synthesize once, MEASURE where each sentence lands from
///     the TTS character alignment, then fit the slot by removing the smallest amount of traffic
///     content needed. The fitter keeps the weather/fuel close and locked bookends, while the
///     caller's pitch-preserving micro-stretch closes the small residual.
/// </summary>
public static partial class BulletinDurationFitter
{
    /// <summary>
    ///     Maps each sentence of <paramref name="script" /> to its [start, end] seconds in the
    ///     synthesized take using the per-character <paramref name="alignment" />. Sentences are
    ///     segmented with <see cref="ScriptSentenceSplitter" /> (the canonical splitter), then
    ///     located back in the script to index the alignment. Returns an empty list when the
    ///     script or alignment is empty.
    /// </summary>
    public static IReadOnlyList<SentenceSpan> MapSentenceSpans(string script, AudioAlignment alignment)
    {
        if (string.IsNullOrWhiteSpace(script) || alignment is null)
            return [];

        int n = Math.Min(alignment.Characters.Count,
            Math.Min(alignment.StartSeconds.Count, alignment.EndSeconds.Count));
        if (n == 0)
            return [];

        var sentences = ScriptSentenceSplitter.Split(script);
        if (sentences.Count == 0)
            return [];

        var spans = new List<SentenceSpan>(sentences.Count);
        int cursor = 0;
        foreach (var sentence in sentences)
        {
            int idx = script.IndexOf(sentence, cursor, StringComparison.Ordinal);
            if (idx < 0)
                idx = script.IndexOf(sentence, StringComparison.Ordinal);
            if (idx < 0)
                continue;

            int startChar = Math.Clamp(idx, 0, n - 1);
            int endChar = Math.Clamp(idx + sentence.Length - 1, 0, n - 1);
            // Everything sitting between the previous sentence and this one — inline delivery tags
            // above all — belongs to THIS sentence's verbatim slice, so a trim that keeps the
            // sentence keeps its direction with it.
            int sliceStart = Math.Min(cursor, idx);
            string source = script[sliceStart..(idx + sentence.Length)].Trim();
            cursor = idx + sentence.Length;

            double start = alignment.StartSeconds[startChar];
            double end = alignment.EndSeconds[endChar];
            if (end < start)
                end = start;

            spans.Add(new SentenceSpan(sentence, start, end, source));
        }

        return spans;
    }

    /// <summary>
    ///     Maps each sentence of <paramref name="script" /> onto an ESTIMATED time axis using
    ///     <paramref name="estimateSeconds" />, for use BEFORE any synthesis exists. Spans are made
    ///     cumulative exactly as <see cref="MapSentenceSpans" /> produces them from real alignment, so
    ///     both feed the same <see cref="FitByTrimming(IReadOnlyList{SentenceSpan}, double, double, int)" />
    ///     selection — including its weather/fuel close protection. Sentence slices keep their inline
    ///     delivery tags for the same reason as the alignment path: a trim must not silently strip
    ///     direction off the sentences it keeps.
    /// </summary>
    public static IReadOnlyList<SentenceSpan> EstimateSentenceSpans(
        string script, Func<string, double> estimateSeconds)
    {
        if (string.IsNullOrWhiteSpace(script) || estimateSeconds is null)
            return [];

        var sentences = ScriptSentenceSplitter.Split(script);
        if (sentences.Count == 0)
            return [];

        var spans = new List<SentenceSpan>(sentences.Count);
        int cursor = 0;
        double elapsed = 0;
        foreach (var sentence in sentences)
        {
            int idx = script.IndexOf(sentence, cursor, StringComparison.Ordinal);
            if (idx < 0)
                idx = script.IndexOf(sentence, StringComparison.Ordinal);
            if (idx < 0)
                continue;

            int sliceStart = Math.Min(cursor, idx);
            string source = script[sliceStart..(idx + sentence.Length)].Trim();
            cursor = idx + sentence.Length;

            double start = elapsed;
            elapsed += Math.Max(0.0, estimateSeconds(sentence));
            spans.Add(new SentenceSpan(sentence, start, elapsed, source));
        }

        return spans;
    }

    /// <summary>
    ///     Rescales spans so their total equals <paramref name="totalSeconds" /> — the whole-script
    ///     prediction — while keeping each sentence's share of the read.
    ///     <para>
    ///         REQUIRED whenever the per-sentence estimator is a per-utterance model.
    ///         <see cref="EstimateSentenceSpans" /> calls it once per sentence, so any fixed
    ///         per-read constant in that model is charged N times and the sum runs far above the
    ///         single whole-script figure the caller is comparing against. Measured live
    ///         2026-08-20 (dev regeneration, 22s slot): the log reported a 28.0s whole-script
    ///         prediction, but the summed spans put the same read high enough that fitting to the
    ///         24.55s budget cut 59 words to 31 — a 12.7s read for a 22s slot, which then needed
    ///         stretching well past the 1.10x slow-down ceiling and pulled in filler.
    ///         Rescaling makes the fitter's arithmetic agree with the number in the log.
    ///     </para>
    /// </summary>
    public static IReadOnlyList<SentenceSpan> RescaleTo(
        IReadOnlyList<SentenceSpan> spans, double totalSeconds)
    {
        if (spans.Count == 0 || totalSeconds <= 0.0)
            return spans;

        double summed = spans[^1].EndSeconds;
        if (summed <= 0.0)
            return spans;

        double factor = totalSeconds / summed;
        var rescaled = new List<SentenceSpan>(spans.Count);
        foreach (SentenceSpan span in spans)
            rescaled.Add(span with
            {
                StartSeconds = span.StartSeconds * factor,
                EndSeconds = span.EndSeconds * factor,
            });

        return rescaled;
    }

    /// <summary>
    ///     Returns the largest count of leading sentences whose audio ends within
    ///     <c>targetSeconds + maxOverrunSeconds</c>, clamped to at least <paramref name="minSentences" />.
    ///     This helper remains a prefix calculation for callers that explicitly need a count; the
    ///     full fitter below uses sentence subsets so a long middle item does not discard later
    ///     short items.
    /// </summary>
    public static int SelectFittingCount(
        IReadOnlyList<SentenceSpan> spans,
        double targetSeconds,
        double maxOverrunSeconds,
        int minSentences = 1)
    {
        if (spans.Count == 0)
            return 0;

        double budget = targetSeconds + Math.Max(0.0, maxOverrunSeconds);
        int keep = 0;
        for (int k = 1; k <= spans.Count; k++)
        {
            if (spans[k - 1].EndSeconds <= budget)
                keep = k;
            else
                break;
        }

        return Math.Clamp(Math.Max(keep, minSentences), 1, spans.Count);
    }

    /// <summary>
    ///     Produces a shorter script that fits <paramref name="targetSeconds" /> by dropping the
    ///     minimum amount of traffic content. The selection is exhaustive for the single-digit
    ///     sentence counts used by bulletins: it first drops as few sentences as possible, then
    ///     the least total duration, and finally chooses the result closest to the budget from
    ///     below. The weather/fuel lines in <paramref name="fixtures" /> and the station outro are
    ///     protected before traffic is shed. Required fixtures are never deleted wherever they sit,
    ///     even when that forces the caller to compress beyond the nominal allowance (observed live
    ///     on dev job 71568e4eac, Port Lincoln, 15s).
    ///     <para>
    ///         The fitter used to know only the ceiling. On dev Gold Coast job 69965aa3
    ///         (2026-09-03), it dropped a roughly 7s sentence from a roughly 35s read into a
    ///         27.8s take that could not be stretched because <c>MaxSlowDownFactor</c> was 1.0
    ///         and the silence pad was 0.3s, so the slot failed. With a positive
    ///         <paramref name="minSeconds" /> floor, a non-required close returns <c>null</c> when
    ///         no ordinary subset reaches that floor, leaving the overrun to the pipeline's TTS
    ///         speed re-render, which can compress the full read honestly. The historical
    ///         floor-overrun fallback remains for a zero floor and for required closes.
    ///     </para>
    /// </summary>
    /// <param name="minSeconds">
    ///     Minimum natural-read duration for an ordinary kept subset. The floor-overrun fallback may
    ///     return a shorter floor when this is zero or a close is required.
    /// </param>
    public static string? FitByTrimming(
        string script,
        AudioAlignment alignment,
        double targetSeconds,
        double maxOverrunSeconds = 2.0,
        int minSentences = 1,
        string? lockedSuffix = null,
        FixtureLines? fixtures = null,
        string? lockedPrefix = null,
        double minSeconds = 0)
    {
        return FitByTrimming(
            MapSentenceSpans(script, alignment), targetSeconds, maxOverrunSeconds, minSentences,
            lockedSuffix, fixtures, lockedPrefix, minSeconds);
    }

    /// <summary>
    ///     The trim itself, over spans from EITHER a real synthesis
    ///     (<see cref="MapSentenceSpans" />) or a pre-synthesis estimate
    ///     (<see cref="EstimateSentenceSpans" />). Both callers use the same subset selection,
    ///     close protection and overrun arithmetic, so a trim decided before synthesis and one
    ///     decided after it cannot diverge in what they consider droppable.
    /// </summary>
    /// <param name="minSeconds">
    ///     Minimum natural-read duration for an ordinary kept subset. The floor-overrun fallback may
    ///     return a shorter floor when this is zero or a close is required.
    /// </param>
    public static string? FitByTrimming(
        IReadOnlyList<SentenceSpan> spans,
        double targetSeconds,
        double maxOverrunSeconds = 2.0,
        int minSentences = 1,
        string? lockedSuffix = null,
        FixtureLines? fixtures = null,
        string? lockedPrefix = null,
        double minSeconds = 0)
    {
        if (spans.Count <= minSentences)
            return null;

        // The station's locked INTRO must never be the sentence the keep-floor "saves": a collapse
        // that kept exactly one sentence kept the intro and aired a weather-only read (observed
        // live, dev jobs 9bb43d88/dce23ad1, 2026-08-17). The intro is excluded from the traffic
        // pool: it is always kept AND never counts toward minSentences.
        int prefixCount = lockedPrefix is not null && spans.Count > 0
                          && NormalizeForSuffixMatch(spans[0].Text) == NormalizeForSuffixMatch(lockedPrefix)
            ? 1
            : 0;

        int tailStart = LockedTailStart(spans, lockedSuffix, fixtures);
        bool hasClose = tailStart > prefixCount && tailStart < spans.Count;
        bool closeIsRequired = fixtures is { Required: true };
        var weightedSpans = WithSentenceCosts(spans);
        var prefix = weightedSpans.Take(prefixCount).ToList();
        var traffic = weightedSpans.Skip(prefixCount)
            .Take((hasClose ? tailStart : spans.Count) - prefixCount).ToList();
        var close = hasClose ? weightedSpans.Skip(tailStart).ToList() : [];
        double budget = targetSeconds + Math.Max(0.0, maxOverrunSeconds);

        // Keep the full script whenever possible. This also avoids rewriting a read whose
        // sentence-level sum differs from the alignment's tiny inter-sentence gaps.
        if (spans[^1].EndSeconds - spans[0].StartSeconds <= budget)
            return null;

        // A required fixture that sits inside the traffic block (weather before fuel before a
        // closing traffic call) is pinned like the prefix: never a candidate, always kept in place.
        Func<SentenceSpan, bool> pinned = closeIsRequired
            ? span => fixtures!.Contains(span.Text)
            : _ => false;

        var selected = SelectTrafficSubset(prefix, traffic, close, budget, minSentences,
            closeIsRequired, pinned, minSeconds: minSeconds, allowFloorOverrun: true);
        if (selected is not null)
            return Join(selected);

        // A preferred close may be dropped only when it cannot coexist with even the minimum
        // traffic floor. A required close never reaches this fallback.
        if (hasClose && !closeIsRequired)
        {
            selected = SelectTrafficSubset(prefix, weightedSpans.Skip(prefixCount).ToList(), [], budget,
                minSentences, closeIsRequired: false, pinned: pinned, minSeconds: minSeconds,
                allowFloorOverrun: true);
        }

        if (selected is null)
            return null;

        return Join(selected);
    }

    private static IReadOnlyList<SentenceSpan>? SelectTrafficSubset(
        IReadOnlyList<SentenceSpan> prefix,
        IReadOnlyList<SentenceSpan> traffic,
        IReadOnlyList<SentenceSpan> close,
        double budget,
        int minSentences,
        bool closeIsRequired,
        Func<SentenceSpan, bool> pinned,
        double minSeconds,
        bool allowFloorOverrun)
    {
        var indexed = traffic.Select((span, index) => (span, index)).ToList();
        var pins = indexed.Where(item => pinned(item.span)).ToList();
        var eligible = indexed
            .Where(item => !pinned(item.span) && !SentenceFragmentGuard.IsDangling(item.span.Text))
            .ToList();
        int floor = Math.Min(Math.Max(0, minSentences), eligible.Count);
        double fixedSeconds = DurationOf(prefix) + DurationOf(close) + DurationOf(pins.Select(item => item.span));
        double totalTrafficSeconds = DurationOf(traffic);

        // Kept traffic goes back in read order with the pinned fixtures in their original places.
        IReadOnlyList<SentenceSpan> Assemble(IEnumerable<(SentenceSpan span, int index)> kept) =>
            prefix.Concat(kept.Concat(pins).OrderBy(item => item.index).Select(item => item.span)).Concat(close).ToList();

        // A required close outranks every traffic item. If its fixed cost leaves no fitting
        // subset, keep the minimum non-dangling traffic and let the caller compress the overrun.
        if (fixedSeconds > budget)
        {
            if (!closeIsRequired || eligible.Count == 0
                || (eligible.Count <= floor && eligible.Count == traffic.Count))
                return null;
            return Assemble(eligible.Take(floor));
        }

        Candidate? best = null;
        int n = eligible.Count;
        int eligibleSweeps = eligible.Count(item => IsGoodRunSweep(item.span.Text));
        if (n <= 20)
        {
            ulong limit = 1UL << n;
            for (ulong mask = 0; mask < limit; mask++)
            {
                int keptCount = BitOperations.PopCount(mask);
                if (keptCount < floor)
                    continue;

                double keptTrafficSeconds = 0.0;
                var kept = new List<(SentenceSpan span, int index)>(keptCount);
                for (int i = 0; i < n; i++)
                {
                    if ((mask & (1UL << i)) == 0)
                        continue;
                    keptTrafficSeconds += DurationOf(eligible[i].span);
                    kept.Add(eligible[i]);
                }

                double total = fixedSeconds + keptTrafficSeconds;
                if (total > budget || total < minSeconds)
                    continue;

                int keptSweeps = kept.Count(item => IsGoodRunSweep(item.span.Text));
                int droppedContent = (n - keptCount) - (eligibleSweeps - keptSweeps);
                var candidate = new Candidate(kept, n - keptCount, droppedContent,
                    totalTrafficSeconds - keptTrafficSeconds, budget - total);
                if (best is null || candidate.IsBetterThan(best.Value))
                    best = candidate;
            }
        }

        if (best is not null)
            return Assemble(best.Value.Kept);

        if (!allowFloorOverrun
            || (minSeconds > 0.0 && !closeIsRequired)
            || (eligible.Count <= floor && eligible.Count == traffic.Count))
            return null;

        // The close fits on its own but the contractual traffic floor does not. Keep that floor
        // rather than silently dropping the close; the post-synthesis fine-tune absorbs the residual.
        // Content before sweeps: the floor must never keep "roads are clear" over an incident.
        var floorTraffic = eligible
            .OrderBy(item => IsGoodRunSweep(item.span.Text) ? 1 : 0)
            .ThenBy(item => DurationOf(item.span))
            .ThenBy(item => item.index)
            .Take(floor);
        return Assemble(floorTraffic);
    }

    private static IReadOnlyList<SentenceSpan> WithSentenceCosts(IReadOnlyList<SentenceSpan> spans)
    {
        var weighted = new List<SentenceSpan>(spans.Count);
        double previousEnd = 0.0;
        foreach (SentenceSpan span in spans)
        {
            double cost = Math.Max(0.0, span.EndSeconds - previousEnd);
            weighted.Add(span with { StartSeconds = 0.0, EndSeconds = cost });
            previousEnd = span.EndSeconds;
        }

        return weighted;
    }

    private readonly record struct Candidate(
        IReadOnlyList<(SentenceSpan span, int index)> Kept,
        int DroppedCount,
        int DroppedContent,
        double DroppedSeconds,
        double UndershootSeconds)
    {
        // DroppedCount, then editorial priority: at equal sentence count, the subset that shed
        // good-run sweeps beats the one that shed content (observed live 2026-08-21, Port Lincoln
        // 15s: the trim dropped the bulletin's only closure and aired "roads are clear"). Only
        // then do seconds and undershoot break the tie.
        public bool IsBetterThan(Candidate other) =>
            DroppedCount < other.DroppedCount
            || (DroppedCount == other.DroppedCount && DroppedContent < other.DroppedContent)
            || (DroppedCount == other.DroppedCount && DroppedContent == other.DroppedContent
                && DroppedSeconds < other.DroppedSeconds - 1e-9)
            || (DroppedCount == other.DroppedCount && DroppedContent == other.DroppedContent
                && Math.Abs(DroppedSeconds - other.DroppedSeconds) <= 1e-9
                && UndershootSeconds < other.UndershootSeconds - 1e-9);
    }

    /// <summary>
    ///     True when a traffic sentence's ONLY claim is that roads are running clear — a good-run
    ///     sweep is the first thing a trim should shed, never preferred over an incident. Any
    ///     incident vocabulary makes the sentence content-bearing even if it also says "clear".
    /// </summary>
    private static bool IsGoodRunSweep(string text)
    {
        const string clearVocab =
            @"\b(are clear|is clear|all clear|moving (?:nicely|well|freely)|running smoothly|flowing (?:well|freely|nicely)|no (?:delays|issues|incidents)|free[\s-]?flowing)\b";
        const string incidentVocab =
            @"\b(closed|closure|closing|delays?|delayed|roadworks|works|crash|collision|breakdown|broken down|crawling|slow(?:er|ing)?|queue(?:ing|d)?|hazard|flooding|flood|debris|incident|congestion|heavy|blocked|reduced|detour|diversions?|accident|stuck|banked up|backed up)\b";
        return System.Text.RegularExpressions.Regex.IsMatch(text, clearVocab,
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase)
               && !System.Text.RegularExpressions.Regex.IsMatch(text, incidentVocab,
                   System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static double DurationOf(IEnumerable<SentenceSpan> spans) =>
        spans.Sum(span => Math.Max(0.0, span.EndSeconds - span.StartSeconds));

    private static double DurationOf(SentenceSpan span) =>
        Math.Max(0.0, span.EndSeconds - span.StartSeconds);

    private static string Join(IEnumerable<SentenceSpan> spans) =>
        string.Join(" ", spans.Select(span => span.SourceText));
    /// <summary>
    ///     Index of the first sentence in the trailing locked run — the station's outro (when
    ///     <paramref name="lockedSuffix" /> is supplied) plus the contiguous weather/fuel close
    ///     before it, as <paramref name="fixtures" /> identifies it. Returns <c>spans.Count</c> when
    ///     the read does not end on anything protectable, and <c>0</c> when every sentence is locked;
    ///     callers treat both as "no protectable close". Only the TRAILING run counts here: a
    ///     required fixture earlier in the read is pinned by the caller instead.
    ///     <para>
    ///         The outro is locked FIRST and unconditionally. A station's closing line is the one
    ///         thing in the read that must survive a trim — it is the last thing the listener hears
    ///         and its whole purpose is to end the bulletin. Before this, an overrun trimmed from the
    ///         end silently deleted it (observed live on dev job 7a33aa7160cd, adelaide: a 93-word
    ///         read trimmed to 82 words dropped "Closer only for Adelaide." with nothing in the
    ///         timeline naming the closer).
    ///     </para>
    /// </summary>
    private static int LockedTailStart(IReadOnlyList<SentenceSpan> spans, string? lockedSuffix, FixtureLines? fixtures)
    {
        int i = spans.Count;

        if (!string.IsNullOrWhiteSpace(lockedSuffix))
        {
            // Grow the candidate leftwards one sentence at a time; each partial accumulation must
            // still be a suffix of the outro, so a body sentence that merely resembles it cannot
            // pull extra content into the locked run.
            string want = NormalizeForSuffixMatch(lockedSuffix);
            string accumulated = "";
            int j = spans.Count;
            while (j > 0 && accumulated != want)
            {
                string candidate = NormalizeForSuffixMatch(spans[j - 1].Text);
                candidate = accumulated.Length > 0 ? $"{candidate} {accumulated}" : candidate;
                if (!want.EndsWith(candidate, StringComparison.Ordinal))
                    break;
                accumulated = candidate;
                j--;
            }

            // Only lock when the whole outro was accounted for; a partial match means the sentence
            // splitter carved it differently and locking half of it would be worse than nothing.
            if (accumulated == want)
                i = j;
        }

        while (i > 0 && fixtures is not null && fixtures.Contains(spans[i - 1].Text))
            i--;
        return i;
    }

    /// <summary>
    ///     Case- and punctuation-insensitive form used to match the outro against the sentence
    ///     spans. The synthesised script may carry inline delivery tags and the splitter may
    ///     normalise punctuation, so an exact string compare would miss.
    /// </summary>
    private static string NormalizeForSuffixMatch(string text)
    {
        var chars = DeliveryTags.Strip(text)
            .Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))
            .Select(char.ToLowerInvariant);
        return string.Join(" ", new string([.. chars]).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

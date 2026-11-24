using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Computes the draft word-count floor and the "low corridor" flag for a bulletin.
///     The product airs in a fixed slot (exactly slot length), so EVERY bulletin must fill it:
///     the floor is the slow-fill floor (~90% of target). A read shorter than that cannot be
///     stretched to the slot by the audio pipeline (<c>MaxSlowDownFactor</c>) and falls back,
///     leaving dead air. Quiet/sparse markets fill the slot with the flowing-road good-runs
///     (+ weather + fuel) provided in the data, never invented incidents (system.md bans
///     padding; the fabrication scorer enforces it). The low-corridor flag still switches the
///     prompt to good-run fill tone.
/// </summary>
internal static class DraftWordBudget
{
    /// <param name="targetWordCount">The full target word count for the slot duration.</param>
    /// <param name="corridorCount">Number of traffic corridors in the data.</param>
    /// <returns>The minimum acceptable word count and whether this is a low-corridor bulletin.</returns>
    internal static (int Floor, bool IsLow) Compute(int targetWordCount, int corridorCount)
    {
        // Slow-fill floor: a read shorter than ~90% of target can't be stretched to the slot,
        // so it is the minimum for EVERY bulletin regardless of how much traffic there is.
        int floor = Math.Max(10, targetWordCount * 9 / 10);
        bool isLow = corridorCount <= 2;
        return (floor, isLow);
    }

    /// <summary>
    ///     An estimate of how many incident sentences fit the slot's word budget — the count the
    ///     draft prompt hands the model as its "voice about the N worst" aim, and from which the
    ///     schema's generous <c>maxItems</c> cap derives. COUNT alone can't hit a word target (a
    ///     minor-heavy market fits ~10 SHORT items where a closure-heavy one busts the slot at ~6
    ///     LONG ones — the reason a flat item cap fails), so this walks the incident corridors in the
    ///     order the evidence presents them (worst-first) and spends a per-corridor spoken-word
    ///     estimate — proxied by how many evidence units the corridor carries, since a full (major)
    ///     bullet renders more fields than a condensed minor — against ~85% of the target (incidents
    ///     dominate a busy read; weather/fuel/good-runs are the short remainder). Estimating over the
    ///     SHOWN evidence set (rather than <see cref="Domain.Services.CorridorTiering.SelectVoiceable"/>,
    ///     which suppresses the minor tier under a major headline and so diverges when
    ///     <c>FillSource.MinorTraffic</c> escalates minors into the read) keeps the aim consistent with
    ///     what the model is actually given. The lead corridor is always counted. Purely an aim / upper
    ///     bound — the word-accurate guarantee stays with <see cref="TrimToCeiling"/>.
    /// </summary>
    internal static int MaxVoicedIncidents(int targetWordCount, IReadOnlyList<EvidenceUnit> evidenceUnits)
    {
        var unitsByCorridor = new Dictionary<string, int>(StringComparer.Ordinal);
        var worstFirst = new List<string>();
        foreach (var unit in evidenceUnits)
        {
            string? corridor = CorridorIdentity(unit);
            if (corridor is null)
                continue;
            if (unitsByCorridor.TryGetValue(corridor, out int seen))
            {
                unitsByCorridor[corridor] = seen + 1;
            }
            else
            {
                unitsByCorridor[corridor] = 1;
                worstFirst.Add(corridor);
            }
        }
        if (worstFirst.Count == 0)
            return 0;

        int incidentBudget = Math.Max(10, targetWordCount * 85 / 100);
        int used = 0;
        int count = 0;
        foreach (string corridor in worstFirst)
        {
            // ~5 base + ~2 words per rendered field, clamped to the observed single-sentence range
            // (a condensed minor ≈ 9 words, a full major with cause + extent ≈ 16).
            int estWords = Math.Clamp(5 + 2 * unitsByCorridor[corridor], 7, 18);
            if (count == 0 || used + estWords <= incidentBudget)
            {
                used += estWords;
                count++;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    private static string? CorridorIdentity(EvidenceUnit unit)
    {
        if (!unit.Id.StartsWith("corridor:", StringComparison.Ordinal))
            return null;

        // New ids put the stable corridor discriminator in the road segment:
        // corridor:<road>#<n>:<field>. Keep the legacy SourceKey fallback for synthetic callers
        // that construct pre-discriminator units directly in tests.
        int hash = unit.Id.IndexOf('#');
        if (hash >= 0)
        {
            int fieldStart = unit.Id.IndexOf(':', hash);
            if (fieldStart > 0)
                return unit.Id[..fieldStart];
        }

        return unit.SourceKey;
    }


    /// <summary>
    ///     The hard word ceiling the assembled draft is trimmed back to, delegated to
    ///     <see cref="ScriptWordCeiling.For" /> so the draft and the post-refine guard in
    ///     <c>ScriptReviewLoop</c> cannot disagree about what over-production means.
    /// </summary>
    internal static int Ceiling(int targetWordCount) => ScriptWordCeiling.For(targetWordCount);

    /// <summary>
    ///     Slot-fit guard for the evidence-grounded structured Draft. A dense market lets the model
    ///     voice more incidents than the slot physically holds — the per-tier item caps
    ///     (<see cref="Domain.Services.CorridorTiering" />) can't fix this alone, because a
    ///     minor-heavy market needs ~10 SHORT items to fill the slot while a closure-heavy one
    ///     overshoots at ~6 LONG ones: only a WORD budget adapts to item length. Keeps the short
    ///     non-incident closers (weather / fuel / good-runs / drive-times / cleared) plus the
    ///     worst-first incident sentences (already ordered by the model) up to
    ///     <paramref name="ceiling" />, dropping the lowest-priority incidents that don't fit. A
    ///     no-op when the draft is already within the ceiling, so thin / minor-heavy markets are
    ///     never trimmed and it cannot re-create the content-limited underfill the slot-capacity
    ///     tuning removed. The lead incident is always kept, even if it alone exceeds the ceiling.
    /// </summary>
    internal static List<DraftSentence> TrimToCeiling(IReadOnlyList<DraftSentence> sentences, int ceiling)
    {
        var ordered = sentences.OrderBy(s => s.Order).ToList();
        if (ordered.Sum(s => WordCount(s.Text)) <= ceiling)
            return ordered;

        static bool IsIncident(DraftSentence s) =>
            s.EvidenceIds.Any(id => id.StartsWith("corridor:", StringComparison.Ordinal));

        int reserved = ordered.Where(s => !IsIncident(s)).Sum(s => WordCount(s.Text));
        int incidentBudget = Math.Max(0, ceiling - reserved);

        var kept = new List<DraftSentence>();
        int used = 0;
        bool incidentBudgetExhausted = false;
        foreach (var sentence in ordered)
        {
            if (!IsIncident(sentence))
            {
                kept.Add(sentence);
                continue;
            }

            int words = WordCount(sentence.Text);
            bool keptAnyIncident = kept.Any(IsIncident);
            if (!incidentBudgetExhausted && (!keptAnyIncident || used + words <= incidentBudget))
            {
                kept.Add(sentence);
                used += words;
            }
            else
            {
                // Worst-first ordering: once one incident overflows, every later incident is
                // lower-priority, so drop this and all after it rather than skip-fit a shorter one.
                incidentBudgetExhausted = true;
            }
        }

        return kept.OrderBy(s => s.Order).ToList();
    }

    private static int WordCount(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}

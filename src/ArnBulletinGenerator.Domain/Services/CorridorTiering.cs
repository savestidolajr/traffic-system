using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The single source of truth for which corridors a bulletin actually VOICES: worst-first
///     magnitude tiering, per-tier caps, and the major-headline minor-suppression. The narrative
///     summarizer renders exactly this selection, so EVERYTHING that reasons about "what the writer
///     saw" must use it too — the filler gap estimate, the good-run fill decision, and the eval's
///     fabrication vocabulary. Using the RAW corridor count instead is the recurring bug this class
///     exists to kill: a market with one voiceable closure and 9 suppressed minors looks "busy"
///     (frozen-eval: the gap estimate collapsed to zero and starved thin markets of filler; the
///     good-run policy suppressed flowing roads for a bulletin with 18s of dead air; the
///     fabrication scorer passed roads the writer never saw).
/// </summary>
public static partial class CorridorTiering
{
    /// <summary>Individually-named corridors per tier — a busy market can return 10+ in one tier,
    /// and the writer's "name at most two" instruction proved unreliable under that load; capping
    /// the DATA makes a roll-call structurally impossible.</summary>
    public const int MajorTierCap = 4;

    public const int ModerateTierCap = 3;
    public const int MinorTierCap = 3;

    /// <summary>
    ///     The corridors the writer is handed to voice: Major capped at <see cref="MajorTierCap" />,
    ///     Moderate at <see cref="ModerateTierCap" />, and the minor tier dropped ENTIRELY when a
    ///     Major-or-worse item leads (a side street's "minor delays" changes no route once a road is
    ///     shut elsewhere; with no Major headline the minors ARE the story and stay, capped).
    ///     <para>
    ///         <paramref name="includeMinorTier" /> mirrors the station's <c>FillSource.MinorTraffic</c>
    ///         escalation, which the summarizer honours when rendering: with it set, minors reach the
    ///         writer even under a Major headline. Callers that must match WHAT THE WRITER SAW — the
    ///         fabrication vocabulary above all — have to pass it, or a legitimately voiced minor road
    ///         reads as invented.
    ///     </para>
    /// </summary>
    public static IReadOnlyList<TrafficCorridor> SelectVoiceable(
        IReadOnlyList<TrafficCorridor> corridors,
        EditorialSettings? settings = null,
        bool includeMinorTier = false)
    {
        if (corridors.Count == 0)
            return corridors;

        int majorCap = settings?.MajorTierCap ?? MajorTierCap;
        int moderateCap = settings?.ModerateTierCap ?? ModerateTierCap;
        int minorCap = settings?.MinorTierCap ?? MinorTierCap;

        var (major, moderate, minor) = ClassifyByTier(corridors);
        int effectiveMinorCap = major.Count > 0 && !includeMinorTier ? 0 : minorCap;
        return major.Take(majorCap)
            .Concat(moderate.Take(moderateCap))
            .Concat(minor.Take(effectiveMinorCap))
            .ToList();
    }

    /// <summary>Count-only companion to <see cref="SelectVoiceable" />.</summary>
    public static int CountVoiceable(
        IReadOnlyList<TrafficCorridor> corridors,
        EditorialSettings? settings = null,
        bool includeMinorTier = false) =>
        SelectVoiceable(corridors, settings, includeMinorTier).Count;

    /// <summary>
    ///     Worst-first magnitude tiering: measured delay / real queue, not HERE's flat severity —
    ///     which floors every freeway corridor at "1" and collapses the whole market into one
    ///     undifferentiated "Major" block. Within a tier the worst delay leads.
    /// </summary>
    public static (List<TrafficCorridor> Major, List<TrafficCorridor> Moderate, List<TrafficCorridor> Minor)
        ClassifyByTier(IReadOnlyList<TrafficCorridor> corridors)
    {
        var classified = corridors
            .Select(c => (Corridor: c, Tier: CorridorMagnitude.Classify(c)))
            .OrderBy(x => x.Tier)
            .ThenByDescending(x => x.Corridor.DelayMinutes.GetValueOr(0))
            .ThenByDescending(x => x.Corridor.ImpactScore)
            .ToList();

        return (
            classified.Where(x => x.Tier <= MagnitudeTier.Major).Select(x => x.Corridor).ToList(),
            classified.Where(x => x.Tier == MagnitudeTier.Moderate).Select(x => x.Corridor).ToList(),
            classified.Where(x => x.Tier == MagnitudeTier.Minor).Select(x => x.Corridor).ToList());
    }
}

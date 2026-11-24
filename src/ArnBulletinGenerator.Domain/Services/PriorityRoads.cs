using ArnBulletinGenerator.Domain.Models;
namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Priority-roads logic (Workshop 2 / SDD §4.2): per-market arterials that matter to
///     listeners even when flowing. Two uses during generation:
///     (1) corridors on priority roads get an impact boost so they survive selection capping;
///     (2) priority roads with NO active corridor are reported as "good runs" — the absence
///     of incidents in fresh provider data is the evidence the road is flowing.
/// </summary>
public static partial class PriorityRoads
{
    /// <summary>Impact multiplier applied to incident corridors that match a priority road.</summary>
    public const double ImpactBoost = 1.3;

    // Additive flow-tier bonuses, calibrated to the incident-impact bands (minor ~80, moderate ~150)
    // so a flowing road's operator tier lands predictably in contention. Added AFTER the damped flow
    // score, never multiplied — the tier dominates, road length/class only nudges within the tier.
    private const double FlowBonusNormal = 15.0;
    private const double FlowBonusHigh = 50.0;
    private const double FlowBonusCritical = 110.0;

    /// <summary>Additive slot-competitiveness bonus for a flowing priority road, by operator tier.</summary>
    public static double FlowTierBonus(FlowPriorityTier tier) => tier switch
    {
        FlowPriorityTier.Normal => FlowBonusNormal,
        FlowPriorityTier.High => FlowBonusHigh,
        FlowPriorityTier.Critical => FlowBonusCritical,
        _ => 0.0
    };

    /// <summary>
    ///     Boosts matching corridors' impact scores and re-sorts descending, so priority
    ///     arterials outrank equally-severe incidents on minor roads at the selection cap.
    /// </summary>
    public static List<TrafficCorridor> BoostAndSort(
        IReadOnlyList<TrafficCorridor> corridors,
        IReadOnlyList<string> priorityRoads)
    {
        if (priorityRoads.Count == 0)
            return corridors.ToList();

        return corridors
            .Select(c =>
            {
                if (!MatchesAny(c.RoadName, priorityRoads)) return c;
                double baseScore = c.BaseScore ?? c.ImpactScore;
                double boostedScore = c.ImpactScore * ImpactBoost;
                double totalBoost = boostedScore - baseScore;
                return c with
                {
                    BaseScore = baseScore,
                    BoostAmount = totalBoost,
                    ImpactScore = boostedScore,
                    PriorityBoosted = true
                };
            })
            .OrderByDescending(c => c.ImpactScore)
            .ToList();
    }
    /// <summary>
    ///     Loose name match: corridor road names are short extracted keys ("M1") while
    ///     expert-entered priority roads may be fuller ("M1 Pacific Motorway") — match when
    ///     either normalized name contains the other AT A TOKEN BOUNDARY.
    /// </summary>
    /// <remarks>
    ///     A bare substring test made every route-numbered road a prefix of its neighbours:
    ///     a priority road "M1" silently boosted M10, M11 and M12 as well (dev, 2026-07-28).
    ///     Requiring the containment to start and end on a word boundary keeps the case the
    ///     looseness exists for ("M1" ↔ "M1 Pacific Motorway") and drops the false positives.
    /// </remarks>
    public static bool IsMatch(string corridorRoadName, string priorityRoad)
    {
        // Canonicalize both sides first: corridors now carry on-air designations ("M4") while
        // expert-entered priority roads may still say "Western Motorway" — same road, must match.
        string corridor = RoadDesignationAliases.Canonicalize(corridorRoadName.Trim());
        string priority = RoadDesignationAliases.Canonicalize(priorityRoad.Trim());
        if (corridor.Length == 0 || priority.Length == 0)
            return false;

        return ContainsAtWordBoundary(corridor, priority) ||
               ContainsAtWordBoundary(priority, corridor);
    }

    /// <summary>True when <paramref name="needle" /> appears in <paramref name="haystack" /> as
    /// a whole word or run of whole words, not as part of a longer one.</summary>
    private static bool ContainsAtWordBoundary(string haystack, string needle)
    {
        int from = 0;
        while (from <= haystack.Length - needle.Length)
        {
            int at = haystack.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;
            bool startsClean = at == 0 || !IsNameChar(haystack[at - 1]);
            int end = at + needle.Length;
            bool endsClean = end == haystack.Length || !IsNameChar(haystack[end]);
            if (startsClean && endsClean)
                return true;
            from = at + 1;
        }

        return false;
    }

    /// <summary>Letters and digits both continue a road name — "M1" must not match inside "M11".</summary>
    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c);
    /// <summary>
    ///     Names of priority roads whose incident corridors should receive the impact boost,
    ///     preserving source list order. Feeds the name list into <see cref="BoostAndSort"/>.
    /// </summary>
    /// <remarks>
    ///     Honours BOTH switches the manager offers: <see cref="PriorityRoad.Enabled" /> (the
    ///     row's on/off) and <see cref="PriorityRoad.BoostIncidents" /> (what the row does).
    ///     Enabled was declared and documented as "skipped at generation time" but never read,
    ///     so switching a priority road off kept boosting it (dev, 2026-07-28).
    /// </remarks>
    public static IReadOnlyList<string> EnabledIncidentBoostNames(IReadOnlyList<PriorityRoad> roads) =>
        roads.Where(r => r.Enabled && r.BoostIncidents).Select(r => r.Name).ToList();


    private static bool MatchesAny(string corridorRoadName, IReadOnlyList<string> priorityRoads)
    {
        return priorityRoads.Any(p => IsMatch(corridorRoadName, p));
    }
}

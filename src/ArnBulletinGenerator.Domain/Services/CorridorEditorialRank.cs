using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     How much a driver needs a road, on ONE banded scale shared by every kind of rundown item.
///     This is the single ordering authority: the candidate default, the operator's rundown order,
///     and the workflow's running order all derive from these bands.
///     <para>
///         It exists because three different orderings were live at once and the weakest of them
///         drew the operator-facing cut. Measured on test job <c>d4db0a5a</c> (2026-08-28,
///         22s Sydney): the cut admitted two <c>Minor</c> three-minute queues (General Holmes Drive
///         rank 6, Mitchell Road rank 7) and excluded a <c>Major</c> blocking crash (Forest Way,
///         rank 18), a fifteen-minute delay (Parramatta Road, rank 16) and two eleven-minute delays
///         (rank 20, 21).
///     </para>
///     <para>
///         Root cause: the cut ranked by <see cref="TrafficCorridor.ImpactScore" /> alone, which is
///         purely structural — road class, severity, extent, incident density.
///         <see cref="CorridorAggregator.CalculateDelayScore" /> was added on 2026-06-17 precisely
///         so "a real jam out-ranks an idle major road", and was silently dropped when
///         <see cref="BulletinContentSelector" /> replaced the old cut on 2026-06-25. Proof in that
///         job's own data: the M5 (17 min) and the M2 (3 min) both scored exactly 240.5, and the M7
///         (16 min) and M4 (10 min) both 214.5. The score cannot see delay at all.
///     </para>
///     <para>
///         BANDS, NOT A CONTINUUM. What separates them is what a driver DOES: route around it,
///         allow more time, or nothing at all. Gaps are wide enough that no delay figure can lift a
///         queue above a blockage, which is why "always admit a closure" needs no special case —
///         it falls out of <see cref="BlockageBand" /> sitting above every delay band.
///     </para>
///     <para>
///         Delay alone is the wrong severity signal for congestion because it scales with segment
///         length and cannot tell a stopped short road from a free-flowing long one. Measured live
///         in Cairns on 2026-08-28 in one snapshot: Foster Road at 61% of free flow over 8,098 m
///         produced a 3.79 min delay while Toogood Road at 24% over 1,084 m produced 3.82 min, and
///         Captain Cook Highway at 37% over 1,384 m produced only 2.61 min. Delay is
///         <c>length/speed − length/freeFlow</c> and grows with the road, not with how bad it is;
///         the length-weighted pace ratio (<c>speed/freeFlow</c>) is length-independent and correctly
///         ordered, so the clear 8 km road was otherwise ranked above both congested ones.
///     </para>
///     <para>
///         Pace and delay are therefore two measurements of the same thing — how far the road is
///         from its usual pace — and the worse one governs the band. Neither may demote the other:
///         a short severe crawl and a long moderate queue each promote on their own axis, exactly
///         like <see cref="CorridorMagnitude.Classify" /> takes the most severe of its type floor,
///         delay tier and length tier.
///     </para>
///     <para>
///         The band constants are shared with the workflow's <c>EventRank</c>, which computes the
///         same judgement from evidence facts rather than from a <see cref="TrafficCorridor" />.
///         Two projections, one set of values: a corridor and the evidence unit built from it must
///         never disagree about which band they are in.
///     </para>
/// </summary>
public static class CorridorEditorialRank
{
    public const int ClosureBonus = 80;
    public const int CrashBonus = 60;
    public const int HazardBonus = 40;
    public const int DisabledVehicleBonus = 30;
    public const int ConstructionBonus = 10;
    public const int ClearRunBase = 50;

    /// <summary>Rank score for one incident/congestion corridor. Higher leads.</summary>
    public static double Of(TrafficCorridor corridor, EditorialSettings? settings = null)
    {
        // 1. Road Class Points (FC1=100, FC2=80, FC3=60, FC4=40, FC5=20)
        int fc = corridor.FunctionalClass > 0 ? corridor.FunctionalClass : 3;
        double roadPoints = (6 - fc) * 20.0;

        // 2. Delay Points (8 pts per minute, max 120 pts = 15+ mins)
        int delay = corridor.DelayMinutes.HasValue && corridor.DelayMinutes.Value > 0
            ? corridor.DelayMinutes.Value
            : 0;
        double delayPoints = Math.Min(delay * 8.0, 120.0);

        // 3. Live Pace / Crawl Points
        double pacePoints = 0.0;
        if (corridor.PaceRatio.HasValue)
        {
            double pace = corridor.PaceRatio.Value;
            if (pace <= 0.25) pacePoints = 80.0;
            else if (pace <= 0.40) pacePoints = 50.0;
            else if (pace <= 0.55) pacePoints = 30.0;
            else if (pace <= 0.70) pacePoints = 15.0;
        }

        // 4. Incident Severity / Type Bonus
        double incidentPoints = 0.0;
        if (IsBlockage(corridor))
        {
            incidentPoints = IncidentTypeClassifier.DataMarksClosure(corridor) || IncidentTypeClassifier.IsClosure(corridor.PrimaryType)
                ? ClosureBonus
                : CrashBonus;
        }
        else
        {
            incidentPoints = corridor.PrimaryType switch
            {
                "roadClosure" => ClosureBonus,
                "accident" => CrashBonus,
                "roadHazard" or "hazard" => HazardBonus,
                "disabledVehicle" or "breakdown" => DisabledVehicleBonus,
                "construction" or "roadworks" => ConstructionBonus,
                _ => 0.0
            };
        }

        // 5. Priority Arterial Boost
        double priorityPoints = 0.0;
        if (corridor.PriorityBoosted)
        {
            priorityPoints = (settings?.PriorityRoadBandBoost ?? 30);
        }

        return Math.Round(roadPoints + delayPoints + pacePoints + incidentPoints + priorityPoints, 1);
    }

    // Pace and delay are two lenses on the same thing — how far the road is from its usual
    // pace. The worse one governs the band and neither may demote the other, mirroring
    // CorridorMagnitude.Classify which takes the most severe of its type floor, delay tier
    // and length tier via MostSevere. Thresholds are derived from the provider's own
    // CalculatePaceScore boundaries (0.25 / 0.40 / 0.55 / 0.70) and its MaxVoicedPaceRatio
    // ceiling (0.70), but those are additive score bonuses on a different scale (0/15/40/70/100).
    // This type deals in bands, and mixing the two scales is the class of error this change
    // exists to remove.

    /// <summary>
    ///     Rank for a flowing "good run" road, on the same scale so the two compete in one list.
    ///     Always below every problem: a clear road is filler that earns airtime only in proportion
    ///     to how much of the read the bad news did not fill.
    /// </summary>
    public static double Of(FlowRoad road, EditorialSettings? settings = null)
    {
        int fc = road.FunctionalClass > 0 ? road.FunctionalClass : 3;
        double roadPoints = (6 - fc) * 20.0;
        double baseFlow = settings?.FlowPriorityBand ?? ClearRunBase;
        double priorityPoints = (road.PriorityBoosted && (settings?.PriorityRoadBandBoost ?? 0) > 0)
            ? settings!.PriorityRoadBandBoost!.Value
            : (road.PriorityBoosted ? 30.0 : 0.0);
        return Math.Round(baseFlow + (roadPoints * 0.2) + priorityPoints, 1);
    }

    /// <summary>
    ///     Whether the road is blocked rather than merely slow — the top band's admission test.
    ///     <para>
    ///         Deliberately broader than <see cref="IncidentTypeClassifier.IsClosure" />: a crash
    ///         holding two of three lanes is a routing decision for a listener even though HERE
    ///         types it <c>accident</c> and reports no measured delay. Forest Way on test job
    ///         <c>d4db0a5a</c> was exactly that shape — an <c>accident</c> with no delay figure,
    ///         which scored 160 structurally and landed 18th behind seventeen queues.
    ///     </para>
    ///     <para>
    ///         Read from the TYPED fields only, never a provider's free text.
    ///         <see cref="IncidentTypeClassifier.DataMarksClosure" /> documents why: a description
    ///         can assert a closure the typed fields deny, and ranking off prose would let a works
    ///         zone reading "Closed" lead a bulletin.
    ///     </para>
    /// </summary>
    public static bool IsBlockage(TrafficCorridor corridor) =>
        IncidentTypeClassifier.DataMarksClosure(corridor)
        || IncidentTypeClassifier.IsCrash(corridor.PrimaryType)
        || corridor.Incidents.Any(incident =>
            IncidentTypeClassifier.IsClosure(incident.Type)
            || IncidentTypeClassifier.IsCrash(incident.Type)
            // Lanes are carried by the atomic reading, not the incident record. Checked here so
            // this agrees with the workflow's own `lanesClosed` evidence-field test: a works zone
            // holding two of three lanes is a routing decision whatever HERE types it.
            || (incident.Fact is { } fact && fact.LanesClosed.HasValue && fact.LanesClosed.Value > 0))
        || (corridor.OfficialEvidences ?? []).Any(evidence => evidence.LanesClosed is > 0);
}

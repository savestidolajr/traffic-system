using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Classifies a corridor by its commuter-facing magnitude — the delay a listener actually
///     feels — rather than HERE's flat per-incident criticality. HERE marks almost every
///     major-road incident "critical", and <c>DeriveSeverity</c> floors every freeway/highway
///     corridor at severity 1, so the raw <see cref="TrafficCorridor.Severity"/> cannot rank a
///     bad jam against a passing nuisance. The richest available signal wins instead: a measured
///     delay first, then a genuine queue length, then an incident-type floor so a blocking crash
///     or closure stays prominent even before routing has attached a drive time.
///     <para>
///         Severity still drives <c>ImpactScore</c> (corridor selection); magnitude drives on-air
///         emphasis and tiering. They are deliberately separate axes.
///     </para>
/// </summary>
public static partial class CorridorMagnitude
{
    /// <summary>
    ///     Returns the corridor's magnitude tier. A measured delay or real queue can only
    ///     <em>promote</em> the type floor, never demote it — a blocking closure stays Major even
    ///     while its routed delay is still small.
    /// </summary>
    public static MagnitudeTier Classify(TrafficCorridor corridor) =>
        Classify(
            corridor.PrimaryType,
            corridor.FunctionalClass,
            corridor.DelayMinutes.HasValue ? corridor.DelayMinutes.Value : null,
            // Longest single observation, not the sum across a road's incidents: a summed works
            // footprint promoted Cairns' Kennedy Highway (12,169 m of overlapping records, zero
            // measured delay) to Major and led the bulletin. See TrafficCorridor.VoicedExtentMeters.
            corridor.VoicedExtentMeters);

    /// <summary>
    ///     Primitive overload so a persisted <c>JobCorridor</c> (which is not a
    ///     <see cref="TrafficCorridor"/>) can be tiered the same way. A measured delay or real
    ///     queue can only <em>promote</em> the type floor, never demote it — a blocking closure
    ///     stays Major even while its routed delay is still small.
    /// </summary>
    public static MagnitudeTier Classify(
        string primaryType, int functionalClass, int? delayMinutes, double totalLengthMeters)
    {
        var tier = TypeFloor(primaryType, functionalClass);

        if (delayMinutes is > 0)
            tier = MostSevere(tier, DelayTier(delayMinutes.Value));

        // A standing footprint (roadworks / lane restriction) is a works-zone extent, not a
        // queue, so its kilometres never promote magnitude.
        if (!IncidentTypeClassifier.IsStandingFootprint(primaryType) && totalLengthMeters > 1000)
            tier = MostSevere(tier, LengthTier(totalLengthMeters));

        return tier;
    }

    /// <summary>
    ///     Baseline tier from incident type and road importance, for corridors that carry no
    ///     measured delay or queue. Only genuinely blocking events on a significant road floor
    ///     above Minor — everything else (breakdowns, hazards, congestion, works) starts Minor and
    ///     is promoted only by a real delay or queue. This is what stops every freeway incident
    ///     from reading the same.
    /// </summary>
    private static MagnitudeTier TypeFloor(string primaryType, int functionalClass)
    {
        if (primaryType.Equals("roadClosure", StringComparison.OrdinalIgnoreCase)
            && functionalClass <= 3)
            return MagnitudeTier.Major;

        if (primaryType.Equals("accident", StringComparison.OrdinalIgnoreCase)
            && functionalClass <= 2)
            return MagnitudeTier.Major;

        return MagnitudeTier.Minor;
    }

    // Thresholds mirror TrafficNarrativeSummarizer.FormatImpact so the tier and the spoken
    // qualitative label never disagree.
    private static MagnitudeTier DelayTier(int delayMinutes) => delayMinutes switch
    {
        > 30 => MagnitudeTier.Severe,
        >= 16 => MagnitudeTier.Major,
        >= 6 => MagnitudeTier.Moderate,
        _ => MagnitudeTier.Minor
    };

    private static MagnitudeTier LengthTier(double lengthMeters) => lengthMeters switch
    {
        > 3000 => MagnitudeTier.Major,
        >= 1500 => MagnitudeTier.Moderate,
        _ => MagnitudeTier.Minor
    };

    // Lower enum value = more severe, so "most severe" is the numeric minimum.
    private static MagnitudeTier MostSevere(MagnitudeTier a, MagnitudeTier b) => a < b ? a : b;
}

/// <summary>Commuter-facing magnitude tier for a corridor. Lower value = more severe.</summary>
public enum MagnitudeTier
{
    Severe = 0,
    Major = 1,
    Moderate = 2,
    Minor = 3
}

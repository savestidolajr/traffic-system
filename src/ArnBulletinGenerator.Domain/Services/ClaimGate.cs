using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>One gate decision, with the fixed reason string a job log and the operator console read.</summary>
/// <param name="Allowed">True when the claim may be made on air.</param>
/// <param name="Reason">Empty when allowed; otherwise one of the catalogued denial reasons.</param>
public readonly record struct ClaimVerdict(bool Allowed, string Reason);

/// <summary>
///     The single place that decides what a record may be claimed to say. Every denial is a fixed
///     reason string so a stored job can answer why a record was not reported — the audit trail that
///     has to exist before mandatory human approval can be relaxed.
///     <para>
///         Nothing here corroborates <c>roadClosed</c> with anything else: HERE's flag agreed with the
///         condition vocabulary on 793 of 793 measured records. A disagreement is a data anomaly, and
///         is treated as non-closure by the caller after logging it.
///     </para>
/// </summary>
public static class ClaimGate
{
    /// <summary>Road identity spans two roads; the record has no single road to be voiced on.</summary>
    public const string IdentitySplit = "identity-split";

    /// <summary>The map named none of the record's segments.</summary>
    public const string IdentityUnnamed = "identity-unnamed";

    /// <summary>HERE's <c>codes[0]</c> is absent or unmapped: we do not know what this record reports.</summary>
    public const string ConditionUnknown = "condition-unknown";

    /// <summary>HERE does not report the road as closed.</summary>
    public const string NotClosed = "not-closed";

    /// <summary>A closure claim needs a road the segments actually named.</summary>
    public const string IdentityUnresolved = "identity-unresolved";

    /// <summary>Neither the description nor the map said WHAT is closed.</summary>
    public const string AssetUnknown = "asset-unknown";

    /// <summary>A ramp closure with no ramp target cannot be voiced without claiming the road is shut.</summary>
    public const string RampTargetUnknown = "ramp-target-unknown";

    /// <summary>A long-planned worksite is not this bulletin's closure news.</summary>
    public const string ScheduledWindow = "scheduled-window";

    /// <summary>A closure with no place of its own claims the WHOLE road is shut.</summary>
    public const string ClosureUnplaced = "closure-unplaced";

    /// <summary>No direction is supported by the record.</summary>
    public const string DirectionUnsourced = "direction-unsourced";

    private static readonly ClaimVerdict Allow = new(true, string.Empty);

    /// <summary>
    ///     Whether this record may be voiced at all. A record with no resolved road, or whose condition
    ///     HERE did not tell us, is a measurement — it may inform ranking, but it may not be claimed.
    /// </summary>
    public static ClaimVerdict CanVoice(IncidentFact fact)
    {
        if (fact.Identity == IdentityVerdict.Split)
            return new ClaimVerdict(false, IdentitySplit);
        if (fact.Identity == IdentityVerdict.Unnamed)
            return new ClaimVerdict(false, IdentityUnnamed);
        return fact.Condition.Kind == IncidentConditionKind.Unknown
            ? new ClaimVerdict(false, ConditionUnknown)
            : Allow;
    }

    /// <summary>
    ///     Whether this record may be claimed as a closure. A ramp closure must name the ramp: 122
    ///     ramp-closure records aired as mainline closures of the roads those ramps leave.
    /// </summary>
    public static ClaimVerdict CanClaimClosure(IncidentFact fact)
    {
        if (!fact.RoadClosed)
            return new ClaimVerdict(false, NotClosed);
        if (fact.Identity is not (IdentityVerdict.Unambiguous or IdentityVerdict.Dominant))
            return new ClaimVerdict(false, IdentityUnresolved);
        if (fact.Asset == AffectedAsset.Unknown)
            return new ClaimVerdict(false, AssetUnknown);
        if (fact.Asset == AffectedAsset.Ramp && fact.RampTarget.IsNone)
            return new ClaimVerdict(false, RampTargetUnknown);
        return fact.Window == WindowClass.Scheduled
            ? new ClaimVerdict(false, ScheduledWindow)
            : Allow;
    }

    /// <summary>
    ///     Whether a CARRIAGEWAY closure can be placed well enough to voice. A closure sentence with no
    ///     locator anywhere in it claims the entire road is shut, which is a bigger falsehood than the
    ///     missing detail it replaces.
    ///     <para>
    ///     Measured on Perth, 2026-08-17. HERE reported Canning Highway as two records: a bare
    ///     <c>"Closed"</c> with <c>roadClosed=true</c>, <c>LengthMeters=45</c> and its own segment, and a
    ///     separate <c>"At Queen Victoria Street - Road construction"</c> with <c>roadClosed=false</c> on
    ///     a different segment <b>6,219 m away</b>. Both renderings of that data were false: welding the
    ///     closure onto the works anchor put a 45 m closure 6 km from where HERE placed it, and dropping
    ///     the anchor aired "Canning Highway's closed for roadworks" — a 14 km arterial reported shut
    ///     over 45 m of it. A driver can act on neither.
    ///     </para>
    ///     <para>
    ///     A locator is the record's own anchor or span, the locality HERE resolved at its position, or a
    ///     locator already on the corridor line the claim sits under (<paramref name="corridorHasLocator" />):
    ///     "closed near Burleigh Heads" is honest and useful, "closed" alone is not. With none of those,
    ///     the closure is withheld and the record's other, placeable facts still air.
    ///     </para>
    /// </summary>
    public static ClaimVerdict CanPlaceClosure(IncidentFact fact, bool corridorHasLocator) =>
        fact.Anchor.IsNone && fact.SecondaryAnchor.IsNone && fact.Suburb.IsNone && !corridorHasLocator
            ? new ClaimVerdict(false, ClosureUnplaced)
            : Allow;

    /// <summary>
    ///     Whether a direction may be stated. Denied unless HERE stated it or every segment reference
    ///     agreed on one — the compass-from-coverage-centre derivation it replaces made direction a
    ///     property of which station fetched the record (one 457 m record aired citybound 15 times and
    ///     outbound once from byte-identical input).
    /// </summary>
    public static ClaimVerdict CanClaimDirection(IncidentFact fact) =>
        fact.DirectionSource == DirectionSource.None
            ? new ClaimVerdict(false, DirectionUnsourced)
            : Allow;
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Reduces a fetched HERE record plus the map's verdict on its segments to the single
///     <see cref="IncidentFact" /> anything downstream may claim from.
///     <para>
///         This is the one place a record's condition, asset, place and window are decided together, so
///         no other code can pair one record's condition with another record's location — the defect
///         that aired a crash at a roadworks anchor.
///     </para>
/// </summary>
public static class IncidentFactBuilder
{
    /// <summary>
    ///     The fact, plus the two things the caller must act on: the closed asset to carry back onto the
    ///     record, and whether HERE's own <c>roadClosed</c> flag contradicted its own condition code.
    /// </summary>
    /// <param name="Fact">The record's atomic reading.</param>
    /// <param name="ClosedAsset">The named ramp when a ramp is what is shut, else none.</param>
    /// <param name="ClosureFlagDisagreement">
    ///     True when <c>roadClosed</c> and the AlertC condition disagree. Measured agreement was 793 of
    ///     793 records, so this is a data anomaly: the caller logs it and the fact reports non-closure.
    /// </param>
    public readonly record struct BuiltFact(
        IncidentFact Fact, Option<string> ClosedAsset, bool ClosureFlagDisagreement);

    /// <summary>Builds the fact for one record against the identity resolved from its own segments.</summary>
    public static BuiltFact Build(TrafficIncident incident, RoadIdentity identity, DateTimeOffset now)
    {
        var parsed = IncidentDescriptionGrammar.Parse(incident.Description);
        var condition = IncidentConditionCatalog.FromCodes(incident.Codes);

        bool disagreement = condition.Kind != IncidentConditionKind.Unknown
                            && incident.RoadClosed != condition.ExpectsClosure;

        bool isRamp = parsed.Shape == DescriptionShape.RampTo || identity.IsRamp;
        var asset = isRamp
            ? AffectedAsset.Ramp
            : identity.Name is not null ? AffectedAsset.Carriageway : AffectedAsset.Unknown;

        var closedAsset = asset == AffectedAsset.Ramp && parsed.RampTarget.HasValue
            ? Option<string>.Some($"Ramp to {parsed.RampTarget.Value}")
            : default;

        var fact = new IncidentFact(
            IncidentId: incident.Id,
            OriginalId: incident.OriginalId,
            Condition: condition,
            Asset: asset,
            RoadName: identity.Name,
            Identity: identity.Verdict,
            Anchor: parsed.PrimaryAnchor,
            SecondaryAnchor: parsed.SecondaryAnchor,
            RampTarget: parsed.RampTarget,
            Direction: incident.Direction,
            DirectionSource: incident.DirectionSource,
            Suburb: incident.Suburb,
            RoadClosed: incident.RoadClosed && !disagreement,
            Window: IncidentWindow.Classify(
                incident.StartTimeUtc, incident.EndTimeUtc, incident.EntryTimeUtc, now),
            LaneDetail: incident.Summary,
            SourceDetail: incident.Comment,
            AuthorityPriority: incident.AuthorityPriority,
            AuthorityPriorityRank: AuthorityPriorityNormalizer.Normalize(
                "here", incident.AuthorityPriority.Match(value => value, () => string.Empty)),
            CanonicalClass: IncidentClassCrosswalk.Classify("here", incident.Type, null));

        return new BuiltFact(fact, closedAsset, disagreement);
    }
}

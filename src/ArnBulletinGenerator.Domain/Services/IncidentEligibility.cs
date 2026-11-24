using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The action eligibility took on one record, kept for the job log and the operator console.
/// </summary>
/// <param name="IncidentId">HERE's per-revision id for the record acted on.</param>
/// <param name="Road">The record's road (or generic marker) at the time of the decision.</param>
/// <param name="Action">"withheld" or "demoted".</param>
/// <param name="Reason">The <see cref="ClaimGate" /> denial that drove the action.</param>
public sealed record EligibilityAction(string IncidentId, string Road, string Action, string Reason);

/// <summary>The transformed incidents plus the ledger of every action taken.</summary>
public sealed record EligibilityOutcome(
    IReadOnlyList<TrafficIncident> Incidents,
    IReadOnlyList<EligibilityAction> Ledger);

/// <summary>
///     Applies <see cref="ClaimGate" /> at the DATA BOUNDARY — after a source has reduced its records
///     to <see cref="IncidentFact" />s, before corridors are aggregated — so nothing downstream (the
///     summarizer, the writer, a stored job) ever sees a claim the record cannot support. The pilot
///     failure this retires: HERE models months-long project sub-closures as bare
///     <c>roadClosure/"Closed"</c> records (59 of 71 live Sydney records on 2026-08-17, median age 326
///     hours, windows to 693 days), and the bulletin aired "the Warringah Freeway's closed" from a
///     scheduled ramp arrangement operators could not find on the road.
///     <para>
///         Three outcomes per record, always ledgered:
///         <b>kept</b> — the claim stands as the record supports it;
///         <b>demoted</b> — the record stays voiceable but its unsupported CLOSURE headline is reduced
///         to the condition it can support (a scheduled worksite voices as roadworks, a closed ramp as
///         a hazard naming the ramp, an unplaceable closure as its underlying cause);
///         <b>withheld</b> — the record supports no claim at all (no resolved road, unknown condition,
///         a closure whose asset is unknown) and is kept out of corridors entirely, remaining on the
///         job's evidence with its reason so the console can answer "why wasn't this reported?".
///     </para>
///     <para>
///         Records with no <see cref="TrafficIncident.Fact" /> (persisted jobs, regression fixtures
///         rebuilt outside the live pipeline) pass through untouched: this gate only ever removes
///         confidence that a fact failed to earn, and absence of a fact is absence of the measurement,
///         not proof of a defect.
///     </para>
/// </summary>
public static class IncidentEligibility
{
    private const string Withheld = "withheld";
    private const string Demoted = "demoted";
    private const string Flagged = "flagged";

    /// <summary>Marker prefix on <see cref="TrafficIncident.WithheldReason" /> for records corridors must skip.</summary>
    public static bool IsWithheld(TrafficIncident incident) =>
        incident.WithheldReason.HasValue &&
        incident.WithheldReason.Value.StartsWith(Withheld, StringComparison.Ordinal);

    /// <summary>
    ///     Transforms a fresh fetch. Pure: no clock (windows were classified when facts were built),
    ///     no I/O, deterministic for a given input list.
    /// </summary>
    public static EligibilityOutcome Transform(IReadOnlyList<TrafficIncident> incidents)
    {
        var ledger = new List<EligibilityAction>();
        var result = new List<TrafficIncident>(incidents.Count);

        // A closure with no locator of its own may still be placed by a sibling record on the same
        // road whose anchor/suburb the corridor line will carry (ClaimGate.CanPlaceClosure's
        // corridorHasLocator). Compute road -> "any voiceable sibling carries a locator" first.
        var roadHasLocator = incidents
            .Where(i => i.Fact is { } f && ClaimGate.CanVoice(f).Allowed &&
                        (f.Anchor.HasValue || f.SecondaryAnchor.HasValue || f.Suburb.HasValue))
            .Select(i => i.Location)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var incident in incidents)
        {
            if (incident.Fact is not { } fact)
            {
                result.Add(incident);
                continue;
            }

            var voice = ClaimGate.CanVoice(fact);
            if (!voice.Allowed && incident.RoadClosed)
            {
                // An unresolved or unreadable CLOSURE record supports no honest sentence at all:
                // voicing it is exactly the phantom class, so it is kept out of corridors entirely.
                ledger.Add(new EligibilityAction(incident.Id, incident.Location, Withheld, voice.Reason));
                result.Add(incident with { WithheldReason = Option<string>.Some($"{Withheld}:{voice.Reason}") });
                continue;
            }

            if (!voice.Allowed)
            {
                // A non-closure record whose road or condition is unresolved may still inform
                // ranking (ClaimGate.CanVoice doc) — it flows with the audit flag, and the
                // script-generation wave enforces the per-claim gate when it lands.
                ledger.Add(new EligibilityAction(incident.Id, incident.Location, Flagged, voice.Reason));
                result.Add(incident with { WithheldReason = Option<string>.Some($"{Flagged}:{voice.Reason}") });
                continue;
            }

            var current = incident;

            // Direction: only a sourced direction may be stated. Stripping here (rather than in the
            // renderer) means an invented compass direction cannot reach any consumer of the model.
            if (current.Direction.HasValue && !ClaimGate.CanClaimDirection(fact).Allowed)
                current = current with { Direction = Option<string>.None };

            if (current.RoadClosed)
            {
                var closure = ClaimGate.CanClaimClosure(fact);
                if (!closure.Allowed && closure.Reason is ClaimGate.IdentityUnresolved or ClaimGate.AssetUnknown
                        or ClaimGate.RampTargetUnknown)
                {
                    // A closure record that cannot say WHAT is closed supports no honest sentence.
                    ledger.Add(new EligibilityAction(current.Id, current.Location, Withheld, closure.Reason));
                    result.Add(current with { WithheldReason = Option<string>.Some($"{Withheld}:{closure.Reason}") });
                    continue;
                }

                if (!closure.Allowed && closure.Reason == ClaimGate.ScheduledWindow)
                {
                    // A long-planned worksite's sub-closure is standing roadworks, not closure news.
                    current = Demote(current, "construction");
                    ledger.Add(new EligibilityAction(current.Id, current.Location, Demoted, closure.Reason));
                }
                else if (!closure.Allowed)
                {
                    // NotClosed: the fact already disagreed with the flag (logged at build); voice the cause.
                    current = Demote(current, DemotionType(fact));
                    ledger.Add(new EligibilityAction(current.Id, current.Location, Demoted, closure.Reason));
                }
                else if (fact.Asset == AffectedAsset.Ramp)
                {
                    // The renderer voices a corridor closure as the ROAD being shut; until it can voice
                    // a ramp first-class, a closed ramp airs as a hazard whose detail line names the
                    // ramp ("Ramp to Alfred StreetNorth - Closed") — true, and scoped to the ramp.
                    current = Demote(current, "roadHazard");
                    ledger.Add(new EligibilityAction(current.Id, current.Location, Demoted, "ramp-scope"));
                }
                else if (!ClaimGate.CanPlaceClosure(fact, roadHasLocator.Contains(current.Location)).Allowed)
                {
                    // "Closed" with no locator anywhere claims the whole road is shut.
                    current = Demote(current, DemotionType(fact));
                    ledger.Add(new EligibilityAction(current.Id, current.Location, Demoted, ClaimGate.ClosureUnplaced));
                }
            }

            result.Add(current);
        }

        return new EligibilityOutcome(result, ledger);
    }

    /// <summary>
    ///     A demoted closure keeps its cause: a construction-caused closure voices as roadworks, anything
    ///     else as a hazard. Never "closed".
    /// </summary>
    private static string DemotionType(IncidentFact fact) =>
        fact.Condition.Kind is IncidentConditionKind.RoadWorks or IncidentConditionKind.ClosedForRoadWorks
            or IncidentConditionKind.ConstructionWork or IncidentConditionKind.MaintenanceWork
            or IncidentConditionKind.ResurfacingWork or IncidentConditionKind.RoadWorksContraflow
            ? "construction"
            : "roadHazard";

    private static TrafficIncident Demote(TrafficIncident incident, string type) =>
        incident with
        {
            Type = type,
            RoadClosed = false,
            WithheldReason = Option<string>.Some($"{Demoted}:closure")
        };
}

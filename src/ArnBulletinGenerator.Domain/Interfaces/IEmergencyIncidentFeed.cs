using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     An emergency-services incident feed for a market's coverage area — fires, floods, storms,
///     hazmat, rescues.
///     <para>
///         Deliberately NOT an <see cref="IOfficialTrafficFeed" />. That contract's events flow
///         through the incident crosswalk into corridor fusion, which would attach an emergency
///         incident to whatever road name sits nearest its coordinates and manufacture closures the
///         source never claimed. Emergency data has no road-impact field at all, so it stays on its
///         own rail: persisted as job evidence, shown to the operator, and (from phase 2) offered to
///         the writer only as a warning, never as a traffic item.
///     </para>
/// </summary>
public interface IEmergencyIncidentFeed
{
    /// <summary>Stable feed identifier, used as the diagnostic and metric dimension ("dataquoll").</summary>
    string SourceId { get; }

    /// <summary>
    ///     Events inside <paramref name="area" />, revision-collapsed, allow-listed and ordered by
    ///     <see cref="EmergencyEvent.CompareForOperator" />. Failure is the upstream's outage, never
    ///     a guess: callers treat it as fail-soft and air the bulletin without it.
    /// </summary>
    Task<Result<IReadOnlyList<EmergencyEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default);
}

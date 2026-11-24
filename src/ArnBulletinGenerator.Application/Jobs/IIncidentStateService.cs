using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>
///     Contract for computing per-market incident deltas (new, updated, cleared) against
///     prior state for same-day script continuity.
/// </summary>
public interface IIncidentStateService
{
    Task<Option<IncidentDeltaContext>> ComputeAndPersistDeltaAsync(
        string marketId,
        Option<string> timezoneId,
        IReadOnlyList<TrafficIncident> currentIncidents,
        CancellationToken cancellationToken = default);
}

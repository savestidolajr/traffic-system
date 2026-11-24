using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Fetches live incidents, congestion corridors, and free-flowing roads for a coverage area from the traffic data source (HERE).</summary>
public interface ITrafficProvider
{
    Task<Result<IEnumerable<TrafficIncident>>> GetIncidentsAsync(CoverageArea area,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Fetches ranked incident/congestion corridors for the area.
    ///     <para>
    ///         <paramref name="exclusions" /> is applied to the raw records BEFORE they are
    ///         aggregated, so no corridor-level field — direction, extent, length, impact,
    ///         provenance — can be derived from a record the operator ruled out. Filtering the
    ///         corridors afterwards is NOT equivalent, and aired a false directional claim on
    ///         2026-08-24: see <see cref="IncidentExclusions" />.
    ///     </para>
    /// </summary>
    Task<Result<IEnumerable<TrafficCorridor>>> GetCorridorsAsync(CoverageArea area, int maxCorridors = 5,
        CancellationToken cancellationToken = default, IncidentExclusions? exclusions = null);

    /// <summary>
    ///     Every road name the source reported an incident on for this area, BEFORE the corridor cap.
    ///     <para>
    ///         <see cref="GetCorridorsAsync" /> ranks and truncates, so a road ranked past
    ///         <c>maxCorridors</c> vanishes — and the good-run pool was filtered against that
    ///         truncated list, so a road HERE reported a crash on could still be voiced as "moving
    ///         nicely". Callers use this for that exclusion. Default: no extra knowledge, which
    ///         preserves the old behaviour for providers that do not track it.
    ///     </para>
    /// </summary>
    Task<Result<IReadOnlyList<string>>> GetIncidentRoadNamesAsync(CoverageArea area,
        int maxCorridors = 5, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<IReadOnlyList<string>>.Success([]));

    // Deliberately NOT exclusion-aware: an omitted record is unaired, not absent. The road still
    // has a real incident on it, so it must stay out of the "moving nicely" pool — verified on dev
    // 2026-08-24 (Church Street and Wakehurst Pkwy appeared in neither FlowRoads nor GoodRuns
    // while omitted).

    /// <summary>
    ///     Returns the data-backed pool of named major roads currently flowing freely (low jam
    ///     factor) — truthful "good run" candidates for quiet periods, ranked by monitored length.
    ///     Surfaced in the review Flow tab and used to fill the slot when there is little congestion
    ///     to report. Fail-soft: callers treat a failure as "no good-run roads".
    /// </summary>
    Task<Result<IReadOnlyList<FlowRoad>>> GetFreeFlowingRoadsAsync(
        CoverageArea area,
        int max = 5,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? excludedRoadNames = null);
}

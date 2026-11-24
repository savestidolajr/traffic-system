using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces.Repositories;

/// <summary>
///     Per-market incident priority rules and dynamic real-time incident landscape occurrences.
/// </summary>
public interface IIncidentPriorityRepository
{
    /// <summary>
    ///     Gets the priority rules configured for a market.
    /// </summary>
    Task<Result<IReadOnlyList<IncidentPriorityRule>>> GetRulesAsync(
        string market, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Saves/replaces the priority rules configured for a market.
    /// </summary>
    Task<Result> SetRulesAsync(
        string market,
        IReadOnlyList<IncidentPriorityRule> rules,
        string updatedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets a bounded landscape page. The normal read path is a top-N projection; an explicit
    ///     complete request may page through the legacy aggregate rows using the returned
    ///     continuation token.
    /// </summary>
    Task<Result<IncidentLandscapePage>> GetLandscapePageAsync(
        string market,
        int limit,
        string? continuationToken = null,
        bool complete = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Gets all dynamically accumulated incident landscape signatures observed for a market.
    ///     This is retained for explicit complete/internal callers; HTTP callers should use the
    ///     bounded page method above.
    /// </summary>
    Task<Result<IReadOnlyList<IncidentLandscapeEntry>>> GetLandscapeAsync(
        string market, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Dynamically records and increments occurrence counts for observed incident signatures in a market.
    /// </summary>
    Task<Result> RecordOccurrencesAsync(
        string market,
        IReadOnlyList<IncidentLandscapeEntry> entries,
        CancellationToken cancellationToken = default);
}

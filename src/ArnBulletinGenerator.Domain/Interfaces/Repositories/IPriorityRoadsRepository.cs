using System.Collections.Generic;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Per-market priority-roads list (Workshop 2 / SDD §4.2): arterials that matter to
///     listeners even when flowing. Experts pre-populate; operators edit over time; the
///     generation pipeline references it for good-run reporting and corridor boosting.
/// </summary>
public interface IPriorityRoadsRepository
{
    /// <summary>Returns the priority roads for a market; empty when none configured.</summary>
    Task<Result<IReadOnlyList<PriorityRoad>>> GetRoadsAsync(string market, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads requested markets in bounded batches. Values retain per-market failures so an
    ///     unavailable row does not discard successful rows or change the inventory's input order.
    ///     Implementations may override this default compatibility path with a storage-aware batch
    ///     reader.
    /// </summary>
    async Task<Result<IReadOnlyDictionary<string, Result<IReadOnlyList<PriorityRoad>>>>> GetRoadsForMarketsAsync(
        IReadOnlyList<string> markets, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, Result<IReadOnlyList<PriorityRoad>>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var market in markets ?? [])
        {
            if (!results.ContainsKey(market))
                results[market] = await GetRoadsAsync(market, cancellationToken);
        }

        return Result<IReadOnlyDictionary<string, Result<IReadOnlyList<PriorityRoad>>>>.Success(results);
    }

    /// <summary>
    ///     Every market that has a stored row (even an empty list). Lets the config-bundle export
    ///     include markets that have road config but no schedule.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the priority roads for a market.</summary>
    Task<Result> SetRoadsAsync(
        string market,
        IReadOnlyList<PriorityRoad> roads,
        string updatedBy,
        CancellationToken cancellationToken = default);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
/// Per-market filler statements: short admin-authored fill phrases used when incident
/// content alone can't reach the target bulletin duration.
/// </summary>
public interface IFillerStatementsRepository
{
    /// <summary>Returns the filler statements for a market; empty when none configured.</summary>
    Task<Result<IReadOnlyList<FillerStatement>>> GetStatementsAsync(string market, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads requested markets in bounded batches. Values retain per-market failures so an
    ///     unavailable row does not discard successful rows or change the inventory's input order.
    ///     Implementations may override this default compatibility path with a storage-aware batch
    ///     reader.
    /// </summary>
    async Task<Result<IReadOnlyDictionary<string, Result<IReadOnlyList<FillerStatement>>>>> GetStatementsForMarketsAsync(
        IReadOnlyList<string> markets, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, Result<IReadOnlyList<FillerStatement>>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var market in markets ?? [])
        {
            if (!results.ContainsKey(market))
                results[market] = await GetStatementsAsync(market, cancellationToken);
        }

        return Result<IReadOnlyDictionary<string, Result<IReadOnlyList<FillerStatement>>>>.Success(results);
    }

    /// <summary>
    ///     Every market that has a stored row (even an empty list). Lets the config-bundle export
    ///     include markets that have filler config but no schedule.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the filler statements for a market.</summary>
    Task<Result> SetStatementsAsync(
        string market,
        IReadOnlyList<FillerStatement> statements,
        string updatedBy,
        CancellationToken cancellationToken = default);
}

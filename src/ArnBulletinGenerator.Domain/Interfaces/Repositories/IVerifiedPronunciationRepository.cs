using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Per-market list of script words an operator has confirmed air correctly despite the
///     audio pre-review flagging them (<see cref="VerifiedPronunciation" />). Whole-list
///     replace, mirroring <see cref="IPriorityRoadsRepository" /> — the list is small,
///     operator-curated, and always edited as a set, so per-item upsert/delete plumbing would
///     buy nothing.
/// </summary>
public interface IVerifiedPronunciationRepository
{
    /// <summary>Returns the verified pronunciations for a market; empty when none recorded.</summary>
    Task<Result<IReadOnlyList<VerifiedPronunciation>>> GetForMarketAsync(
        string market, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads requested markets in bounded batches. Values retain per-market failures so an
    ///     unavailable row does not discard successful rows or change the inventory's input order.
    ///     Implementations may override this default compatibility path with a storage-aware batch
    ///     reader.
    /// </summary>
    async Task<Result<IReadOnlyDictionary<string, Result<IReadOnlyList<VerifiedPronunciation>>>>>
        GetForMarketsAsync(
            IReadOnlyList<string> markets, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, Result<IReadOnlyList<VerifiedPronunciation>>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var market in markets ?? [])
        {
            if (!results.ContainsKey(market))
                results[market] = await GetForMarketAsync(market, cancellationToken);
        }

        return Result<IReadOnlyDictionary<string, Result<IReadOnlyList<VerifiedPronunciation>>>>.Success(results);
    }

    /// <summary>
    ///     Every market that has a stored row (even an empty list). Lets the config-bundle export
    ///     include markets that have verified-pronunciation config but no schedule.
    /// </summary>
    Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the verified pronunciations for a market.</summary>
    Task<Result> SetForMarketAsync(
        string market,
        IReadOnlyList<VerifiedPronunciation> words,
        string updatedBy,
        CancellationToken cancellationToken = default);
}

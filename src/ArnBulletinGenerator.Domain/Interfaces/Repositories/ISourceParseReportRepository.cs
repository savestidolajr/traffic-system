using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
/// Persists one source's parse coverage per fetch so deterministic parser improvements can be
/// measured against the free-prose extraction share over time.
/// </summary>
public interface ISourceParseReportRepository
{
    /// <summary>
    /// Saves the reports produced by one market fetch at a common UTC timestamp. Implementations
    /// retain a bounded history; callers should treat a successful result as durable completion.
    /// </summary>
    Task<Result> SaveAsync(
        string marketId,
        DateTimeOffset atUtc,
        IReadOnlyList<SourceParseReport> reports,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists reports in the inclusive UTC range. A non-null market uses a single
    /// market-partition row-key range.
    /// </summary>
    Task<Result<IReadOnlyList<StoredParseReport>>> ListAsync(
        string? marketId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists reports for an explicit bounded market set in the inclusive UTC range. Implementations
    /// must keep both time boundaries in the storage filter; callers must not use an empty set as a
    /// request to scan every market partition.
    /// </summary>
    Task<Result<IReadOnlyList<StoredParseReport>>> ListAsync(
        IReadOnlyList<string> marketIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legacy since-only shape retained for internal source compatibility. Implementations bound
    /// the upper edge to the current UTC instant rather than issuing an unbounded partition scan.
    /// </summary>
    Task<Result<IReadOnlyList<StoredParseReport>>> ListAsync(
        string? marketId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default);
}

/// <summary>A persisted parse report together with the market and fetch timestamp that produced it.</summary>
public sealed record StoredParseReport(
    string MarketId,
    DateTimeOffset AtUtc,
    SourceParseReport Report);

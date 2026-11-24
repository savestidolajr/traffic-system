using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Maintains and reads incremental market/day ledger buckets.</summary>
public interface ILedgerBucketRepository
{
    Task<Result<LedgerBucketPage>> ReadPageAsync(
        string market,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? continuationToken,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<Result> UpsertAsync(LedgerBucketEntry entry, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(LedgerBucketEntry entry, CancellationToken cancellationToken = default);
}

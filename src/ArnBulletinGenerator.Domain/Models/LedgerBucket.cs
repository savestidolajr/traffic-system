namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Scalar reference stored in an incremental market/day ledger bucket.</summary>
public sealed record LedgerBucketEntry(
    string JobId,
    string Market,
    string Station,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    JobStatus Status,
    bool HasIncidents);

/// <summary>One continuation page from the incremental ledger read model.</summary>
public sealed record LedgerBucketPage(
    IReadOnlyList<LedgerBucketEntry> Items,
    string? ContinuationToken);

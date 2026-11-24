namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Canonical point-read routing information for a job. The row is maintained alongside the
///     aggregate in the <c>JobIndex</c> partition and lets authorization/load paths resolve a
///     market without first materialising the full job.
/// </summary>
public sealed record JobIndex(
    string JobId,
    string Market,
    string PartitionKey,
    string? ETag = null);

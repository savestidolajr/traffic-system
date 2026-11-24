namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Scalar row used for bounded newest-first job reads.</summary>
public sealed record JobReadIndexItem(
    string JobId,
    string Market,
    string Station,
    string PartitionKey,
    string? ETag,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    double? MeasuredDurationSec,
    string? AudioUrl);

public sealed record JobReadIndexRetentionPage(
    IReadOnlyList<JobReadIndexItem> Items,
    string? ContinuationToken);

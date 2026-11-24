namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Scalar row used by the bounded retention sweep before a full recheck.</summary>
public sealed record JobRetentionCandidate(
    string JobId,
    string Market,
    string Station,
    string PartitionKey,
    string? ETag,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    string? AudioUrl,
    string? PreviousJobId,
    string? FallbackSourceJobId,
    DateTimeOffset? ScheduledForUtc,
    DateTimeOffset? FallbackDeadlineUtc,
    DateTimeOffset? FallbackAppliedAt,
    bool HasAuditHistory);

/// <summary>One bounded page from the retention candidate index.</summary>
public sealed record JobRetentionPage(
    IReadOnlyList<JobRetentionCandidate> Items,
    string? ContinuationToken);

/// <summary>Outcome of one retention sweep; useful for dry-run and audit telemetry.</summary>
public sealed record JobRetentionReport(
    int Examined,
    int Protected,
    int Deleted,
    int Failed,
    int Orphans,
    string? ContinuationToken,
    IReadOnlyList<JobRetentionDecision> Decisions);

/// <summary>Protection decision recorded for one retention candidate.</summary>
public sealed record JobRetentionDecision(
    string JobId,
    bool Protected,
    string Reason,
    bool Deleted);

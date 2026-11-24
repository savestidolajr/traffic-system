namespace ArnBulletinGenerator.Domain.Models;

/// <summary>The bounded classes of due work consumed by watchdog sweeps.</summary>
public enum JobWorkKind
{
    CurationDue,
    FallbackDue,
    Escalation,
    Missable,
    StaleRequested,
    UndeliveredCovered
}

/// <summary>Scalar work-index projection of a job; no large job payload is copied.</summary>
public sealed record JobWorkItem
{
    public required JobWorkKind Kind { get; init; }
    public required string JobId { get; init; }
    public required string Market { get; init; }
    public required string Station { get; init; }
    public required JobStatus Status { get; init; }
    public required DateTime DueUtc { get; init; }
    public required DateTime LastUpdatedAt { get; init; }
    public DateTime? FallbackDeadlineUtc { get; init; }
    public DateTime? ScheduledForUtc { get; init; }
    public DateTime? CurationDeadlineUtc { get; init; }
    public bool HasAudio { get; init; }
}

/// <summary>A continuation page from the job work index.</summary>
public sealed record JobWorkIndexPage(
    IReadOnlyList<JobWorkItem> Items,
    string? ContinuationToken);

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>One append-only lifecycle or operator-log event for a job.</summary>
public sealed record JobTimelineEntry(
    string JobId,
    long Sequence,
    DateTimeOffset AtUtc,
    string Kind,
    JobStatus? Status = null,
    string? By = null,
    string? Note = null,
    JobLogLevel? Level = null,
    string? Message = null);

/// <summary>Reconstructed append-only histories read from the timeline model.</summary>
public sealed record JobTimelineSnapshot(
    IReadOnlyList<StatusHistoryEntry> StatusHistory,
    IReadOnlyList<JobLog> Logs);

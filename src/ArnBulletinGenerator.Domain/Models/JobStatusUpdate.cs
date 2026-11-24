namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Result of an ETag-guarded targeted status merge.</summary>
public sealed record JobStatusUpdateResult(
    JobStatus PreviousStatus,
    JobStatus CurrentStatus,
    string? ETag);

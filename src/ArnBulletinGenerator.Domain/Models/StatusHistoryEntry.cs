namespace ArnBulletinGenerator.Domain.Models;

/// <summary>One recorded transition in a Job's status timeline, capturing who made the change, when, and why.</summary>
public sealed record StatusHistoryEntry(
    JobStatus Status,
    DateTime At,
    string By,
    Option<string> Note = default);

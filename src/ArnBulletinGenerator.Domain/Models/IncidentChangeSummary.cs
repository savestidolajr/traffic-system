namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Aggregate counts of new, updated, and cleared incidents since the last bulletin, persisted on Job for continuity narration.</summary>
public sealed record IncidentChangeSummary(
    DateTime GeneratedAt,
    int NewCount,
    int UpdatedCount,
    int ClearedCount,
    Option<string> SummaryText = default);

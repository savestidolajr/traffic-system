namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Categories of explicit, reusable editorial instruction that may be replayed later.</summary>
public enum FutureLessonCategory
{
    PriorityOrdering = 0,
    ContentSelection = 1,
    WordingStyle = 2,
    StructureRepetition = 3
}

/// <summary>Lifecycle of a lesson: candidates are stored but not yet replayed into prompts.</summary>
public enum FutureLessonStatus
{
    Candidate = 0,
    Active = 1
}

/// <summary>How a lesson entered the store: taught by an operator or inferred from a published edit.</summary>
public enum FutureLessonOrigin
{
    Operator = 0,
    Inferred = 1
}

/// <summary>
/// One explicit editorial lesson for future bulletins on one station, either taught by an
/// operator or inferred from a published script edit. Lessons expire and repeated
/// teaching/reinforcement merges into the same row.
/// </summary>
public sealed record FutureLessonEntry(
    string Id,
    string Station,
    string SourceJobId,
    FutureLessonCategory Category,
    string Text,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    int OccurrenceCount,
    DateTime ExpiresAtUtc,
    FutureLessonStatus Status = FutureLessonStatus.Active,
    FutureLessonOrigin Origin = FutureLessonOrigin.Operator,
    string? ConflictWithLessonId = null);

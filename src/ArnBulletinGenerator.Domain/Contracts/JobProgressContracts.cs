namespace ArnBulletinGenerator.Domain.Contracts;

/// <summary>Stages exposed by the live generation progress document.</summary>
public enum ProgressStage
{
    Evidence,
    Select,
    Write,
    Compose,
    FitWords,
    Edit,
    Gates,
    Voice,
    FitAudio,
    Ready,
    Failed
}

/// <summary>Event kinds appended to a job's live generation progress document.</summary>
public enum ProgressEventKind
{
    StageStarted,
    StageCompleted,
    SubStep,
    AttemptStarted,
    AttemptFailed,
    LlmCall,
    Note
}

/// <summary>Operator-facing progress snapshot returned by the jobs progress endpoint.</summary>
public sealed record JobProgressDto(
    string JobId,
    string Status,
    string Stage,
    string StageStartedAt,
    int Attempt,
    int MaxAttempts,
    string UpdatedAt,
    int Version,
    IReadOnlyList<JobProgressEventDto> Events,
    IReadOnlyDictionary<string, double>? TypicalSeconds);

/// <summary>One chronological event in a job's append-only progress timeline.</summary>
public sealed record JobProgressEventDto(
    string At,
    int Attempt,
    string Stage,
    string Kind,
    string Detail,
    IReadOnlyDictionary<string, double>? Counts,
    int? DurationMs,
    string? Model,
    string? Route);

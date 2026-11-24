namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Records the outcome of one script/synthesis attempt on a Job, including the speaking rate tried and whether it hit the target duration.</summary>
public sealed record AttemptResult(
    int AttemptNumber,
    double SyllablesPerSecond,
    bool Succeeded,
    DateTime At,
    Option<double> EstimatedDurationSec = default,
    Option<double> ActualDurationSec = default,
    Option<string> FailureReason = default);

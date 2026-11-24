using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts;

/// <summary>What the lesson summariser decided one published operator edit means for the
/// station's stored lessons.</summary>
public enum LessonSummaryAction
{
    /// <summary>The edit is not safely generalisable; store nothing.</summary>
    None = 0,

    /// <summary>A genuinely new station preference.</summary>
    Create = 1,

    /// <summary>The edit supports an existing lesson; increment its evidence.</summary>
    Reinforce = 2,

    /// <summary>The edit contradicts an existing lesson; replace or conflict-mark it.</summary>
    Supersede = 3
}

/// <summary>Structured verdict of the lesson summariser over one published operator edit.</summary>
public sealed record LessonSummaryResult(
    LessonSummaryAction Action,
    FutureLessonCategory Category,
    string Instruction,
    string? TargetLessonId,
    double Confidence,
    string Reason);

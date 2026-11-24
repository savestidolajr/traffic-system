using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Isolated single-call LLM that generalises one published operator script edit into a
///     station-scoped lesson verdict: create / reinforce / supersede / none. Mirrors the Safety
///     role's isolation — no pipeline state, one structured request, one structured answer.
/// </summary>
public interface ILessonSummarizer
{
    Task<Result<LessonSummaryResult>> SummarizeAsync(
        LessonSummaryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>One published operator edit plus the station's stored lessons for dedupe context.</summary>
public sealed record LessonSummaryRequest(
    string Station,
    string OriginalScript,
    string PublishedScript,
    string RenderedDiff,
    IReadOnlyList<FutureLessonEntry> ExistingLessons,
    string? JobId);

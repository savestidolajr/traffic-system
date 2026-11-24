using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Learning;

/// <inheritdoc cref="ILessonLearningService" />
public sealed class LessonLearningService(
    IFutureLessonRepository futureLessonRepository,
    ILessonSummarizer summarizer,
    IJobRepository jobRepository,
    TimeProvider timeProvider,
    ILogger<LessonLearningService> logger) : ILessonLearningService
{
    /// <summary>Summariser verdicts below this probability are discarded.</summary>
    public const double ConfidenceFloor = 0.6;

    public async Task<Result> ProcessAsync(
        LessonLearningQueueMessage message, CancellationToken cancellationToken = default)
    {
        if (ScriptDiff.NormalizedEquals(message.OriginalScript, message.PublishedScript))
        {
            logger.LogInformation(
                "[LessonLearning] Job {JobId}: published script matches the original (tone/voice-only edit); nothing to learn.",
                message.JobId);
            return Result.Success();
        }

        var hunks = ScriptDiff.Compute(message.OriginalScript, message.PublishedScript);
        if (hunks.Count == 0)
        {
            logger.LogInformation(
                "[LessonLearning] Job {JobId}: no word-level changes; nothing to learn.", message.JobId);
            return Result.Success();
        }

        var existingResult = await futureLessonRepository.GetRecentAsync(
            message.Station, FutureLessonPolicy.MaxStoredPerStation, cancellationToken);
        if (existingResult.IsFailure)
        {
            logger.LogWarning(
                "[LessonLearning] Job {JobId}: could not read station lessons ({Error}); retrying.",
                message.JobId, existingResult.Error);
            return existingResult;
        }

        var existing = existingResult.Value;
        var summaryResult = await summarizer.SummarizeAsync(new LessonSummaryRequest(
                message.Station, message.OriginalScript, message.PublishedScript,
                ScriptDiff.Render(hunks), existing, message.JobId),
            cancellationToken);
        if (summaryResult.IsFailure)
        {
            logger.LogWarning(
                "[LessonLearning] Job {JobId}: summariser failed ({Error}); retrying.",
                message.JobId, summaryResult.Error);
            return summaryResult;
        }

        var summary = summaryResult.Value;
        if (summary.Action == LessonSummaryAction.None)
        {
            logger.LogInformation(
                "[LessonLearning] Job {JobId}: edit not generalisable ({Reason}).",
                message.JobId, summary.Reason);
            await AddJobLogSafeAsync(message.JobId,
                "Published operator edit reviewed for station lessons: not generalisable.",
                cancellationToken);
            return Result.Success();
        }

        if (summary.Confidence < ConfidenceFloor)
        {
            logger.LogInformation(
                "[LessonLearning] Job {JobId}: verdict below confidence floor ({Confidence:F2} < {Floor}); discarded instruction: \"{Instruction}\"",
                message.JobId, summary.Confidence, ConfidenceFloor, summary.Instruction);
            await AddJobLogSafeAsync(message.JobId,
                $"Published operator edit reviewed for station lessons: candidate discarded (confidence {summary.Confidence:F2}).",
                cancellationToken);
            return Result.Success();
        }

        // Resolve targets before validation: an unresolved reinforce/supersede degrades to create.
        var action = summary.Action;
        FutureLessonEntry? target = summary.TargetLessonId is null
            ? null
            : existing.FirstOrDefault(l => string.Equals(l.Id, summary.TargetLessonId, StringComparison.Ordinal));
        if (action is LessonSummaryAction.Reinforce or LessonSummaryAction.Supersede && target is null)
        {
            logger.LogInformation(
                "[LessonLearning] Job {JobId}: {Action} target {TargetId} not found; degrading to create.",
                message.JobId, action, summary.TargetLessonId);
            action = LessonSummaryAction.Create;
        }

        if (action is LessonSummaryAction.Create or LessonSummaryAction.Supersede)
        {
            var validation = FutureLessonPolicy.Validate(summary.Category, summary.Instruction);
            if (validation.IsFailure)
            {
                logger.LogInformation(
                    "[LessonLearning] Job {JobId}: instruction failed validation ({Error}); skipping.",
                    message.JobId, validation.Error);
                return Result.Success();
            }
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Result<string?> operation = action switch
        {
            LessonSummaryAction.Reinforce => await ReinforceAsync(message, target!, now, cancellationToken),
            LessonSummaryAction.Supersede => await SupersedeAsync(
                message, summary, target!, existing, now, cancellationToken),
            _ => await CreateAsync(message, summary, existing, conflictWithLessonId: null, now, cancellationToken)
        };
        if (operation.IsFailure)
            return operation;

        if (operation.Value is null)
            return Result.Success();

        var activation = await TryAutoActivateAsync(message, operation.Value, cancellationToken);
        if (activation.IsFailure)
            return activation;
        return Result.Success();
    }

    private async Task<Result<string?>> ReinforceAsync(
        LessonLearningQueueMessage message, FutureLessonEntry target, DateTime now,
        CancellationToken cancellationToken)
    {
        // Rebuild from the TARGET's category/text so the policy id matches and the repository
        // merges (incrementing OccurrenceCount unless this job already reinforced it).
        var entry = new FutureLessonEntry(
            FutureLessonPolicy.CreateId(message.Station, target.Category, target.Text),
            message.Station,
            message.JobId,
            target.Category,
            target.Text,
            now,
            now,
            1,
            now.Add(FutureLessonPolicy.Lifetime),
            FutureLessonStatus.Candidate,
            FutureLessonOrigin.Inferred);

        var result = await futureLessonRepository.AddAsync(entry, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "[LessonLearning] Job {JobId}: reinforcement of lesson {LessonId} failed ({Error}).",
                message.JobId, target.Id, result.Error);
            return result.PropagateFailure<string?>();
        }

        await AddJobLogSafeAsync(message.JobId,
            $"Existing station lesson reinforced ({target.OccurrenceCount + 1} published edits): \"{target.Text}\"",
            cancellationToken);
        return Result<string?>.Success(entry.Id);

    }

    private async Task<Result<string?>> SupersedeAsync(
        LessonLearningQueueMessage message, LessonSummaryResult summary, FutureLessonEntry target,
        IReadOnlyList<FutureLessonEntry> existing, DateTime now, CancellationToken cancellationToken)
    {
        if (target.Status == FutureLessonStatus.Candidate)
        {
            // An unproven candidate loses to fresh contradicting evidence outright.
            var delete = await futureLessonRepository.DeleteAsync(
                message.Station, target.Id, cancellationToken);
            if (delete.IsFailure)
            {
                logger.LogWarning(
                    "[LessonLearning] Job {JobId}: could not delete superseded candidate {LessonId} ({Error}); retrying.",
                    message.JobId, target.Id, delete.Error);
                return delete.PropagateFailure<string?>();
            }

            return await CreateAsync(message, summary, existing.Where(l => l.Id != target.Id).ToList(),
                conflictWithLessonId: null, now, cancellationToken);
        }

        // An ACTIVE lesson is never silently replaced by one edit: the replacement is stored as
        // a conflicted candidate (which can never auto-activate) for an admin to resolve.
        return await CreateAsync(message, summary, existing, target.Id, now, cancellationToken);
    }

    private async Task<Result<string?>> CreateAsync(
        LessonLearningQueueMessage message, LessonSummaryResult summary,
        IReadOnlyList<FutureLessonEntry> existing, string? conflictWithLessonId, DateTime now,
        CancellationToken cancellationToken)
    {
        if (existing.Count >= FutureLessonPolicy.MaxStoredPerStation)
        {
            // Capacity: evict the stalest candidate; never evict an Active lesson.
            var evictee = existing
                .Where(l => l.Status == FutureLessonStatus.Candidate)
                .OrderBy(l => l.LastSeenAtUtc)
                .FirstOrDefault();
            if (evictee is null)
            {
                logger.LogWarning(
                    "[LessonLearning] Job {JobId}: station {Station} stores {Count} lessons, all active; not learning.",
                    message.JobId, message.Station, existing.Count);
                return Result<string?>.Success(null);
            }

            var evict = await futureLessonRepository.DeleteAsync(
                message.Station, evictee.Id, cancellationToken);
            if (evict.IsFailure)
            {
                logger.LogWarning(
                    "[LessonLearning] Job {JobId}: could not evict candidate {LessonId} ({Error}); retrying.",
                    message.JobId, evictee.Id, evict.Error);
                return evict.PropagateFailure<string?>();
            }
        }

        string instruction = summary.Instruction.Trim();
        var entry = new FutureLessonEntry(
            FutureLessonPolicy.CreateId(message.Station, summary.Category, instruction),
            message.Station,
            message.JobId,
            summary.Category,
            instruction,
            now,
            now,
            1,
            now.Add(FutureLessonPolicy.Lifetime),
            FutureLessonStatus.Candidate,
            FutureLessonOrigin.Inferred,
            conflictWithLessonId);

        var result = await futureLessonRepository.AddAsync(entry, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "[LessonLearning] Job {JobId}: could not store lesson candidate ({Error}); retrying.",
                message.JobId, result.Error);
            return result.PropagateFailure<string?>();
        }

        await AddJobLogSafeAsync(message.JobId,
            conflictWithLessonId is null
                ? $"Station lesson candidate learned from this published edit: \"{instruction}\""
                : $"Station lesson candidate learned from this published edit (conflicts with an active lesson): \"{instruction}\"",
            cancellationToken);
        return Result<string?>.Success(entry.Id);
    }

    private async Task<Result> TryAutoActivateAsync(
        LessonLearningQueueMessage message, string affectedId, CancellationToken cancellationToken)
    {
        var refreshed = await futureLessonRepository.GetRecentAsync(
            message.Station, FutureLessonPolicy.MaxStoredPerStation, cancellationToken);
        if (refreshed.IsFailure)
            return refreshed;

        var affected = refreshed.Value.FirstOrDefault(
            l => string.Equals(l.Id, affectedId, StringComparison.Ordinal));
        if (affected is null
            || affected.Status != FutureLessonStatus.Candidate
            || affected.Origin != FutureLessonOrigin.Inferred
            || affected.ConflictWithLessonId is not null
            || affected.OccurrenceCount < FutureLessonPolicy.ActivationOccurrences)
            return Result.Success();

        int activeCount = refreshed.Value.Count(l => l.Status == FutureLessonStatus.Active);
        if (activeCount >= FutureLessonPolicy.MaxActivePerStation)
        {
            logger.LogInformation(
                "[LessonLearning] Job {JobId}: candidate {LessonId} earned activation but station {Station} already has {Count} active lessons.",
                message.JobId, affectedId, message.Station, activeCount);
            return Result.Success();
        }

        var activate = await futureLessonRepository.SetStatusAsync(
            message.Station, affectedId, FutureLessonStatus.Active, cancellationToken);
        if (activate.IsFailure)
        {
            logger.LogWarning(
                "[LessonLearning] Job {JobId}: could not activate lesson {LessonId} ({Error}); retrying.",
                message.JobId, affectedId, activate.Error);
            return activate;
        }

        await AddJobLogSafeAsync(message.JobId,
            $"Station lesson activated after {affected.OccurrenceCount} published edits: \"{affected.Text}\"",
            cancellationToken);
        return Result.Success();
    }

    private async Task AddJobLogSafeAsync(string jobId, string message, CancellationToken cancellationToken)
    {
        try
        {
            await jobRepository.AddLogAsync(jobId, message, JobLogLevel.Info, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[LessonLearning] Could not write job log for {JobId}.", jobId);
        }
    }
}

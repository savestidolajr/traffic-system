using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>Runs the disabled-by-default, checkpointed job retention policy.</summary>
public interface IJobRetentionService
{
    Task<Result<JobRetentionReport>> RunAsync(CancellationToken cancellationToken = default);
}

public sealed class JobRetentionService(
    IJobRetentionStore store,
    IOptions<JobRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<JobRetentionService> logger) : IJobRetentionService
{
    public async Task<Result<JobRetentionReport>> RunAsync(
        CancellationToken cancellationToken = default)
    {
        var policy = options.Value;
        var now = timeProvider.GetUtcNow();
        if (policy.Disable || policy.RetentionDays <= 0)
        {
            logger.LogDebug("Job retention disabled; no rows examined");
            return Result<JobRetentionReport>.Success(new JobRetentionReport(
                0, 0, 0, 0, 0, policy.Checkpoint, []));
        }

        if (policy.MaxRowsPerRun <= 0)
            return Result<JobRetentionReport>.Success(new JobRetentionReport(
                0, 0, 0, 0, 0, policy.Checkpoint, []));
        int maxRows = Math.Min(policy.MaxRowsPerRun, 1000);
        int pageSize = Math.Clamp(policy.PageSize, 1, Math.Min(200, maxRows));
        var page = await store.ReadPageAsync(
            now.AddDays(-policy.RetentionDays), policy.Checkpoint, pageSize, cancellationToken);
        if (page.IsFailure)
            return Result<JobRetentionReport>.Failure(page.Error);

        var decisions = new List<JobRetentionDecision>(page.Value.Items.Count);
        int examined = 0;
        int protectedCount = 0;
        int deleted = 0;
        int failed = 0;

        foreach (var candidate in page.Value.Items.Take(maxRows))
        {
            examined++;
            string? protection = ProtectionReason(candidate, now, policy);
            if (protection is not null)
            {
                protectedCount++;
                decisions.Add(new JobRetentionDecision(candidate.JobId, true, protection, false));
                continue;
            }

            var recheck = await store.RecheckAsync(candidate.JobId, cancellationToken);
            if (recheck.IsFailure)
            {
                failed++;
                decisions.Add(new JobRetentionDecision(candidate.JobId, true,
                    "recheck-failed", false));
                continue;
            }
            var current = recheck.Value;
            protection = ProtectionReason(current, now, policy);
            if (protection is not null
                || !string.Equals(current.ETag, candidate.ETag, StringComparison.Ordinal))
            {
                protectedCount++;
                decisions.Add(new JobRetentionDecision(candidate.JobId, true,
                    protection ?? "etag-changed", false));
                continue;
            }

            if (policy.DryRun || policy.AuditOnly)
            {
                decisions.Add(new JobRetentionDecision(candidate.JobId, false,
                    policy.AuditOnly ? "audit-only" : "dry-run", false));
                continue;
            }

            var deletion = await store.DeleteAsync(current, cancellationToken);
            if (deletion.IsSuccess)
            {
                deleted++;
                decisions.Add(new JobRetentionDecision(candidate.JobId, false, "expired", true));
            }
            else
            {
                failed++;
                decisions.Add(new JobRetentionDecision(candidate.JobId, true,
                    deletion.Error ?? "delete-failed", false));
            }
        }

        int orphans = 0;
        if (policy.ReconcileOrphans)
        {
            var reconciliation = await store.ReconcileOrphansAsync(
                now.AddDays(-policy.RetentionDays), pageSize,
                deleteOrphans: !policy.DryRun && !policy.AuditOnly, cancellationToken);
            if (reconciliation.IsSuccess)
                orphans = reconciliation.Value;
            else
                logger.LogWarning("Retention orphan reconciliation failed: {Error}", reconciliation.Error);
        }

        return Result<JobRetentionReport>.Success(new JobRetentionReport(
            examined, protectedCount, deleted, failed, orphans,
            page.Value.ContinuationToken, decisions));
    }

    private static string? ProtectionReason(
        JobRetentionCandidate candidate,
        DateTimeOffset now,
        JobRetentionOptions policy)
    {
        if (candidate.Status is not (JobStatus.Rejected or JobStatus.Published
            or JobStatus.FallbackUsed or JobStatus.Failed or JobStatus.Missed))
            return "active";

        var grace = now.AddHours(-Math.Max(0, policy.GracePeriodHours));
        if (candidate.LastUpdatedAt >= grace)
            return "settlement-grace";

        // A row tied to a slot that is still in or just past its playout window is not safe to
        // remove: the watchdog/fallback path may still need its source or audit trail.
        if (candidate.ScheduledForUtc is { } scheduled
            && scheduled >= grace)
            return "scheduled-grace";
        if (candidate.FallbackDeadlineUtc is { } deadline
            && deadline >= grace)
            return "fallback-grace";

        if (candidate.CreatedAt >= now.AddDays(-Math.Max(0, policy.ContinuityWindowDays)))
            return "continuity-window";
        if (candidate.FallbackAppliedAt is { } fallback
            && fallback >= now.AddDays(-Math.Max(0, policy.FallbackWindowDays)))
            return "fallback-window";
        if (candidate.HasAuditHistory
            && candidate.CreatedAt >= now.AddDays(-Math.Max(0, policy.AuditWindowDays)))
            return "audit-window";
        if (!string.IsNullOrWhiteSpace(candidate.AudioUrl)
            && candidate.CreatedAt >= now.AddDays(-Math.Max(0, policy.AudioWindowDays)))
            return "audio-window";
        if (candidate.HasAuditHistory
            && candidate.CreatedAt >= now.AddDays(-Math.Max(0, policy.OperatorHistoryWindowDays)))
            return "operator-history-window";
        return null;
    }
}

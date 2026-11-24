using System.Collections.Concurrent;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Owns one job's structured progress document. Event mutation is serialized in memory while
///     replacement upserts are queued in the same order, so a storage delay or outage never changes
///     the generation result.
/// </summary>
internal sealed class JobProgressReporter(
    string jobId,
    string market,
    JobStatus initialStatus,
    IJobProgressRepository repository,
    TimeProvider timeProvider,
    ILogger<JobProgressReporter> logger) : IGenerationProgressReporter
{
    private readonly object _queueGate = new();
    private readonly object _persistenceGate = new();
    private readonly Dictionary<ProgressStage, DateTimeOffset> _stageStarts = [];
    private readonly Dictionary<string, double> _stageSeconds = new(StringComparer.Ordinal);
    private Task _eventTail = Task.CompletedTask;
    private Task _persistenceTail = Task.CompletedTask;
    private JobProgressDocument? _document;
    private bool _initializationAttempted;

    public ValueTask StatusChanged(JobStatus status) => Enqueue(document =>
    {
        bool wasInitialized = _initializationAttempted;
        document.SetStatus(timeProvider.GetUtcNow(), status);
        // Persist the first status even when it is equal to the factory's initial status. This creates
        // a point-readable document for jobs that fail before their first stage starts.
        ScheduleUpsert(document.Clone(), persistEvenWhenUnchanged: !wasInitialized || document.Version == 0);
        return Task.CompletedTask;
    });

    public ValueTask StageStarted(
        ProgressStage stage,
        string detail,
        IReadOnlyDictionary<string, double>? counts = null) => Enqueue(document =>
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        _stageStarts[stage] = now;
        document.Append(now, document.Attempt, stage, ProgressEventKind.StageStarted, detail, counts);
        ScheduleUpsert(document.Clone());
        return Task.CompletedTask;
    });

    public ValueTask StageCompleted(
        ProgressStage stage,
        string detail,
        IReadOnlyDictionary<string, double>? counts = null,
        int? durationMs = null) => Enqueue(document =>
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        int? effectiveDuration = durationMs;
        if (effectiveDuration is null && _stageStarts.TryGetValue(stage, out var started))
            effectiveDuration = Math.Max(0, (int)Math.Round((now - started).TotalMilliseconds));
        if (effectiveDuration is >= 0)
            _stageSeconds[stage.ToString()] = _stageSeconds.GetValueOrDefault(stage.ToString())
                + effectiveDuration.Value / 1000.0;
        document.Append(now, document.Attempt, stage, ProgressEventKind.StageCompleted, detail, counts,
            effectiveDuration);
        ScheduleUpsert(document.Clone());
        return Task.CompletedTask;
    });

    public ValueTask SubStep(
        ProgressStage stage,
        string detail,
        IReadOnlyDictionary<string, double>? counts = null) => Enqueue(document =>
    {
        document.Append(timeProvider.GetUtcNow(), document.Attempt, stage,
            ProgressEventKind.SubStep, detail, counts);
        ScheduleUpsert(document.Clone());
        return Task.CompletedTask;
    });

    public ValueTask AttemptStarted(int attempt, int maxAttempts, string detail) => Enqueue(document =>
    {
        document.SetAttemptBounds(attempt, maxAttempts);
        ProgressStage stage = ParseStage(document.Stage);
        if (stage is ProgressStage.Ready or ProgressStage.Failed)
            stage = ProgressStage.Write;
        document.Append(timeProvider.GetUtcNow(), attempt, stage,
            ProgressEventKind.AttemptStarted, detail);
        ScheduleUpsert(document.Clone());
        return Task.CompletedTask;
    });

    public ValueTask AttemptFailed(string detail) => Enqueue(document =>
    {
        document.Append(timeProvider.GetUtcNow(), document.Attempt, ParseStage(document.Stage),
            ProgressEventKind.AttemptFailed, detail);
        ScheduleUpsert(document.Clone());
        return Task.CompletedTask;
    });

    public ValueTask LlmCall(ProgressStage stage, string route, string model, int durationMs) => Enqueue(document =>
    {
        int safeDuration = Math.Max(0, durationMs);
        document.Append(timeProvider.GetUtcNow(), document.Attempt, stage,
            ProgressEventKind.LlmCall, $"{route} completed in {safeDuration}ms.",
            durationMs: safeDuration, model: model, route: route);
        ScheduleUpsert(document.Clone());
        return Task.CompletedTask;
    });

    public ValueTask Ready(string detail) => Enqueue(async document =>
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        document.Append(now, document.Attempt, ProgressStage.Ready,
            ProgressEventKind.Note, detail);
        var completed = new Dictionary<string, double>(_stageSeconds, StringComparer.Ordinal);
        if (completed.Count > 0)
        {
            try
            {
                var typical = await repository.UpdateTypicalAsync(market, completed).ConfigureAwait(false);
                if (typical.IsSuccess)
                    document.SetTypicalSeconds(typical.Value);
                else
                    logger.LogWarning(
                        "[JobProgress] Job {JobId} typical duration update failed: {Error}", jobId, typical.Error);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "[JobProgress] Job {JobId} typical duration update threw", jobId);
            }
        }
        ScheduleUpsert(document.Clone());
    });

    public ValueTask Failed(string detail) => Enqueue(document =>
    {
        document.Append(timeProvider.GetUtcNow(), document.Attempt, ProgressStage.Failed,
            ProgressEventKind.Note, detail);
        ScheduleUpsert(document.Clone());
        return Task.CompletedTask;
    });

    private ValueTask Enqueue(Func<JobProgressDocument, Task> operation)
    {
        lock (_queueGate)
        {
            _eventTail = RunAfterAsync(_eventTail, operation);
            return new ValueTask(_eventTail);
        }
    }

    private async Task RunAfterAsync(Task previous, Func<JobProgressDocument, Task> operation)
    {
        try
        {
            await previous.ConfigureAwait(false);
            JobProgressDocument document = await EnsureInitializedAsync().ConfigureAwait(false);
            await operation(document).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The progress channel is diagnostic and must not turn an otherwise valid bulletin into
            // a failed generation. Every repository and observer exception ends here.
            logger.LogWarning(exception,
                "[JobProgress] Job {JobId} progress event could not be persisted", jobId);
        }
    }

    private async Task<JobProgressDocument> EnsureInitializedAsync()
    {
        if (_document is not null)
            return _document;
        if (_initializationAttempted)
            return _document ??= NewDocument();

        _initializationAttempted = true;
        try
        {
            var existing = await repository.GetAsync(jobId).ConfigureAwait(false);
            if (existing.IsSuccess && existing.Value.HasValue)
            {
                _document = existing.Value.Value.Document;
                HydrateStageDurations(_document);
                return _document;
            }
            if (existing.IsFailure)
                logger.LogWarning("[JobProgress] Job {JobId} progress read failed: {Error}", jobId, existing.Error);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[JobProgress] Job {JobId} progress read threw", jobId);
        }

        return _document ??= NewDocument();
    }

    private JobProgressDocument NewDocument() =>
        new(jobId, market, initialStatus, timeProvider.GetUtcNow());

    private void ScheduleUpsert(JobProgressDocument snapshot, bool persistEvenWhenUnchanged = true)
    {
        if (!persistEvenWhenUnchanged)
            return;

        lock (_persistenceGate)
        {
            Task prior = _persistenceTail;
            _persistenceTail = prior.IsCompleted
                ? PersistSnapshotAsync(snapshot)
                : prior.ContinueWith(
                    _ => PersistSnapshotAsync(snapshot),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default).Unwrap();
        }
    }

    private async Task PersistSnapshotAsync(JobProgressDocument snapshot)
    {
        try
        {
            var result = await repository.UpsertAsync(snapshot).ConfigureAwait(false);
            if (result.IsFailure)
                logger.LogWarning("[JobProgress] Job {JobId} upsert failed: {Error}", jobId, result.Error);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "[JobProgress] Job {JobId} upsert threw", jobId);
        }
    }

    private static ProgressStage ParseStage(string stage) =>
        Enum.TryParse(stage, ignoreCase: true, out ProgressStage parsed)
            ? parsed
            : ProgressStage.Evidence;

    private void HydrateStageDurations(JobProgressDocument document)
    {
        foreach (var item in document.Events)
        {
            if (!Enum.TryParse(item.Stage, ignoreCase: true, out ProgressStage stage))
                continue;
            bool isStageStart = Enum.TryParse(
                item.Kind, ignoreCase: true, out ProgressEventKind kind)
                && kind == ProgressEventKind.StageStarted;
            if (isStageStart && DateTimeOffset.TryParse(item.At, out var started))
                _stageStarts[stage] = started;
            if (kind == ProgressEventKind.StageCompleted && item.DurationMs is >= 0)
                _stageSeconds[item.Stage] = _stageSeconds.GetValueOrDefault(item.Stage)
                    + item.DurationMs.Value / 1000.0;
        }
    }

}
internal sealed class GenerationProgressReporterFactory(
    IJobProgressRepository repository,
    TimeProvider timeProvider,
    ILogger<JobProgressReporter> logger) : IGenerationProgressReporterFactory
{
    private readonly ConcurrentDictionary<string, IGenerationProgressReporter> _reporters = new(StringComparer.Ordinal);

    public IGenerationProgressReporter Create(string jobId, string market, JobStatus status) =>
        _reporters.GetOrAdd(jobId, _ => new JobProgressReporter(
            jobId, market, status, repository, timeProvider, logger));

    public IGenerationProgressReporter? TryGet(string jobId) =>
        _reporters.TryGetValue(jobId, out var reporter) ? reporter : null;
}

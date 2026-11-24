using System.Globalization;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;

namespace ArnBulletinGenerator.Workflow.Trace;

/// <summary>
///     Adapts the workflow's diagnostic trace to the domain progress reporter. The trace remains the
///     workflow's complete record while this relay projects only the operator-facing stages and keeps
///     diagnostic observer failures outside the generation result.
/// </summary>
internal sealed class GenerationProgressTraceRelay(IGenerationProgressReporter reporter)
{
    private readonly object _gate = new();
    private readonly List<TraceEvent> _pendingGates = [];
    private int _candidateNumber;
    private int _composeDepth;
    private DateTimeOffset? _composeStartedAt;
    private bool _editCompleted;
    private bool _fitWordsActive;
    private bool _fitWordsFallbackStarted;

    /// <summary>Receives one trace event immediately after it is recorded.</summary>
    public void Observe(TraceEvent traceEvent)
    {
        if (traceEvent is null)
            return;

        if (traceEvent.Kind is TraceEventKind.StageStart or TraceEventKind.StageEnd)
        {
            if (!TryMapStage(traceEvent.Stage, out var stage))
                return;

            if (stage == ProgressStage.Gates)
            {
                BufferOrPublishGate(traceEvent, stage);
                return;
            }

            if (stage == ProgressStage.Compose)
            {
                HandleComposeBoundary(traceEvent);
                return;
            }

            if (traceEvent.Kind == TraceEventKind.StageStart)
            {
                if (stage == ProgressStage.FitWords)
                {
                    lock (_gate)
                    {
                        _fitWordsActive = true;
                        _fitWordsFallbackStarted = true;
                    }
                }
                Publish(r => r.StageStarted(stage, Sentence(traceEvent.Summary)));
            }
            else
            {
                if (stage == ProgressStage.FitWords)
                {
                    lock (_gate)
                        _fitWordsActive = false;
                }
                Publish(r => r.StageCompleted(
                    stage,
                    Sentence(traceEvent.Summary),
                    durationMs: DurationMilliseconds(traceEvent.DurationMs)));
                if (stage == ProgressStage.Edit)
                {
                    lock (_gate)
                        _editCompleted = true;
                    TryFlushGates();
                }
            }

            return;
        }

        if (traceEvent.Kind == TraceEventKind.Decision
            && string.Equals(traceEvent.Stage, "Edit", StringComparison.Ordinal)
            && traceEvent.Summary.StartsWith("Editor review skipped", StringComparison.Ordinal))
        {
            Publish(r => r.SubStep(ProgressStage.Edit, "skipped: deterministic checks already failed; revising"));
            lock (_gate)
                _editCompleted = true;
            TryFlushGates();
            return;
        }

        if (traceEvent.Kind == TraceEventKind.LlmCall && traceEvent.LlmCall is { } call)
        {
            if (!TryMapStage(traceEvent.Stage, out var stage))
                return;

            lock (_gate)
            {
                // Word-loop continuations are recorded by the composer under its provider role,
                // while the surrounding FitWords stage identifies their operator-facing purpose.
                if (stage == ProgressStage.Compose && _fitWordsActive)
                    stage = ProgressStage.FitWords;
            }

            Publish(r => r.LlmCall(
                stage,
                string.IsNullOrWhiteSpace(call.Role) ? traceEvent.Stage : call.Role,
                call.Model,
                DurationMilliseconds(call.DurationMs)));
            return;
        }

        if (traceEvent.Kind == TraceEventKind.Measurement)
        {
            if (string.Equals(traceEvent.Stage, "Compose", StringComparison.Ordinal)
                && traceEvent.Summary.StartsWith("Candidate aimed at", StringComparison.Ordinal))
            {
                PublishCandidate(traceEvent);
                return;
            }

            if (string.Equals(traceEvent.Stage, "FitWords", StringComparison.Ordinal)
                || (string.Equals(traceEvent.Stage, "Compose", StringComparison.Ordinal)
                    && traceEvent.Summary.StartsWith("Word-loop round", StringComparison.Ordinal)))
            {
                PublishWordLoopRound(traceEvent);
            }

            return;
        }

        if (traceEvent.Kind == TraceEventKind.Check)
        {
            if (string.Equals(traceEvent.Stage, "CandidateChecks", StringComparison.Ordinal))
                return;

            if (string.Equals(traceEvent.Stage, "Gates", StringComparison.Ordinal))
            {
                BufferOrPublishGate(traceEvent, ProgressStage.Gates);
                return;
            }

            if (string.Equals(traceEvent.Stage, "Edit", StringComparison.Ordinal))
            {
                bool approved = traceEvent.Error is null;
                string detail = approved
                    ? "approved"
                    : $"revision {Math.Max(1, traceEvent.Iteration).ToString(CultureInfo.InvariantCulture)}";
                Publish(r => r.SubStep(ProgressStage.Edit, detail));
            }
        }
    }

    private void HandleComposeBoundary(TraceEvent traceEvent)
    {
        if (traceEvent.Kind == TraceEventKind.StageStart)
        {
            bool publish;
            lock (_gate)
            {
                publish = _composeDepth == 0;
                if (publish)
                    _composeStartedAt = traceEvent.At;
                _composeDepth++;
            }

            if (publish)
                Publish(r => r.StageStarted(ProgressStage.Compose, Sentence(traceEvent.Summary)));
            return;
        }

        DateTimeOffset? started;
        bool publishEnd;
        lock (_gate)
        {
            if (_composeDepth == 0)
                return;
            _composeDepth--;
            publishEnd = _composeDepth == 0;
            started = _composeStartedAt;
            if (publishEnd)
                _composeStartedAt = null;
        }

        if (publishEnd)
        {
            int? durationMs = started is { } value
                ? DurationMilliseconds((traceEvent.At - value).TotalMilliseconds)
                : DurationMilliseconds(traceEvent.DurationMs);
            Publish(r => r.StageCompleted(
                ProgressStage.Compose,
                Sentence(traceEvent.Summary),
                durationMs: durationMs));
        }
    }

    private void PublishCandidate(TraceEvent traceEvent)
    {
        int candidate;
        lock (_gate)
        {
            candidate = ReadCount(traceEvent.Data, "candidate")
                ?? ++_candidateNumber;
            _candidateNumber = Math.Max(_candidateNumber, candidate);
        }

        int words = ReadCount(traceEvent.Data, "words") ?? 0;
        var counts = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["candidate"] = candidate,
            ["words"] = words
        };
        Publish(r => r.SubStep(
            ProgressStage.Compose,
            Sentence($"Candidate {candidate} returned {words} words."),
            counts));
    }

    private void PublishWordLoopRound(TraceEvent traceEvent)
    {
        bool startFallback;
        lock (_gate)
        {
            startFallback = string.Equals(traceEvent.Stage, "Compose", StringComparison.Ordinal)
                && !_fitWordsFallbackStarted;
            if (startFallback)
                _fitWordsFallbackStarted = true;
        }

        if (startFallback)
            Publish(r => r.StageStarted(ProgressStage.FitWords, "Fitting the chosen read to its word band."));

        int round = ReadCount(traceEvent.Data, "round") ?? 0;
        int words = ReadCount(traceEvent.Data, "afterWords")
            ?? ReadCount(traceEvent.Data, "words")
            ?? ReadCount(traceEvent.Data, "beforeWords")
            ?? 0;
        var counts = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["round"] = round,
            ["words"] = words
        };
        Publish(r => r.SubStep(
            ProgressStage.FitWords,
            Sentence(traceEvent.Summary),
            counts));
    }

    private void BufferOrPublishGate(TraceEvent traceEvent, ProgressStage stage)
    {
        bool publishNow;
        lock (_gate)
        {
            // Each deterministic check pass precedes its editor pass. Resetting here prevents a
            // later revision's checks from appearing before that revision's Edit stage.
            if (traceEvent.Kind == TraceEventKind.StageStart)
                _editCompleted = false;
            publishNow = _editCompleted;
            if (!publishNow)
                _pendingGates.Add(traceEvent);
        }

        if (publishNow)
            PublishGateEvent(traceEvent, stage);
    }

    private void TryFlushGates()
    {
        List<TraceEvent> events;
        lock (_gate)
        {
            events = [.. _pendingGates];
            _pendingGates.Clear();
        }

        foreach (TraceEvent traceEvent in events)
            PublishGateEvent(traceEvent, ProgressStage.Gates);
    }

    private void PublishGateEvent(TraceEvent traceEvent, ProgressStage stage)
    {
        if (traceEvent.Kind == TraceEventKind.StageStart)
        {
            Publish(r => r.StageStarted(stage, Sentence(traceEvent.Summary)));
            return;
        }

        if (traceEvent.Kind == TraceEventKind.StageEnd)
        {
            Publish(r => r.StageCompleted(
                stage,
                Sentence(traceEvent.Summary),
                durationMs: DurationMilliseconds(traceEvent.DurationMs)));
            return;
        }

        bool passed = traceEvent.Error is null;
        var counts = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["passed"] = passed ? 1 : 0
        };
        Publish(r => r.SubStep(
            stage,
            Sentence(traceEvent.Summary),
            counts));
    }

    private void Publish(Func<IGenerationProgressReporter, ValueTask> operation) =>
        _ = DispatchAsync(operation);

    private async Task DispatchAsync(Func<IGenerationProgressReporter, ValueTask> operation)
    {
        try
        {
            await operation(reporter).ConfigureAwait(false);
        }
        catch
        {
            // Progress is diagnostic only. Observer failures cannot fail a bulletin run.
        }
    }

    private static bool TryMapStage(string source, out ProgressStage stage)
    {
        stage = source switch
        {
            "Write" => ProgressStage.Write,
            "Compose" => ProgressStage.Compose,
            "FitWords" => ProgressStage.FitWords,
            "Edit" => ProgressStage.Edit,
            "Gates" or "Checks" => ProgressStage.Gates,
            _ => default
        };
        return source is "Write" or "Compose" or "FitWords" or "Edit" or "Gates" or "Checks";
    }

    private static int? ReadCount(
        IReadOnlyDictionary<string, string>? data,
        string key)
    {
        if (data is null || !data.TryGetValue(key, out string? raw))
            return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static int DurationMilliseconds(double? durationMs) =>
        durationMs is { } value && double.IsFinite(value)
            ? Math.Max(0, (int)Math.Round(value))
            : 0;

    private static string Sentence(string? detail)
    {
        string value = string.IsNullOrWhiteSpace(detail) ? "Progress updated." : detail.Trim();
        value = value.Replace('\u2014', '-').Replace('\u2013', '-');
        return value.EndsWith(".", StringComparison.Ordinal)
            || value.EndsWith("!", StringComparison.Ordinal)
            || value.EndsWith("?", StringComparison.Ordinal)
            ? value
            : value + ".";
    }
}

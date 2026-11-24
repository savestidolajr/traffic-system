using System.Diagnostics;

namespace ArnBulletinGenerator.Workflow.Trace;

/// <summary>
///     Collects trace events during a run and freezes them into a <see cref="WorkflowTrace" />.
///     Not thread-safe by design: a workflow run is a single logical thread of decisions, and
///     concurrent stages would make the timeline meaningless.
/// </summary>
public sealed class TraceRecorder
{
    private readonly List<TraceEvent> _events = [];

    /// <summary>
    ///     Guards the event list. The composer fans out candidate reads concurrently, and each one
    ///     records its own stage and LLM call, so appends genuinely race. Sequence numbers are
    ///     assigned under the same lock as the append: derived from Count outside it, two events
    ///     silently share a number and the trace viewer renders them out of order.
    /// </summary>
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly Action<TraceEvent>? _onEvent;
    private readonly DateTimeOffset _startedAt;

    public TraceRecorder(
        string runId,
        string workflowId,
        string workflowDescription,
        string jobId,
        string market,
        string station,
        double targetSeconds,
        TimeProvider? timeProvider = null,
        Action<TraceEvent>? onEvent = null)
    {
        RunId = runId;
        WorkflowId = workflowId;
        WorkflowDescription = workflowDescription;
        JobId = jobId;
        Market = market;
        Station = station;
        TargetSeconds = targetSeconds;
        _time = timeProvider ?? TimeProvider.System;
        _onEvent = onEvent;
        _startedAt = _time.GetUtcNow();
    }

    public string RunId { get; }
    public string WorkflowId { get; }
    public string WorkflowDescription { get; }
    public string JobId { get; }
    public string Market { get; }
    public string Station { get; }
    public double TargetSeconds { get; }

    /// <summary>The loop pass every subsequent event is attributed to. Stages never set this themselves.</summary>
    public int Iteration { get; private set; } = 1;

    public void BeginIteration(int iteration) => Iteration = iteration;

    public void StageStart(string stage, string summary, IReadOnlyDictionary<string, string>? data = null) =>
        Add(stage, TraceEventKind.StageStart, summary, null, data);

    public void StageEnd(string stage, string summary, double durationMs, IReadOnlyDictionary<string, string>? data = null) =>
        Add(stage, TraceEventKind.StageEnd, summary, durationMs, data);

    public void Decision(string stage, string summary, IReadOnlyDictionary<string, string>? data = null) =>
        Add(stage, TraceEventKind.Decision, summary, null, data);

    public void Artifact(string stage, string summary, IReadOnlyDictionary<string, string> data) =>
        Add(stage, TraceEventKind.Artifact, summary, null, data);

    public void Check(string stage, string summary, bool passed, IReadOnlyDictionary<string, string>? data = null) =>
        Add(stage, TraceEventKind.Check, summary, null, data, error: passed ? null : summary);

    public void Measurement(string stage, string summary, double durationMs, IReadOnlyDictionary<string, string>? data = null) =>
        Add(stage, TraceEventKind.Measurement, summary, durationMs, data);

    public void Failure(string stage, string summary, string error, IReadOnlyDictionary<string, string>? data = null) =>
        Add(stage, TraceEventKind.Failure, summary, null, data, error: error);

    public void LlmCall(string stage, LlmCallRecord call)
    {
        TraceEvent traceEvent;
        lock (_gate)
        {
            traceEvent = new TraceEvent(
                _events.Count + 1, Iteration, stage, TraceEventKind.LlmCall, _time.GetUtcNow(),
                call.Error is null
                    ? $"{call.Role} responded in {call.DurationMs:F0}ms"
                    : $"{call.Role} failed: {call.Error}",
                call.DurationMs, null, call, call.Error);
            _events.Add(traceEvent);
        }

        Publish(traceEvent);
    }

    /// <summary>A stable copy of the events so far, taken under the append lock.</summary>
    private List<TraceEvent> Snapshot()
    {
        lock (_gate)
            return [.. _events];
    }

    /// <summary>Times an operation and records a stage start/end pair around it.</summary>
    public async Task<T> TimeStageAsync<T>(string stage, string startSummary, Func<Task<T>> operation,
        Func<T, string>? endSummary = null)
    {
        StageStart(stage, startSummary);
        var sw = Stopwatch.StartNew();
        T result = await operation();
        sw.Stop();
        StageEnd(stage, endSummary?.Invoke(result) ?? $"{stage} complete", sw.Elapsed.TotalMilliseconds);
        return result;
    }

    public WorkflowTrace Freeze(string? outcome = null, string? error = null) =>
        new(RunId, WorkflowId, WorkflowDescription, JobId, Market, Station, TargetSeconds,
            _startedAt, _time.GetUtcNow(), Snapshot(), outcome, error);

    private void Add(string stage, TraceEventKind kind, string summary, double? durationMs,
        IReadOnlyDictionary<string, string>? data, string? error = null)
    {
        TraceEvent traceEvent;
        lock (_gate)
        {
            traceEvent = new TraceEvent(
                _events.Count + 1, Iteration, stage, kind, _time.GetUtcNow(), summary, durationMs, data, null, error);
            _events.Add(traceEvent);
        }

        Publish(traceEvent);
    }

    /// <summary>
    ///     Relays a trace event to the optional live observer without allowing observer failures to
    ///     alter the workflow. The callback is intentionally outside the trace lock because the
    ///     observer may enqueue asynchronous persistence work.
    /// </summary>
    private void Publish(TraceEvent traceEvent)
    {
        if (_onEvent is null)
            return;

        try
        {
            _onEvent(traceEvent);
        }
        catch
        {
            // Progress is diagnostic only. A broken observer must never fail a bulletin run.
        }
    }
}

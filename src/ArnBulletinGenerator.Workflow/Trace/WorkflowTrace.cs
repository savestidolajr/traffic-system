namespace ArnBulletinGenerator.Workflow.Trace;

/// <summary>
///     Everything that happened in one workflow run, in order. The trace is the product, not a
///     debugging afterthought: a stage that does not record what it decided cannot be evaluated,
///     and an approach that cannot be evaluated cannot be compared against the one it replaces.
/// </summary>
/// <param name="RunId">Unique per run. Names the emitted HTML file.</param>
/// <param name="WorkflowId">Which implementation produced this.</param>
/// <param name="Events">Ordered. Sequence numbers are dense and start at 1.</param>
public sealed record WorkflowTrace(
    string RunId,
    string WorkflowId,
    string WorkflowDescription,
    string JobId,
    string Market,
    string Station,
    double TargetSeconds,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<TraceEvent> Events,
    string? Outcome = null,
    string? Error = null)
{
    public double TotalMs => CompletedAt is null ? 0 : (CompletedAt.Value - StartedAt).TotalMilliseconds;

    public IReadOnlyList<LlmCallRecord> LlmCalls =>
        Events.Where(e => e.LlmCall is not null).Select(e => e.LlmCall!).ToList();

    public int LlmCallCount => LlmCalls.Count;

    /// <summary>Highest loop iteration reached. 1 means the read landed on the first pass.</summary>
    public int Iterations => Events.Count == 0 ? 0 : Events.Max(e => e.Iteration);
}

/// <summary>
///     One thing that happened. Deliberately a single shape rather than a hierarchy: the viewer
///     renders a flat timeline, and a flat record keeps every stage free to say what it needs
///     through <see cref="Data" /> without widening a base class.
/// </summary>
/// <param name="Sequence">1-based, dense, assigned by the recorder.</param>
/// <param name="Iteration">Which pass of the fit/compose/verify loop. 1-based.</param>
/// <param name="Stage">Stage name, e.g. <c>Normalize</c>, <c>Plan</c>, <c>Fit</c>, <c>Compose</c>.</param>
/// <param name="Kind">What sort of event, drives the viewer's styling.</param>
/// <param name="Summary">One line, always shown.</param>
/// <param name="DurationMs">Wall time, when the event measured something.</param>
/// <param name="Data">Expandable detail. Keys become section headings in the viewer.</param>
/// <param name="LlmCall">Present only on <see cref="TraceEventKind.LlmCall" /> events.</param>
public sealed record TraceEvent(
    int Sequence,
    int Iteration,
    string Stage,
    TraceEventKind Kind,
    DateTimeOffset At,
    string Summary,
    double? DurationMs = null,
    IReadOnlyDictionary<string, string>? Data = null,
    LlmCallRecord? LlmCall = null,
    string? Error = null);

public enum TraceEventKind
{
    StageStart,
    StageEnd,
    LlmCall,
    Decision,
    Artifact,
    Check,
    Measurement,
    Failure
}

/// <summary>
///     One model round trip, captured whole. The prompts are stored as sent — a trace that shows a
///     template rather than the rendered text cannot explain why a model answered as it did.
///     Trailing fields describe the physical attempt that produced this logical trace event.
/// </summary>
public sealed record LlmCallRecord(
    string Role,
    string Model,
    string SystemPrompt,
    string UserPrompt,
    string RawResponse,
    double DurationMs,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    string? Error = null,
    int? CachedInputTokens = null,
    int? ReasoningTokens = null,
    int Attempt = 1,
    int? StatusCode = null,
    bool ViaFallback = false,
    DateTimeOffset? DeadlineUtc = null,
    long? AttemptElapsedMs = null,
    bool Stalled = false,
    string? ResponseId = null);

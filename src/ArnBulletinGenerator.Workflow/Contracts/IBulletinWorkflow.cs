using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Trace;

namespace ArnBulletinGenerator.Workflow.Contracts;

/// <summary>
///     The substitutable seam. One implementation is one whole approach to turning fetched bulletin
///     data into a finished read: it owns planning, selection, composition, verification and the
///     duration loop end to end.
///     <para>
///         Nothing outside this interface knows how the workflow is built; the application registers
///         the single live implementation. Implementations MUST be free of hidden state: the same
///         request and the same ports produce the same outcome.
///     </para>
/// </summary>
public interface IBulletinWorkflow
{
    /// <summary>Stable id for this workflow (<c>conversational-v1</c>).</summary>
    string Id { get; }

    /// <summary>One-line description shown in the trace viewer header.</summary>
    string Description { get; }

    Task<WorkflowOutcome> RunAsync(WorkflowRequest request, CancellationToken cancellationToken = default);
    /// <summary>
    ///     Continues the existing composer conversation with one measured-duration correction,
    ///     then runs the deterministic checks and one editor pass over the returned read. Writer
    ///     desks and candidate composition are deliberately not invoked.
    /// </summary>
    /// <param name="request">The same evidence, station policy and slot used by the original run.</param>
    /// <param name="composerThreadId">Provider response id from the composer read being corrected.</param>
    /// <param name="currentRead">The read whose measured duration missed the slot.</param>
    /// <param name="measuredSeconds">Natural spoken duration measured for <paramref name="currentRead" />.</param>
    /// <param name="cancellationToken">Cancellation requested by the workflow host.</param>
    Task<WorkflowOutcome> ReviseForMeasuredDurationAsync(
        WorkflowRequest request,
        string composerThreadId,
        string currentRead,
        double measuredSeconds,
        CancellationToken cancellationToken = default);

}

/// <summary>
///     Everything a workflow is allowed to read. Deliberately a value: a workflow never reaches back
///     into a repository, a clock or an HTTP client except through the ports handed to it.
/// </summary>
/// <param name="JobId">Correlates the trace with the job record and the deployed captures.</param>
/// <param name="Market">Market key, e.g. <c>sydney</c>.</param>
/// <param name="Station">Station display name, used in the trace header only.</param>
/// <param name="Location">Spoken location the read covers.</param>
/// <param name="TargetSeconds">Slot the finished read must fill.</param>
/// <param name="Evidence">Every citable fact, already built by the caller. The unit of citation.</param>
/// <param name="RequiredSections">Fixtures the station always carries, e.g. <c>weather</c>, <c>fuel</c>.</param>
/// <param name="HouseStyle">
///     Legacy undifferentiated style string. Superseded by <paramref name="Policy" />, which routes
///     editorial direction to the planner and voice direction to the composer instead of handing the
///     same blob to every stage. Retained only until the stages finish migrating.
/// </param>
/// <param name="PreviousScript">The station's previous read today, for continuity. Null on the first.</param>
/// <param name="Policy">
///     What ARN may configure, already split by the stage that owns it: editorial direction reaches
///     the planner, voice direction reaches the composer, and neither can reach grounding or safety.
/// </param>
public sealed record WorkflowRequest(
    string JobId,
    string Market,
    string Station,
    string Location,
    double TargetSeconds,
    IReadOnlyList<EvidenceUnit> Evidence,
    IReadOnlyList<string> RequiredSections,
    string HouseStyle = "",
    string RoadFeelPalette = "",
    string? PreviousScript = null,
    StationPolicy? Policy = null,
    IReadOnlyList<string>? SegmentOrder = null,
    TrafficRunningOrder TrafficOrder = TrafficRunningOrder.WorstFirst,
    IReadOnlyList<FutureLessonEntry>? FutureLessons = null)
{
    /// <summary>
    ///     Optional live observer for the structured generation progress feed. The workflow reports
    ///     best-effort stage and trace events through this interface and never makes observer failure
    ///     part of the workflow outcome.
    /// </summary>
    public IGenerationProgressReporter? ProgressReporter { get; init; }
    /// <summary>
    ///     Composed standing guidance lines for this bulletin. The application resolves scope,
    ///     precedence, sanitisation and the eight-directive cap before handing this plain-string
    ///     boundary to the workflow; an empty list means no operator-authored guidance is in effect.
    /// </summary>
    public IReadOnlyList<string> Guidance { get; init; } = [];

    /// <summary>Station configuration, never null at the point of use.</summary>
    public StationPolicy EffectivePolicy => Policy ?? StationPolicy.Empty;

    /// <summary>
    ///     The longest read the slot fitter can compress into <see cref="TargetSeconds" /> without
    ///     re-synthesising. Zero (the default) means "unknown", and every consumer then falls back
    ///     to <see cref="EffectiveMaxSeconds" />'s derived value.
    ///     <para>
    ///         This exists because <see cref="TargetSeconds" /> alone cannot express the constraint
    ///         that actually matters. A read one second over the slot is free — the fitter
    ///         compresses it. A read past this ceiling costs a full TTS re-render, measured at
    ///         14.4s on 8 of 20 dev jobs (2026-08-30). Optimising to the target while blind to the
    ///         ceiling is what let the composer's 19.3% length variance spend that cost.
    ///     </para>
    /// </summary>
    public double MaxSeconds { get; init; }

    /// <summary>
    ///     What a natural read of <see cref="TargetSeconds" /> holds, in words, measured by the
    ///     caller against the resolved voice's calibrated rate. Zero means "not supplied" and the
    ///     workflow falls back to asking its duration oracle.
    ///     <para>
    ///         Supplied rather than derived because the oracle cannot answer this question well. It
    ///         has no text to price before a read exists, so it prices a fixed 27-word probe and
    ///         divides — and the history predictor it delegates to holds nothing but 30-second
    ///         bulletins, so the probe was returned a full bulletin's duration. Measured across 400
    ///         real dev records, the same 30-second slot came back as 25, 26, 36, 66 or 80 words
    ///         depending only on which (model, speed) bucket matched. The caller already knows the
    ///         calibrated rate and has no probe problem.
    ///     </para>
    /// </summary>
    public int SlotWords { get; init; }

    /// <summary>
    ///     <see cref="MaxSeconds" /> when the caller supplied one, else the target plus the
    ///     conventional 15% compression headroom. Never below <see cref="TargetSeconds" />.
    /// </summary>
    public double EffectiveMaxSeconds =>
        MaxSeconds > TargetSeconds ? MaxSeconds : TargetSeconds * 1.15;
}

/// <summary>
///     What a workflow produces. The trace is not optional: a run that cannot be inspected did not
///     happen as far as this system is concerned.
/// </summary>
/// <param name="Script">The finished read, or empty when the workflow failed.</param>
/// <param name="Sentences">Per-sentence evidence mapping, for audit and targeted repair.</param>
/// <param name="Trace">Every stage, decision and LLM call, in order.</param>
/// <param name="Error">Null on success.</param>
public sealed record WorkflowOutcome(
    string Script,
    IReadOnlyList<ComposedSentence> Sentences,
    WorkflowTrace Trace,
    string? Error = null)
{
    /// <summary>
    ///     Provider response id for the composer conversation that authored the returned read, when
    ///     the provider supplied one. A null value means a future duration retry must start fresh.
    /// </summary>
    public string? ComposerThreadId { get; init; }

    public bool IsSuccess => Error is null;

    public static WorkflowOutcome Success(string script, IReadOnlyList<ComposedSentence> sentences, WorkflowTrace trace) =>
        new(script, sentences, trace);

    public static WorkflowOutcome Failure(string error, WorkflowTrace trace) =>
        new(string.Empty, [], trace, error);
}

namespace ArnBulletinGenerator.Workflow.Contracts;

/// <summary>
///     The only way a workflow reaches a language model. One port serves every role: the role name
///     selects the deployed agent, so adding a stage never widens this interface.
/// </summary>
public interface IWorkflowLlm
{
    /// <param name="role">Agent role, e.g. <c>plan</c>, <c>compose</c>, <c>ground</c>.</param>
    /// <param name="systemPrompt">Standing instructions for the role.</param>
    /// <param name="userPrompt">The rendered request.</param>
    /// <param name="jsonSchema">Response schema, or null for free JSON.</param>
    Task<LlmReply> CompleteAsync(
        string role,
        string systemPrompt,
        string userPrompt,
        string? jsonSchema,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Continues a completed response in the provider's existing conversation rather than
    ///     starting a fresh request. This is required for a targeted correction to retain the
    ///     original read and its grounded roads without resending the full prompt.
    ///     <para>
    ///         This same-conversation turn was verified against the live Foundry agent route on
    ///         2026-08-31: <c>previous_response_id</c> preserved the conversation context, and a
    ///         69-word read was corrected to 79 words with all roads intact. A fresh call cannot
    ///         provide that context reliably.
    ///     </para>
    /// </summary>
    Task<LlmReply> ContinueAsync(
        string role,
        string previousResponseId,
        string followUpPrompt,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     The transport's observation seam for one HTTP attempt to a language model. This port lives
///     in Workflow.Contracts because the transport project must not depend on Application Insights.
/// </summary>
public interface ILlmCallTelemetry
{
    void Record(in LlmCallTelemetryRecord record);
}

/// <summary>Facts emitted for one HTTP attempt to a language model.</summary>
public readonly record struct LlmCallTelemetryRecord(
    string Role,
    string Route,
    string Model,
    string? ResponseId,
    int Attempt,
    int StallAttempt,
    bool Stalled,
    bool Success,
    string? ResultCode,
    TimeSpan Duration,
    int? InputTokens,
    int? OutputTokens,
    int? CachedInputTokens,
    int? ReasoningTokens,
    string? ErrorMessage,
    bool ViaFallback = false);

/// <summary>Default no-op sink for hosts that do not register transport telemetry.</summary>
public sealed class NullLlmCallTelemetry : ILlmCallTelemetry
{
    public static readonly NullLlmCallTelemetry Instance = new();

    public NullLlmCallTelemetry() { }

    public void Record(in LlmCallTelemetryRecord record) { }
}

/// <param name="Text">Raw model output, exactly as returned.</param>
/// <param name="Model">Deployment that answered, for the trace.</param>
public sealed record LlmReply(
    string Text,
    string Model,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    string? Error = null,
    int? CachedInputTokens = null,
    int? ReasoningTokens = null,
    string? ResponseId = null,
    int Attempt = 1,
    int? StatusCode = null,
    bool ViaFallback = false,
    DateTimeOffset? DeadlineUtc = null,
    long? AttemptElapsedMs = null,
    bool Stalled = false)
{
    public bool IsSuccess => Error is null;

    public static LlmReply Failure(
        string error,
        string model = "",
        int attempt = 1,
        int? statusCode = null,
        bool viaFallback = false,
        DateTimeOffset? deadlineUtc = null,
        long? attemptElapsedMs = null,
        bool stalled = false) =>
        new(
            string.Empty,
            model,
            Error: error,
            Attempt: attempt,
            StatusCode: statusCode,
            ViaFallback: viaFallback,
            DeadlineUtc: deadlineUtc,
            AttemptElapsedMs: attemptElapsedMs,
            Stalled: stalled);
}

/// <summary>
///     How long a read takes to speak. The fitter needs this BEFORE prose exists, so an
///     implementation must answer for a word count as well as for finished text.
/// </summary>
public interface IDurationOracle
{
    /// <summary>Predicted spoken seconds for finished text.</summary>
    double PredictSeconds(string text);

    /// <summary>Predicted spoken seconds for a word count, used to fit a plan that has no prose yet.</summary>
    double SecondsForWords(int words);

    /// <summary>Words that fit in a slot. Inverse of <see cref="SecondsForWords" />.</summary>
    int WordsForSeconds(double seconds);
}

/// <summary>
///     The read's TRUE spoken length, obtained by actually rendering it.
///     <para>
///         <see cref="IDurationOracle" /> is a model of how long speech takes and is right on
///         average and wrong in particular. Measured across five deployed 30-second reads on
///         2026-08-26 the real rate ranged from 148 to 190 words per minute, a spread wider than the
///         tolerance the fit loop chases, so no single predicted rate can decide whether a specific
///         read fills a specific slot in a specific voice.
///     </para>
///     <para>
///         The division of labour: PREDICT while iterating, because prediction is free and the loop
///         runs several times; MEASURE once before acceptance, because that is the number the
///         station actually hears. A workflow with no measurer configured still works, and its trace
///         says plainly that acceptance rested on a prediction.
///     </para>
/// </summary>
public interface ISpokenDurationMeasurer
{
    /// <summary>
    ///     Renders the script and returns its spoken seconds, or null when measurement is
    ///     unavailable. Returning null is a legitimate answer, not an error: the caller falls back
    ///     to prediction and records that it did.
    /// </summary>
    Task<double?> MeasureSecondsAsync(string script, CancellationToken cancellationToken = default);
}

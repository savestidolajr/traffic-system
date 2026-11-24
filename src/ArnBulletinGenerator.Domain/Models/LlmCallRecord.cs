namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One captured LLM call — the exact rendered prompt sent and the exact raw response
///     received, for dev-only reproduction of a bulletin failure. See
///     <see cref="ArnBulletinGenerator.Domain.Interfaces.ILlmCallCapture" />.
/// </summary>
/// <param name="JobId">Bulletin job this call belongs to; a call with no job context (e.g. a
///     delivery-style live preview) is never captured — see the capture implementation.</param>
/// <param name="Stage">Pipeline stage: "Draft", "Review", "RefineSentence", "Safety", "Director", or
///     "Attribute".</param>
/// <param name="Attempt">The model-call attempt within this stage invocation (retries on
///     token-budget exhaustion or transient auth failure) — 1 unless retried.</param>
/// <param name="Model">Azure OpenAI deployment name used for this call.</param>
/// <param name="SystemInstructions">The static <c>prompts/system.md</c> content sent as the
///     Responses API <c>instructions</c> field — included per-call for a self-contained record,
///     even though it does not vary within a job.</param>
/// <param name="RenderedPrompt">The fully-substituted Handlebars template — the exact
///     stage-specific prompt text before it was wrapped into the request payload.</param>
/// <param name="RequestBody">The exact JSON body sent to the Azure OpenAI Responses API.</param>
/// <param name="ResponseBody">The exact raw JSON response received, including the <c>usage</c>
///     token-count field the app's own parsing otherwise discards.</param>
/// <param name="ElapsedMs">Wall-clock latency of this call.</param>
/// <param name="TimestampUtc">When the call completed.</param>
/// <param name="Usage">Token accounting parsed out of <paramref name="ResponseBody" />, or null
///     when the response carried none (error bodies). Kept as a first-class field rather than
///     left buried in the raw body so prompt-cache behaviour is queryable without re-parsing
///     every capture blob.</param>
public sealed record LlmCallRecord(
    string? JobId,
    string Stage,
    int Attempt,
    string Model,
    string SystemInstructions,
    string RenderedPrompt,
    string RequestBody,
    string ResponseBody,
    long ElapsedMs,
    DateTimeOffset TimestampUtc,
    LlmTokenUsage? Usage = null,
    string? Error = null,
    int? StatusCode = null,
    bool ViaFallback = false,
    DateTimeOffset? DeadlineUtc = null,
    string? ResponseId = null);

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Workflow.Contracts;
using Azure.Core;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Workflow.Hosting;

/// <summary>
///     <see cref="IWorkflowLlm" /> over the Azure OpenAI Responses API, sending the role's
///     instructions EXPLICITLY on every call.
///     <para>
///         It deliberately does not use the Foundry agent route the deployed pipeline uses. There,
///         instructions live server-side, so a captured request contains only a placeholder — the
///         2026-08-26 investigation could not explain a bad read because the exporter had stored
///         "(Foundry agent arn-draft - instructions server-side)" instead of the prompt. Sending the
///         prompt from disk means the trace holds the complete input by construction, and a prompt
///         change is a reviewable diff rather than a republish.
///     </para>
/// </summary>
public sealed class AzureOpenAiWorkflowLlm(
    HttpClient http,
    TokenCredential credential,
    AzureOpenAiWorkflowLlmOptions options,
    ILogger<AzureOpenAiWorkflowLlm>? logger = null,
    TimeProvider? timeProvider = null,
    ILlmCallTelemetry? llmCallTelemetry = null) : IWorkflowLlm
{
    private static readonly string[] Scope = ["https://cognitiveservices.azure.com/.default"];
    private static readonly string[] AgentScope = ["https://ai.azure.com/.default"];
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ILlmCallTelemetry telemetry =
        llmCallTelemetry ?? NullLlmCallTelemetry.Instance;

    public async Task<LlmReply> CompleteAsync(
        string role,
        string systemPrompt,
        string userPrompt,
        string? jsonSchema,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var budget = new AzureOpenAiCallBudget(
            options.EffectiveCallBudget,
            clock,
            cancellationToken);
        return await SendAsync(
            role,
            systemPrompt,
            userPrompt,
            jsonSchema,
            budget,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<LlmReply> ContinueAsync(
        string role,
        string previousResponseId,
        string followUpPrompt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var budget = new AzureOpenAiCallBudget(
            options.EffectiveCallBudget,
            clock,
            cancellationToken);
        return await SendAsync(
            role,
            string.Empty,
            followUpPrompt,
            jsonSchema: null,
            budget,
            cancellationToken,
            previousResponseId).ConfigureAwait(false);
    }

    private async Task<LlmReply> SendAsync(
        string role,
        string systemPrompt,
        string userPrompt,
        string? jsonSchema,
        AzureOpenAiCallBudget budget,
        CancellationToken cancellationToken,
        string? previousResponseId = null)
    {
        bool viaFallback = false;
        string? previousError = null;
        int stallRetries = 0;
        int throttleRetries = 0;

        while (true)
        {
            string? agentName = viaFallback
                ? options.FallbackAgentFor(role)
                : options.AgentFor(role);
            bool viaAgent = !string.IsNullOrWhiteSpace(agentName);
            string activeModel = viaFallback
                ? options.FallbackModel ?? options.Model
                : options.Model;
            string served = viaAgent ? $"agent:{agentName}" : activeModel;
            AzureOpenAiCallBudget.AzureOpenAiCallAttempt? attempt = null;
            bool started = viaFallback
                ? budget.TryStartAlternateSend(out attempt)
                : budget.TryStartPrimarySend(out attempt);
            if (!started || attempt is null)
            {
                string exhausted = budget.IsExpired
                    ? $"no response within {budget.Options.Overall.TotalSeconds:0}s absolute call budget"
                    : $"retry budget exhausted after {Math.Max(1, budget.TotalSends)} attempt(s)";
                logger?.LogWarning(
                    "[Workflow:{Role}] {Detail} LastError={LastError}",
                    role,
                    exhausted,
                    previousError);
                return LlmReply.Failure(
                    exhausted,
                    served,
                    attempt: Math.Max(1, budget.TotalSends),
                    deadlineUtc: budget.DeadlineUtc);
            }

            string deployment = served;
            IDisposable? permit = null;
            var callSw = Stopwatch.StartNew();
            try
            {
                var deploymentWait = Stopwatch.StartNew();
                permit = await budget.AcquireDeploymentAsync(
                    deployment,
                    cancellationToken).ConfigureAwait(false);
                long waitMs = deploymentWait.ElapsedMilliseconds;
                if (waitMs > 500)
                    logger?.LogInformation(
                        "[AoaiBulkhead] Waited {WaitMs}ms for one of {Permits} permits on deployment {Deployment}",
                        waitMs,
                        budget.Options.BulkheadPermits,
                        deployment);
                if (permit is null)
                {
                    string detail = $"no response within {budget.Options.Overall.TotalSeconds:0}s " +
                        "absolute call budget";
                    return LlmReply.Failure(
                        detail,
                        served,
                        attempt: attempt.LogicalAttempt + 1,
                        deadlineUtc: budget.DeadlineUtc);
                }

                var scope = viaAgent ? AgentScope : Scope;
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext(scope),
                    budget.CancellationToken).ConfigureAwait(false);

                var body = new Dictionary<string, object?>
                {
                    ["input"] = userPrompt,
                    ["max_output_tokens"] = options.MaxOutputTokens
                };
                if (previousResponseId is not null)
                    body["previous_response_id"] = previousResponseId;

                if (viaAgent && previousResponseId is null)
                {
                    // A provisioned agent bakes `json_object`, not this role's schema, so the
                    // schema only reaches the model if it is in the input. Without it the editor
                    // answered {"verdict":"APPROVED","feedback":""} to a parser expecting
                    // {"approved":bool,"feedback":[]} on every call (dev 2e158de3, 2026-09-02): two
                    // paid calls per read, both discarded, and the read shipped unreviewed.
                    string combined = $"{systemPrompt}\n\n---\n\n{userPrompt}";
                    if (jsonSchema is not null)
                        combined += "\n\nReply with a single JSON object that validates against this JSON Schema, and nothing else:\n"
                            + jsonSchema;
                    body["input"] = combined;
                }

                if (!viaAgent)
                {
                    body["model"] = activeModel;
                    if (previousResponseId is null)
                    {
                        body["instructions"] = systemPrompt;
                        string? effort = options.EffortFor(role);
                        if (!string.IsNullOrWhiteSpace(effort))
                            body["reasoning"] = new Dictionary<string, object?>
                            {
                                ["effort"] = effort
                            };

                        if (!string.IsNullOrWhiteSpace(jsonSchema))
                        {
                            using var schemaDoc = JsonDocument.Parse(jsonSchema);
                            body["text"] = new Dictionary<string, object?>
                            {
                                ["format"] = new Dictionary<string, object?>
                                {
                                    ["type"] = "json_schema",
                                    ["name"] = $"arn_{role}",
                                    ["strict"] = true,
                                    ["schema"] = schemaDoc.RootElement.Clone()
                                }
                            };
                        }
                    }
                }

                string url = viaAgent
                    ? $"{options.ProjectEndpoint!.TrimEnd('/')}/agents/{agentName}/endpoint/protocols/openai/responses?api-version=v1"
                    : $"{options.Endpoint.TrimEnd('/')}/openai/v1/responses?api-version={options.ApiVersion}";
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json");
                budget.Attach(request, deployment, viaFallback, attempt);

                logger?.LogInformation(
                    "[Workflow:{Role}] Sending logical attempt {Attempt} via {Route} deployment {Deployment}",
                    role,
                    attempt.LogicalAttempt,
                    viaFallback ? "fallback" : "primary",
                    deployment);

                using var attemptCts = budget.CreateAttemptCancellationSource(cancellationToken);
                using var response = await http.SendAsync(
                    request,
                    attemptCts.Token).ConfigureAwait(false);
                string payload = await response.Content.ReadAsStringAsync(
                    attemptCts.Token).ConfigureAwait(false);
                callSw.Stop();
                bool retryable = IsRetryable(response.StatusCode);
                if (!response.IsSuccessStatusCode)
                {
                    previousError = $"HTTP {(int)response.StatusCode}: {Truncate(payload, 400)}";
                    telemetry.Record(new LlmCallTelemetryRecord(
                        role,
                        viaAgent ? "agent" : "raw",
                        served,
                        ResponseId: null,
                        Attempt: attempt.LogicalAttempt + 1,
                        StallAttempt: stallRetries,
                        Stalled: false,
                        Success: false,
                        ResultCode: ((int)response.StatusCode).ToString(),
                        Duration: callSw.Elapsed,
                        InputTokens: null,
                        OutputTokens: null,
                        CachedInputTokens: null,
                        ReasoningTokens: null,
                        ErrorMessage: previousError,
                        ViaFallback: viaFallback));
                    logger?.LogWarning(
                        "[Workflow:{Role}] HTTP {Status} at logical attempt {Attempt}, fallback={Fallback}",
                        role,
                        (int)response.StatusCode,
                        attempt.LogicalAttempt,
                        viaFallback);

                    bool fallbackAvailable = !viaFallback
                        && (!string.IsNullOrWhiteSpace(options.FallbackModel)
                            || options.FallbackAgentFor(role) is not null);
                    if (retryable && fallbackAvailable)
                    {
                        viaFallback = true;
                        continue;
                    }

                    if (retryable && !viaFallback
                        && throttleRetries++ < options.MaxThrottleRetries
                        && await DelayRetryAsync(
                            budget,
                            response,
                            throttleRetries - 1,
                            cancellationToken).ConfigureAwait(false))
                        continue;

                    return LlmReply.Failure(
                        previousError ?? "Azure OpenAI request failed",
                        served,
                        attempt: attempt.LogicalAttempt + 1,
                        statusCode: (int)response.StatusCode,
                        viaFallback: viaFallback,
                        deadlineUtc: budget.DeadlineUtc,
                        attemptElapsedMs: callSw.ElapsedMilliseconds);
                }

                LlmReply parsed = ParseResponse(payload, served);
                telemetry.Record(new LlmCallTelemetryRecord(
                    role,
                    viaAgent ? "agent" : "raw",
                    parsed.Model,
                    parsed.ResponseId,
                    attempt.LogicalAttempt + 1,
                    stallRetries,
                    false,
                    parsed.IsSuccess,
                    "200",
                    callSw.Elapsed,
                    parsed.PromptTokens,
                    parsed.CompletionTokens,
                    parsed.CachedInputTokens,
                    parsed.ReasoningTokens,
                    parsed.Error,
                    viaFallback));
                return parsed with
                {
                    Attempt = attempt.LogicalAttempt + 1,
                    StatusCode = (int)response.StatusCode,
                    ViaFallback = viaFallback,
                    DeadlineUtc = budget.DeadlineUtc,
                    AttemptElapsedMs = callSw.ElapsedMilliseconds,
                    Stalled = false
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                int attempts = Math.Max(1, budget.TotalSends);
                previousError = $"no response within {budget.Options.Attempt.TotalSeconds:0}s " +
                    $"after {attempts} attempt(s)";
                bool canRetryStall = !viaFallback
                    && stallRetries++ < options.MaxStallRetries
                    && !budget.IsExpired;
                if (canRetryStall)
                    continue;

                if (!viaFallback
                    && (!string.IsNullOrWhiteSpace(options.FallbackModel)
                        || options.FallbackAgentFor(role) is not null)
                    && !budget.IsExpired)
                {
                    viaFallback = true;
                    continue;
                }

                if (budget.IsExpired)
                    previousError += " and the absolute call deadline was reached";
                logger?.LogWarning(
                    "[Workflow:{Role}] {Detail} at logical attempt {Attempt}",
                    role,
                    previousError,
                    attempt.LogicalAttempt);
                return LlmReply.Failure(
                    previousError,
                    served,
                    attempt: attempts,
                    viaFallback: viaFallback,
                    deadlineUtc: budget.DeadlineUtc,
                    attemptElapsedMs: callSw.ElapsedMilliseconds,
                    stalled: true);
            }
            catch (Exception ex)
            {
                previousError = ex.Message;
                logger?.LogWarning(
                    ex,
                    "[Workflow:{Role}] transport failure at logical attempt {Attempt}",
                    role,
                    attempt.LogicalAttempt);
                return LlmReply.Failure(
                    previousError,
                    served,
                    attempt: attempt.LogicalAttempt + 1,
                    viaFallback: viaFallback,
                    deadlineUtc: budget.DeadlineUtc,
                    attemptElapsedMs: callSw.ElapsedMilliseconds);
            }
            finally
            {
                permit?.Dispose();
            }
        }
    }

    private async Task<bool> DelayRetryAsync(
        AzureOpenAiCallBudget budget,
        HttpResponseMessage response,
        int attempt,
        CancellationToken cancellationToken)
    {
        TimeSpan requested = RetryAfter(response)
            ?? TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2);
        return await budget.DelayBeforeRetryAsync(
            requested,
            cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRetryable(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    /// <summary>
    ///     Pulls the assistant text out of a Responses envelope. Tolerates both the flattened
    ///     <c>output_text</c> convenience field and the nested <c>output[].content[].text</c> form.
    /// </summary>
    internal static LlmReply ParseResponse(string payload, string model)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            string served = root.TryGetProperty("model", out var servedEl)
                && servedEl.ValueKind == JsonValueKind.String
                ? servedEl.GetString() ?? model
                : model;
            string? responseId = root.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
            int? promptTokens = null;
            int? completionTokens = null;
            int? cachedInputTokens = null;
            int? reasoningTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var input)
                    && input.TryGetInt32(out int inputValue))
                    promptTokens = inputValue;
                if (usage.TryGetProperty("output_tokens", out var output)
                    && output.TryGetInt32(out int outputValue))
                    completionTokens = outputValue;
                if (usage.TryGetProperty("input_tokens_details", out var inputDetails)
                    && inputDetails.TryGetProperty("cached_tokens", out var cached)
                    && cached.TryGetInt32(out int cachedValue))
                    cachedInputTokens = cachedValue;
                if (usage.TryGetProperty("output_tokens_details", out var outputDetails)
                    && outputDetails.TryGetProperty("reasoning_tokens", out var reasoning)
                    && reasoning.TryGetInt32(out int reasoningValue))
                    reasoningTokens = reasoningValue;
            }

            if (root.TryGetProperty("output_text", out var flat)
                && flat.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(flat.GetString()))
                return new LlmReply(
                    flat.GetString()!,
                    served,
                    promptTokens,
                    completionTokens,
                    CachedInputTokens: cachedInputTokens,
                    ReasoningTokens: reasoningTokens,
                    ResponseId: responseId);

            if (root.TryGetProperty("output", out var outputArray)
                && outputArray.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in outputArray.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content)
                        || content.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (var chunk in content.EnumerateArray())
                    {
                        if (chunk.TryGetProperty("text", out var text)
                            && text.ValueKind == JsonValueKind.String)
                            parts.Add(text.GetString() ?? string.Empty);
                    }
                }

                string joined = string.Join("\n", parts.Where(part => part.Length > 0));
                if (joined.Length > 0)
                    return new LlmReply(
                        joined,
                        served,
                        promptTokens,
                        completionTokens,
                        CachedInputTokens: cachedInputTokens,
                        ReasoningTokens: reasoningTokens,
                        ResponseId: responseId);
            }

            if (root.TryGetProperty("error", out var error))
                return LlmReply.Failure(error.ToString(), model);
            return LlmReply.Failure(
                $"No assistant text in response: {Truncate(payload, 300)}",
                model);
        }
        catch (JsonException ex)
        {
            return LlmReply.Failure($"Unparseable response: {ex.Message}", model);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
            return null;
        if (header.Delta is { } delta)
            return delta;
        if (header.Date is { } date)
        {
            TimeSpan wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "...";
}

/// <param name="Endpoint">Azure OpenAI resource endpoint, e.g. <c>https://name.cognitiveservices.azure.com</c>.</param>
/// <param name="Model">Deployment name.</param>
public sealed record AzureOpenAiWorkflowLlmOptions
{
    public required string Endpoint { get; init; }
    public required string Model { get; init; }

    /// <summary>
    ///     Alternate deployment for calls that take the raw Responses route. The fallback is
    ///     charged to the same operation budget and can be selected at most once.
    /// </summary>
    public string? FallbackModel { get; init; }

    public string ApiVersion { get; init; } = "preview";
    public int MaxOutputTokens { get; init; } = 4096;

    /// <summary>
    ///     Foundry project endpoint, required only when <see cref="AgentByRole" /> routes a role to
    ///     an agent. Shape: <c>https://{resource}.services.ai.azure.com/api/projects/{project}</c>.
    /// </summary>
    public string? ProjectEndpoint { get; init; }

    /// <summary>Roles served by a published Foundry agent instead of the raw model.</summary>
    public IReadOnlyDictionary<string, string> AgentByRole { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Alternate published Foundry agents, selected at most once after a primary transient
    ///     failure. The agent route does not carry a request-side model.
    /// </summary>
    public IReadOnlyDictionary<string, string> FallbackAgentByRole { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string? AgentFor(string role) =>
        AgentByRole.TryGetValue(role, out string? agent) && !string.IsNullOrWhiteSpace(agent)
            ? agent
            : null;

    public string? FallbackAgentFor(string role) =>
        FallbackAgentByRole.TryGetValue(role, out string? agent)
            && !string.IsNullOrWhiteSpace(agent)
            ? agent
            : null;

    /// <summary>Reasoning effort for every raw-model role.</summary>
    public string? ReasoningEffort { get; init; } = "none";

    public string? EffortFor(string role) => ReasoningEffort;

    /// <summary>
    ///     Per-attempt timeout retained as an options-level compatibility knob. The shared budget
    ///     is authoritative; this value maps into it unless CallBudget.Attempt is explicitly
    ///     changed.
    /// </summary>
    public int AttemptTimeoutSeconds { get; init; } = AzureOpenAiCallBudget.DefaultAttemptSeconds;

    /// <summary>Maximum transient status retries on the primary route. The budget still caps sends.</summary>
    public int MaxThrottleRetries { get; init; } = 4;

    /// <summary>
    ///     Maximum primary stall retries. These are one loop owned by this adapter, not nested
    ///     transport retries, and are bounded by the shared primary-send budget.
    /// </summary>
    public int MaxStallRetries { get; init; } = 2;

    /// <summary>Shared deadline/retry/bulkhead limits for one logical operation.</summary>
    public AzureOpenAiCallBudgetOptions CallBudget { get; init; } = new();

    internal AzureOpenAiCallBudgetOptions EffectiveCallBudget =>
        CallBudget.Attempt == TimeSpan.FromSeconds(AzureOpenAiCallBudget.DefaultAttemptSeconds)
            && AttemptTimeoutSeconds != AzureOpenAiCallBudget.DefaultAttemptSeconds
            ? CallBudget with { Attempt = TimeSpan.FromSeconds(AttemptTimeoutSeconds) }
            : CallBudget;
}

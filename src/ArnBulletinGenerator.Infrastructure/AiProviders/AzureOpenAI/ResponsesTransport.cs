using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text.Json;
using Azure.Core;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Workflow.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     The Azure OpenAI Responses API transport shared by every role
///     (Draft/Review/Refine/Attribute/Safety/Director/Polish/CitationFix): auth resolution (Managed
///     Identity with API-key fallback), Foundry-agent vs. raw-model routing, the retrying
///     one-shot <see cref="ResponsesClient" /> call, and the raw-capture logging every call needs
///     for job diagnostics. Split out of <see cref="LlmScriptGenerator" /> — this is "how do we
///     send it", independent of which prompt/role is being sent (<see cref="PromptLibrary" />).
///     <para>
///         Issued as one-shot Azure OpenAI Responses API calls via the official
///         <see cref="ResponsesClient" /> SDK (the same client Microsoft Agent Framework's own
///         <c>Microsoft.Agents.AI.OpenAI</c> connectors wrap). Used directly rather than through
///         MAF's <c>AIAgent</c>/session abstraction: every call here is stateless (system
///         instructions + one input, no conversation history, no tool calls), so wrapping in an
///         agent/session would add a layer without a matching need, and would fight the exact-
///         status-code auth-fallback and raw-capture logic below. Revisit this once the
///         Draft-Review-Refine loop is restructured into an actual multi-step MAF workflow.
///     </para>
/// </summary>
internal sealed class ResponsesTransport(
    IHttpClientFactory httpClientFactory,
    IOptions<AzureAIOptions> azureOptions,
    TimeProvider timeProvider,
    ILogger<LlmScriptGenerator> logger,
    ILlmCallCapture llmCallCapture,
    TokenCredential? tokenCredential = null)
{
    private const int MaxModelAttemptsPerOperation = AzureOpenAiCallBudget.DefaultMaxPrimarySends;
    private const int MaxOutputTokenBudget = 16384;

    private static readonly string[] AzureOpenAIScopes = ["https://cognitiveservices.azure.com/.default"];

    // A Foundry project's agent endpoint is audience ai.azure.com, not the Azure OpenAI resource.
    // The same scope the workflow hosting (AzureOpenAiWorkflowLlm) uses for every arn-wf-* call.
    private static readonly string[] FoundryAgentScopes = ["https://ai.azure.com/.default"];

    private static readonly string[] PlaceholderApiKeys =
        ["", "<api-key>", "<azure-ai-api-key>", "REPLACE_ME", "your-api-key-here"];

    private readonly AzureAIOptions _azureOptions = azureOptions.Value;

    /// <summary>Base endpoint for the <see cref="ResponsesClient" />, resolved once at construction.</summary>
    internal readonly Result<Uri> Endpoint = BuildResponsesClientEndpoint(azureOptions.Value.Endpoint);

    internal async Task<Result<(string Name, string Value)>> GetAuthHeaderAsync(CancellationToken cancellationToken)
    {
        if (tokenCredential is not null)
        {
            try
            {
                var tokenRequest = new TokenRequestContext(AzureOpenAIScopes);
                var token = await tokenCredential.GetTokenAsync(tokenRequest, cancellationToken);
                return Result<(string, string)>.Success(("Authorization", $"Bearer {token.Token}"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Managed Identity token acquisition failed, falling back to API key");
            }
        }

        // Fallback to API key (local dev, or MI unavailable)
        var keyHeader = GetApiKeyHeader();
        return keyHeader is not null
            ? Result<(string, string)>.Success(keyHeader.Value)
            : Result<(string, string)>.Failure(
                "Azure AI authentication failed: Managed Identity unavailable and no API key configured.");
    }

    /// <summary>
    ///     Returns the API-key auth header when a real (non-placeholder) key is configured, otherwise null.
    /// </summary>
    private (string Name, string Value)? GetApiKeyHeader()
    {
        string key = _azureOptions.ApiKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key) || PlaceholderApiKeys.Contains(key.Trim()))
            return null;
        return ("api-key", key);
    }

    /// <summary>
    ///     Calls a Foundry prompt agent via its responses-protocol endpoint. The agent carries the
    ///     system prompt, model, and reasoning effort baked into its server-side definition — only
    ///     the rendered user template (the data/framing) is sent as input. Brought to parity with
    ///     <see cref="SendToModelAsync" />: same shared HttpClient (rate-limit retry + circuit
    ///     breaker), an equivalent token-exhaustion retry loop, API-key auth fallback, an optional
    ///     JSON/JSON-schema response format on the request, and — crucially — the SAME
    ///     <see cref="ILlmCallCapture" /> so agent calls are visible to job-diagnostics exactly like
    ///     raw-model calls (without this, routing a role to an agent silently drops it from the
    ///     captures the diagnostics tooling reads). Returns the agent's text output.
    /// </summary>
    internal async Task<Result<string>> SendToAgentAsync(
        string renderedPrompt,
        string agentName,
        string operationName,
        int maxOutputTokens,
        bool preferJson,
        CancellationToken cancellationToken,
        (string Name, string Value) authHeader,
        string? jobId = null,
        BinaryData? jsonSchema = null)
    {
        using var budget = new AzureOpenAiCallBudget(
            _azureOptions.CallBudget,
            timeProvider,
            cancellationToken);
        bool triedApiKeyFallback = false;
        int outputTokenBudget = maxOutputTokens;

        // The caller's header is minted for the Azure OpenAI resource. Foundry rejected it and the
        // API-key fallback then went out under the wrong header name, so every safety review on dev
        // ended "AI safety review unavailable (HTTP 401: Bearer token not provided)" (2026-09-02).
        if (tokenCredential is not null)
        {
            try
            {
                var token = await tokenCredential.GetTokenAsync(
                    new TokenRequestContext(FoundryAgentScopes), cancellationToken).ConfigureAwait(false);
                authHeader = ("Authorization", $"Bearer {token.Token}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[{Operation}] Foundry agent token acquisition failed; using the caller's header.", operationName);
            }
        }

        try
        {
            string projectEndpoint = _azureOptions.FoundryProjectEndpoint?.TrimEnd('/')
                ?? throw new InvalidOperationException(
                    "FoundryProjectEndpoint is required when an agent name is set");
            string agentUrl =
                $"{projectEndpoint}/agents/{agentName}/endpoint/protocols/openai/responses?api-version=v1";

            while (true)
            {
                if (!budget.TryStartPrimarySend(out var attempt))
                    return Result<string>.Failure(
                        $"{operationName} agent call exceeded its absolute retry budget.");

                string input = renderedPrompt;
                if (preferJson && !input.Contains("json", StringComparison.OrdinalIgnoreCase))
                    input += "\n\nReturn valid JSON.";
                var body = new Dictionary<string, object?>
                {
                    ["input"] = input,
                    ["max_output_tokens"] = outputTokenBudget
                };
                string requestBody = JsonSerializer.Serialize(body);

                logger.LogInformation(
                    "[{Operation}] Calling Foundry agent {AgentName} — LogicalAttempt={Attempt}, OutputLimit={OutputLimit}, JobId={JobId}",
                    operationName,
                    agentName,
                    attempt.LogicalAttempt,
                    outputTokenBudget,
                    jobId);

                var httpClient = httpClientFactory.CreateClient("AzureOpenAI");
                using var request = new HttpRequestMessage(HttpMethod.Post, agentUrl);
                request.Headers.TryAddWithoutValidation(authHeader.Name, authHeader.Value);
                request.Content = new StringContent(
                    requestBody,
                    System.Text.Encoding.UTF8,
                    "application/json");
                budget.Attach(request, $"agent:{agentName}", viaAlternate: false, attempt);

                using var routeScope = budget.Activate(viaAlternate: false, attempt);
                using var attemptCts = budget.CreateAttemptCancellationSource(cancellationToken);
                var callSw = Stopwatch.StartNew();
                using var response = await httpClient.SendAsync(
                    request,
                    attemptCts.Token).ConfigureAwait(false);
                string responseContent = await response.Content.ReadAsStringAsync(
                    attemptCts.Token).ConfigureAwait(false);
                callSw.Stop();
                var usage = TokenUsageParser.TryParse(responseContent);
                LogTokenUsage(operationName, $"agent:{agentName}", usage, jobId);

                int statusCode = (int)response.StatusCode;
                string? attemptError = response.IsSuccessStatusCode
                    ? null
                    : $"HTTP {statusCode}: {ModelResponseParser.Truncate(responseContent, 500)}";
                QueueCapture(new LlmCallRecord(
                    jobId,
                    operationName,
                    attempt.LogicalAttempt + 1,
                    $"agent:{agentName}",
                    $"(Foundry agent {agentName} — instructions/model/reasoning server-side)",
                    renderedPrompt,
                    requestBody,
                    responseContent,
                    callSw.ElapsedMilliseconds,
                    timeProvider.GetUtcNow(),
                    usage,
                    attemptError,
                    statusCode,
                    ViaFallback: false,
                    DeadlineUtc: budget.DeadlineUtc,
                    ResponseId: TryReadResponseId(responseContent)));

                if (!response.IsSuccessStatusCode)
                {
                    int status = (int)response.StatusCode;
                    bool authRejected = status is 401 or 403
                        || (status == 400
                            && responseContent.Contains(
                                "does not match resource token",
                                StringComparison.OrdinalIgnoreCase));
                    if (authRejected && !triedApiKeyFallback)
                    {
                        var keyHeader = GetApiKeyHeader();
                        if (keyHeader is not null)
                        {
                            logger.LogWarning(
                                "[{Operation}] Foundry agent auth rejected ({Status}); retrying with configured API key.",
                                operationName,
                                status);
                            authHeader = keyHeader.Value;
                            triedApiKeyFallback = true;
                            continue;
                        }
                    }

                    return Result<string>.Failure(
                        $"{operationName} agent call failed (HTTP {status}): " +
                        $"{ModelResponseParser.Truncate(responseContent, 500)}");
                }

                string result;
                bool tokenExhausted;
                using (var doc = JsonDocument.Parse(responseContent))
                {
                    var outputText = new System.Text.StringBuilder();
                    if (doc.RootElement.TryGetProperty("output", out var outputEl)
                        && outputEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in outputEl.EnumerateArray())
                        {
                            if (!item.TryGetProperty("type", out var typeEl)
                                || typeEl.GetString() != "message"
                                || !item.TryGetProperty("content", out var contentEl)
                                || contentEl.ValueKind != JsonValueKind.Array)
                                continue;
                            foreach (var content in contentEl.EnumerateArray())
                                if (content.TryGetProperty("text", out var textEl)
                                    && textEl.ValueKind == JsonValueKind.String)
                                    outputText.Append(textEl.GetString());
                        }
                    }

                    if (doc.RootElement.TryGetProperty("output_text", out var flat)
                        && flat.ValueKind == JsonValueKind.String)
                        outputText.Append(flat.GetString());
                    result = outputText.ToString().Trim();
                    tokenExhausted = result.Length == 0
                        && ((doc.RootElement.TryGetProperty("status", out var statusEl)
                                && statusEl.GetString() is string statusText
                                && statusText != "completed")
                            || responseContent.Contains(
                                "max_output_tokens",
                                StringComparison.OrdinalIgnoreCase));
                }

                if (result.Length > 0)
                    return Result<string>.Success(result);

                if (tokenExhausted && budget.CanStartPrimarySend)
                {
                    int nextBudget = Math.Min(outputTokenBudget * 2, MaxOutputTokenBudget);
                    logger.LogWarning(
                        "[{Operation}] Foundry agent returned no text; increasing output budget {Old} -> {New}",
                        operationName,
                        outputTokenBudget,
                        nextBudget);
                    outputTokenBudget = nextBudget;
                    continue;
                }

                return Result<string>.Failure(
                    $"{operationName} agent returned no visible text. Raw body: " +
                    $"{ModelResponseParser.Truncate(responseContent, 500)}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Failure($"{operationName} agent call was cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[{Operation}] Error calling Foundry agent {AgentName}",
                operationName,
                agentName);
            return Result<string>.Failure(ex.Message);
        }
    }

    /// <summary>
    ///     Routes one LLM operation to its Foundry agent when one is configured for the role
    ///     (<see cref="AzureAIOptions.AgentNameFor" />), else to the raw model. This is the single
    ///     seam that makes "every LLM call goes through an agent" a per-role config choice: set the
    ///     role's agent name and the call moves to the agent endpoint (gaining a full Foundry trace)
    ///     with no code change. The two paths match behaviourally — same prompt, JSON/schema format,
    ///     and <see cref="ILlmCallCapture" /> — the agent additionally carries its model + reasoning
    ///     effort server-side (so e.g. the Review agent MUST be provisioned with effort "low").
    ///     <paramref name="modelRole" /> lets a call resolve its raw-model deployment under a
    ///     different role than its agent lookup (CitationFix shares the Draft deployment).
    /// </summary>
    internal Task<Result<string>> DispatchLlmAsync(
        string role,
        string renderedPrompt,
        int maxOutputTokens,
        bool preferJson,
        CancellationToken cancellationToken,
        (string Name, string Value) authHeader,
        bool hasProducerFeedback,
        string? jobId,
        BinaryData? jsonSchema,
        string systemInstructionsOverride,
        string? modelRole = null)
    {
        string? agentName = _azureOptions.AgentNameFor(role);
        if (agentName is not null)
            return SendToAgentAsync(renderedPrompt, agentName, role, maxOutputTokens, preferJson,
                cancellationToken, authHeader, jobId, jsonSchema);
        return SendToModelAsync(renderedPrompt, role, maxOutputTokens, preferJson,
            _azureOptions.ModelFor(modelRole ?? role), cancellationToken, authHeader,
            hasProducerFeedback, jobId, jsonSchema, systemInstructionsOverride);
    }

    /// <summary>
    ///     Sends a schema-bearing request to the raw model endpoint. Foundry agent endpoints reject
    ///     request-side response formats, while each structured Draft schema contains this bulletin's
    ///     per-request evidence-id enum and therefore cannot be baked into an agent definition.
    /// </summary>
    internal Task<Result<string>> DispatchStructuredLlmAsync(
        string role, string renderedPrompt, int maxOutputTokens, CancellationToken cancellationToken,
        (string Name, string Value) authHeader, bool hasProducerFeedback, string? jobId,
        BinaryData? jsonSchema = null, string? systemInstructionsOverride = null, string? modelRole = null) =>
        SendToModelAsync(renderedPrompt, role, maxOutputTokens, false,
            _azureOptions.ModelFor(modelRole ?? role), cancellationToken, authHeader,
            hasProducerFeedback, jobId, jsonSchema, systemInstructionsOverride);

    /// <summary>
    ///     One Responses API call, with Arn's own retry/auth-fallback/capture semantics layered
    ///     around the official <see cref="ResponsesClient" />:
    ///     <list type="bullet">
    ///         <item>Transport is the SAME named "AzureOpenAI" <see cref="HttpClient" /> the DI
    ///             container already builds with <c>RateLimitRetryHandler</c> + the Polly circuit
    ///             breaker — that hardening lives at the HttpClient layer, unrelated to which SDK
    ///             sits above it, so it carries over untouched.</item>
    ///         <item>The SDK's OWN retry policy is explicitly disabled
    ///             (<c>new ClientRetryPolicy(0)</c>) — leaving it on would silently layer a second,
    ///             undocumented-here retry loop on top of RateLimitRetryHandler AND this method's
    ///             own attempt loop, compounding tail latency under exactly the 429-storm conditions
    ///             RateLimitRetryHandler exists to control (verified live: an unbounded SDK retry
    ///             against a fake unreachable host retried 4x on its own before this was set).</item>
    ///         <item>The protocol-level call (<c>BinaryContent</c> in, raw <see cref="ClientResult" />
    ///             out, <see cref="ClientErrorBehaviors.NoThrow" />) is used instead of the
    ///             convenience overload so a non-success status is a normal branch — matching the
    ///             original raw-HttpClient code's control flow — not an exception, which matters
    ///             when 429s are the expected/frequent case this handler was built for.</item>
    ///     </list>
    /// </summary>
    internal async Task<Result<string>> SendToModelAsync(
        string renderedPrompt,
        string operationName,
        int maxOutputTokens,
        bool preferJson,
        string model,
        CancellationToken cancellationToken,
        (string Name, string Value) authHeader,
        bool hasProducerFeedback = false,
        string? jobId = null,
        BinaryData? jsonSchema = null,
        string? systemInstructionsOverride = null)
    {
        using var budget = new AzureOpenAiCallBudget(
            _azureOptions.CallBudget,
            timeProvider,
            cancellationToken);
        int outputTokenBudget = maxOutputTokens;
        bool triedApiKeyFallback = false;

        try
        {
            if (Endpoint.IsFailure)
                return Result<string>.Failure(Endpoint.Error);

            while (true)
            {
                if (!budget.TryStartPrimarySend(out var attempt))
                    return Result<string>.Failure(
                        $"{operationName} exceeded its absolute Azure OpenAI call budget.");

                using var routeScope = budget.Activate(
                    viaAlternate: false,
                    pendingAttempt: attempt);
                var createOptions = BuildCreateResponseOptions(
                    renderedPrompt,
                    outputTokenBudget,
                    preferJson,
                    model,
                    operationName,
                    hasProducerFeedback,
                    jsonSchema,
                    systemInstructionsOverride);
                BinaryContent requestContent = createOptions;
                string requestBody = ReadBinaryContentAsString(requestContent);

                var httpClient = httpClientFactory.CreateClient("AzureOpenAI");
                var client = new ResponsesClient(
                    new AuthHeaderPolicy(authHeader.Name, authHeader.Value),
                    new ResponsesClientOptions
                    {
                        Endpoint = Endpoint.Value,
                        Transport = new HttpClientPipelineTransport(httpClient),
                        // The shared RateLimitRetryHandler owns HTTP retry/backoff. The SDK retry
                        // policy must remain disabled or it silently multiplies attempts.
                        RetryPolicy = new ClientRetryPolicy(0),
                    });

                using var attemptCts = budget.CreateAttemptCancellationSource(cancellationToken);
                var callSw = Stopwatch.StartNew();
                var requestOptions = new RequestOptions
                {
                    ErrorOptions = ClientErrorBehaviors.NoThrow,
                    CancellationToken = attemptCts.Token,
                };
                ClientResult protocolResult = await client.CreateResponseAsync(
                    requestContent,
                    requestOptions).ConfigureAwait(false);
                var rawResponse = protocolResult.GetRawResponse();
                string responseBody = rawResponse.Content.ToString();
                callSw.Stop();

                logger.LogInformation(
                    "[{Operation}] Model call finished in {ElapsedMs}ms — LogicalAttempt={Attempt}, Status={StatusCode}, PromptChars={PromptChars}, OutputLimit={OutputLimit}",
                    operationName,
                    callSw.ElapsedMilliseconds,
                    attempt.LogicalAttempt,
                    rawResponse.Status,
                    renderedPrompt.Length,
                    outputTokenBudget);

                var usage = TokenUsageParser.TryParse(responseBody);
                LogTokenUsage(operationName, model, usage, jobId);
                QueueCapture(new LlmCallRecord(
                    jobId,
                    operationName,
                    attempt.LogicalAttempt + 1,
                    model,
                    systemInstructionsOverride ?? PromptLibrary.SystemInstructions,
                    renderedPrompt,
                    requestBody,
                    responseBody,
                    callSw.ElapsedMilliseconds,
                    timeProvider.GetUtcNow(),
                    usage,
                    rawResponse.IsError
                        ? $"HTTP {rawResponse.Status}: {ModelResponseParser.Truncate(responseBody, 500)}"
                        : null,
                    rawResponse.Status,
                    ViaFallback: false,
                    DeadlineUtc: budget.DeadlineUtc,
                    ResponseId: TryReadResponseId(responseBody)));

                if (rawResponse.IsError)
                {
                    bool authRejected = rawResponse.Status is 401 or 403
                        || (rawResponse.Status == 400
                            && responseBody.Contains(
                                "does not match resource token",
                                StringComparison.OrdinalIgnoreCase));
                    if (authRejected
                        && !triedApiKeyFallback
                        && authHeader.Name == "Authorization"
                        && budget.CanStartPrimarySend)
                    {
                        var keyHeader = GetApiKeyHeader();
                        if (keyHeader is not null)
                        {
                            logger.LogWarning(
                                "[{Operation}] Azure AD token rejected ({StatusCode}); retrying with configured API key.",
                                operationName,
                                rawResponse.Status);
                            authHeader = keyHeader.Value;
                            triedApiKeyFallback = true;
                            continue;
                        }
                    }

                    return Result<string>.Failure(
                        $"{operationName} failed ({rawResponse.Status} " +
                        $"{(System.Net.HttpStatusCode)rawResponse.Status}): " +
                        $"{ModelResponseParser.Truncate(responseBody, 500)}");
                }

                var result = ModelReaderWriter.Read<ResponseResult>(rawResponse.Content)!;
                string? text = result.GetOutputText();
                if (!string.IsNullOrWhiteSpace(text))
                    return Result<string>.Success(text.Trim());

                if (ModelResponseParser.IsTokenExhausted(result)
                    && budget.CanStartPrimarySend)
                {
                    int nextBudget = Math.Min(outputTokenBudget * 2, MaxOutputTokenBudget);
                    logger.LogWarning(
                        "[{Operation}] No visible text due to token exhaustion. Retrying with higher output budget {OldBudget} -> {NewBudget}",
                        operationName,
                        outputTokenBudget,
                        nextBudget);
                    outputTokenBudget = nextBudget;
                    continue;
                }

                string diagnostic = ModelResponseParser.BuildResponseDiagnostic(result);
                return Result<string>.Failure(
                    $"{operationName} returned no visible text ({diagnostic}). Raw body: " +
                    $"{ModelResponseParser.Truncate(responseBody, 500)}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<string>.Failure($"{operationName} was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Failure(
                $"{operationName} reached its absolute Azure OpenAI call deadline.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[{Operation}] Unhandled error while calling model API",
                operationName);
            return Result<string>.Failure(ex.Message);
        }
    }

    /// <summary>
    ///     Emits one structured line per LLM call so prompt-cache behaviour is queryable in Log
    ///     Analytics without cracking open capture blobs. Kept here rather than pushed through
    ///     <c>IBroadcastMetrics</c> because that interface lives in the Application layer, which
    ///     Infrastructure must not reference (enforced by
    ///     <c>LayerDependencyTests.Infrastructure_ShouldNotDependOn_Application</c>).
    ///     <para>
    ///         A zero <c>CachedTokens</c> on a prompt whose prefix should be stable means the
    ///         prefix went cold between slots — on gpt-5.6+ that is billed as a cache write with
    ///         no read to amortise it, so it is worth seeing rather than inferring.
    ///     </para>
    /// </summary>
    private void LogTokenUsage(string operationName, string model, LlmTokenUsage? usage, string? jobId)
    {
        if (usage is null)
            return;

        logger.LogInformation(
            "[{Operation}] Token usage — Model={Model}, InputTokens={InputTokens}, CachedTokens={CachedTokens}, "
            + "CacheHitRatio={CacheHitRatio:F2}, OutputTokens={OutputTokens}, ReasoningTokens={ReasoningTokens}, JobId={JobId}",
            operationName,
            model,
            usage.InputTokens,
            usage.CachedInputTokens,
            usage.CacheHitRatio,
            usage.OutputTokens,
            usage.ReasoningTokens,
            jobId);
    }
    /// <summary>
    ///     Invokes capture as a best-effort bounded admission. A custom capture implementation may
    ///     still return an asynchronous task, so observe it separately without putting exporter
    ///     latency on the provider's logical-call path.
    /// </summary>
    private void QueueCapture(LlmCallRecord record)
    {
        try
        {
            Task captureTask = llmCallCapture.CaptureAsync(record, CancellationToken.None);
            if (!captureTask.IsCompletedSuccessfully)
                _ = ObserveCaptureAsync(captureTask, record);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[LlmCallCapture] Capture admission failed for {Operation}, JobId={JobId}, Attempt={Attempt}",
                record.Stage,
                record.JobId,
                record.Attempt);
        }
    }

    private async Task ObserveCaptureAsync(Task captureTask, LlmCallRecord record)
    {
        try
        {
            await captureTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "[LlmCallCapture] Capture exporter failed for {Operation}, JobId={JobId}, Attempt={Attempt}",
                record.Stage,
                record.JobId,
                record.Attempt);
        }
    }

    private static string? TryReadResponseId(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return null;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadBinaryContentAsString(BinaryContent content)
    {
        using var ms = new MemoryStream();
        content.WriteTo(ms, default);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Sets a fixed header on every request — the SDK-agnostic equivalent of the raw-HttpClient
    /// code's <c>request.Headers.TryAddWithoutValidation</c>, carrying either an AAD bearer token
    /// or the Azure OpenAI <c>api-key</c> header depending on what <see cref="GetAuthHeaderAsync" />
    /// resolved for this attempt.</summary>
    private sealed class AuthHeaderPolicy(string name, string value) : AuthenticationPolicy
    {
        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set(name, value);
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            message.Request.Headers.Set(name, value);
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }

    private CreateResponseOptions BuildCreateResponseOptions(string renderedPrompt, int maxOutputTokens,
        bool preferJson, string model, string operationName, bool hasProducerFeedback = false,
        BinaryData? jsonSchema = null, string? systemInstructionsOverride = null)
    {
        var createOptions = new CreateResponseOptions
        {
            Model = model,
            Instructions = systemInstructionsOverride ?? PromptLibrary.SystemInstructions,
            MaxOutputTokenCount = maxOutputTokens,
            // Reasoning effort is resolved per model AND role: Review always gets "low"
            // (validated A/B on a fixed fixture: 0/10 clean approvals at "none" vs 9/9 at "low"
            // — the reasoner catches real fabrication "none" misses), regardless of whether this
            // is a feedback pass. Every other role uses the model's fastest valid tier ("minimal"
            // for the original gpt-5 family, "none" for gpt-5.1+, "low" for o-series) unless a
            // feedback pass bumps it to "low". Medium+ causes 60s+ reasoning that exhausts
            // max_output_tokens, so we never go higher.
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = string.IsNullOrWhiteSpace(_azureOptions.ReasoningEffort)
                    ? ResolveReasoningEffort(model, hasProducerFeedback, operationName)
                    : _azureOptions.ReasoningEffort.Trim().ToLowerInvariant()
            },
            TextOptions = new ResponseTextOptions
            {
                // A JSON-schema format (strict Structured Outputs) takes priority over the
                // preferJson json_object/text choice — only the Draft/CitationFix calls pass a
                // schema; every other stage passes null and keeps its existing text/json_object behaviour.
                TextFormat = jsonSchema is not null
                    ? ResponseTextFormat.CreateJsonSchemaFormat("draft_response", jsonSchema, jsonSchemaFormatDescription: null, jsonSchemaIsStrict: true)
                    : preferJson ? ResponseTextFormat.CreateJsonObjectFormat() : ResponseTextFormat.CreateTextFormat()
            }
        };
        createOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(renderedPrompt));
        return createOptions;
    }

    /// <summary>
    ///     Picks a valid Responses API <c>reasoning.effort</c> for the configured model and role.
    ///     "minimal" is accepted only by the original gpt-5 family (gpt-5, gpt-5-mini,
    ///     gpt-5-nano); gpt-5.1+ reject it and use "none" instead; o-series and other
    ///     reasoning models support neither, so they fall back to "low". <paramref name="operationName" />
    ///     is optional (defaults to "") so existing 2-arg callers (tests, the eval harness) are
    ///     unaffected and keep exercising the original model-family-only behaviour.
    /// </summary>
    internal static string ResolveReasoningEffort(string modelName, bool hasProducerFeedback, string operationName = "")
    {
        // Review's judgment quality benefits from a reasoning pass every round, not just on
        // feedback rounds — validated A/B on a fixed fixture: 0/10 clean approvals at "none" vs
        // 9/9 at "low" (the reasoner catches real fabrication "none" misses). Every other role
        // keeps the fastest tier unless a feedback pass needs it.
        if (operationName == "Review")
            return "low";

        // Feedback passes need a little reasoning to weigh nuanced producer notes;
        // "low" is supported by every reasoning model.
        if (hasProducerFeedback)
            return "low";

        modelName = modelName?.Trim() ?? string.Empty;

        if (modelName.Equals("gpt-5", StringComparison.OrdinalIgnoreCase) ||
            modelName.StartsWith("gpt-5-", StringComparison.OrdinalIgnoreCase))
            return "minimal";

        if (modelName.StartsWith("gpt-5.", StringComparison.OrdinalIgnoreCase))
            return "none";

        return "low";
    }

    /// <summary>
    ///     Base endpoint for <see cref="ResponsesClientOptions.Endpoint" /> — the SDK appends its
    ///     own <c>/responses</c> path segment (verified: pointing it at
    ///     <c>.../openai/v1/responses</c> directly would double up to
    ///     <c>.../openai/v1/responses/responses</c>), so this stops one segment short of the raw
    ///     HttpClient-era <c>BuildResponsesEndpoint</c>.
    /// </summary>
    private static Result<Uri> BuildResponsesClientEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return Result<Uri>.Failure("AiSettings:AzureAI:Endpoint must be configured.");

        string trimmed = endpoint.Trim().TrimEnd('/');
        string baseUrl = trimmed.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/openai/v1";

        return Result<Uri>.Success(new Uri(baseUrl, UriKind.Absolute));
    }
}

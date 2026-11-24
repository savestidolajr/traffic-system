using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Hosting;
using Azure.Core;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Hosting;

/// <summary>
///     Protects the two wire contracts of the workflow LLM: published Foundry agents receive the
///     rendered system prompt inside <c>input</c>, while raw Responses calls receive the normal
///     model/instructions/reasoning envelope. These are deliberately request-level tests because
///     both regressions otherwise look like successful but inexplicable model behaviour.
/// </summary>
public sealed class AzureOpenAiWorkflowLlmRequestTests
{
    private const string ResponseBody = """{"output_text":"The response is ready."}""";

    [Fact]
    public async Task Agent_route_combines_system_and_user_prompts_without_rejected_fields()
    {
        const string systemPrompt = "Station voice: say road names plainly.";
        const string userPrompt = "Describe the delay on the M7.";
        var handler = new CapturingHandler(ResponseBody);
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-stub",
            ProjectEndpoint = "https://foundry.invalid/api/projects/bulletin-generator",
            AgentByRole = new Dictionary<string, string>
            {
                ["compose2"] = "arn-wf-compose"
            }
        });

        _ = await llm.CompleteAsync("compose2", systemPrompt, userPrompt, jsonSchema: "{\"type\":\"object\"}");

        handler.RequestUri.ShouldNotBeNull();
        // Prevents agent calls silently posting to the raw endpoint and bypassing the published agent.
        handler.RequestUri!.AbsolutePath.ShouldBe(
            "/api/projects/bulletin-generator/agents/arn-wf-compose/endpoint/protocols/openai/responses");

        using var body = JsonDocument.Parse(handler.RequestBody);
        var root = body.RootElement;
        // Prevents the station voicing overlay from being dropped when the agent only accepts input.
        // This was an exact equality on "{system}\n\n---\n\n{user}" until 2026-08-31, when switching
        // agent routing on in dev failed EVERY workflow role with HTTP 400 "Response input messages
        // must contain the word 'json' in some form to use 'text.format' of type 'json_object'" — the
        // agents bake json_object while the raw route enforces shape with a strict json_schema, so
        // writeIncidents, writeFlow and compose2 contain no occurrence of the word at all. The
        // transport now appends a JSON mention when one is absent, so the contract is that both
        // prompts survive in order, not that the input is byte-identical to the join.
        string input = root.GetProperty("input").GetString()!;
        input.ShouldStartWith($"{systemPrompt}\n\n---\n\n{userPrompt}");
        input.ShouldContain("json", Case.Insensitive);
        // Prevents the agent endpoint's invalid_payload failure caused by request-side instructions.
        root.TryGetProperty("instructions", out _).ShouldBeFalse();
        // Prevents the agent endpoint's invalid_payload failure caused by request-side text format.
        root.TryGetProperty("text", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Raw_route_sends_model_instructions_reasoning_and_strict_schema()
    {
        const string systemPrompt = "Use only the supplied traffic facts.";
        const string userPrompt = "Write one concise traffic line.";
        var handler = new CapturingHandler(ResponseBody);
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-stub",
            ReasoningEffort = "none"
        });

        _ = await llm.CompleteAsync(
            "unmapped-role", systemPrompt, userPrompt,
            jsonSchema: "{\"type\":\"object\",\"properties\":{\"line\":{\"type\":\"string\"}}}");

        handler.RequestUri.ShouldNotBeNull();
        // Prevents an unmapped role from accidentally using the Foundry-agent URL and its payload rules.
        handler.RequestUri!.AbsolutePath.ShouldBe("/openai/v1/responses");

        using var body = JsonDocument.Parse(handler.RequestBody);
        var root = body.RootElement;
        // Prevents the raw model deployment from losing the deployment name needed to select a model.
        root.GetProperty("model").GetString().ShouldBe("gpt-stub");
        // Prevents the raw route from omitting the rendered system instructions.
        root.GetProperty("instructions").GetString().ShouldBe(systemPrompt);
        // Prevents the model from using its higher-cost default reasoning budget when none is configured.
        root.GetProperty("reasoning").GetProperty("effort").GetString().ShouldBe("none");
        // Prevents schema-bearing raw calls from losing provider-enforced structured-output mode.
        root.GetProperty("text").GetProperty("format").GetProperty("type").GetString()
            .ShouldBe("json_schema");
        // Prevents malformed or non-strict structured output from reaching downstream JSON parsing.
        root.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean()
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Raw_route_falls_back_to_alternate_model_on_429_without_waiting()
    {
        var handler = new CapturingHandler(
            new ScriptedResponse(HttpStatusCode.TooManyRequests, """{"error":"busy"}"""),
            new ScriptedResponse(HttpStatusCode.OK, ResponseBody));
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-5.6-luna",
            FallbackModel = "gpt-5.6-terra",
            MaxThrottleRetries = 0
        });

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        // Prevents a shared-pool 429 (luna returned 149/150 requests and 149,936/150,000
        // tokens remaining) from paying the old 2s throttle rung before trying healthy terra.
        // Request count is asserted instead of wall-clock timing because test-host scheduling can
        // jitter elapsed time; the scripted sequence makes any third/backoff request observable.
        reply.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        GetModel(handler.Requests[0].Body).ShouldBe("gpt-5.6-luna");
        GetModel(handler.Requests[1].Body).ShouldBe("gpt-5.6-terra");
    }

    [Fact]
    public async Task Raw_route_falls_back_to_alternate_model_on_transient_503()
    {
        var handler = new CapturingHandler(
            new ScriptedResponse(HttpStatusCode.ServiceUnavailable, """{"error":"no healthy upstream"}"""),
            new ScriptedResponse(HttpStatusCode.OK, ResponseBody));
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-5.6-luna",
            FallbackModel = "gpt-5.6-terra",
            MaxThrottleRetries = 0
        });

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        // Prevents the measured luna "no healthy upstream" HTTP 503 from entering the 2+4+8+16s
        // ladder when the separate terra pool measured zero throttles and zero stalls.
        reply.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        GetModel(handler.Requests[0].Body).ShouldBe("gpt-5.6-luna");
        GetModel(handler.Requests[1].Body).ShouldBe("gpt-5.6-terra");
    }

    [Fact]
    public async Task Raw_route_without_fallback_retries_the_primary_model()
    {
        var handler = new CapturingHandler(
            new ScriptedResponse(
                HttpStatusCode.TooManyRequests,
                """{"error":"busy"}""",
                RetryAfter: TimeSpan.Zero),
            new ScriptedResponse(HttpStatusCode.OK, ResponseBody));
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-5.6-luna",
            FallbackModel = null,
            MaxThrottleRetries = 1
        });

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        // Prevents an unset fallback from changing today's throttle-ladder contract; Retry-After: 0
        // keeps this regression test on the 429 retry branch without making it sleep for two seconds.
        reply.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        GetModel(handler.Requests[0].Body).ShouldBe("gpt-5.6-luna");
        GetModel(handler.Requests[1].Body).ShouldBe("gpt-5.6-luna");
    }

    [Fact]
    public async Task Raw_route_does_not_switch_again_after_fallback_failure()
    {
        var handler = new CapturingHandler(
            new ScriptedResponse(
                HttpStatusCode.TooManyRequests,
                """{"error":"busy"}""",
                RetryAfter: TimeSpan.Zero),
            new ScriptedResponse(
                HttpStatusCode.TooManyRequests,
                """{"error":"terra busy"}""",
                RetryAfter: TimeSpan.Zero));
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-5.6-luna",
            FallbackModel = "gpt-5.6-terra",
            MaxThrottleRetries = 1
        });

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        // The alternate route is one physical send. A failed fallback is surfaced as a bounded
        // stage failure; it must not switch back to Luna or multiply sends on Terra.
        reply.IsSuccess.ShouldBeFalse();
        reply.Error.ShouldNotBeNull();
        reply.Error.ShouldContain("429");
        reply.Model.ShouldBe("gpt-5.6-terra");
        reply.ViaFallback.ShouldBeTrue();
        reply.Attempt.ShouldBe(2);
        handler.Requests.Count.ShouldBe(2);
        GetModel(handler.Requests[0].Body).ShouldBe("gpt-5.6-luna");
        GetModel(handler.Requests[1].Body).ShouldBe("gpt-5.6-terra");
    }

    [Fact]
    public async Task Agent_route_falls_back_by_swapping_agent_without_request_model()
    {
        var handler = new CapturingHandler(
            new ScriptedResponse(HttpStatusCode.TooManyRequests, """{"error":"busy"}"""),
            new ScriptedResponse(HttpStatusCode.OK, ResponseBody));
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-5.6-luna",
            ProjectEndpoint = "https://foundry.invalid/api/projects/bulletin-generator",
            AgentByRole = new Dictionary<string, string>
            {
                ["compose2"] = "arn-wf-compose"
            },
            FallbackAgentByRole = new Dictionary<string, string>
            {
                ["compose2"] = "arn-wf-compose-terra"
            },
            MaxThrottleRetries = 0
        });

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        // Prevents the agent fallback from sending a request-side model: Azure rejects that payload
        // with 400 invalid_payload ("Model must match the agent's model ... when agent is specified.").
        reply.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        handler.Requests[0].Uri!.AbsolutePath.ShouldContain("/agents/arn-wf-compose/");
        handler.Requests[1].Uri!.AbsolutePath.ShouldContain("/agents/arn-wf-compose-terra/");
        HasProperty(handler.Requests[0].Body, "model").ShouldBeFalse();
        HasProperty(handler.Requests[1].Body, "model").ShouldBeFalse();
    }

    [Fact]
    public async Task Raw_route_falls_back_after_stall_retry_exhaustion()
    {
        var handler = new CapturingHandler(
            new ScriptedResponse(HttpStatusCode.OK, string.Empty, Stall: true),
            new ScriptedResponse(HttpStatusCode.OK, ResponseBody));
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-5.6-luna",
            FallbackModel = "gpt-5.6-terra",
            AttemptTimeoutSeconds = 1,
            MaxStallRetries = 0
        });

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        // Prevents exhaustion of the measured 60s upstream stall from becoming a stage failure when
        // terra measured zero stalls; MaxStallRetries=0 makes this specifically the exhaustion path.
        reply.IsSuccess.ShouldBeTrue();
        handler.Requests.Count.ShouldBe(2);
        GetModel(handler.Requests[0].Body).ShouldBe("gpt-5.6-luna");
        GetModel(handler.Requests[1].Body).ShouldBe("gpt-5.6-terra");
    }

    private static string? GetModel(string requestBody)
    {
        using var body = JsonDocument.Parse(requestBody);
        return body.RootElement.TryGetProperty("model", out var model)
            ? model.GetString()
            : null;
    }

    private static bool HasProperty(string requestBody, string propertyName)
    {
        using var body = JsonDocument.Parse(requestBody);
        return body.RootElement.TryGetProperty(propertyName, out _);
    }

    private sealed record ScriptedResponse(
        HttpStatusCode StatusCode,
        string Body,
        TimeSpan? RetryAfter = null,
        bool Stall = false);

    private sealed record CapturedRequest(Uri? Uri, string Body);


    [Fact]
    public async Task ParseResponse_reads_response_id_and_detailed_token_counts()
    {
        const string payload = """
            {
              "id": "resp-compose-42",
              "model": "gpt-5.6-luna",
              "output_text": "The response is ready.",
              "usage": {
                "input_tokens": 4277,
                "input_tokens_details": { "cached_tokens": 4274 },
                "output_tokens": 23,
                "output_tokens_details": { "reasoning_tokens": 7 }
              }
            }
            """;
        var handler = new CapturingHandler(payload);
        using var http = new HttpClient(handler);
        var llm = new AzureOpenAiWorkflowLlm(http, new StubCredential(), new AzureOpenAiWorkflowLlmOptions
        {
            Endpoint = "https://raw.invalid",
            Model = "gpt-stub"
        });

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        // These values were previously hardcoded to 0, producing a false "0% prompt cache hit rate"
        // diagnosis when a direct probe of the same deployment measured 100% cache hits (4,274 of
        // 4,277 input tokens on repeat calls).
        reply.CachedInputTokens.ShouldBe(4274);
        reply.ReasoningTokens.ShouldBe(7);
        reply.ResponseId.ShouldBe("resp-compose-42");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Queue<ScriptedResponse> responses;

        public CapturingHandler(string responseBody)
            : this(new ScriptedResponse(HttpStatusCode.OK, responseBody))
        {
        }

        public CapturingHandler(params ScriptedResponse[] responses) =>
            this.responses = new Queue<ScriptedResponse>(responses);

        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(CancellationToken.None);
            Requests.Add(new CapturedRequest(RequestUri, RequestBody));

            if (responses.Count == 0)
                throw new InvalidOperationException("The scripted response sequence was exhausted.");

            ScriptedResponse scripted = responses.Dequeue();
            if (scripted.Stall)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

            var response = new HttpResponseMessage(scripted.StatusCode)
            {
                Content = new StringContent(scripted.Body)
            };
            if (scripted.RetryAfter is { } retryAfter)
                response.Headers.RetryAfter =
                    new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
            return response;
        }
    }

    private sealed class StubCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("stub-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}

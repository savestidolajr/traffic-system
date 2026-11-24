using System.Net;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Hosting;
using Azure.Core;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Hosting;

/// <summary>
///     The per-attempt deadline, which exists because the serving layer stalls in whole minutes
///     rather than failing. Measured on dev 2026-08-29: every slow call was a healthy call plus a
///     precise 60s of dead time, and job 69cd1b3650754c78aebd0e06f1b270bc had three composer
///     candidates issued in the same second on identical prompts return in 2,533ms, 2,597ms and
///     64,367ms. Waiting that out cost a pipeline slot for nothing, so an attempt that goes quiet
///     is abandoned and re-sent.
/// </summary>
public class WorkflowLlmStallRetryTests
{
    private const string Body = """{"output_text":"Delays easing on the Monash."}""";

    private static AzureOpenAiWorkflowLlmOptions Options(int attemptSeconds, int stallRetries) => new()
    {
        Endpoint = "https://stub.invalid",
        Model = "gpt-stub",
        AttemptTimeoutSeconds = attemptSeconds,
        MaxStallRetries = stallRetries
    };

    [Fact]
    public async Task Re_sends_the_attempt_when_it_goes_quiet_past_the_deadline()
    {
        var handler = new StallingHandler(stallFirst: 1, Body);
        var llm = new AzureOpenAiWorkflowLlm(
            new HttpClient(handler), new StubCredential(), Options(attemptSeconds: 1, stallRetries: 2));

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        reply.IsSuccess.ShouldBeTrue(reply.Error);
        reply.Text.ShouldBe("Delays easing on the Monash.");
        // The point of the deadline: the stalled attempt is dropped and a second one sent, rather
        // than the caller waiting out the stall on the first.
        handler.Requests.ShouldBe(2);
    }

    [Fact]
    public async Task Reports_a_stage_failure_rather_than_a_cancellation_once_retries_are_spent()
    {
        var handler = new StallingHandler(stallFirst: int.MaxValue, Body);
        var llm = new AzureOpenAiWorkflowLlm(
            new HttpClient(handler), new StubCredential(), Options(attemptSeconds: 1, stallRetries: 1));

        LlmReply reply = await llm.CompleteAsync("compose2", "system", "user", jsonSchema: null);

        // A cancellation would unwind the whole bulletin; a failure leaves the decision to the
        // pipeline's own retry and fallback.
        reply.IsSuccess.ShouldBeFalse();
        reply.Error.ShouldNotBeNull();
        reply.Error.ShouldContain("no response within 1s");
        reply.Error.ShouldContain("2 attempt(s)");
        reply.Model.ShouldBe("gpt-stub");
        reply.Attempt.ShouldBe(2);
        handler.Requests.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Retries_a_transient_server_error_instead_of_failing_the_stage(HttpStatusCode transient)
    {
        // Measured against the deployed dev endpoint 2026-08-30: replaying one real writeIncidents
        // prompt 32 times concurrently returned ONE HTTP 500 ("The server had an error processing
        // your request") in 1159ms. This client is registered on a plain HttpClient — no
        // RateLimitRetryHandler, and that handler covers only 429 and 503 anyway — and this loop
        // covered only 429, so a 500 was retried NOWHERE. It surfaced as a stage failure, which
        // BulletinWriter then turned into empty briefs: adelaide job 369b0c37 aired "Clear runs are
        // the story across Adelaide" over three unreported road closures.
        var handler = new FailThenSucceedHandler(transient, Body);
        var llm = new AzureOpenAiWorkflowLlm(
            new HttpClient(handler), new StubCredential(),
            new AzureOpenAiWorkflowLlmOptions
            {
                Endpoint = "https://stub.invalid",
                Model = "gpt-stub",
                AttemptTimeoutSeconds = 30,
                MaxThrottleRetries = 2
            });

        LlmReply reply = await llm.CompleteAsync("writeIncidents", "system", "user", jsonSchema: null);

        reply.IsSuccess.ShouldBeTrue(reply.Error);
        reply.Text.ShouldBe("Delays easing on the Monash.");
        handler.Requests.ShouldBe(2, "the transient status must be re-sent, not surfaced as a failure");
    }

    [Fact]
    public async Task Gives_up_on_a_server_error_that_never_clears_rather_than_retrying_forever()
    {
        // A retry budget, not an infinite loop: a sustained outage must reach the pipeline as a
        // failure so its own retry and fallback decide, instead of holding a slot indefinitely.
        var handler = new FailThenSucceedHandler(HttpStatusCode.InternalServerError, Body,
            failCount: int.MaxValue);
        var llm = new AzureOpenAiWorkflowLlm(
            new HttpClient(handler), new StubCredential(),
            new AzureOpenAiWorkflowLlmOptions
            {
                Endpoint = "https://stub.invalid",
                Model = "gpt-stub",
                AttemptTimeoutSeconds = 30,
                MaxThrottleRetries = 1
            });

        LlmReply reply = await llm.CompleteAsync("writeIncidents", "system", "user", jsonSchema: null);

        reply.IsSuccess.ShouldBeFalse();
        reply.Error.ShouldNotBeNull();
        reply.Error.ShouldContain("500");
        handler.Requests.ShouldBe(2, "one initial call plus one retry, then surfaced");
    }

    [Fact]
    public async Task Still_propagates_a_cancellation_the_caller_actually_asked_for()
    {
        var handler = new StallingHandler(stallFirst: int.MaxValue, Body);
        var llm = new AzureOpenAiWorkflowLlm(
            new HttpClient(handler), new StubCredential(), Options(attemptSeconds: 30, stallRetries: 2));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            llm.CompleteAsync("compose2", "system", "user", jsonSchema: null, cts.Token));
    }

    /// <summary>Goes quiet for the first <paramref name="stallFirst" /> requests, honouring the deadline.</summary>
    private sealed class StallingHandler(int stallFirst, string body) : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) <= stallFirst)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    /// <summary>
    ///     Returns <paramref name="status" /> for the first <paramref name="failCount" /> requests,
    ///     then the success body — the shape a transient Azure OpenAI 5xx actually takes.
    /// </summary>
    private sealed class FailThenSucceedHandler(HttpStatusCode status, string body, int failCount = 1)
        : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) <= failCount)
            {
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(
                        """{"error":{"message":"The server had an error processing your request."}}""")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }

    private sealed class StubCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("stub-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}

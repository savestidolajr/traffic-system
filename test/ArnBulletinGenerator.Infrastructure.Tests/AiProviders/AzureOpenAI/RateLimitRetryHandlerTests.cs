using System.Net;
using System.Net.Http.Headers;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.AzureOpenAI;

[Trait("Category", "Slow")]
public sealed class RateLimitRetryHandlerTests
{
    private readonly ILogger<RateLimitRetryHandler> _logger = Substitute.For<ILogger<RateLimitRetryHandler>>();
    private readonly Random _rng = new(42); // deterministic seed for reproducible jitter

    // ── Retry-behaviour tests ────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_429WithoutRetryAfter_RetriesMaxTimes()
    {
        int callCount = 0;
        using var handler = new TestHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("rate limited")
            };
        });
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        callCount.ShouldBe(RateLimitRetryHandler.MaxRetries + 1);
    }

    [Fact]
    public async Task SendAsync_429WithRetryAfterSeconds_RetriesAndSucceeds()
    {
        int callCount = 0;
        using var handler = new TestHandler(_ =>
        {
            callCount++;
            if (callCount <= 2)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent("rate limited")
                };
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
                return resp;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("success")
            };
        });
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe("success");
        callCount.ShouldBe(3); // 2 retries + 1 success
    }

    [Fact]
    public async Task SendAsync_503WithRetryAfter_RetriesAndSucceeds()
    {
        int callCount = 0;
        using var handler = new TestHandler(_ =>
        {
            callCount++;
            if (callCount == 1)
            {
                var resp = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("unavailable")
                };
                resp.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
                return resp;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("ok after 503")
            };
        });
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task SendAsync_SuccessOnFirstAttempt_ReturnsPayloadWithoutRetry()
    {
        int callCount = 0;
        using var handler = new TestHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("immediate success")
            };
        });
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe("immediate success");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_RetriesExhaust_ReturnsOriginal429Failure()
    {
        int callCount = 0;
        using var handler = new TestHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("still rate limited")
            };
        });
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe("still rate limited");
        callCount.ShouldBe(RateLimitRetryHandler.MaxRetries + 1);
    }

    [Fact]
    public async Task SendAsync_400_NotRetried()
    {
        int callCount = 0;
        using var handler = new TestHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("bad request")
            };
        });
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_200Ok_NotRetried()
    {
        int callCount = 0;
        using var handler = new TestHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("fine")
            };
        });
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_MixedStatuses_OnlyRetries429And503()
    {
        int callCount = 0;
        using var handler = new TestHandler(_ =>
        {
            callCount++;
            return callCount switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("429") },
                2 => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("503") },
                3 => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") },
                _ => throw new Exception("Should not be called")
            };
        });
        using var client = CreateClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        callCount.ShouldBe(3);
    }

    // ── Per-attempt timeout tests ────────────────────────────────────────

    [Fact]
    public async Task SendAsync_AttemptTimesOut_RetriesAndSucceeds()
    {
        // First attempt hangs forever (like a stalled upstream connection); it must be cut at
        // the configured per-attempt timeout and retried, not left to block for the client's
        // full outer timeout.
        using var handler = new SequencedHandler(
            HangForever,
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("recovered")
            }));
        using var retryHandler = new RateLimitRetryHandler(_logger, _rng,
            delay: (_, _) => Task.CompletedTask, perAttemptTimeoutSeconds: 1)
        {
            InnerHandler = handler
        };
        using var client = new HttpClient(retryHandler) { BaseAddress = new Uri("https://example.com") };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldBe("recovered");
        handler.CallCount.ShouldBe(2); // 1 timed-out attempt + 1 success
    }

    [Fact]
    public async Task SendAsync_AllAttemptsTimeOut_ThrowsTimeoutException()
    {
        using var handler = new SequencedHandler(HangForever);
        using var retryHandler = new RateLimitRetryHandler(_logger, _rng,
            delay: (_, _) => Task.CompletedTask, perAttemptTimeoutSeconds: 1)
        {
            InnerHandler = handler
        };
        using var client = new HttpClient(retryHandler) { BaseAddress = new Uri("https://example.com") };

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");

        await Should.ThrowAsync<TimeoutException>(() => client.SendAsync(request));
        handler.CallCount.ShouldBe(RateLimitRetryHandler.MaxRetries + 1);
    }

    [Fact]
    public async Task SendAsync_CallerCancellation_SurfacesAsOperationCanceledException_NotTimeoutException()
    {
        // Per-attempt timeout is generous (60s) so only the caller's own token can end this
        // call — proving caller cancellation is never mistaken for a per-attempt timeout (and
        // is never retried).
        var attemptStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new SequencedHandler(
            async (_, cancellationToken) =>
            {
                attemptStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                throw new InvalidOperationException("Task.Delay(Timeout.Infinite) only returns via cancellation");
            });
        using var retryHandler = new RateLimitRetryHandler(_logger, _rng,
            delay: (_, _) => Task.CompletedTask, perAttemptTimeoutSeconds: 60)
        {
            InnerHandler = handler
        };
        using var client = new HttpClient(retryHandler) { BaseAddress = new Uri("https://example.com") };
        using var callerCts = new CancellationTokenSource();

        using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
        Task<HttpResponseMessage> sendTask = client.SendAsync(request, callerCts.Token);
        await attemptStarted.Task;
        callerCts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => sendTask);
        handler.CallCount.ShouldBe(1); // caller cancellation propagates immediately — no retry
    }

    private static async Task<HttpResponseMessage> HangForever(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("Task.Delay(Timeout.Infinite) only returns via cancellation");
    }

    // ── ComputeDelay unit tests ──────────────────────────────────────────

    [Fact]
    public void ComputeDelay_WithRetryAfterSeconds_ReturnsRetryAfterDelta()
    {
        var handler = CreateHandlerForDelayTests();
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));

        var delay = handler.ComputeDelay(response, attempt: 0);

        delay.ShouldBe(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public void ComputeDelay_WithRetryAfterDate_ReturnsApproximateDelta()
    {
        var handler = CreateHandlerForDelayTests();
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(7));

        var delay = handler.ComputeDelay(response, attempt: 0);

        delay.TotalSeconds.ShouldBe(7.0, tolerance: 0.5);
    }

    [Fact]
    public void ComputeDelay_WithoutRetryAfter_ReturnsExponentialBackoff()
    {
        var handler = CreateHandlerForDelayTests();
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        // No Retry-After header

        var delay0 = handler.ComputeDelay(response, attempt: 0);
        var delay1 = handler.ComputeDelay(response, attempt: 1);
        var delay2 = handler.ComputeDelay(response, attempt: 2);

        // Base delay is 2s, jitter is [0, 2), so each attempt should be roughly double the previous
        delay0.TotalSeconds.ShouldBeGreaterThanOrEqualTo(RateLimitRetryHandler.BaseDelaySeconds);
        delay0.TotalSeconds.ShouldBeLessThan(RateLimitRetryHandler.BaseDelaySeconds * 2);

        delay1.TotalSeconds.ShouldBeGreaterThanOrEqualTo(RateLimitRetryHandler.BaseDelaySeconds * 2);
        delay1.TotalSeconds.ShouldBeLessThan(RateLimitRetryHandler.BaseDelaySeconds * 4);

        delay2.TotalSeconds.ShouldBeGreaterThanOrEqualTo(RateLimitRetryHandler.BaseDelaySeconds * 4);
        delay2.TotalSeconds.ShouldBeLessThan(RateLimitRetryHandler.BaseDelaySeconds * 8);
    }

    [Fact]
    public void ComputeDelay_WithExpiredRetryAfterDate_FallsBackToExponential()
    {
        var handler = CreateHandlerForDelayTests();
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        // Retry-After date is in the past
        response.Headers.RetryAfter = new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(-5));

        var delay = handler.ComputeDelay(response, attempt: 0);

        // Should fall back to exponential backoff since the date is in the past
        delay.TotalSeconds.ShouldBeGreaterThanOrEqualTo(RateLimitRetryHandler.BaseDelaySeconds);
    }

    [Fact]
    public void ComputeDelay_ExponentialBackoffRespectsBaseDelay()
    {
        var handler = CreateHandlerForDelayTests();
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        // Run multiple times with different attempts to verify exponential pattern
        var delays = Enumerable.Range(0, 4)
            .Select(i => handler.ComputeDelay(response, i))
            .ToList();

        // Each delay should be approximately BaseDelay * 2^attempt (plus jitter [0, BaseDelay))
        for (int i = 0; i < delays.Count; i++)
        {
            double expectedMin = RateLimitRetryHandler.BaseDelaySeconds * Math.Pow(2, i);
            double expectedMax = expectedMin + RateLimitRetryHandler.BaseDelaySeconds;
            delays[i].TotalSeconds.ShouldBeGreaterThanOrEqualTo(expectedMin - 0.01);
            delays[i].TotalSeconds.ShouldBeLessThan(expectedMax + 0.01);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private HttpClient CreateClient(HttpMessageHandler handler)
    {
        var retryHandler = new RateLimitRetryHandler(_logger, _rng, delay: (_, _) => Task.CompletedTask)
        {
            InnerHandler = handler
        };
        return new HttpClient(retryHandler)
        {
            BaseAddress = new Uri("https://example.com")
        };
    }

    private RateLimitRetryHandler CreateHandlerForDelayTests()
    {
        // Use a minimal inner handler — ComputeDelay doesn't make HTTP calls
        return new RateLimitRetryHandler(_logger, _rng)
        {
            InnerHandler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
        };
    }

    /// <summary>
    /// A test handler that allows specifying the response per-request via a delegate.
    /// </summary>
    private sealed class TestHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }

    /// <summary>
    /// A test handler that plays back a fixed sequence of async, cancellation-aware responders —
    /// one per attempt (the last responder repeats for any attempt beyond the sequence length).
    /// Unlike <see cref="TestHandler"/>, responders here can observe (and hang on) the
    /// per-attempt <see cref="CancellationToken"/>, the same way a real stalled HTTP call would.
    /// </summary>
    private sealed class SequencedHandler(
        params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] responders) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => _callCount;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            int index = Interlocked.Increment(ref _callCount) - 1;
            var responder = responders[Math.Min(index, responders.Length - 1)];
            return await responder(request, cancellationToken).ConfigureAwait(false);
        }
    }
}

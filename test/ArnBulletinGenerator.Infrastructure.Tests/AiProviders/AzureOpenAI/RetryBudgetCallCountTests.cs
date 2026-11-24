using System.Net;
using System.Net.Http.Headers;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.AzureOpenAI;

[Trait("Category", "Slow")]
public sealed class RetryBudgetCallCountTests
{
    private readonly ILogger<RateLimitRetryHandler> _logger = Substitute.For<ILogger<RateLimitRetryHandler>>();

    [Fact]
    public async Task All429_UsesExactlyMaxRetriesPlusOneSends_AndStaysWithinDelayBudget()
    {
        int calls = 0;
        var delays = new List<TimeSpan>();
        using var transport = new DelegateHandler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        });
        using var retry = CreateHandler(transport, delays);
        using var client = new HttpClient(retry) { BaseAddress = new Uri("https://example.test") };

        using var response = await client.GetAsync("/all-429");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        calls.ShouldBe(RateLimitRetryHandler.MaxRetries + 1);
        delays.Count.ShouldBe(RateLimitRetryHandler.MaxRetries);
        delays.Sum(delay => delay.TotalSeconds).ShouldBeLessThanOrEqualTo(RateLimitRetryHandler.TotalDelayCapSeconds);
    }

    [Fact]
    public async Task ServiceUnavailableThenSuccess_SendsExactlyTwice()
    {
        int calls = 0;
        using var transport = new DelegateHandler((_, _) =>
        {
            int attempt = Interlocked.Increment(ref calls);
            return Task.FromResult(attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var retry = CreateHandler(transport, []);
        using var client = new HttpClient(retry) { BaseAddress = new Uri("https://example.test") };

        using var response = await client.GetAsync("/503-then-200");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task PerAttemptTimeoutThenSuccess_RetriesOnce()
    {
        int calls = 0;
        using var transport = new DelegateHandler(async (_, cancellationToken) =>
        {
            int attempt = Interlocked.Increment(ref calls);
            if (attempt == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The first attempt should be cancelled.");
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var retry = CreateHandler(transport, [], perAttemptTimeoutSeconds: 1);
        using var client = new HttpClient(retry) { BaseAddress = new Uri("https://example.test") };

        using var response = await client.GetAsync("/timeout-then-200");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task RetryAfter_IsHonouredButCappedByRemainingTotalBudget()
    {
        int calls = 0;
        var delays = new List<TimeSpan>();
        using var transport = new DelegateHandler((_, _) =>
        {
            int attempt = Interlocked.Increment(ref calls);
            if (attempt <= 2)
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(
                    TimeSpan.FromSeconds(attempt == 1 ? 10 : 1_000));
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var retry = CreateHandler(transport, delays);
        using var client = new HttpClient(retry) { BaseAddress = new Uri("https://example.test") };

        using var response = await client.GetAsync("/retry-after");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        calls.ShouldBe(3);
        delays.Count.ShouldBe(2);
        delays[0].ShouldBe(TimeSpan.FromSeconds(RateLimitRetryHandler.TotalDelayCapSeconds / 2));
        delays.Sum(delay => delay.TotalSeconds).ShouldBe(RateLimitRetryHandler.TotalDelayCapSeconds);
        delays[1].ShouldBe(TimeSpan.FromSeconds(RateLimitRetryHandler.TotalDelayCapSeconds / 2));
    }

    private RateLimitRetryHandler CreateHandler(
        HttpMessageHandler transport,
        List<TimeSpan> delays,
        int perAttemptTimeoutSeconds = RateLimitRetryHandler.DefaultTimeoutSeconds)
    {
        var retry = new RateLimitRetryHandler(
            _logger,
            new Random(42),
            (delay, cancellationToken) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            perAttemptTimeoutSeconds)
        {
            InnerHandler = transport
        };
        return retry;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responder(request, cancellationToken);
    }
}

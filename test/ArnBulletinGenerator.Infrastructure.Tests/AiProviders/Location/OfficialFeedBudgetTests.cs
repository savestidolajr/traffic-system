using System.Collections.Concurrent;
using ArnBulletinGenerator.Domain.Services;
using System.Net;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public sealed class OfficialFeedBudgetTests
{
    [Fact]
    public async Task RetryHandler_AllowsTwoTotalAttemptsPerRequest()
    {
        var inner = new SequenceHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var retry = new OfficialFeedRetryHandler(
            Substitute.For<ILogger<OfficialFeedRetryHandler>>(),
            new Random(1),
            delay: (_, _) => Task.CompletedTask);
        retry.InnerHandler = inner;
        using var client = new HttpClient(retry);

        using var response = await client.GetAsync("https://official.test/feed");

        inner.RequestCount.ShouldBe(2);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RetryHandler_SharedFeedBudgetCapsAggregateBackoffAtTwoSeconds()
    {
        var inner = new SequenceHandler(
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.TooManyRequests,
            retryAfter: TimeSpan.FromSeconds(1));
        var delays = new ConcurrentBag<TimeSpan>();
        var retry = new OfficialFeedRetryHandler(
            Substitute.For<ILogger<OfficialFeedRetryHandler>>(),
            new Random(1),
            delay: (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });
        retry.InnerHandler = inner;
        using var client = new HttpClient(retry);
        using var budget = OfficialFeedCallBudget.Begin(TimeSpan.FromSeconds(15));

        await Task.WhenAll(
            client.GetAsync("https://official.test/feed-a"),
            client.GetAsync("https://official.test/feed-b"));

        inner.RequestCount.ShouldBe(4);
        delays.Sum(delay => delay.TotalSeconds).ShouldBeLessThanOrEqualTo(2.0);
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode[] _statuses;
        private readonly TimeSpan? _retryAfter;
        private int _requestCount;

        internal SequenceHandler(params HttpStatusCode[] statuses)
        {
            _statuses = statuses;
        }

        internal SequenceHandler(HttpStatusCode first, HttpStatusCode second, TimeSpan retryAfter)
            : this(first, second)
        {
            _retryAfter = retryAfter;
        }

        internal SequenceHandler(
            HttpStatusCode first, HttpStatusCode second, HttpStatusCode third, HttpStatusCode fourth,
            TimeSpan retryAfter)
            : this(first, second, third, fourth)
        {
            _retryAfter = retryAfter;
        }

        internal int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int index = Interlocked.Increment(ref _requestCount) - 1;
            var response = new HttpResponseMessage(_statuses[Math.Min(index, _statuses.Length - 1)]);
            if (_retryAfter.HasValue)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(_retryAfter.Value);
            return Task.FromResult(response);
        }
    }

}

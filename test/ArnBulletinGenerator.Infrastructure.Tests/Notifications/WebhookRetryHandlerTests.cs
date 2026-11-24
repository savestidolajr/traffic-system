using System.Net;
using ArnBulletinGenerator.Infrastructure.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Notifications;

/// <summary>
///     The contract: a transient webhook failure must not drop an operator's card. Before
///     2026-07-31 <c>WebhookOptions.MaxRetries</c> was documented but read by nothing, so one
///     Power Automate blip lost the notification permanently.
/// </summary>
public sealed class WebhookRetryHandlerTests
{
    private static WebhookRetryHandler CreateSut(
        SequencedHandler inner,
        int maxRetries = 2,
        int perAttemptTimeoutSeconds = 15)
    {
        return new WebhookRetryHandler(
            NullLogger<WebhookRetryHandler>.Instance,
            new Random(1),
            maxRetries,
            perAttemptTimeoutSeconds,
            (_, _) => Task.CompletedTask)
        {
            InnerHandler = inner
        };
    }

    private static async Task<HttpResponseMessage> SendAsync(WebhookRetryHandler sut)
    {
        using var client = new HttpClient(sut);
        return await client.PostAsync("https://webhook.example.com/notify", new StringContent("{}"));
    }

    [Fact]
    public async Task ServerError_ThenSuccess_Retries_AndSucceeds()
    {
        var inner = new SequencedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var sut = CreateSut(inner);

        using var response = await SendAsync(sut);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.Attempts.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task TransientStatus_IsRetried(HttpStatusCode transient)
    {
        var inner = new SequencedHandler(transient, HttpStatusCode.OK);
        using var sut = CreateSut(inner);

        using var response = await SendAsync(sut);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.Attempts.ShouldBe(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task ClientError_IsNotRetried(HttpStatusCode permanent)
    {
        // A malformed payload or a revoked endpoint will fail identically every time;
        // retrying only delays the failure the caller needs to log.
        var inner = new SequencedHandler(permanent, HttpStatusCode.OK);
        using var sut = CreateSut(inner);

        using var response = await SendAsync(sut);

        response.StatusCode.ShouldBe(permanent);
        inner.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task RetriesExhausted_ReturnsTheLastFailure_SoTheCallerCanLogIt()
    {
        var inner = new SequencedHandler(
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.OK);
        using var sut = CreateSut(inner);

        using var response = await SendAsync(sut);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        inner.Attempts.ShouldBe(3, "maxRetries 2 means three attempts total, and the fourth (OK) is never reached");
    }

    [Fact]
    public async Task MaxRetriesZero_MakesExactlyOneAttempt()
    {
        var inner = new SequencedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var sut = CreateSut(inner, maxRetries: 0);

        using var response = await SendAsync(sut);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        inner.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task TransportFailure_IsRetried_ThenRethrownWhenExhausted()
    {
        var inner = new SequencedHandler(new HttpRequestException("connection refused"));
        using var sut = CreateSut(inner, maxRetries: 1);

        var thrown = await Should.ThrowAsync<HttpRequestException>(() => SendAsync(sut));

        thrown.Message.ShouldBe("connection refused");
        inner.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task TransportFailure_ThenSuccess_Recovers()
    {
        var inner = new SequencedHandler(new HttpRequestException("dns"), HttpStatusCode.OK);
        using var sut = CreateSut(inner);

        using var response = await SendAsync(sut);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task RetryAfterHeader_IsHonoured_AndCountsTowardTheDelayCap()
    {
        // Two 60s Retry-After values exceed TotalDelayCapSeconds, so the handler stops
        // rather than holding a review card past the slot it describes.
        var inner = new SequencedHandler(
            RetryAfter(TimeSpan.FromSeconds(60)),
            RetryAfter(TimeSpan.FromSeconds(60)),
            () => new HttpResponseMessage(HttpStatusCode.OK));
        using var sut = CreateSut(inner, maxRetries: 5);

        using var response = await SendAsync(sut);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        inner.Attempts.ShouldBe(1, "a 60s Retry-After already exceeds the 30s total cap, so no retry is made");
    }

    [Fact]
    public async Task CallerCancellation_IsNotTreatedAsATimeout()
    {
        using var cts = new CancellationTokenSource();
        var inner = new SequencedHandler(() =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        using var sut = CreateSut(inner);
        using var client = new HttpClient(sut);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            client.PostAsync("https://webhook.example.com/notify", new StringContent("{}"), cts.Token));

        inner.Attempts.ShouldBe(1);
    }

    private static Func<HttpResponseMessage> RetryAfter(TimeSpan delta)
    {
        return () =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delta);
            return response;
        };
    }

    /// <summary>Replays one queued outcome per attempt, repeating the last once drained.</summary>
    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _outcomes = new();
        private Func<HttpResponseMessage> _last = () => new HttpResponseMessage(HttpStatusCode.OK);

        public SequencedHandler(params HttpStatusCode[] statuses)
        {
            foreach (var status in statuses)
            {
                var captured = status;
                _outcomes.Enqueue(() => new HttpResponseMessage(captured));
            }
        }

        public SequencedHandler(params Func<HttpResponseMessage>[] outcomes)
        {
            foreach (var outcome in outcomes) _outcomes.Enqueue(outcome);
        }

        public SequencedHandler(Exception always)
        {
            _last = () => throw always;
        }

        public SequencedHandler(Exception first, HttpStatusCode then)
        {
            _outcomes.Enqueue(() => throw first);
            _outcomes.Enqueue(() => new HttpResponseMessage(then));
        }

        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (_outcomes.Count > 0) _last = _outcomes.Dequeue();
            return Task.FromResult(_last());
        }
    }
}

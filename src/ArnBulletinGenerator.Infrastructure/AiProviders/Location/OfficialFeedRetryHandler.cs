using System.Net;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Retries short-lived official-feed throttles and server failures without allowing retry
///     backoff to consume the bulletin's five-second per-feed deadline. The caller owns that hard
///     deadline; this handler caps each honoured Retry-After and permits two total attempts.
/// </summary>
public sealed class OfficialFeedRetryHandler : DelegatingHandler
{
    internal const int MaxRetries = 1;
    internal const double BaseDelaySeconds = 0.3;
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan TotalDelayCap = TimeSpan.FromSeconds(2);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger<OfficialFeedRetryHandler> _logger;
    private readonly Random _random;
    private readonly int _maxRetries;

    public OfficialFeedRetryHandler(ILogger<OfficialFeedRetryHandler> logger)
        : this(logger, Random.Shared)
    {
    }

    internal OfficialFeedRetryHandler(
        ILogger<OfficialFeedRetryHandler> logger,
        Random random,
        int maxRetries = MaxRetries,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _logger = logger;
        _random = random;
        _maxRetries = Math.Clamp(maxRetries, 0, MaxRetries);
        _delay = delay ?? Task.Delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        HttpRequestException? transportFailure = null;
        TimeSpan totalDelay = TimeSpan.Zero;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            response?.Dispose();
            response = null;
            transportFailure = null;
            var attemptDeadlineExpired = false;

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var budget = OfficialFeedCallBudget.Current;
            var attemptTimeout = budget is null
                ? TimeSpan.FromSeconds(5)
                : budget.Remaining < OfficialFeedCallBudget.AttemptTimeout
                    ? budget.Remaining
                    : OfficialFeedCallBudget.AttemptTimeout;
            // When the budget is the binding constraint, waiting out this attempt exhausts it by
            // construction. Say so from the cancellation itself rather than re-reading the clock:
            // CancelAfter fires off the timer wheel while Remaining is measured by Stopwatch, so a
            // fractionally early fire would otherwise leave Remaining positive and buy a doomed
            // second call to a state authority after the bulletin's deadline had already passed.
            var deadlineIsBudgetBound =
                budget is not null && attemptTimeout < OfficialFeedCallBudget.AttemptTimeout;
            if (attemptTimeout <= TimeSpan.Zero)
                attemptCts.Cancel();
            else
                attemptCts.CancelAfter(attemptTimeout);

            try
            {
                response = await base.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                attemptDeadlineExpired = true;
                transportFailure = new HttpRequestException(
                    "The official feed attempt exceeded its deadline.", ex);
            }
            catch (HttpRequestException ex)
            {
                transportFailure = ex;
            }

            bool retryable = transportFailure is not null
                || response is not null && IsRetryable(response.StatusCode);
            if (!retryable)
                return response!;

            if (attempt == _maxRetries)
                break;

            if (attemptDeadlineExpired && deadlineIsBudgetBound)
                break;

            if (budget is not null && budget.Remaining <= TimeSpan.Zero)
                break;

            var delay = ResolveDelay(response, attempt);
            if (budget is not null)
            {
                if (!budget.TryReserveBackoff(delay, out delay))
                    break;
            }
            else
            {
                if (totalDelay + delay > TotalDelayCap)
                    break;
                totalDelay += delay;
            }

            _logger.LogWarning(
                "[OfficialFeedRetry] Attempt {Attempt}/{MaxAttempts} failed ({Outcome}) — retrying in {DelayMs}ms",
                attempt + 1,
                _maxRetries + 1,
                transportFailure?.Message ?? response!.StatusCode.ToString(),
                (int)delay.TotalMilliseconds);
            await _delay(delay, cancellationToken).ConfigureAwait(false);
        }

        if (transportFailure is not null)
            throw transportFailure;
        return response!;
    }

    private static bool IsRetryable(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)status >= 500;

    private TimeSpan ResolveDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta > MaxRetryAfter ? MaxRetryAfter : delta;
        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
                return until > MaxRetryAfter ? MaxRetryAfter : until;
        }

        var backoff = TimeSpan.FromSeconds(BaseDelaySeconds * Math.Pow(2, attempt));
        return backoff + TimeSpan.FromMilliseconds(_random.Next(0, 250));
    }
}

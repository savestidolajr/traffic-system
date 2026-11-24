using System.Net;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Notifications;

/// <summary>
///     Retries a webhook POST on a transient failure, honouring <c>Retry-After</c> when the
///     endpoint supplies it and falling back to exponential backoff with jitter. Also owns the
///     PER-ATTEMPT timeout (<see cref="WebhookOptions.TimeoutSeconds" />): the client's own global
///     timeout is disabled so one hung attempt is cut and retried rather than consuming the whole
///     retry budget.
///     <para>
///         Added 2026-07-31. <see cref="WebhookOptions.MaxRetries" /> had existed since the PoC,
///         documented as "maximum retry attempts for webhook delivery on transient failures", and
///         was read by nothing: <c>WebhookNotificationService.PostAsync</c> made exactly one POST
///         and the client had no retry policy, so a single Power Automate blip dropped an
///         operator's review card permanently with only a log line.
///     </para>
///     <para>
///         A retried POST can duplicate a card if the endpoint processed a request whose response
///         was lost. That is the deliberate trade: a duplicate card is noise an operator can
///         ignore, a dropped review card is a missed bulletin.
///     </para>
/// </summary>
public sealed class WebhookRetryHandler : DelegatingHandler
{
    /// <summary>Base delay in seconds for the exponential backoff (doubles each attempt).</summary>
    internal const double BaseDelaySeconds = 1.0;

    /// <summary>
    ///     Hard cap on total accumulated delay across all retries, so a run of large
    ///     <c>Retry-After</c> values cannot hold a notification past the point of usefulness —
    ///     a review card that arrives after the slot has aired is worse than no card.
    /// </summary>
    internal const double TotalDelayCapSeconds = 30.0;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ILogger<WebhookRetryHandler> _logger;
    private readonly int _maxRetries;
    private readonly Random _random;
    private readonly int _perAttemptTimeoutSeconds;

    public WebhookRetryHandler(ILogger<WebhookRetryHandler> logger, IOptions<WebhookOptions> options)
        : this(logger, Random.Shared, options.Value.MaxRetries, options.Value.TimeoutSeconds)
    {
    }

    /// <summary>
    ///     Visible for unit testing: deterministic jitter, a no-op delay so tests do not wait on the
    ///     wall clock, and an explicit retry count / per-attempt timeout.
    /// </summary>
    internal WebhookRetryHandler(
        ILogger<WebhookRetryHandler> logger,
        Random random,
        int maxRetries,
        int perAttemptTimeoutSeconds,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _logger = logger;
        _random = random;
        _maxRetries = maxRetries;
        _perAttemptTimeoutSeconds = perAttemptTimeoutSeconds;
        _delay = delay ?? Task.Delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        HttpRequestException? transportFailure = null;
        double totalDelay = 0;

        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            response?.Dispose();
            response = null;
            transportFailure = null;
            bool attemptTimedOut = false;

            // A fresh timeout for EACH attempt. The caller's token stays linked so a real
            // cancellation still wins the race and surfaces as OperationCanceledException.
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(_perAttemptTimeoutSeconds));

            try
            {
                response = await base.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                attemptTimedOut = true;
            }
            catch (HttpRequestException ex)
            {
                // DNS, connection refused, TLS — retryable at this layer.
                transportFailure = ex;
            }

            if (!attemptTimedOut && transportFailure is null && !IsRetryable(response!.StatusCode))
                return response;

            if (attempt == _maxRetries)
                break;

            var delay = ResolveDelay(response, attempt);
            if (totalDelay + delay.TotalSeconds > TotalDelayCapSeconds)
                break;
            totalDelay += delay.TotalSeconds;

            _logger.LogWarning(
                "[WebhookRetry] Attempt {Attempt}/{MaxAttempts} failed ({Outcome}) — retrying in {DelayMs}ms",
                attempt + 1,
                _maxRetries + 1,
                attemptTimedOut
                    ? $"timed out after {_perAttemptTimeoutSeconds}s"
                    : transportFailure is not null
                        ? transportFailure.Message
                        : response!.StatusCode.ToString(),
                (int)delay.TotalMilliseconds);

            await _delay(delay, cancellationToken).ConfigureAwait(false);
        }

        // Retries exhausted (or the delay cap reached). Hand the caller the real outcome:
        // WebhookNotificationService.PostAsync logs the status and returns Result.Failure.
        if (transportFailure is not null)
            throw transportFailure;

        if (response is null)
            throw new TimeoutException(
                $"Webhook POST timed out after {_maxRetries + 1} attempts of {_perAttemptTimeoutSeconds}s each.");

        return response;
    }

    private static bool IsRetryable(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)status >= 500;

    private TimeSpan ResolveDelay(HttpResponseMessage? response, int attempt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero) return until;
        }

        double seconds = BaseDelaySeconds * Math.Pow(2, attempt);
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(_random.Next(0, 250));
    }
}

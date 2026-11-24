using System.Diagnostics;
using System.Net;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Workflow.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
/// HTTP delegating handler that retries requests on 429 (Too Many Requests) and 503 (Service Unavailable),
/// respecting the <c>Retry-After</c> header when present and falling back to exponential backoff with jitter.
/// Also owns the PER-ATTEMPT timeout (<see cref="ArnBulletinGenerator.Infrastructure.Configuration.AzureAIOptions.TimeoutSeconds"/>):
/// each attempt is wrapped in a linked <see cref="CancellationTokenSource"/> so a single hung call is cut and
/// retried exactly like a 429/503, instead of blocking a pipeline slot for the client's 5-minute overall timeout
/// (which stays as the absolute backstop for the whole retry loop, not per attempt).
///
/// Live evidence (6 Jul 2026): 12 concurrent dev jobs exhausted the gpt-5.4-mini TPM quota; every
/// draft/review/refine call failed instantly with 429 and the pipeline burned all generation attempts
/// in ~1 s each, shipping unreviewed drafts. Dev schedules share cron minutes (0/15/30/45) so production
/// mornings WILL reproduce this without client-level backoff.
///
/// This handler sits at the HttpClient layer so it fires before any pipeline-level retry logic.
/// It does NOT handle 5xx (other than 503) — the existing Polly circuit breaker on the same client
/// still covers cascading transient failures.
/// </summary>
public sealed class RateLimitRetryHandler : DelegatingHandler
{
    internal const int MaxRetries = AzureOpenAiCallBudget.DefaultMaxPrimarySends - 1;
    internal const double BaseDelaySeconds = 2.0;
    internal const double TotalDelayCapSeconds = AzureOpenAiCallBudget.DefaultMaxAggregateBackoffSeconds;
    internal const int DefaultTimeoutSeconds = AzureOpenAiCallBudget.DefaultAttemptSeconds;

    private const int LegacyMaxRetries = 5;
    private const double LegacyTotalDelayCapSeconds = 90.0;

    private readonly ILogger<RateLimitRetryHandler> logger;
    private readonly Random random;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly AzureOpenAiCallBudgetOptions budgetOptions;
    private readonly TimeProvider timeProvider;
    private readonly int legacyTimeoutSeconds;

    public RateLimitRetryHandler(
        ILogger<RateLimitRetryHandler> logger,
        IOptions<AzureAIOptions> options)
    {
        this.logger = logger;
        random = Random.Shared;
        delay = Task.Delay;
        budgetOptions = options.Value.CallBudget;
        timeProvider = TimeProvider.System;
        legacyTimeoutSeconds = options.Value.TimeoutSeconds;
    }

    internal RateLimitRetryHandler(
        ILogger<RateLimitRetryHandler> logger,
        Random random,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int perAttemptTimeoutSeconds = DefaultTimeoutSeconds)
        : this(
            logger,
            random,
            delay ?? Task.Delay,
            new AzureOpenAiCallBudgetOptions
            {
                Attempt = TimeSpan.FromSeconds(perAttemptTimeoutSeconds)
            },
            TimeProvider.System)
    {
    }

    internal RateLimitRetryHandler(
        ILogger<RateLimitRetryHandler> logger,
        Random random,
        Func<TimeSpan, CancellationToken, Task> delay,
        AzureOpenAiCallBudgetOptions budgetOptions,
        TimeProvider timeProvider)
    {
        this.logger = logger;
        this.random = random;
        this.delay = delay;
        this.budgetOptions = budgetOptions;
        this.timeProvider = timeProvider;
        legacyTimeoutSeconds = (int)Math.Max(1, budgetOptions.Attempt.TotalSeconds);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        AzureOpenAiCallBudget? budget = AzureOpenAiCallBudget.ForRequest(request);
        bool ownsBudget = budget is null;
        budget ??= new AzureOpenAiCallBudget(
            budgetOptions,
            timeProvider,
            cancellationToken,
            delay);

        try
        {
            if (budget.Options.IsLegacyPolicy)
                return await SendLegacyAsync(
                    request,
                    budget,
                    cancellationToken).ConfigureAwait(false);

            bool viaAlternate = AzureOpenAiCallBudget.RequestViaAlternate(request)
                || AzureOpenAiCallBudget.CurrentViaAlternate(budget);
            string deployment = AzureOpenAiCallBudget.RequestDeployment(request)
                ?? ResolveDeployment(request);
            HttpResponseMessage? response = null;
            int attemptNumber = 0;

            while (true)
            {
                response?.Dispose();
                response = null;
                AzureOpenAiCallBudget.AzureOpenAiCallAttempt? attempt = null;
                bool reserved = AzureOpenAiCallBudget.TryTakeRequestAttempt(request, out attempt)
                    || AzureOpenAiCallBudget.TryTakeCurrentAttempt(budget, out attempt);
                bool started = reserved || (viaAlternate
                    ? budget.TryStartAlternateSend(out attempt)
                    : budget.TryStartPrimarySend(out attempt));
                if (!started || attempt is null)
                {
                    ThrowIfCallerCancellationRequested(cancellationToken, budget);
                    throw new TimeoutException(
                        $"Azure OpenAI retry budget exhausted before attempt {attemptNumber + 1} " +
                        $"for deployment {deployment}.");
                }

                attemptNumber++;
                var deploymentWait = Stopwatch.StartNew();
                using var permit = await budget.AcquireDeploymentAsync(
                    deployment, cancellationToken).ConfigureAwait(false);
                long waitMs = deploymentWait.ElapsedMilliseconds;
                if (waitMs > 500)
                    logger.LogInformation(
                        "[AoaiBulkhead] Waited {WaitMs}ms for one of {Permits} permits on deployment {Deployment}",
                        waitMs,
                        budget.Options.BulkheadPermits,
                        deployment);

                if (permit is null)
                {
                    ThrowIfCallerCancellationRequested(cancellationToken, budget);
                    throw new TimeoutException(
                        $"Azure OpenAI deployment bulkhead/deadline prevented attempt {attemptNumber} " +
                        $"for deployment {deployment}.");
                }

                bool attemptTimedOut = false;
                using var attemptCts = budget.CreateAttemptCancellationSource(cancellationToken);
                try
                {
                    response = await base.SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                    if (cancellationToken.IsCancellationRequested
                        || budget.IsCallerCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested
                    || budget.IsCallerCancellationRequested)
                {
                    // A caller cancellation must never be classified as this handler's timeout.
                    throw;
                }
                catch (OperationCanceledException)
                {
                    attemptTimedOut = true;
                }

                ThrowIfCallerCancellationRequested(cancellationToken, budget);
                bool retryable = attemptTimedOut
                    || response?.StatusCode is HttpStatusCode.TooManyRequests
                        or HttpStatusCode.ServiceUnavailable;
                if (!retryable)
                    return response!;

                if (attemptTimedOut)
                    logger.LogWarning(
                        "[RateLimitRetry] Attempt {Attempt} to {Method} {Uri} timed out after {TimeoutSeconds}s",
                        attempt.LogicalAttempt,
                        request.Method,
                        request.RequestUri,
                        budget.Options.Attempt.TotalSeconds);
                else
                    logger.LogWarning(
                        "[RateLimitRetry] {StatusCode} on {Method} {Uri} at logical attempt {Attempt}",
                        (int)response!.StatusCode,
                        request.Method,
                        request.RequestUri,
                        attempt.LogicalAttempt);

                bool canRetry = viaAlternate
                    ? budget.CanStartAlternateSend
                    : budget.CanStartPrimarySend;
                if (!canRetry || budget.IsExpired)
                {
                    ThrowIfCallerCancellationRequested(cancellationToken, budget);
                    if (attemptTimedOut)
                        throw new TimeoutException(
                            $"Azure OpenAI request to {request.RequestUri} exhausted its " +
                            "absolute retry budget.");
                    return response!;
                }

                TimeSpan requested = attemptTimedOut
                    ? TimeSpan.FromSeconds(BaseDelaySeconds * Math.Pow(2, attempt.LogicalAttempt))
                    : ComputeDelay(response!, attempt.LogicalAttempt);
                if (!await budget.DelayBeforeRetryAsync(requested, cancellationToken)
                        .ConfigureAwait(false))
                {
                    ThrowIfCallerCancellationRequested(cancellationToken, budget);
                    if (attemptTimedOut)
                        throw new TimeoutException(
                            $"Azure OpenAI request to {request.RequestUri} reached its absolute deadline.");
                    return response!;
                }
            }
        }
        finally
        {
            if (ownsBudget)
                budget.Dispose();
        }
    }

    private async Task<HttpResponseMessage> SendLegacyAsync(
        HttpRequestMessage request,
        AzureOpenAiCallBudget budget,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        int attemptNumber = 0;
        while (true)
        {
            response?.Dispose();
            response = null;
            if (!budget.TryStartPrimarySend(out var attempt))
            {
                ThrowIfCallerCancellationRequested(cancellationToken, budget);
                throw new TimeoutException(
                    $"Azure OpenAI request to {request.RequestUri} exhausted its retry budget.");
            }

            string deployment = AzureOpenAiCallBudget.RequestDeployment(request) ?? ResolveDeployment(request);
            var deploymentWait = Stopwatch.StartNew();
            using var permit = await budget.AcquireDeploymentAsync(
                deployment,
                cancellationToken).ConfigureAwait(false);
            long waitMs = deploymentWait.ElapsedMilliseconds;
            if (waitMs > 500)
                logger.LogInformation(
                    "[AoaiBulkhead] Waited {WaitMs}ms for one of {Permits} permits on deployment {Deployment}",
                    waitMs,
                    budget.Options.BulkheadPermits,
                    deployment);
            if (permit is null)
            {
                ThrowIfCallerCancellationRequested(cancellationToken, budget);
                throw new TimeoutException(
                    $"Azure OpenAI request to {request.RequestUri} reached its deployment deadline.");
            }

            bool timedOut = false;
            using var attemptCts = budget.CreateAttemptCancellationSource(cancellationToken);
            try
            {
                response = await base.SendAsync(
                    request,
                    attemptCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested
                || budget.IsCallerCancellationRequested)
            {
                // A caller cancellation must never be classified as this handler's timeout.
                throw;
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }
            if (cancellationToken.IsCancellationRequested
                || budget.IsCallerCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            if (!timedOut && response!.StatusCode is not (
                    HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable))
                return response;

            if (timedOut)
                logger.LogWarning(
                    "[RateLimitRetry] Legacy attempt {Attempt} to {Method} {Uri} timed out after {TimeoutSeconds}s",
                    attempt.LogicalAttempt,
                    request.Method,
                    request.RequestUri,
                    budget.Options.Attempt.TotalSeconds);
            if (!budget.CanStartPrimarySend)
            {
                ThrowIfCallerCancellationRequested(cancellationToken, budget);
                if (timedOut)
                    throw new TimeoutException(
                        $"Azure OpenAI request to {request.RequestUri} exhausted its retry budget.");
                return response!;
            }

            TimeSpan requested = timedOut
                ? TimeSpan.FromSeconds(BaseDelaySeconds * Math.Pow(2, attemptNumber))
                : ComputeDelay(response!, attemptNumber);
            attemptNumber++;
            if (!await budget.DelayBeforeRetryAsync(requested, cancellationToken)
                    .ConfigureAwait(false))
            {
                ThrowIfCallerCancellationRequested(cancellationToken, budget);
                if (timedOut)
                    throw new TimeoutException(
                        $"Azure OpenAI request to {request.RequestUri} reached its absolute deadline.");
                return response!;
            }
        }
    }

    private static void ThrowIfCallerCancellationRequested(
        CancellationToken cancellationToken,
        AzureOpenAiCallBudget budget)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException(cancellationToken);
        if (budget.IsCallerCancellationRequested)
            throw new OperationCanceledException();
    }

    private static string ResolveDeployment(HttpRequestMessage request)
    {
        string path = request.RequestUri?.AbsolutePath ?? string.Empty;
        int agents = path.IndexOf("/agents/", StringComparison.OrdinalIgnoreCase);
        if (agents >= 0)
        {
            string tail = path[(agents + "/agents/".Length)..];
            int slash = tail.IndexOf('/');
            return $"agent:{(slash >= 0 ? tail[..slash] : tail)}";
        }

        return request.RequestUri?.Host ?? "azure-openai";
    }

    internal TimeSpan ComputeDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
                return delta;

            if (retryAfter.Date is { } date)
            {
                TimeSpan retryDelay = date - DateTimeOffset.UtcNow;
                if (retryDelay > TimeSpan.Zero)
                    return retryDelay;
            }
        }

        double exponentialDelay = BaseDelaySeconds * Math.Pow(2, attempt);
        double jitter = random.NextDouble() * BaseDelaySeconds;
        return TimeSpan.FromSeconds(exponentialDelay + jitter);
    }
}

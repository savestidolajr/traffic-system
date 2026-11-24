using System.Collections.Concurrent;
using System.Net.Http;

namespace ArnBulletinGenerator.Workflow.Hosting;

/// <summary>
/// Immutable limits for one Azure OpenAI logical operation.
/// </summary>
public sealed record AzureOpenAiCallBudgetOptions
{
    /// <summary>Absolute wall-clock budget for one logical operation.</summary>
    public TimeSpan Overall { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Maximum wall-clock duration of one provider attempt.</summary>
    public TimeSpan Attempt { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>Maximum duration of one retry wait.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum aggregate retry wait across the logical operation.</summary>
    public TimeSpan MaxAggregateBackoff { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Maximum primary deployment sends.</summary>
    public int MaxPrimarySends { get; init; } = 3;

    /// <summary>Maximum sends on the alternate deployment.</summary>
    public int MaxAlternateSends { get; init; } = 1;

    /// <summary>Maximum concurrent sends admitted for one deployment.</summary>
    public int BulkheadPermits { get; init; } = 4;

    /// <summary>
    /// Rollout kill switch. When true, callers may use their legacy retry policy, but the budget
    /// remains available for hard deadline and cancellation enforcement.
    /// </summary>
    public bool KillSwitch { get; init; }

    /// <summary>Alias accepted by configuration during the rollout migration.</summary>
    public bool UseLegacyRetryPolicy { get; init; }

    /// <summary>
    /// Whether the explicit rollout kill switch requests the pre-budget retry policy.
    /// </summary>
    public bool IsLegacyPolicy => KillSwitch || UseLegacyRetryPolicy;

    internal AzureOpenAiCallBudgetOptions Normalized()
    {
        static TimeSpan Positive(TimeSpan value, TimeSpan fallback) =>
            value > TimeSpan.Zero ? value : fallback;
        return this with
        {
            Overall = Positive(Overall, TimeSpan.FromSeconds(60)),
            Attempt = Positive(Attempt, TimeSpan.FromSeconds(20)),
            MaxRetryDelay = Positive(MaxRetryDelay, TimeSpan.FromSeconds(5)),
            MaxAggregateBackoff = Positive(MaxAggregateBackoff, TimeSpan.FromSeconds(10)),
            MaxPrimarySends = Math.Max(1, MaxPrimarySends),
            MaxAlternateSends = Math.Max(0, MaxAlternateSends),
            BulkheadPermits = Math.Max(1, BulkheadPermits)
        };
    }
}

/// <summary>
/// A cancellation-aware budget shared by every physical send belonging to one logical Azure
/// OpenAI operation. It owns the absolute deadline, send counts, retry wait accounting and the
/// process-local per-deployment bulkhead. Raw Infrastructure calls and Workflow.Hosting calls use
/// this same type so retries cannot multiply at a layer boundary.
/// </summary>
public sealed class AzureOpenAiCallBudget : IDisposable
{
    public const int DefaultOverallSeconds = 60;
    public const int DefaultAttemptSeconds = 20;
    public const int DefaultMaxPrimarySends = 3;
    public const int DefaultMaxAlternateSends = 1;
    public const int DefaultMaxRetryDelaySeconds = 5;
    public const int DefaultMaxAggregateBackoffSeconds = 10;
    public const int DefaultBulkheadPermits = 4;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> DeploymentGates = new(
        StringComparer.OrdinalIgnoreCase);
    private static readonly AsyncLocal<ExecutionContext?> CurrentExecution = new();

    private readonly object sync = new();
    private readonly AzureOpenAiCallBudgetOptions limits;
    private readonly TimeProvider timeProvider;
    private readonly CancellationToken callerCancellationToken;
    private readonly CancellationTokenSource deadlineCancellation;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private int primarySends;
    private int alternateSends;
    private int totalSends;
    private TimeSpan aggregateBackoff;
    private int disposed;

    /// <summary>
    /// Creates a budget whose deadline starts at construction time.
    /// </summary>
    public AzureOpenAiCallBudget(
        AzureOpenAiCallBudgetOptions? options = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
        : this(options, timeProvider, cancellationToken, delay: null)
    {
    }

    /// <summary>
    /// Overload convenient for callers that already have a clock instance.
    /// </summary>
    public AzureOpenAiCallBudget(
        TimeProvider timeProvider,
        AzureOpenAiCallBudgetOptions? options = null,
        CancellationToken cancellationToken = default)
        : this(options, timeProvider, cancellationToken, delay: null)
    {
    }

    /// <summary>Clock/delay-injectable constructor used by deterministic policy tests.</summary>
    public AzureOpenAiCallBudget(
        AzureOpenAiCallBudgetOptions? options,
        TimeProvider? timeProvider,
        CancellationToken cancellationToken,
        Func<TimeSpan, CancellationToken, Task>? delay)
    {
        limits = (options ?? new AzureOpenAiCallBudgetOptions()).Normalized();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        callerCancellationToken = cancellationToken;
        StartedAtUtc = this.timeProvider.GetUtcNow();
        DeadlineUtc = StartedAtUtc + limits.Overall;
        deadlineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadlineCancellation.CancelAfter(limits.Overall);
        this.delay = delay ?? Task.Delay;
    }

    public DateTimeOffset StartedAtUtc { get; }
    public DateTimeOffset DeadlineUtc { get; }
    public CancellationToken CancellationToken => deadlineCancellation.Token;
    public CancellationToken Token => deadlineCancellation.Token;
    public AzureOpenAiCallBudgetOptions Options => limits;
    public bool IsCallerCancellationRequested => callerCancellationToken.IsCancellationRequested;
    public bool IsExpired => timeProvider.GetUtcNow() >= DeadlineUtc || deadlineCancellation.IsCancellationRequested;
    public TimeSpan Remaining => RemainingAt(timeProvider.GetUtcNow());

    public int PrimarySends
    {
        get { lock (sync) return primarySends; }
    }

    public int AlternateSends
    {
        get { lock (sync) return alternateSends; }
    }

    public int TotalSends
    {
        get { lock (sync) return totalSends; }
    }

    public TimeSpan AggregateBackoff
    {
        get { lock (sync) return aggregateBackoff; }
    }

    public bool CanStartPrimarySend
    {
        get
        {
            lock (sync)
                return !IsExpired && primarySends < limits.MaxPrimarySends;
        }
    }

    public bool CanStartAlternateSend
    {
        get
        {
            lock (sync)
                return !IsExpired && alternateSends < limits.MaxAlternateSends;
        }
    }

    /// <summary>Reserves one primary send and returns its zero-based logical attempt number.</summary>
    public bool TryStartPrimarySend(out AzureOpenAiCallAttempt attempt) =>
        TryStartSend(alternate: false, out attempt);

    /// <summary>Reserves one alternate send and returns its zero-based logical attempt number.</summary>
    public bool TryStartAlternateSend(out AzureOpenAiCallAttempt attempt) =>
        TryStartSend(alternate: true, out attempt);

    /// <summary>
    /// Reserves a send. The reserve is atomic with its limit check, so cancellation or competing
    /// callers can never start a fourth primary or second alternate send.
    /// </summary>
    public bool TryStartSend(bool alternate, out AzureOpenAiCallAttempt attempt)
    {
        lock (sync)
        {
            attempt = default!;
            if (IsExpired)
                return false;

            if (alternate)
            {
                if (alternateSends >= limits.MaxAlternateSends)
                    return false;
                alternateSends++;
            }
            else
            {
                if (primarySends >= limits.MaxPrimarySends)
                    return false;
                primarySends++;
            }

            attempt = new AzureOpenAiCallAttempt(
                logicalAttempt: totalSends++,
                viaAlternate: alternate,
                startedAtUtc: timeProvider.GetUtcNow());
            return true;
        }
    }

    /// <summary>
    /// Reserves a retry wait after applying both the per-wait and aggregate caps. A false result
    /// means the next send must not start under this budget.
    /// </summary>
    public bool TryReserveBackoff(TimeSpan requested, out TimeSpan wait)
    {
        lock (sync)
        {
            wait = TimeSpan.Zero;
            if (IsExpired || requested <= TimeSpan.Zero)
                return requested <= TimeSpan.Zero && !IsExpired;

            TimeSpan remainingAggregate = limits.MaxAggregateBackoff - aggregateBackoff;
            TimeSpan remainingDeadline = RemainingAt(timeProvider.GetUtcNow());
            if (remainingAggregate <= TimeSpan.Zero || remainingDeadline <= TimeSpan.Zero)
                return false;

            wait = requested;
            if (wait > limits.MaxRetryDelay)
                wait = limits.MaxRetryDelay;
            if (wait > remainingAggregate)
                wait = remainingAggregate;
            if (wait > remainingDeadline)
                wait = remainingDeadline;
            if (wait <= TimeSpan.Zero)
                return false;

            aggregateBackoff += wait;
            return true;
        }
    }

    public async Task<bool> DelayBeforeRetryAsync(
        TimeSpan requested,
        CancellationToken cancellationToken = default)
    {
        if (!TryReserveBackoff(requested, out TimeSpan wait))
            return false;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            CancellationToken, cancellationToken);
        try
        {
            await delay(wait, linked.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!IsCallerCancellationRequested && IsExpired)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates the token for one physical attempt. The timeout is always the smaller of the
    /// per-attempt limit and the remaining absolute budget.
    /// </summary>
    public CancellationTokenSource CreateAttemptCancellationSource(
        CancellationToken cancellationToken = default)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(
            CancellationToken, cancellationToken);
        TimeSpan remaining = Remaining;
        TimeSpan timeout = remaining < limits.Attempt ? remaining : limits.Attempt;
        if (timeout <= TimeSpan.Zero)
            source.Cancel();
        else
            source.CancelAfter(timeout);
        return source;
    }

    /// <summary>
    /// Makes this budget visible to a handler invoked below an SDK transport. The previous route
    /// context is restored on dispose, which keeps concurrent logical operations isolated.
    /// </summary>
    public IDisposable Activate(
        bool viaAlternate = false,
        AzureOpenAiCallAttempt? pendingAttempt = null)
    {
        var prior = CurrentExecution.Value;
        CurrentExecution.Value = new ExecutionContext(this, viaAlternate, prior)
        {
            PendingAttempt = pendingAttempt
        };
        return new Scope(() => CurrentExecution.Value = prior);
    }

    /// <summary>Returns the budget flowing through the current async transport call, if any.</summary>
    public static AzureOpenAiCallBudget? Current => CurrentExecution.Value?.Budget;

    /// <summary>
    /// Attaches budget metadata to a normal <see cref="HttpRequestMessage"/>. This is used by the
    /// Workflow.Hosting adapter and by direct callers; SDK-generated requests use <see cref="Activate"/>
    /// because the SDK owns their request construction.
    /// </summary>
    public void Attach(
        HttpRequestMessage request,
        string deployment,
        bool viaAlternate = false,
        AzureOpenAiCallAttempt? attempt = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.Set(RequestBudgetKey, this);
        request.Options.Set(RequestDeploymentKey, deployment);
        request.Options.Set(RequestAlternateKey, viaAlternate);
        if (attempt is not null)
            request.Options.Set(RequestAttemptKey, attempt);
    }

    /// <summary>Gets the budget attached to a request or flowing through the current async scope.</summary>
    public static AzureOpenAiCallBudget? ForRequest(HttpRequestMessage request)
    {
        if (request.Options.TryGetValue(RequestBudgetKey, out AzureOpenAiCallBudget? budget))
            return budget;
        return Current;
    }

    public static bool TryTakeRequestAttempt(
        HttpRequestMessage request,
        out AzureOpenAiCallAttempt? attempt)
    {
        if (request.Options.TryGetValue(RequestAttemptKey, out AzureOpenAiCallAttempt? candidate)
            && candidate.TryConsume())
        {
            attempt = candidate;
            return true;
        }

        attempt = null;
        return false;
    }

    public static bool TryTakeCurrentAttempt(
        AzureOpenAiCallBudget budget,
        out AzureOpenAiCallAttempt? attempt)
    {
        var execution = CurrentExecution.Value;
        if (execution is not null && ReferenceEquals(execution.Budget, budget)
            && execution.PendingAttempt is { } pending && pending.TryConsume())
        {
            attempt = pending;
            return true;
        }

        attempt = null;
        return false;
    }

    public static bool CurrentViaAlternate(AzureOpenAiCallBudget budget)
    {
        var execution = CurrentExecution.Value;
        return execution is not null && ReferenceEquals(execution.Budget, budget) && execution.ViaAlternate;
    }

    public static string? RequestDeployment(HttpRequestMessage request) =>
        request.Options.TryGetValue(RequestDeploymentKey, out string? deployment) ? deployment : null;

    public static bool RequestViaAlternate(HttpRequestMessage request) =>
        request.Options.TryGetValue(RequestAlternateKey, out bool alternate) && alternate;

    public async Task<IDisposable?> AcquireDeploymentAsync(
        string deployment,
        CancellationToken cancellationToken = default)
    {
        if (IsExpired)
            return null;

        string key = string.IsNullOrWhiteSpace(deployment) ? "(unknown)" : deployment.Trim();
        var gate = DeploymentGates.GetOrAdd(
            key,
            _ => new SemaphoreSlim(limits.BulkheadPermits, limits.BulkheadPermits));

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            CancellationToken, cancellationToken);
        TimeSpan remaining = Remaining;
        if (remaining <= TimeSpan.Zero)
            return null;

        bool acquired;
        try
        {
            acquired = await gate.WaitAsync(remaining, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!IsCallerCancellationRequested && IsExpired)
        {
            return null;
        }

        if (!acquired)
            return null;

        if (IsExpired || linked.IsCancellationRequested)
        {
            gate.Release();
            if (IsCallerCancellationRequested)
                throw new OperationCanceledException(callerCancellationToken);
            return null;
        }

        return new DeploymentPermit(gate, linked.Token);
    }

    private TimeSpan RemainingAt(DateTimeOffset now) =>
        DeadlineUtc > now ? DeadlineUtc - now : TimeSpan.Zero;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            deadlineCancellation.Dispose();
    }

    public static readonly HttpRequestOptionsKey<AzureOpenAiCallBudget> RequestBudgetKey =
        new("Arn.AzureOpenAiCallBudget");
    public static readonly HttpRequestOptionsKey<string> RequestDeploymentKey =
        new("Arn.AzureOpenAiDeployment");
    public static readonly HttpRequestOptionsKey<bool> RequestAlternateKey =
        new("Arn.AzureOpenAiAlternate");
    internal static readonly HttpRequestOptionsKey<AzureOpenAiCallAttempt> RequestAttemptKey =
        new("Arn.AzureOpenAiAttempt");

    /// <summary>Current logical send metadata used by request-level telemetry.</summary>
    public sealed class AzureOpenAiCallAttempt
    {
        private int consumed;

        internal AzureOpenAiCallAttempt(int logicalAttempt, bool viaAlternate, DateTimeOffset startedAtUtc)
        {
            LogicalAttempt = logicalAttempt;
            ViaAlternate = viaAlternate;
            StartedAtUtc = startedAtUtc;
        }

        public int LogicalAttempt { get; }
        public bool ViaAlternate { get; }
        public DateTimeOffset StartedAtUtc { get; }

        internal bool TryConsume() => Interlocked.Exchange(ref consumed, 1) == 0;
    }

    private sealed record ExecutionContext(
        AzureOpenAiCallBudget Budget,
        bool ViaAlternate,
        ExecutionContext? Parent)
    {
        public AzureOpenAiCallAttempt? PendingAttempt { get; init; }
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? restoreAction = restore;

        public void Dispose() => Interlocked.Exchange(ref restoreAction, null)?.Invoke();
    }

    private sealed class DeploymentPermit : IDisposable
    {
        private readonly SemaphoreSlim gate;
        private readonly CancellationTokenRegistration cancellationRegistration;
        private int disposed;

        public DeploymentPermit(SemaphoreSlim gate, CancellationToken cancellationToken)
        {
            this.gate = gate;
            cancellationRegistration = cancellationToken.Register(
                static state => ((DeploymentPermit)state!).Dispose(),
                this);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                cancellationRegistration.Dispose();
                gate.Release();
            }
        }
    }
}

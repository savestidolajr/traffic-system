using System.Collections.Concurrent;
using System.Diagnostics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;


/// <summary>
///     Compatibility implementation for direct-construction tests and callers that intentionally
///     opt out of DI. Production composition must register <see cref="InteractiveWorkGate"/> as a
///     singleton; this implementation does not cache or bulkhead.
/// </summary>
public sealed class NoOpInteractiveWorkGate : IInteractiveWorkGate
{
    public static NoOpInteractiveWorkGate Instance { get; } = new();

    public Task<Result<T>> ExecuteLlmAsync<T>(
        InteractiveWorkKey key,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
        => work(cancellationToken);

    public Task<Result<T>> ExecuteTtsAsync<T>(
        InteractiveWorkKey key,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
        => work(cancellationToken);

    public Task<Result<T>> ExecuteAsync<T>(
        InteractiveWorkRequest request,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default)
        => work(cancellationToken);
}

/// <summary>
///     Process-wide interactive bulkhead. The service is intentionally singleton-scoped: a scoped
///     instance would allow each HTTP request to create its own pools and defeat admission control.
/// </summary>
public sealed class InteractiveWorkGate : IInteractiveWorkGate
{
    private readonly InteractiveWorkOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IInteractiveWorkMetrics _metrics;
    private readonly ILogger<InteractiveWorkGate>? _logger;
    private readonly PermitPool _llmPool;
    private readonly PermitPool _ttsPool;
    private readonly ConcurrentDictionary<FlightKey, IFlight> _flights = new();
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _cache = new();

    public InteractiveWorkGate(
        IOptions<InteractiveWorkOptions> options,
        TimeProvider? timeProvider = null,
        IInteractiveWorkMetrics? metrics = null,
        ILogger<InteractiveWorkGate>? logger = null)
    {
        _options = options?.Value ?? new InteractiveWorkOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _metrics = metrics ?? new NoOpInteractiveWorkMetrics();
        _logger = logger;
        _llmPool = new PermitPool(_options.EffectiveLlmPoolSize);
        _ttsPool = new PermitPool(_options.EffectiveTtsPoolSize);
    }

    public Task<Result<T>> ExecuteLlmAsync<T>(
        InteractiveWorkKey key,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
        => ExecuteAsync(new InteractiveWorkRequest(InteractiveWorkPool.Llm, key, null, bypassCache),
            work, cancellationToken);

    public Task<Result<T>> ExecuteTtsAsync<T>(
        InteractiveWorkKey key,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default,
        bool bypassCache = false)
        => ExecuteAsync(new InteractiveWorkRequest(InteractiveWorkPool.Tts, key, null, bypassCache),
            work, cancellationToken);

    public async Task<Result<T>> ExecuteAsync<T>(
        InteractiveWorkRequest request,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        cancellationToken.ThrowIfCancellationRequested();

        InteractiveWorkKey key = request.Key.Normalize();
        string operation = key.Operation;
        var cacheKey = new CacheKey(request.Pool, key, typeof(T));
        bool cacheEnabled = _options.Enabled && !request.BypassCache && !_options.CacheDisabled
            && _options.SuccessCacheLifetime > TimeSpan.Zero;

        if (cacheEnabled && TryGetCached(cacheKey, operation, request.Pool, out Result<T>? cached))
            return cached!;

        var flightKey = new FlightKey(request.Pool, key, typeof(T));
        if (_flights.TryGetValue(flightKey, out IFlight? existing))
        {
            SafeMetric(() => _metrics.TrackSingleFlight(request.Pool, operation));
            if (existing is not Flight<T> typedExisting)
                return Result<T>.Failure("Interactive work key collided with a different result type.");
            return await typedExisting.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        var flight = new Flight<T>(
            this,
            flightKey,
            request,
            work,
            cacheEnabled,
            ResolveDeadline(request));
        if (!_flights.TryAdd(flightKey, flight))
        {
            SafeMetric(() => _metrics.TrackSingleFlight(request.Pool, operation));
            var raced = (Flight<T>)_flights[flightKey];
            return await raced.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        // The flight owns its provider cancellation token. It continues for other callers when one
        // HTTP request disconnects and is cancelled only after the final waiter leaves.
        _ = flight.RunAsync();
        return await flight.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private TimeSpan ResolveDeadline(InteractiveWorkRequest request)
    {
        TimeSpan configured = _options.DeadlineFor(request.Pool, request.Key.Operation);
        if (request.Deadline is not { } requested || requested <= TimeSpan.Zero)
            return configured;

        // A caller can ask for a shorter diagnostic deadline, never a longer one that defeats the
        // operation contract. Cache bypass therefore cannot extend this bound either.
        return requested < configured ? requested : configured;
    }

    private async Task<Result<T>> RunFlightAsync<T>(Flight<T> flight)
    {
        Stopwatch waitClock = Stopwatch.StartNew();
        CancellationTokenSource deadlineCancellation = new();
        try
        {
            deadlineCancellation.CancelAfter(flight.Deadline);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                flight.SharedCancellationToken, deadlineCancellation.Token);

            PermitPool pool = flight.Request.Pool == InteractiveWorkPool.Tts ? _ttsPool : _llmPool;
            IDisposable? permit = null;
            try
            {
                permit = await pool.AcquireAsync(linked.Token).ConfigureAwait(false);
                SafeMetric(() => _metrics.TrackWait(flight.Request.Pool, flight.Operation,
                    waitClock.Elapsed.TotalMilliseconds, acquired: true, cancelled: false));

                Stopwatch executionClock = Stopwatch.StartNew();
                Result<T> result;
                try
                {
                    result = await flight.Work(linked.Token).ConfigureAwait(false);
                    if (linked.IsCancellationRequested)
                    {
                        bool timedOut = deadlineCancellation.IsCancellationRequested;
                        result = Result<T>.Failure(CancellationMessage(
                            flight.Operation, flight.Deadline, timedOut));
                    }
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    bool timedOut = deadlineCancellation.IsCancellationRequested;
                    result = Result<T>.Failure(CancellationMessage(
                        flight.Operation, flight.Deadline, timedOut));
                }
                catch (Exception exception)
                {
                    _logger?.LogWarning(exception,
                        "[InteractiveGate] {Pool} operation {Operation} failed",
                        flight.Request.Pool, flight.Operation);
                    result = Result<T>.Failure(
                        $"Interactive operation '{flight.Operation}' failed: {exception.Message}");
                }

                bool cancelled = linked.IsCancellationRequested;
                bool timedOutAfterCall = deadlineCancellation.IsCancellationRequested;
                SafeMetric(() => _metrics.TrackExecution(
                    flight.Request.Pool,
                    flight.Operation,
                    executionClock.Elapsed.TotalMilliseconds,
                    result.IsSuccess,
                    cancelled,
                    timedOutAfterCall));

                if (result.IsSuccess && !cancelled && flight.CacheEnabled)
                    StoreCached(flight.CacheKey, result);

                return result;
            }
            catch (OperationCanceledException)
            {
                bool timedOut = deadlineCancellation.IsCancellationRequested;
                SafeMetric(() => _metrics.TrackWait(flight.Request.Pool, flight.Operation,
                    waitClock.Elapsed.TotalMilliseconds, acquired: false, cancelled: true));
                return Result<T>.Failure(CancellationMessage(
                    flight.Operation, flight.Deadline, timedOut));
            }
            finally
            {
                permit?.Dispose();
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception,
                "[InteractiveGate] {Pool} operation {Operation} failed before execution",
                flight.Request.Pool, flight.Operation);
            return Result<T>.Failure(
                $"Interactive operation '{flight.Operation}' failed: {exception.Message}");
        }
        finally
        {
            deadlineCancellation.Dispose();
        }
    }

    private bool TryGetCached<T>(
        CacheKey cacheKey,
        string operation,
        InteractiveWorkPool pool,
        out Result<T>? result)
    {
        result = null;
        if (!_cache.TryGetValue(cacheKey, out CacheEntry? entry))
            return false;

        if (entry.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _cache.TryRemove(new KeyValuePair<CacheKey, CacheEntry>(cacheKey, entry));
            return false;
        }

        if (entry.Value is not Result<T> typed || typed.IsFailure)
            return false;

        SafeMetric(() => _metrics.TrackCacheHit(pool, operation));
        result = typed;
        return true;
    }

    private void StoreCached<T>(CacheKey key, Result<T> result)
    {
        TimeSpan lifetime = _options.SuccessCacheLifetime;
        if (lifetime <= TimeSpan.Zero || result.IsFailure)
            return;

        _cache[key] = new CacheEntry(result, _timeProvider.GetUtcNow() + lifetime);
    }

    private static string CancellationMessage(string operation, TimeSpan deadline, bool timedOut)
        => timedOut
            ? $"Interactive operation '{operation}' timed out after {deadline.TotalSeconds:0}s."
            : $"Interactive operation '{operation}' was cancelled.";

    private void SafeMetric(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            _logger?.LogDebug(exception, "[InteractiveGate] Metric emission failed");
        }
    }

    private readonly record struct FlightKey(
        InteractiveWorkPool Pool,
        InteractiveWorkKey Key,
        Type ResultType);

    private readonly record struct CacheKey(
        InteractiveWorkPool Pool,
        InteractiveWorkKey Key,
        Type ResultType);

    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAtUtc);

    private interface IFlight
    {
    }

    private sealed class Flight<T> : IFlight
    {
        private readonly InteractiveWorkGate _owner;
        private readonly CancellationTokenSource _sharedCancellation = new();
        private readonly TaskCompletionSource<Result<T>> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _waiterCount;

        internal Flight(
            InteractiveWorkGate owner,
            FlightKey key,
            InteractiveWorkRequest request,
            Func<CancellationToken, Task<Result<T>>> work,
            bool cacheEnabled,
            TimeSpan deadline)
        {
            _owner = owner;
            Key = key;
            Request = request;
            Work = work;
            CacheEnabled = cacheEnabled;
            Deadline = deadline;
            CacheKey = new CacheKey(request.Pool, key.Key, key.ResultType);
            Operation = key.Key.Operation;
        }

        internal FlightKey Key { get; }
        internal InteractiveWorkRequest Request { get; }
        internal Func<CancellationToken, Task<Result<T>>> Work { get; }
        internal bool CacheEnabled { get; }
        internal TimeSpan Deadline { get; }
        internal CacheKey CacheKey { get; }
        internal string Operation { get; }
        internal CancellationToken SharedCancellationToken => _sharedCancellation.Token;

        internal async Task<Result<T>> WaitAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _waiterCount);
            try
            {
                return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (Interlocked.Decrement(ref _waiterCount) == 0 && !_completion.Task.IsCompleted)
                {
                    try
                    {
                        _sharedCancellation.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // RunAsync completed at the same instant as the final waiter leaving.
                    }
                }
            }
        }

        internal async Task RunAsync()
        {
            try
            {
                Result<T> result = await _owner.RunFlightAsync(this).ConfigureAwait(false);
                _completion.TrySetResult(result);
            }
            catch (Exception exception)
            {
                _completion.TrySetResult(Result<T>.Failure(
                    $"Interactive operation '{Operation}' failed: {exception.Message}"));
            }
            finally
            {
                // Keep the completed flight visible until its result is published. This closes the
                // tiny race where a duplicate arrives after provider completion but before the
                // completion source is signalled.
                _owner._flights.TryRemove(new KeyValuePair<FlightKey, IFlight>(Key, this));
                _sharedCancellation.Dispose();
            }
        }
    }

    /// <summary>
    ///     FIFO permit queue. New callers cannot skip an older waiter, while cancellation removes a
    ///     waiter from the queue without consuming a permit.
    /// </summary>
    private sealed class PermitPool
    {
        private readonly object _sync = new();
        private readonly LinkedList<PermitWaiter> _waiters = new();
        private int _available;

        internal PermitPool(int capacity)
        {
            _available = Math.Max(1, capacity);
        }

        internal ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_available > 0 && _waiters.Count == 0)
                {
                    _available--;
                    return ValueTask.FromResult<IDisposable>(new PermitReleaser(this));
                }

                var waiter = new PermitWaiter(this);
                waiter.Node = _waiters.AddLast(waiter);
                return WaitForPermitAsync(waiter, cancellationToken);
            }
        }

        private static async ValueTask<IDisposable> WaitForPermitAsync(
            PermitWaiter waiter,
            CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(
                static state => ((PermitWaiter)state!).Cancel(), waiter);
            await waiter.Completion.Task.ConfigureAwait(false);
            return new PermitReleaser(waiter.Owner);
        }

        private void Cancel(PermitWaiter waiter)
        {
            lock (_sync)
            {
                if (waiter.Node is not null)
                {
                    _waiters.Remove(waiter.Node);
                    waiter.Node = null;
                    waiter.Completion.TrySetCanceled();
                }
            }
        }

        private void Release()
        {
            lock (_sync)
            {
                while (_waiters.First is { } node)
                {
                    _waiters.RemoveFirst();
                    PermitWaiter waiter = node.Value;
                    waiter.Node = null;
                    if (waiter.Completion.TrySetResult(true))
                        return;
                }

                _available++;
            }
        }

        private sealed class PermitWaiter
        {
            internal PermitWaiter(PermitPool owner) => Owner = owner;
            internal PermitPool Owner { get; }
            internal LinkedListNode<PermitWaiter>? Node { get; set; }
            internal TaskCompletionSource<bool> Completion { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal void Cancel() => Owner.Cancel(this);
        }

        private sealed class PermitReleaser : IDisposable
        {
            private readonly PermitPool _owner;
            private int _released;

            internal PermitReleaser(PermitPool owner) => _owner = owner;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                    _owner.Release();
            }
        }
    }
}

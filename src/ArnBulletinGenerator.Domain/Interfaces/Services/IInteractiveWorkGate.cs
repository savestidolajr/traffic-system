using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
/// Boundary for synchronous LLM and TTS admission. Implementations provide independent pools,
/// cancellation-safe fair admission, single-flight coalescing, and a short successful-result cache.
/// </summary>
public interface IInteractiveWorkGate
{
    Task<Result<T>> ExecuteLlmAsync<T>(
        InteractiveWorkKey key,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default,
        bool bypassCache = false);

    Task<Result<T>> ExecuteTtsAsync<T>(
        InteractiveWorkKey key,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default,
        bool bypassCache = false);

    Task<Result<T>> ExecuteAsync<T>(
        InteractiveWorkRequest request,
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken = default);
}

/// <summary>Bounded-cardinality telemetry emitted by the interactive admission gate.</summary>
public interface IInteractiveWorkMetrics
{
    void TrackCacheHit(InteractiveWorkPool pool, string operation);
    void TrackSingleFlight(InteractiveWorkPool pool, string operation);
    void TrackWait(InteractiveWorkPool pool, string operation, double durationMilliseconds,
        bool acquired, bool cancelled);
    void TrackExecution(InteractiveWorkPool pool, string operation, double durationMilliseconds,
        bool success, bool cancelled, bool timedOut);
}

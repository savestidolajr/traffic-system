using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.AiProviders;

/// <summary>
///     Process-wide voice catalogue cache. Concurrent misses share one provider call, callers may
///     cancel their own wait without cancelling the shared request, and an expired catalogue is
///     served on provider failure. A mutation invalidates both this cache and ModelSelector's
///     compatibility cache through <see cref="InvalidateVoiceCatalog" />.
/// </summary>
public sealed class CachedVoiceProvider(
    IVoiceProvider inner,
    TimeProvider timeProvider,
    ILogger<CachedVoiceProvider> logger,
    IMemoryCache? modelCache = null)
    : IVoiceProvider, IVoiceCatalogCacheInvalidator
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
    private const string ModelSelectorCacheKey = "ModelSelector:Voices";

    private readonly object _gate = new();
    private readonly IVoiceProvider _inner = inner;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ILogger<CachedVoiceProvider> _logger = logger;
    private readonly IMemoryCache? _modelCache = modelCache;
    private IReadOnlyList<Voice>? _cached;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;
    private Task<Result<IReadOnlyList<Voice>>>? _inFlight;
    private int _generation;

    public async Task<Result<IReadOnlyList<Voice>>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        Task<Result<IReadOnlyList<Voice>>> flight;
        int generation;
        IReadOnlyList<Voice>? stale;
        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            if (_cached is not null && now < _expiresAtUtc)
                return Result<IReadOnlyList<Voice>>.Success(_cached);

            stale = _cached;
            generation = _generation;
            flight = _inFlight ??= FetchAsync();
        }

        try
        {
            var result = await flight.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (ReferenceEquals(_inFlight, flight))
                {
                    _inFlight = null;
                    if (result.IsSuccess && generation == _generation)
                    {
                        _cached = result.Value;
                        _expiresAtUtc = _timeProvider.GetUtcNow() + CacheLifetime;
                    }
                }
            }

            if (result.IsFailure && stale is not null)
            {
                _logger.LogWarning("Voice catalogue refresh failed; serving expired catalogue: {Error}", result.Error);
                return Result<IReadOnlyList<Voice>>.Success(stale);
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            // WaitAsync cancellation is scoped to this caller. The provider request remains in
            // flight for other waiters and can populate the cache when it completes.
            throw;
        }
    }

    public void InvalidateVoiceCatalog()
    {
        lock (_gate)
        {
            _generation++;
            _inFlight = null;
            _cached = null;
            _expiresAtUtc = DateTimeOffset.MinValue;
        }
        _modelCache?.Remove(ModelSelectorCacheKey);
    }

    private async Task<Result<IReadOnlyList<Voice>>> FetchAsync()
    {
        try
        {
            return await _inner.GetVoicesAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice catalogue refresh failed");
            return Result<IReadOnlyList<Voice>>.Failure($"Error fetching voices: {ex.Message}");
        }
    }
}

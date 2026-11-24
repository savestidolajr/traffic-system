using System.Collections.Concurrent;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Tests.Mocks;

/// <summary>
///     In-memory incident state repository for local development.
/// </summary>
public sealed class InMemoryIncidentStateRepository : IIncidentStateRepository
{
    private readonly ConcurrentDictionary<string, IncidentState> _states = new();

    public Task<Result<IEnumerable<IncidentState>>> GetCurrentByMarketAsync(
        string marketId,
        CancellationToken cancellationToken = default)
    {
        var states = _states.Values.Where(s => s.MarketId == marketId).ToList();
        return Task.FromResult(Result<IEnumerable<IncidentState>>.Success(states));
    }

    public Task<Result> UpsertAsync(IncidentState state, CancellationToken cancellationToken = default)
    {
        _states[$"{state.MarketId}:{state.IncidentId}"] = state;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> MarkClearedAsync(
        string marketId,
        string incidentId,
        DateTime clearedAtUtc,
        CancellationToken cancellationToken = default)
    {
        string key = $"{marketId}:{incidentId}";
        if (_states.TryGetValue(key, out var state))
            _states[key] = state with { Status = IncidentStateStatus.Cleared, UpdatedAt = clearedAtUtc };

        return Task.FromResult(Result.Success());
    }

    public Task<Result> PurgeExpiredAsync(
        string marketId,
        DateTime olderThanUtc,
        CancellationToken cancellationToken = default)
    {
        var keysToRemove = _states
            .Where(kvp => kvp.Value.MarketId == marketId && kvp.Value.LastSeenAt < olderThanUtc)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (string key in keysToRemove) _states.TryRemove(key, out _);

        return Task.FromResult(Result.Success());
    }
}

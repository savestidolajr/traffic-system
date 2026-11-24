using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Persists per-market incident state across bulletins so a later fetch can detect new, updated, and cleared incidents instead of treating every fetch as a blank slate.</summary>
public interface IIncidentStateRepository
{
    Task<Result<IEnumerable<IncidentState>>> GetCurrentByMarketAsync(string marketId,
        CancellationToken cancellationToken = default);

    Task<Result> UpsertAsync(IncidentState state, CancellationToken cancellationToken = default);

    async Task<Result> UpsertBatchAsync(IEnumerable<IncidentState> states, CancellationToken cancellationToken = default)
    {
        foreach (var state in states)
        {
            var res = await UpsertAsync(state, cancellationToken);
            if (res.IsFailure) return res;
        }
        return Result.Success();
    }
    Task<Result> MarkClearedAsync(string marketId, string incidentId, DateTime clearedAtUtc,
        CancellationToken cancellationToken = default);

    Task<Result> PurgeExpiredAsync(string marketId, DateTime olderThanUtc,
        CancellationToken cancellationToken = default);
}

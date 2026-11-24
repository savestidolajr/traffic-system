using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Infrastructure.Tests.ContentSafety;

/// <summary>
///     In-memory <see cref="ISafetyTermRepository" /> test double. <see cref="GetForMarketAsync" />
///     mimics the production union (all Global terms plus the requested market's), and
///     <paramref name="fails" /> simulates a store outage so the filter's
///     degrade-to-baseline path can be exercised.
/// </summary>
internal sealed class FakeSafetyTermRepository(
    IReadOnlyList<SafetyTerm>? terms = null,
    bool fails = false) : ISafetyTermRepository
{
    private readonly IReadOnlyList<SafetyTerm> _terms = terms ?? [];

    public Task<Result<IReadOnlyList<SafetyTerm>>> GetForMarketAsync(
        string? market, CancellationToken ct = default)
    {
        if (fails)
            return Task.FromResult(Result<IReadOnlyList<SafetyTerm>>.Failure("safety term store unavailable"));

        string? normalized = string.IsNullOrWhiteSpace(market) ? null : market.Trim().ToLowerInvariant();
        var scoped = _terms.Where(t =>
                t.Scope == SafetyScope.Global ||
                (normalized != null && string.Equals(t.ScopeId, normalized, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return Task.FromResult(Result<IReadOnlyList<SafetyTerm>>.Success(scoped));
    }

    public Task<Result<IReadOnlyList<SafetyTerm>>> ListAllAsync(CancellationToken ct = default)
        => Task.FromResult(Result<IReadOnlyList<SafetyTerm>>.Success(_terms));

    public Task<Result> UpsertAsync(SafetyTerm term, CancellationToken ct = default)
        => Task.FromResult(Result.Success());

    public Task<Result> DeleteAsync(SafetyScope scope, string scopeId, string id, CancellationToken ct = default)
        => Task.FromResult(Result.Success());
}

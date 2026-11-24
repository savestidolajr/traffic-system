using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ArnBulletinGenerator.Domain.ValueObjects;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// Content-safety terms (banned words + allowed exceptions) for the deterministic filter.
// PartitionKey encodes the scope so a bulletin reads only its ≤2 relevant partitions:
//   Global -> "Global"
//   Market -> "Market:{market}"   (market normalised: trimmed, lower-invariant)
// RowKey = term.Id. Kind (Banned/Allowed) is a column. Composition is the filter's job
// (union global ∪ market, exception masking) — this type is pure persistence.
public sealed class TableStorageSafetyTermRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageSafetyTermRepository> logger)
    : TableRepositoryBase("safetyTerms", options, credential, logger), ISafetyTermRepository
{
    private const string GlobalPartition = "Global";

    public async Task<Result<IReadOnlyList<SafetyTerm>>> GetForMarketAsync(
        string? market, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            var results = new List<SafetyTerm>();
            results.AddRange(await QueryPartitionAsync(GlobalPartition, ct));
            if (!string.IsNullOrWhiteSpace(market))
                results.AddRange(await QueryPartitionAsync(MarketPartition(market!), ct));
            return results as IReadOnlyList<SafetyTerm>;
        });
    }

    public async Task<Result<IReadOnlyList<SafetyTerm>>> ListAllAsync(CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<SafetyTermEntity>(null, ct);
            return entities
                .Select(MapToDomain)
                .OrderByDescending(t => t.UpdatedAtUtc)
                .ToList() as IReadOnlyList<SafetyTerm>;
        });
    }

    public async Task<Result> UpsertAsync(SafetyTerm term, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(MapToEntity(term), TableUpdateMode.Replace, ct);
        });
    }

    public async Task<Result> DeleteAsync(
        SafetyScope scope, string scopeId, string id, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            await TableClient.DeleteEntityAsync(PartitionFor(scope, scopeId), id, ETag.All, ct);
        });
    }

    // ─── Query helper ────────────────────────────────────────────────

    private async Task<List<SafetyTerm>> QueryPartitionAsync(string partition, CancellationToken ct)
    {
        string filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        var entities = await QueryEntitiesAsync<SafetyTermEntity>(filter, ct);
        return entities.Select(MapToDomain).ToList();
    }

    // ─── Partition keys ──────────────────────────────────────────────

    private static string MarketPartition(string market) => $"Market:{Normalize(market)}";

    private static string PartitionFor(SafetyScope scope, string scopeId) =>
        scope == SafetyScope.Global ? GlobalPartition : MarketPartition(scopeId);

    private static string Normalize(string key) => MarketKey.Normalize(key);

    // ─── Mapping ─────────────────────────────────────────────────────

    private static SafetyTerm MapToDomain(SafetyTermEntity e) => new(
        e.Id,
        Enum.TryParse<SafetyTermKind>(e.Kind, out var kind) ? kind : SafetyTermKind.Banned,
        Enum.TryParse<SafetyScope>(e.Scope, out var scope) ? scope : SafetyScope.Global,
        e.ScopeId,
        e.Term ?? string.Empty,
        e.CreatedBy ?? string.Empty,
        e.CreatedAtUtc,
        e.UpdatedAtUtc,
        e.Enabled,
        Option.OfWhitespace(e.Note));

    private static SafetyTermEntity MapToEntity(SafetyTerm t) => new()
    {
        PartitionKey = PartitionFor(t.Scope, t.ScopeId),
        RowKey = t.Id,
        Id = t.Id,
        Kind = t.Kind.ToString(),
        Scope = t.Scope.ToString(),
        ScopeId = t.ScopeId,
        Term = t.Term,
        Enabled = t.Enabled,
        Note = t.Note.Match(n => (string?)n, () => null),
        CreatedBy = t.CreatedBy,
        CreatedAtUtc = DateTime.SpecifyKind(t.CreatedAtUtc, DateTimeKind.Utc),
        UpdatedAtUtc = DateTime.SpecifyKind(t.UpdatedAtUtc, DateTimeKind.Utc)
    };

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class SafetyTermEntity : ITableEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string ScopeId { get; set; } = string.Empty;
        public string Term { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string? Note { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

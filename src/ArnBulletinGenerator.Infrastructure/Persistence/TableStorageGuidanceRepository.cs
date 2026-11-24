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

// Standing-guidance directives (Layer 2 of the self-service guidance model).
// PartitionKey encodes the scope so a bulletin reads only its ≤3 relevant partitions:
//   Global  -> "Global"
//   Market  -> "Market:{market}"   (market normalised: trimmed, lower-invariant)
//   Station -> "Station:{station}"  (station normalised)
// RowKey = entry.Id (guid-N). Composition/ranking/sanitisation is GuidanceComposer's job,
// not the repository's — this type is pure persistence.

public sealed class TableStorageGuidanceRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageGuidanceRepository> logger)
    : TableRepositoryBase("guidance", options, credential, logger), IGuidanceRepository
{
    private const string GlobalPartition = "Global";

    public async Task<Result<IReadOnlyList<GuidanceEntry>>> GetForScopesAsync(
        string market, Option<string> station, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            var results = new List<GuidanceEntry>();
            results.AddRange(await QueryPartitionAsync(GlobalPartition, ct));
            results.AddRange(await QueryPartitionAsync(MarketPartition(market), ct));
            if (station.HasValue && !string.IsNullOrWhiteSpace(station.Value))
                results.AddRange(await QueryPartitionAsync(StationPartition(station.Value), ct));
            return results as IReadOnlyList<GuidanceEntry>;
        });
    }

    public async Task<Result<IReadOnlyList<GuidanceEntry>>> ListAsync(
        GuidanceScope scope, string scopeId, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entries = await QueryPartitionAsync(PartitionFor(scope, scopeId), ct);
            return entries
                .OrderByDescending(e => e.UpdatedAtUtc)
                .ToList() as IReadOnlyList<GuidanceEntry>;
        });
    }

    public async Task<Result> UpsertAsync(GuidanceEntry entry, CancellationToken ct = default)
    {
        return await ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(MapToEntity(entry), TableUpdateMode.Replace, ct);
        });
    }

    public async Task<Result<bool>> DeleteAsync(
        GuidanceScope scope, string scopeId, string id, CancellationToken ct = default)
    {
        // Azure Table treats an unconditional delete (ETag.All) of a missing row as a silent
        // no-op success, so existence is checked first and the delete is conditional on the ETag
        // we actually saw — a wrong-scope or stale id reports false instead of a fake success.
        var existing = await GetEntityOrDefaultAsync<GuidanceEntity>(PartitionFor(scope, scopeId), id, ct);
        if (existing.IsFailure)
            return Result<bool>.Failure(existing.Error);
        if (existing.Value.IsNone)
            return Result<bool>.Success(false);

        var entity = existing.Value.Value;
        return await ExecuteAsync(async () =>
        {
            await TableClient.DeleteEntityAsync(PartitionFor(scope, scopeId), id, entity.ETag, ct);
            return true;
        });
    }

    // ─── Query helper ────────────────────────────────────────────────

    private async Task<List<GuidanceEntry>> QueryPartitionAsync(string partition, CancellationToken ct)
    {
        string filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
        var entities = await QueryEntitiesAsync<GuidanceEntity>(filter, ct);
        return entities.Select(MapToDomain).ToList();
    }

    // ─── Partition keys ──────────────────────────────────────────────

    private static string MarketPartition(string market) => $"Market:{Normalize(market)}";
    private static string StationPartition(string station) => $"Station:{Normalize(station)}";

    private static string PartitionFor(GuidanceScope scope, string scopeId) => scope switch
    {
        GuidanceScope.Global => GlobalPartition,
        GuidanceScope.Market => MarketPartition(scopeId),
        GuidanceScope.Station => StationPartition(scopeId),
        _ => GlobalPartition
    };

    private static string Normalize(string key) => MarketKey.Normalize(key);

    // ─── Mapping ─────────────────────────────────────────────────────

    private static GuidanceEntry MapToDomain(GuidanceEntity e) => new(
        e.Id,
        Enum.TryParse<GuidanceScope>(e.Scope, out var scope) ? scope : GuidanceScope.Global,
        e.ScopeId,
        Enum.TryParse<GuidanceType>(e.GuidanceType, out var type) ? type : GuidanceType.Editorial,
        e.Text ?? string.Empty,
        e.Enabled,
        e.CreatedBy ?? string.Empty,
        e.CreatedAtUtc,
        e.UpdatedAtUtc,
        Option.OfWhitespace(e.Source),
        e.ReviewAfterUtc.HasValue ? Option<DateTime>.Some(e.ReviewAfterUtc.Value) : default);

    private static GuidanceEntity MapToEntity(GuidanceEntry e) => new()
    {
        PartitionKey = PartitionFor(e.Scope, e.ScopeId),
        RowKey = e.Id,
        Id = e.Id,
        Scope = e.Scope.ToString(),
        ScopeId = e.ScopeId,
        GuidanceType = e.Type.ToString(),
        Text = e.Text,
        Enabled = e.Enabled,
        CreatedBy = e.CreatedBy,
        CreatedAtUtc = DateTime.SpecifyKind(e.CreatedAtUtc, DateTimeKind.Utc),
        UpdatedAtUtc = DateTime.SpecifyKind(e.UpdatedAtUtc, DateTimeKind.Utc),
        Source = e.Source.Match(s => (string?)s, () => null),
        ReviewAfterUtc = e.ReviewAfterUtc.Match(d => (DateTime?)DateTime.SpecifyKind(d, DateTimeKind.Utc), () => null)
    };

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class GuidanceEntity : ITableEntity
    {
        public string Id { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string ScopeId { get; set; } = string.Empty;
        public string GuidanceType { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public string? Source { get; set; }
        public DateTime? ReviewAfterUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

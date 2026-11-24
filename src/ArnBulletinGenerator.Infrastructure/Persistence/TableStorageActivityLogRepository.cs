using System.Security.Cryptography;
using System.Text;

using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// PartitionKey = UTC date bucket, "yyyy-MM-dd" (same format AuditExportTimerTrigger.cs uses).
// RowKey = inverted-ticks plus a suffix: inline writes use a GUID, while queued writes use a
//          deterministic SHA-256 operation identity so queue redelivery upserts the same row.
//          Newer entries have lexicographically smaller RowKey values, so ascending RowKey order
//          returns newest-first within a partition. Partitions themselves come back in ascending
//          (oldest-first) PartitionKey order, so callers must sort the full result set by AtUtc.
//   Format: {(DateTime.MaxValue.Ticks - AtUtc.Ticks):D19}-{Guid:N|SHA256(RequestId|ActorObjectId|Path)}
//   D19 covers the full Tick range (DateTime.MaxValue.Ticks has 19 digits).

public sealed class TableStorageActivityLogRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageActivityLogRepository> logger)
    : TableRepositoryBase("activityLog", options, credential, logger), IActivityLogRepository
{
    // Azure Table Storage hard-caps a single string property at 64 KB (32 K UTF-16 chars).
    // Request bodies are audit aids, not required to reconstruct what changed (the resolved
    // resource/entityId already identify it) — an oversized one is dropped (with a warning)
    // rather than lose the whole entry or fail the write.
    private const int MaxRequestBodyChars = 8_000;

    public async Task<Result> RecordAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entity = MapToEntity(entry);
            await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        });
    }

    public async Task<Result<IReadOnlyList<ActivityLogEntry>>> QueryAsync(
        ActivityLogQuery query, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            string fromKey = query.FromUtc.UtcDateTime.ToString("yyyy-MM-dd");
            string toKey = query.ToUtc.UtcDateTime.ToString("yyyy-MM-dd");
            // The day-bucket PartitionKey range is a coarse, cheap prefilter. AtUtc is the
            // authoritative half-open interval: inclusive lower bound, exclusive upper bound.
            // A composite cursor additionally admits rows tied on AtUtc after its RowKey in the
            // deterministic (AtUtc descending, RowKey ascending) order.
            string filter = TableClient.CreateQueryFilter(
                $"PartitionKey ge {fromKey} and PartitionKey le {toKey}");
            filter += " and " + (string.IsNullOrWhiteSpace(query.CursorRowKey)
                ? TableClient.CreateQueryFilter(
                    $"AtUtc ge {query.FromUtc} and AtUtc lt {query.ToUtc}")
                : TableClient.CreateQueryFilter(
                    $"AtUtc ge {query.FromUtc} and (AtUtc lt {query.ToUtc} or (AtUtc eq {query.ToUtc} and RowKey gt {query.CursorRowKey}))"));
            if (!string.IsNullOrWhiteSpace(query.Resource))
                filter += " and " + TableClient.CreateQueryFilter($"Resource eq {query.Resource}");
            if (!string.IsNullOrWhiteSpace(query.EntityId))
                filter += " and " + TableClient.CreateQueryFilter($"EntityId eq {query.EntityId}");

            var entities = await QueryEntitiesAsync<ActivityLogEntity>(filter, cancellationToken);
            return entities
                .OrderByDescending(e => e.AtUtc)
                .ThenBy(e => e.RowKey, StringComparer.Ordinal)
                .Select(MapToDomain)
                .Take(query.Limit)
                .ToList() as IReadOnlyList<ActivityLogEntry>;
        });
    }

    // ─── RowKey helper ───────────────────────────────────────────────

    private static string CreateRowKey(ActivityLogEntry entry)
    {
        long inverted = DateTime.MaxValue.Ticks - entry.AtUtc.Ticks;
        string suffix = string.IsNullOrWhiteSpace(entry.RequestId)
            ? Guid.NewGuid().ToString("N")
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{entry.RequestId}|{entry.ActorObjectId}|{entry.Path}")));
        return $"{inverted:D19}-{suffix}";
    }

    // ─── Mapping ─────────────────────────────────────────────────────

    private static ActivityLogEntry MapToDomain(ActivityLogEntity e)
    {
        return new ActivityLogEntry(
            e.AtUtc,
            e.ActorName ?? string.Empty,
            e.ActorObjectId ?? string.Empty,
            e.Method ?? string.Empty,
            e.Path ?? string.Empty,
            e.Resource ?? string.Empty,
            e.EntityId,
            e.StatusCode,
            e.RequestBodyJson,
            e.RequestId);
    }

    private ActivityLogEntity MapToEntity(ActivityLogEntry entry)
    {
        return new ActivityLogEntity
        {
            PartitionKey = entry.AtUtc.ToString("yyyy-MM-dd"),
            RowKey = CreateRowKey(entry),
            AtUtc = entry.AtUtc,
            ActorName = entry.ActorName,
            ActorObjectId = entry.ActorObjectId,
            Method = entry.Method,
            Path = entry.Path,
            Resource = entry.Resource,
            EntityId = entry.EntityId,
            StatusCode = entry.StatusCode,
            RequestBodyJson = FitRequestBody(entry.RequestBodyJson, entry.Method, entry.Path),
            RequestId = entry.RequestId
        };
    }

    private string? FitRequestBody(string? json, string method, string path)
    {
        if (json is null || json.Length <= MaxRequestBodyChars)
            return json;
        logger.LogWarning(
            "[ActivityLog] Entry for {Method} {Path} has a {Length}-char request body, over the " +
            "{Max}-char Table Storage limit — dropping it so the entry still persists.",
            method, path, json.Length, MaxRequestBodyChars);
        return null;
    }

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class ActivityLogEntity : ITableEntity
    {
        public DateTime AtUtc { get; set; }
        public string? ActorName { get; set; }
        public string? ActorObjectId { get; set; }
        public string? Method { get; set; }
        public string? Path { get; set; }
        public string? Resource { get; set; }
        public string? EntityId { get; set; }
        public int StatusCode { get; set; }
        public string? RequestBodyJson { get; set; }
        public string? RequestId { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

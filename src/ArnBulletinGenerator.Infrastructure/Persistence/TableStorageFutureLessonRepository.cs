using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ArnBulletinGenerator.Domain.ValueObjects;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// PartitionKey = normalized station id. RowKey = stable SHA-256 identity for
// station + category + normalized text, so repeated teaching merges atomically.
public sealed class TableStorageFutureLessonRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    TimeProvider timeProvider,
    ILogger<TableStorageFutureLessonRepository> logger)
    : TableRepositoryBase("futureLessons", options, credential, logger), IFutureLessonRepository
{
    private const int MaxMergeAttempts = 3;

    public async Task<Result> AddAsync(
        FutureLessonEntry entry, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var incoming = MapToEntity(entry);
            for (int attempt = 0; attempt < MaxMergeAttempts; attempt++)
            {
                var current = await TableClient.GetEntityIfExistsAsync<FutureLessonEntity>(
                    incoming.PartitionKey, incoming.RowKey, cancellationToken: cancellationToken);

                if (!current.HasValue)
                {
                    try
                    {
                        await TableClient.AddEntityAsync(incoming, cancellationToken);
                        return;
                    }
                    catch (RequestFailedException ex) when (ex.Status == 409)
                    {
                        continue;
                    }
                }

                var merged = current.Value!;
                // Queue redelivery of the same job must not fake reinforcement, but the
                // observation is still fresh evidence.
                bool sameSource = string.Equals(
                    merged.SourceJobId, incoming.SourceJobId, StringComparison.Ordinal);
                merged.SourceJobId = incoming.SourceJobId;
                merged.Text = incoming.Text;
                merged.LastSeenAtUtc = incoming.LastSeenAtUtc;
                merged.ExpiresAtUtc = incoming.ExpiresAtUtc;
                if (!sameSource)
                    merged.OccurrenceCount = Math.Max(1, merged.OccurrenceCount) + 1;
                // A merge never downgrades an Active row back to Candidate: keep the
                // existing Status and ConflictWithLessonId; take the incoming Origin only
                // when the legacy row never recorded one.
                merged.Origin ??= incoming.Origin;
                try
                {
                    await TableClient.UpdateEntityAsync(
                        merged, merged.ETag, TableUpdateMode.Replace, cancellationToken);
                    return;
                }
                catch (RequestFailedException ex) when (ex.Status == 412)
                {
                    // A concurrent teaching event updated the row; re-read and merge.
                }
            }

            throw new RequestFailedException(412,
                "Future lesson changed concurrently too many times; retry the rejection.");
        });
    }

    public async Task<Result<IReadOnlyList<FutureLessonEntry>>> GetRecentAsync(
        string station, int maxEntries, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var partitionKey = MarketKey.Normalize(station);
            string? filter = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
            var entities = await QueryEntitiesAsync<FutureLessonEntity>(filter, cancellationToken);
            DateTimeOffset now = timeProvider.GetUtcNow();

            return entities
                .Where(e => e.ExpiresAtUtc > now)
                .OrderByDescending(e => e.LastSeenAtUtc)
                .Take(Math.Clamp(maxEntries, 0, FutureLessonPolicy.MaxStoredPerStation))
                .Select(MapToDomain)
                .ToList() as IReadOnlyList<FutureLessonEntry>;
        });
    }

    public async Task<Result> DeleteAsync(
        string station, string id, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            try
            {
                await TableClient.DeleteEntityAsync(
                    MarketKey.Normalize(station), id, ETag.All, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Idempotent dismissal.
            }
        });
    }

    public async Task<Result> SetStatusAsync(
        string station, string id, FutureLessonStatus status,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var partitionKey = MarketKey.Normalize(station);
            for (int attempt = 0; attempt < MaxMergeAttempts; attempt++)
            {
                var current = await TableClient.GetEntityIfExistsAsync<FutureLessonEntity>(
                    partitionKey, id, cancellationToken: cancellationToken);
                if (!current.HasValue)
                    return; // Missing rows are a no-op success.

                var entity = current.Value!;
                entity.Status = status.ToString();
                try
                {
                    await TableClient.UpdateEntityAsync(
                        entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
                    return;
                }
                catch (RequestFailedException ex) when (ex.Status == 412)
                {
                    // A concurrent update touched the row; re-read and retry.
                }
            }

            throw new RequestFailedException(412,
                "Future lesson changed concurrently too many times; retry the status change.");
        });
    }

    private static FutureLessonEntry MapToDomain(FutureLessonEntity entity) =>
        new(
            entity.RowKey,
            entity.PartitionKey,
            entity.SourceJobId ?? string.Empty,
            Enum.TryParse<FutureLessonCategory>(entity.Category, out var category)
                ? category
                : FutureLessonCategory.WordingStyle,
            entity.Text ?? string.Empty,
            entity.CreatedAtUtc.UtcDateTime,
            entity.LastSeenAtUtc.UtcDateTime,
            Math.Max(1, entity.OccurrenceCount),
            entity.ExpiresAtUtc.UtcDateTime,
            // Legacy operator-taught rows have no Status/Origin columns and stay active.
            Enum.TryParse<FutureLessonStatus>(entity.Status, out var status)
                ? status
                : FutureLessonStatus.Active,
            Enum.TryParse<FutureLessonOrigin>(entity.Origin, out var origin)
                ? origin
                : FutureLessonOrigin.Operator,
            entity.ConflictWithLessonId);

    private static FutureLessonEntity MapToEntity(FutureLessonEntry entry) =>
        new()
        {
            PartitionKey = MarketKey.Normalize(entry.Station),
            RowKey = entry.Id,
            SourceJobId = entry.SourceJobId,
            Category = entry.Category.ToString(),
            Text = entry.Text,
            CreatedAtUtc = entry.CreatedAtUtc,
            LastSeenAtUtc = entry.LastSeenAtUtc,
            OccurrenceCount = Math.Max(1, entry.OccurrenceCount),
            ExpiresAtUtc = entry.ExpiresAtUtc,
            Status = entry.Status.ToString(),
            Origin = entry.Origin.ToString(),
            ConflictWithLessonId = entry.ConflictWithLessonId
        };

    internal sealed class FutureLessonEntity : ITableEntity
    {
        public string SourceJobId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset LastSeenAtUtc { get; set; }
        public int OccurrenceCount { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        public string? Status { get; set; }
        public string? Origin { get; set; }
        public string? ConflictWithLessonId { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

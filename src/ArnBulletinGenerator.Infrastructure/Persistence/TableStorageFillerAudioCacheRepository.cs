using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// PartitionKey = FillerStatement.Id, RowKey = cache key (voice/settings/model configuration).

public sealed class TableStorageFillerAudioCacheRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageFillerAudioCacheRepository> logger)
    : TableRepositoryBase("fillerAudioCache", options, credential, logger), IFillerAudioCacheRepository
{
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    public async Task<Result<Option<FillerAudioClip>>> GetAsync(
        string fillerStatementId, string cacheKey, CancellationToken cancellationToken = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<FillerAudioCacheEntity>(fillerStatementId, cacheKey, cancellationToken);
        if (entityResult.IsFailure)
            return Result<Option<FillerAudioClip>>.Failure(entityResult.Error);

        return Result<Option<FillerAudioClip>>.Success(entityResult.Value.Match(
            e => Option<FillerAudioClip>.Some(MapToDomain(e)),
            () => Option<FillerAudioClip>.None));
    }

    public Task<Result> SaveAsync(FillerAudioClip clip, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(MapToEntity(clip), TableUpdateMode.Replace, cancellationToken);
        });

    public Task<Result<IReadOnlyList<string>>> DeleteAllForStatementAsync(
        string fillerStatementId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<FillerAudioCacheEntity>(
                $"PartitionKey eq '{fillerStatementId.Replace("'", "''")}'", cancellationToken);
            var rowKeys = new List<string>(entities.Count);
            foreach (var e in entities)
            {
                await TableClient.DeleteEntityAsync(e.PartitionKey, e.RowKey, ETag.All, cancellationToken);
                rowKeys.Add(e.RowKey);
            }
            return (IReadOnlyList<string>)rowKeys;
        });

    private static FillerAudioCacheEntity MapToEntity(FillerAudioClip clip) => new()
    {
        PartitionKey = clip.FillerStatementId,
        RowKey = clip.CacheKey,
        DurationSeconds = clip.DurationSeconds,
        WordTimingsJson = JsonSerializer.Serialize(clip.WordTimings, JsonOptions),
        CreatedAtUtc = clip.CreatedAtUtc
    };

    private FillerAudioClip MapToDomain(FillerAudioCacheEntity e) => new(
        e.PartitionKey, e.RowKey, e.DurationSeconds,
        SafeJson.Deserialize<WordTiming[]>(e.WordTimingsJson, logger, "filler audio cache", JsonOptions) ?? [],
        e.CreatedAtUtc.UtcDateTime);

    internal sealed class FillerAudioCacheEntity : ITableEntity
    {
        public double DurationSeconds { get; set; }
        public string WordTimingsJson { get; set; } = "[]";
        public DateTimeOffset CreatedAtUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

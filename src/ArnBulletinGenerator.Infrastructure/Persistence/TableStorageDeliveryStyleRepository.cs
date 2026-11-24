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

// Partition key: PK = "DeliveryStyle" (all styles share one partition — admin-managed, low volume).
// Row key: style id. Versions + assigned stations are serialized as JSON columns (mirrors the
// voice-profile repository), so the whole aggregate round-trips in a single entity.

public sealed class TableStorageDeliveryStyleRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageDeliveryStyleRepository> logger)
    : TableRepositoryBase(TableName, options, credential, logger), IDeliveryStyleRepository
{
    private const string TableName = "radioDeliveryStyles";
    private const string PK = "DeliveryStyle";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    private readonly ILogger<TableStorageDeliveryStyleRepository> _logger = logger;

    public async Task<Result<VersionedLibraryListPage<DeliveryStyle>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default)
    {
        var valid = VersionedLibraryStorage.Validate(query);
        if (valid.IsFailure)
            return Result<VersionedLibraryListPage<DeliveryStyle>>.Failure(valid.Error);

        var listed = await ListAsync(cancellationToken);
        return listed.IsFailure
            ? listed.PropagateFailure<VersionedLibraryListPage<DeliveryStyle>>()
            : Result<VersionedLibraryListPage<DeliveryStyle>>.Success(
                VersionedLibraryStorage.Page(listed.Value, query));
    }

    public Task<Result<IReadOnlyList<DeliveryStyle>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<DeliveryStyleEntity>($"PartitionKey eq '{PK}'", cancellationToken);
            IReadOnlyList<DeliveryStyle> styles = entities
                .Select(MapToDomain)
                .Where(s => s is not null)
                .Cast<DeliveryStyle>()
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return styles;
        });
    }

    public Task<Result<IReadOnlyDictionary<string, Option<DeliveryStyle>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        VersionedLibraryStorage.GetManyAsync(
            ids, includeHistory, 8,
            (id, _, ct) => GetAsync(id, ct), cancellationToken);

    public async Task<Result<Option<DeliveryStyle>>> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await GetEntityOrDefaultAsync<DeliveryStyleEntity>(PK, id, cancellationToken);
        if (result.IsFailure)
            return Result<Option<DeliveryStyle>>.Failure(result.Error);

        return result.Value.Match(
            entity =>
            {
                var style = MapToDomain(entity);
                return Result<Option<DeliveryStyle>>.Success(
                    style is null ? Option<DeliveryStyle>.None : Option<DeliveryStyle>.Some(style));
            },
            () => Result<Option<DeliveryStyle>>.Success(Option<DeliveryStyle>.None));
    }

    public Task<Result> SaveAsync(DeliveryStyle style, CancellationToken cancellationToken = default)
    {
        if (!VersionHistoryTrimmer.TrySerialize(
                style.Versions, v => v.Version, _logger, "Delivery style", style.Id,
                out string versionsJson, out var failure))
            return Task.FromResult(Result.Failure(failure!));

        return ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(
                MapToEntity(style, versionsJson), TableUpdateMode.Replace, cancellationToken);
        });
    }

    public Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            try
            {
                await TableClient.DeleteEntityAsync(PK, id, ETag.All, cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Idempotent — service-level callers perform the user-facing not-found check.
            }
        });
    }

    // ─── Mapping ─────────────────────────────────────────────────────

    private DeliveryStyle? MapToDomain(DeliveryStyleEntity e)
    {
        var versions = SafeJson.Deserialize<List<DeliveryStyleVersion>>(
            e.VersionsJson, _logger, $"delivery style {e.RowKey} versions", JsonOptions);
        if (versions is null || versions.Count == 0)
        {
            _logger.LogError("Skipping corrupt delivery style {RowKey}: no versions", e.RowKey);
            return null;
        }

        var assignedStations = SafeJson.Deserialize<List<string>>(
            e.AssignedStationsJson, _logger, $"delivery style {e.RowKey} assignedStations", JsonOptions) ?? [];

        return DeliveryStyle.Reconstitute(
            e.RowKey ?? string.Empty,
            e.Name ?? string.Empty,
            e.Description,
            string.IsNullOrEmpty(e.Status) ? "active" : e.Status,
            e.CreatedAt.UtcDateTime,
            e.UpdatedAt.UtcDateTime,
            e.CreatedBy,
            assignedStations,
            versions);
    }

    private static DeliveryStyleEntity MapToEntity(DeliveryStyle s, string versionsJson)
    {
        return new DeliveryStyleEntity
        {
            PartitionKey = PK,
            RowKey = s.Id,
            Name = s.Name,
            Description = s.Description,
            Status = s.Status,
            CurrentVersion = s.CurrentVersion,
            CreatedBy = s.CreatedBy,
            AssignedStationsJson = JsonSerializer.Serialize(s.AssignedStations, JsonOptions),
            VersionsJson = versionsJson,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt
        };
    }

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class DeliveryStyleEntity : ITableEntity
    {
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public string Status { get; set; } = "active";
        public int CurrentVersion { get; set; }
        public string? CreatedBy { get; set; }
        public string? AssignedStationsJson { get; set; }
        public string VersionsJson { get; set; } = "[]";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

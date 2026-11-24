using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Azure Table persistence for bulletin playbooks. New writes use a compact current row and
///     immutable per-version rows under <c>PK=Library_{id}</c>; legacy aggregate rows remain in
///     place for dual-read/dual-write rollback.
/// </summary>
public sealed class TableStorageBulletinPlaybookRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageBulletinPlaybookRepository> logger,
    IOptions<VersionedLibraryOptions>? libraryOptions = null,
    IMemoryCache? cache = null)
    : TableRepositoryBase(TableName, options, credential, logger), IBulletinPlaybookRepository
{
    private const string TableName = "radioBulletinPlaybooks";
    private const string LegacyPartitionKey = "BulletinPlaybook";
    private const string CacheKey = "VersionedLibrary:BulletinPlaybooks:Current";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    private readonly ILogger<TableStorageBulletinPlaybookRepository> _logger = logger;
    private readonly VersionedLibraryOptions _libraryOptions = libraryOptions?.Value ?? new();
    private readonly IMemoryCache? _cache = cache;

    public async Task<Result<VersionedLibraryListPage<BulletinPlaybook>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default)
    {
        var valid = VersionedLibraryStorage.Validate(query);
        if (valid.IsFailure)
            return Result<VersionedLibraryListPage<BulletinPlaybook>>.Failure(valid.Error);

        bool summary = !query.IncludeHistory;
        bool canUseSummary = summary && _libraryOptions.SummaryListsEnabled;
        if (canUseSummary && !query.IsPaged
            && _cache?.TryGetValue(CacheKey, out IReadOnlyList<BulletinPlaybook>? cached) == true
            && cached is not null)
            return Result<VersionedLibraryListPage<BulletinPlaybook>>.Success(
                VersionedLibraryStorage.Page(cached, query));

        var loaded = await ExecuteAsync(() => LoadLibraryAsync(query.IncludeHistory, canUseSummary, cancellationToken));
        if (loaded.IsFailure)
            return loaded.PropagateFailure<VersionedLibraryListPage<BulletinPlaybook>>();

        var ordered = loaded.Value.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (canUseSummary && !query.IsPaged)
            _cache?.Set(CacheKey, (IReadOnlyList<BulletinPlaybook>)ordered,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _libraryOptions.SummaryCacheTtl });

        return Result<VersionedLibraryListPage<BulletinPlaybook>>.Success(
            VersionedLibraryStorage.Page(ordered, query));
    }

    public async Task<Result<IReadOnlyList<BulletinPlaybook>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var page = await ListAsync(VersionedLibraryListQuery.FullHistoryDefault, cancellationToken);
        return page.IsFailure
            ? page.PropagateFailure<IReadOnlyList<BulletinPlaybook>>()
            : Result<IReadOnlyList<BulletinPlaybook>>.Success(page.Value.Items);
    }

    public async Task<Result<IReadOnlyDictionary<string, Option<BulletinPlaybook>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        await VersionedLibraryStorage.GetManyAsync(
            ids, includeHistory, _libraryOptions.BulkReadConcurrency,
            (id, history, ct) => GetByIdAsync(id, history, ct), cancellationToken);

    public Task<Result<Option<BulletinPlaybook>>> GetAsync(
        string id, CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, includeHistory: true, cancellationToken: cancellationToken);

    private async Task<Result<Option<BulletinPlaybook>>> GetByIdAsync(
        string id, bool includeHistory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result<Option<BulletinPlaybook>>.Success(Option<BulletinPlaybook>.None);

        if (_libraryOptions.NormalizedStorageEnabled)
        {
            var normalized = await GetNormalizedAsync(id, includeHistory, cancellationToken);
            if (normalized.IsFailure)
                return normalized;
            if (normalized.Value.HasValue)
                return normalized;
        }

        if (!_libraryOptions.LegacyReadEnabled)
            return Result<Option<BulletinPlaybook>>.Success(Option<BulletinPlaybook>.None);
        return await GetLegacyAsync(id, cancellationToken);
    }

    public async Task<Result> SaveAsync(BulletinPlaybook playbook, CancellationToken cancellationToken = default)
    {
        if (!VersionHistoryTrimmer.TrySerialize(
                playbook.Versions, v => v.Version, _logger, "Bulletin playbook", playbook.Id,
                out string legacyVersionsJson, out var failure))
            return Result.Failure(failure!);

        var versionEntities = new List<BulletinPlaybookEntity>(playbook.Versions.Count);
        foreach (var version in playbook.Versions)
        {
            string versionJson = JsonSerializer.Serialize(version, JsonOptions);
            if (versionJson.Length > VersionHistoryTrimmer.PropertyCeiling)
                return Result.Failure(OperationalFailure.InvalidInput(
                    "versionhistory.entry_too_large",
                    $"This bulletin playbook is too large to save — version {version.Version} exceeds the " +
                    $"{VersionHistoryTrimmer.PropertyCeiling:N0}-character limit."));
            versionEntities.Add(MapVersionEntity(playbook, version, versionJson));
        }

        string currentJson = JsonSerializer.Serialize(playbook.Versions[^1], JsonOptions);
        var currentEntity = MapCurrentEntity(playbook, currentJson);
        var legacyEntity = MapLegacyEntity(playbook, legacyVersionsJson);
        var result = await ExecuteAsync(async () =>
        {
            if (_libraryOptions.NormalizedStorageEnabled)
            {
                await TableClient.UpsertEntityAsync(currentEntity, TableUpdateMode.Replace, cancellationToken);
                foreach (var versionEntity in versionEntities)
                    await TableClient.UpsertEntityAsync(versionEntity, TableUpdateMode.Replace, cancellationToken);
            }
            if (_libraryOptions.LegacyWriteEnabled)
                await TableClient.UpsertEntityAsync(legacyEntity, TableUpdateMode.Replace, cancellationToken);
        });
        if (result.IsSuccess)
            _cache?.Remove(CacheKey);
        return result;
    }

    public async Task<Result> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(async () =>
        {
            if (_libraryOptions.NormalizedStorageEnabled)
            {
                await DeleteIfPresentAsync(VersionedLibraryStorage.PartitionKey(id), VersionedLibraryStorage.CurrentRowKey,
                    cancellationToken);
                var versions = await QueryEntitiesAsync<BulletinPlaybookEntity>(
                    $"PartitionKey eq '{VersionedLibraryStorage.PartitionKey(id)}' and " +
                    $"RowKey ne '{VersionedLibraryStorage.CurrentRowKey}'", cancellationToken);
                foreach (var version in versions)
                    await DeleteIfPresentAsync(version.PartitionKey, version.RowKey, cancellationToken);
            }
            if (_libraryOptions.LegacyWriteEnabled || _libraryOptions.LegacyReadEnabled)
                await DeleteIfPresentAsync(LegacyPartitionKey, id, cancellationToken);
        });
        if (result.IsSuccess)
            _cache?.Remove(CacheKey);
        return result;
    }

    private async Task<List<BulletinPlaybook>> LoadLibraryAsync(
        bool includeHistory, bool summary, CancellationToken cancellationToken)
    {
        var byId = new Dictionary<string, BulletinPlaybook>(StringComparer.Ordinal);
        if (_libraryOptions.NormalizedStorageEnabled && (summary || _libraryOptions.FullAggregateReadEnabled))
        {
            var currents = await QueryEntitiesAsync<BulletinPlaybookEntity>(
                $"RowKey eq '{VersionedLibraryStorage.CurrentRowKey}'", cancellationToken);
            foreach (var entity in currents)
            {
                var normalized = await MapNormalizedAsync(entity, includeHistory, cancellationToken);
                if (normalized.HasValue)
                    byId[normalized.Value.Id] = normalized.Value;
            }
        }
        if (!_libraryOptions.LegacyReadEnabled)
            return byId.Values.ToList();

        var legacyEntities = await QueryEntitiesAsync<BulletinPlaybookEntity>(
            $"PartitionKey eq '{LegacyPartitionKey}'", cancellationToken);
        foreach (var entity in legacyEntities)
        {
            var playbook = MapLegacy(entity);
            if (playbook is null || byId.ContainsKey(playbook.Id))
                continue;
            byId[playbook.Id] = includeHistory || !summary ? playbook : CurrentOnly(playbook);
        }
        return byId.Values.ToList();
    }

    private async Task<Result<Option<BulletinPlaybook>>> GetNormalizedAsync(
        string id, bool includeHistory, CancellationToken cancellationToken)
    {
        var result = await GetEntityOrDefaultAsync<BulletinPlaybookEntity>(
            VersionedLibraryStorage.PartitionKey(id), VersionedLibraryStorage.CurrentRowKey, cancellationToken);
        if (result.IsFailure)
            return Result<Option<BulletinPlaybook>>.Failure(result.Error);
        if (result.Value.IsNone)
            return Result<Option<BulletinPlaybook>>.Success(Option<BulletinPlaybook>.None);
        var mapped = await MapNormalizedAsync(result.Value.Value, includeHistory, cancellationToken);
        return mapped.HasValue
            ? Result<Option<BulletinPlaybook>>.Success(mapped)
            : Result<Option<BulletinPlaybook>>.Success(Option<BulletinPlaybook>.None);
    }

    private async Task<Option<BulletinPlaybook>> MapNormalizedAsync(
        BulletinPlaybookEntity current, bool includeHistory, CancellationToken cancellationToken)
    {
        var latest = DeserializeVersion(current.VersionJson);
        if (latest is null || latest.Version != current.CurrentVersion)
            return Option<BulletinPlaybook>.None;
        var versions = new List<BulletinPlaybookVersion> { latest };
        if (includeHistory)
        {
            var rows = await QueryEntitiesAsync<BulletinPlaybookEntity>(
                $"PartitionKey eq '{current.PartitionKey}' and RowKey ge '0000000000' and " +
                $"RowKey le '{VersionedLibraryStorage.VersionRowKey(current.CurrentVersion)}'",
                cancellationToken);
            foreach (var row in rows)
            {
                var version = DeserializeVersion(row.VersionJson);
                if (version is not null)
                    versions.Add(version);
            }
            versions = versions.GroupBy(v => v.Version).Select(g => g.First()).OrderBy(v => v.Version).ToList();
            if (current.VersionCount > 0 && versions.Count < current.VersionCount)
                return Option<BulletinPlaybook>.None;
        }
        var assigned = SafeJson.Deserialize<List<string>>(
            current.AssignedStationsJson, _logger, $"bulletin playbook {current.Id} assignedStations", JsonOptions) ?? [];
        return Option<BulletinPlaybook>.Some(BulletinPlaybook.Reconstitute(
            current.Id ?? string.Empty, current.Name ?? string.Empty, current.Description,
            current.Role ?? string.Empty, current.Status ?? "active", current.CreatedAt.UtcDateTime,
            current.UpdatedAt.UtcDateTime, current.CreatedBy, assigned, versions));
    }

    private async Task<Result<Option<BulletinPlaybook>>> GetLegacyAsync(string id, CancellationToken cancellationToken)
    {
        var result = await GetEntityOrDefaultAsync<BulletinPlaybookEntity>(LegacyPartitionKey, id, cancellationToken);
        if (result.IsFailure)
            return Result<Option<BulletinPlaybook>>.Failure(result.Error);
        if (result.Value.IsNone)
            return Result<Option<BulletinPlaybook>>.Success(Option<BulletinPlaybook>.None);
        var playbook = MapLegacy(result.Value.Value);
        return playbook is null
            ? Result<Option<BulletinPlaybook>>.Success(Option<BulletinPlaybook>.None)
            : Result<Option<BulletinPlaybook>>.Success(Option<BulletinPlaybook>.Some(playbook));
    }

    private BulletinPlaybook? MapLegacy(BulletinPlaybookEntity entity)
    {
        var versions = SafeJson.Deserialize<List<BulletinPlaybookVersion>>(
            entity.VersionsJson, _logger, $"bulletin playbook {entity.RowKey} versions", JsonOptions);
        if (versions is null || versions.Count == 0)
        {
            _logger.LogError("Skipping corrupt bulletin playbook {RowKey}: no versions", entity.RowKey);
            return null;
        }
        var assigned = SafeJson.Deserialize<List<string>>(
            entity.AssignedStationsJson, _logger, $"bulletin playbook {entity.RowKey} assignedStations", JsonOptions) ?? [];
        return BulletinPlaybook.Reconstitute(
            entity.RowKey ?? string.Empty, entity.Name ?? string.Empty, entity.Description,
            entity.Role ?? string.Empty, entity.Status ?? "active", entity.CreatedAt.UtcDateTime,
            entity.UpdatedAt.UtcDateTime, entity.CreatedBy, assigned, versions);
    }

    private static BulletinPlaybook CurrentOnly(BulletinPlaybook playbook) =>
        BulletinPlaybook.Reconstitute(playbook.Id, playbook.Name, playbook.Description, playbook.Role,
            playbook.Status, playbook.CreatedAt, playbook.UpdatedAt, playbook.CreatedBy,
            playbook.AssignedStations, [playbook.Versions[^1]]);

    private static BulletinPlaybookEntity MapLegacyEntity(BulletinPlaybook playbook, string versionsJson) =>
        new()
        {
            PartitionKey = LegacyPartitionKey,
            RowKey = playbook.Id,
            Id = playbook.Id,
            Name = playbook.Name,
            Description = playbook.Description,
            Role = playbook.Role,
            Status = playbook.Status,
            CurrentVersion = playbook.CurrentVersion,
            VersionCount = playbook.Versions.Count,
            CreatedBy = playbook.CreatedBy,
            AssignedStationsJson = JsonSerializer.Serialize(playbook.AssignedStations, JsonOptions),
            VersionsJson = versionsJson,
            CreatedAt = playbook.CreatedAt,
            UpdatedAt = playbook.UpdatedAt,
            RowKind = "legacy"
        };

    private static BulletinPlaybookEntity MapCurrentEntity(BulletinPlaybook playbook, string versionJson) =>
        new()
        {
            PartitionKey = VersionedLibraryStorage.PartitionKey(playbook.Id),
            RowKey = VersionedLibraryStorage.CurrentRowKey,
            Id = playbook.Id,
            Name = playbook.Name,
            Description = playbook.Description,
            Role = playbook.Role,
            Status = playbook.Status,
            CurrentVersion = playbook.CurrentVersion,
            VersionCount = playbook.Versions.Count,
            CreatedBy = playbook.CreatedBy,
            AssignedStationsJson = JsonSerializer.Serialize(playbook.AssignedStations, JsonOptions),
            VersionJson = versionJson,
            CreatedAt = playbook.CreatedAt,
            UpdatedAt = playbook.UpdatedAt,
            RowKind = VersionedLibraryStorage.CurrentRowKind
        };

    private static BulletinPlaybookEntity MapVersionEntity(
        BulletinPlaybook playbook, BulletinPlaybookVersion version, string versionJson) =>
        new()
        {
            PartitionKey = VersionedLibraryStorage.PartitionKey(playbook.Id),
            RowKey = VersionedLibraryStorage.VersionRowKey(version.Version),
            Id = playbook.Id,
            CurrentVersion = playbook.CurrentVersion,
            VersionCount = playbook.Versions.Count,
            VersionJson = versionJson,
            RowKind = VersionedLibraryStorage.VersionRowKind,
            CreatedAt = version.CreatedAt,
            UpdatedAt = version.CreatedAt
        };

    private static BulletinPlaybookVersion? DeserializeVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<BulletinPlaybookVersion>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task DeleteIfPresentAsync(string partitionKey, string rowKey, CancellationToken cancellationToken)
    {
        try
        {
            await TableClient.DeleteEntityAsync(partitionKey, rowKey, ETag.All, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Idempotent delete; service-level reference checks own the user-facing guard.
        }
    }

    internal sealed class BulletinPlaybookEntity : ITableEntity
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Role { get; set; }
        public string? Status { get; set; }
        public string RowKind { get; set; } = "legacy";
        public int CurrentVersion { get; set; }
        public int VersionCount { get; set; }
        public string? CreatedBy { get; set; }
        public string? AssignedStationsJson { get; set; }
        public string VersionsJson { get; set; } = "[]";
        public string? VersionJson { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

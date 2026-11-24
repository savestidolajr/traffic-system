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
///     Azure Table persistence for bookend pairs. New writes use a compact current row and
///     immutable per-version rows under <c>PK=Library_{id}</c>; legacy aggregate rows remain in
///     place for dual-read/dual-write rollback.
/// </summary>
public sealed class TableStorageBookendRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageBookendRepository> logger,
    IOptions<VersionedLibraryOptions>? libraryOptions = null,
    IMemoryCache? cache = null)
    : TableRepositoryBase(TableName, options, credential, logger), IBookendRepository
{
    private const string TableName = "radioBookends";
    private const string LegacyPartitionKey = "Bookend";
    private const string CacheKey = "VersionedLibrary:Bookends:Current";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    private readonly ILogger<TableStorageBookendRepository> _logger = logger;
    private readonly VersionedLibraryOptions _libraryOptions = libraryOptions?.Value ?? new();
    private readonly IMemoryCache? _cache = cache;

    public async Task<Result<VersionedLibraryListPage<Bookend>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default)
    {
        var valid = VersionedLibraryStorage.Validate(query);
        if (valid.IsFailure)
            return Result<VersionedLibraryListPage<Bookend>>.Failure(valid.Error);

        bool summary = !query.IncludeHistory;
        bool canUseSummary = summary && _libraryOptions.SummaryListsEnabled;
        if (canUseSummary && !query.IsPaged
            && _cache?.TryGetValue(CacheKey, out IReadOnlyList<Bookend>? cached) == true
            && cached is not null)
            return Result<VersionedLibraryListPage<Bookend>>.Success(
                VersionedLibraryStorage.Page(cached, query));

        var loaded = await ExecuteAsync(() => LoadLibraryAsync(query.IncludeHistory, canUseSummary, cancellationToken));
        if (loaded.IsFailure)
            return loaded.PropagateFailure<VersionedLibraryListPage<Bookend>>();

        var ordered = loaded.Value.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (canUseSummary && !query.IsPaged)
            _cache?.Set(CacheKey, (IReadOnlyList<Bookend>)ordered,
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = _libraryOptions.SummaryCacheTtl });

        return Result<VersionedLibraryListPage<Bookend>>.Success(
            VersionedLibraryStorage.Page(ordered, query));
    }

    public async Task<Result<IReadOnlyList<Bookend>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var page = await ListAsync(VersionedLibraryListQuery.FullHistoryDefault, cancellationToken);
        return page.IsFailure
            ? page.PropagateFailure<IReadOnlyList<Bookend>>()
            : Result<IReadOnlyList<Bookend>>.Success(page.Value.Items);
    }

    public async Task<Result<IReadOnlyDictionary<string, Option<Bookend>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        await VersionedLibraryStorage.GetManyAsync(
            ids, includeHistory, _libraryOptions.BulkReadConcurrency,
            (id, history, ct) => GetByIdAsync(id, history, ct), cancellationToken);

    public Task<Result<Option<Bookend>>> GetAsync(
        string id, CancellationToken cancellationToken = default) =>
        GetByIdAsync(id, includeHistory: true, cancellationToken: cancellationToken);

    private async Task<Result<Option<Bookend>>> GetByIdAsync(
        string id, bool includeHistory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
            return Result<Option<Bookend>>.Success(Option<Bookend>.None);

        if (_libraryOptions.NormalizedStorageEnabled)
        {
            var normalized = await GetNormalizedAsync(id, includeHistory, cancellationToken);
            if (normalized.IsFailure)
                return normalized;
            if (normalized.Value.HasValue)
                return normalized;
        }

        if (!_libraryOptions.LegacyReadEnabled)
            return Result<Option<Bookend>>.Success(Option<Bookend>.None);
        return await GetLegacyAsync(id, cancellationToken);
    }

    public async Task<Result> SaveAsync(Bookend bookend, CancellationToken cancellationToken = default)
    {
        if (!VersionHistoryTrimmer.TrySerialize(
                bookend.Versions, v => v.Version, _logger, "Bookend", bookend.Id,
                out string legacyVersionsJson, out var failure))
            return Result.Failure(failure!);

        var versionEntities = new List<BookendEntity>(bookend.Versions.Count);
        foreach (var version in bookend.Versions)
        {
            string versionJson = JsonSerializer.Serialize(version, JsonOptions);
            if (versionJson.Length > VersionHistoryTrimmer.PropertyCeiling)
                return Result.Failure(OperationalFailure.InvalidInput(
                    "versionhistory.entry_too_large",
                    $"This bookend is too large to save — version {version.Version} exceeds the " +
                    $"{VersionHistoryTrimmer.PropertyCeiling:N0}-character limit."));
            versionEntities.Add(MapVersionEntity(bookend, version, versionJson));
        }

        string currentJson = JsonSerializer.Serialize(bookend.Versions[^1], JsonOptions);
        var currentEntity = MapCurrentEntity(bookend, currentJson);
        var legacyEntity = MapLegacyEntity(bookend, legacyVersionsJson);
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
                var versions = await QueryEntitiesAsync<BookendEntity>(
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

    private async Task<List<Bookend>> LoadLibraryAsync(
        bool includeHistory, bool summary, CancellationToken cancellationToken)
    {
        var byId = new Dictionary<string, Bookend>(StringComparer.Ordinal);
        if (_libraryOptions.NormalizedStorageEnabled && (summary || _libraryOptions.FullAggregateReadEnabled))
        {
            var currents = await QueryEntitiesAsync<BookendEntity>(
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

        var legacyEntities = await QueryEntitiesAsync<BookendEntity>(
            $"PartitionKey eq '{LegacyPartitionKey}'", cancellationToken);
        foreach (var entity in legacyEntities)
        {
            var bookend = MapLegacy(entity);
            if (bookend is null || byId.ContainsKey(bookend.Id))
                continue;
            byId[bookend.Id] = includeHistory || !summary ? bookend : CurrentOnly(bookend);
        }
        return byId.Values.ToList();
    }

    private async Task<Result<Option<Bookend>>> GetNormalizedAsync(
        string id, bool includeHistory, CancellationToken cancellationToken)
    {
        var result = await GetEntityOrDefaultAsync<BookendEntity>(
            VersionedLibraryStorage.PartitionKey(id), VersionedLibraryStorage.CurrentRowKey, cancellationToken);
        if (result.IsFailure)
            return Result<Option<Bookend>>.Failure(result.Error);
        if (result.Value.IsNone)
            return Result<Option<Bookend>>.Success(Option<Bookend>.None);
        var mapped = await MapNormalizedAsync(result.Value.Value, includeHistory, cancellationToken);
        return mapped.HasValue
            ? Result<Option<Bookend>>.Success(mapped)
            : Result<Option<Bookend>>.Success(Option<Bookend>.None);
    }

    private async Task<Option<Bookend>> MapNormalizedAsync(
        BookendEntity current, bool includeHistory, CancellationToken cancellationToken)
    {
        var latest = DeserializeVersion(current.VersionJson);
        if (latest is null || latest.Version != current.CurrentVersion)
            return Option<Bookend>.None;
        var versions = new List<BookendVersion> { latest };
        if (includeHistory)
        {
            var rows = await QueryEntitiesAsync<BookendEntity>(
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
                return Option<Bookend>.None;
        }
        var assigned = SafeJson.Deserialize<List<string>>(
            current.AssignedStationsJson, _logger, $"bookend {current.Id} assignedStations", JsonOptions) ?? [];
        return Option<Bookend>.Some(Bookend.Reconstitute(
            current.Id ?? string.Empty, current.Name ?? string.Empty, current.Description,
            string.IsNullOrEmpty(current.Status) ? "active" : current.Status,
            current.CreatedAt.UtcDateTime, current.UpdatedAt.UtcDateTime, current.CreatedBy, assigned, versions));
    }

    private async Task<Result<Option<Bookend>>> GetLegacyAsync(string id, CancellationToken cancellationToken)
    {
        var result = await GetEntityOrDefaultAsync<BookendEntity>(LegacyPartitionKey, id, cancellationToken);
        if (result.IsFailure)
            return Result<Option<Bookend>>.Failure(result.Error);
        if (result.Value.IsNone)
            return Result<Option<Bookend>>.Success(Option<Bookend>.None);
        var bookend = MapLegacy(result.Value.Value);
        return bookend is null
            ? Result<Option<Bookend>>.Success(Option<Bookend>.None)
            : Result<Option<Bookend>>.Success(Option<Bookend>.Some(bookend));
    }

    private Bookend? MapLegacy(BookendEntity entity)
    {
        var versions = SafeJson.Deserialize<List<BookendVersion>>(
            entity.VersionsJson, _logger, $"bookend {entity.RowKey} versions", JsonOptions);
        if (versions is null || versions.Count == 0)
        {
            _logger.LogError("Skipping corrupt bookend {RowKey}: no versions", entity.RowKey);
            return null;
        }
        var assigned = SafeJson.Deserialize<List<string>>(
            entity.AssignedStationsJson, _logger, $"bookend {entity.RowKey} assignedStations", JsonOptions) ?? [];
        return Bookend.Reconstitute(
            entity.RowKey ?? string.Empty, entity.Name ?? string.Empty, entity.Description,
            string.IsNullOrEmpty(entity.Status) ? "active" : entity.Status,
            entity.CreatedAt.UtcDateTime, entity.UpdatedAt.UtcDateTime, entity.CreatedBy, assigned, versions);
    }

    private static Bookend CurrentOnly(Bookend bookend) =>
        Bookend.Reconstitute(bookend.Id, bookend.Name, bookend.Description, bookend.Status, bookend.CreatedAt,
            bookend.UpdatedAt, bookend.CreatedBy, bookend.AssignedStations, [bookend.Versions[^1]]);

    private static BookendEntity MapLegacyEntity(Bookend bookend, string versionsJson) =>
        new()
        {
            PartitionKey = LegacyPartitionKey,
            RowKey = bookend.Id,
            Id = bookend.Id,
            Name = bookend.Name,
            Description = bookend.Description,
            Status = bookend.Status,
            CurrentVersion = bookend.CurrentVersion,
            VersionCount = bookend.Versions.Count,
            CreatedBy = bookend.CreatedBy,
            AssignedStationsJson = JsonSerializer.Serialize(bookend.AssignedStations, JsonOptions),
            VersionsJson = versionsJson,
            CreatedAt = bookend.CreatedAt,
            UpdatedAt = bookend.UpdatedAt,
            RowKind = "legacy"
        };

    private static BookendEntity MapCurrentEntity(Bookend bookend, string versionJson) =>
        new()
        {
            PartitionKey = VersionedLibraryStorage.PartitionKey(bookend.Id),
            RowKey = VersionedLibraryStorage.CurrentRowKey,
            Id = bookend.Id,
            Name = bookend.Name,
            Description = bookend.Description,
            Status = bookend.Status,
            CurrentVersion = bookend.CurrentVersion,
            VersionCount = bookend.Versions.Count,
            CreatedBy = bookend.CreatedBy,
            AssignedStationsJson = JsonSerializer.Serialize(bookend.AssignedStations, JsonOptions),
            VersionJson = versionJson,
            CreatedAt = bookend.CreatedAt,
            UpdatedAt = bookend.UpdatedAt,
            RowKind = VersionedLibraryStorage.CurrentRowKind
        };

    private static BookendEntity MapVersionEntity(Bookend bookend, BookendVersion version, string versionJson) =>
        new()
        {
            PartitionKey = VersionedLibraryStorage.PartitionKey(bookend.Id),
            RowKey = VersionedLibraryStorage.VersionRowKey(version.Version),
            Id = bookend.Id,
            CurrentVersion = bookend.CurrentVersion,
            VersionCount = bookend.Versions.Count,
            VersionJson = versionJson,
            RowKind = VersionedLibraryStorage.VersionRowKind,
            CreatedAt = version.CreatedAt,
            UpdatedAt = version.CreatedAt
        };

    private static BookendVersion? DeserializeVersion(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<BookendVersion>(json, JsonOptions);
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

    internal sealed class BookendEntity : ITableEntity
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string Status { get; set; } = "active";
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

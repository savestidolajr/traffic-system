using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
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

// Partition key: PK = "PronunciationDictionary" (all dictionaries share one partition —
// admin-managed, low volume). Row key: library id. The owned ElevenLabs dictionary id is a
// column; versions (each carrying its ElevenLabs version id + rule snapshot) and assigned
// stations are JSON columns, so the whole aggregate round-trips in a single entity (mirrors the
// delivery-style / voice-profile repositories).

public sealed class TableStoragePronunciationDictionaryRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStoragePronunciationDictionaryRepository> logger)
    : TableRepositoryBase(TableName, options, credential, logger), IPronunciationDictionaryRepository
{
    private const string TableName = "radioPronunciationDictionaries";
    private const string PK = "PronunciationDictionary";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    private readonly ILogger<TableStoragePronunciationDictionaryRepository> _logger = logger;

    public async Task<Result<VersionedLibraryListPage<PronunciationDictionary>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default)
    {
        var valid = VersionedLibraryStorage.Validate(query);
        if (valid.IsFailure)
            return Result<VersionedLibraryListPage<PronunciationDictionary>>.Failure(valid.Error);

        var listed = await ListAsync(cancellationToken);
        return listed.IsFailure
            ? listed.PropagateFailure<VersionedLibraryListPage<PronunciationDictionary>>()
            : Result<VersionedLibraryListPage<PronunciationDictionary>>.Success(
                VersionedLibraryStorage.Page(listed.Value, query));
    }

    public Task<Result<IReadOnlyList<PronunciationDictionary>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<PronunciationDictionaryEntity>(
                $"PartitionKey eq '{PK}'", cancellationToken);
            IReadOnlyList<PronunciationDictionary> dictionaries = entities
                .Select(MapToDomain)
                .Where(d => d is not null)
                .Cast<PronunciationDictionary>()
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return dictionaries;
        });
    }

    public Task<Result<IReadOnlyDictionary<string, Option<PronunciationDictionary>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        VersionedLibraryStorage.GetManyAsync(
            ids, includeHistory, 8,
            (id, _, ct) => GetAsync(id, ct), cancellationToken);

    public async Task<Result<Option<PronunciationDictionary>>> GetAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var result = await GetEntityOrDefaultAsync<PronunciationDictionaryEntity>(PK, id, cancellationToken);
        if (result.IsFailure)
            return Result<Option<PronunciationDictionary>>.Failure(result.Error);

        return result.Value.Match(
            entity =>
            {
                var dictionary = MapToDomain(entity);
                return Result<Option<PronunciationDictionary>>.Success(
                    dictionary is null
                        ? Option<PronunciationDictionary>.None
                        : Option<PronunciationDictionary>.Some(dictionary));
            },
            () => Result<Option<PronunciationDictionary>>.Success(Option<PronunciationDictionary>.None));
    }

    public async Task<Result> SaveAsync(
        PronunciationDictionary dictionary, CancellationToken cancellationToken = default)
    {
        // Keep the base repository's table-creation and unexpected-error handling, while
        // returning the typed conflict produced by the concurrency-aware save path.
        var wrapped = await ExecuteAsync(async () =>
            await SaveWithConcurrencyAsync(dictionary, cancellationToken));
        return wrapped.IsFailure ? Result.Failure(wrapped.Error) : wrapped.Value;
    }

    private async Task<Result> SaveWithConcurrencyAsync(
        PronunciationDictionary dictionary, CancellationToken cancellationToken)
    {
        // ElevenLabs still holds every published version, so a trimmed local snapshot loses the
        // audit copy of the rule set, never the pronunciation itself.
        if (!VersionHistoryTrimmer.TrySerialize(
                dictionary.Versions, v => v.Version, _logger, "Pronunciation dictionary", dictionary.Id,
                out string versionsJson, out var oversized))
            return Result.Failure(oversized!);

        var entity = MapToEntity(dictionary, versionsJson);

        if (string.IsNullOrEmpty(dictionary.ETag))
        {
            var created = await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
            // A fresh entity has no ETag; the post-write revision must be copied back so a
            // follow-up conditional edit off the create response does not conflict against the
            // write this very call just performed.
            if (!string.IsNullOrEmpty(created.Headers.ETag.ToString()))
                dictionary.ETag = created.Headers.ETag.ToString();
            return Result.Success();
        }

        // An existing aggregate must retain the revision it was loaded with. Retrying the
        // same conditional write is bounded (matching the other whole-list repositories) but
        // never refreshes the ETag and overwrites a concurrent rule edit.
        entity.ETag = new Azure.ETag(dictionary.ETag);
        const int maxRetries = 3;
        for (int attempt = 0; attempt < maxRetries; attempt++)
            try
            {
                var updated = await TableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace,
                    cancellationToken);
                // The response's ETag is the POST-write revision. A mutation response carries the
                // pre-write revision otherwise, so a client chaining a conditional edit off it gets
                // a spurious revision conflict (observed live on /jobs/{id}/pronunciation-rules).
                if (!string.IsNullOrEmpty(updated.Headers.ETag.ToString()))
                    dictionary.ETag = updated.Headers.ETag.ToString();
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                _logger.LogWarning(
                    "ETag conflict on SaveAsync for pronunciation dictionary {Id}, attempt {Attempt}. Retrying.",
                    dictionary.Id, attempt + 1);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                _logger.LogWarning(
                    "ETag conflict (retries exhausted) on SaveAsync for pronunciation dictionary {Id}.",
                    dictionary.Id);
                return Result.Failure(OperationalFailure.Conflict(
                    "pronunciation.revision_mismatch",
                    $"Conflict: the pronunciation dictionary '{dictionary.Id}' was modified by another request. " +
                    "Reload and try again."));
            }

        return Result.Failure(OperationalFailure.Conflict(
            "pronunciation.revision_mismatch",
            $"Conflict: the pronunciation dictionary '{dictionary.Id}' was modified by another request. " +
            "Reload and try again."));
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

    private PronunciationDictionary? MapToDomain(PronunciationDictionaryEntity e)
    {
        var versions = SafeJson.Deserialize<List<PronunciationDictionaryVersion>>(
            e.VersionsJson, _logger, $"pronunciation dictionary {e.RowKey} versions", JsonOptions);
        if (versions is null || versions.Count == 0)
        {
            _logger.LogError("Skipping corrupt pronunciation dictionary {RowKey}: no versions", e.RowKey);
            return null;
        }

        var assignedStations = SafeJson.Deserialize<List<string>>(
            e.AssignedStationsJson, _logger, $"pronunciation dictionary {e.RowKey} assignedStations", JsonOptions) ?? [];

        // A blank or unrecognised Scope reads back as Station — that is what every row written
        // before the platform-wide layer existed actually was.
        var scope = Enum.TryParse<PronunciationScope>(e.Scope, ignoreCase: true, out var parsedScope)
            ? parsedScope
            : PronunciationScope.Station;

        return PronunciationDictionary.Reconstitute(
            e.RowKey ?? string.Empty,
            e.DictionaryId ?? string.Empty,
            e.Name ?? string.Empty,
            e.Description,
            string.IsNullOrEmpty(e.Status) ? "active" : e.Status,
            e.CreatedAt.UtcDateTime,
            e.UpdatedAt.UtcDateTime,
            e.CreatedBy,
            assignedStations,
            versions,
            e.ETag.ToString(),
            scope);
    }

    private static PronunciationDictionaryEntity MapToEntity(
        PronunciationDictionary d, string versionsJson)
    {
        return new PronunciationDictionaryEntity
        {
            PartitionKey = PK,
            RowKey = d.Id,
            DictionaryId = d.DictionaryId,
            Name = d.Name,
            Description = d.Description,
            Status = d.Status,
            Scope = d.Scope.ToString(),
            CurrentVersion = d.CurrentVersion,
            CreatedBy = d.CreatedBy,
            AssignedStationsJson = JsonSerializer.Serialize(d.AssignedStations, JsonOptions),
            VersionsJson = versionsJson,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        };
    }

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class PronunciationDictionaryEntity : ITableEntity
    {
        public string DictionaryId { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public string Status { get; set; } = "active";
        public string? Scope { get; set; }
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

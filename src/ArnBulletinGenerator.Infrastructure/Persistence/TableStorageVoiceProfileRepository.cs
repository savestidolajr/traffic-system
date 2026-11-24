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

// Partition key: PK = "VoiceProfile" (all profiles share one partition — admin-managed, low volume).
// Row key: profile id. Versions + assigned stations are serialized as JSON columns (mirrors how
// Job stores its StatusHistory/Logs), so the whole aggregate round-trips in a single entity.

public sealed class TableStorageVoiceProfileRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageVoiceProfileRepository> logger)
    : TableRepositoryBase(TableName, options, credential, logger), IVoiceProfileRepository
{
    private const string TableName = "radioVoiceProfiles";
    private const string PK = "VoiceProfile";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    private readonly ILogger<TableStorageVoiceProfileRepository> _logger = logger;

    public async Task<Result<VersionedLibraryListPage<VoiceProfile>>> ListAsync(
        VersionedLibraryListQuery query, CancellationToken cancellationToken = default)
    {
        var valid = VersionedLibraryStorage.Validate(query);
        if (valid.IsFailure)
            return Result<VersionedLibraryListPage<VoiceProfile>>.Failure(valid.Error);

        var listed = await ListAsync(cancellationToken);
        return listed.IsFailure
            ? listed.PropagateFailure<VersionedLibraryListPage<VoiceProfile>>()
            : Result<VersionedLibraryListPage<VoiceProfile>>.Success(
                VersionedLibraryStorage.Page(listed.Value, query));
    }

    public Task<Result<IReadOnlyList<VoiceProfile>>> ListAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<VoiceProfileEntity>($"PartitionKey eq '{PK}'", cancellationToken);
            IReadOnlyList<VoiceProfile> profiles = entities
                .Select(MapToDomain)
                .Where(p => p is not null)
                .Cast<VoiceProfile>()
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return profiles;
        });
    }

    public Task<Result<IReadOnlyDictionary<string, Option<VoiceProfile>>>> GetManyAsync(
        IReadOnlyCollection<string> ids, bool includeHistory = false,
        CancellationToken cancellationToken = default) =>
        VersionedLibraryStorage.GetManyAsync(
            ids, includeHistory, 8,
            (id, _, ct) => GetAsync(id, ct), cancellationToken);

    public async Task<Result<Option<VoiceProfile>>> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var result = await GetEntityOrDefaultAsync<VoiceProfileEntity>(PK, id, cancellationToken);
        if (result.IsFailure)
            return Result<Option<VoiceProfile>>.Failure(result.Error);

        return result.Value.Match(
            entity =>
            {
                var profile = MapToDomain(entity);
                return Result<Option<VoiceProfile>>.Success(
                    profile is null ? Option<VoiceProfile>.None : Option<VoiceProfile>.Some(profile));
            },
            () => Result<Option<VoiceProfile>>.Success(Option<VoiceProfile>.None));
    }

    public Task<Result> SaveAsync(VoiceProfile profile, CancellationToken cancellationToken = default)
    {
        if (!VersionHistoryTrimmer.TrySerialize(
                profile.Versions, v => v.Version, _logger, "Voice profile", profile.Id,
                out string versionsJson, out var failure))
            return Task.FromResult(Result.Failure(failure!));

        return ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(
                MapToEntity(profile, versionsJson), TableUpdateMode.Replace, cancellationToken);
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

    private VoiceProfile? MapToDomain(VoiceProfileEntity e)
    {
        var versions = SafeJson.Deserialize<List<VoiceProfileVersion>>(
            e.VersionsJson, _logger, $"voice profile {e.RowKey} versions", JsonOptions);
        if (versions is null || versions.Count == 0)
        {
            // A profile with no versions is unusable (current version resolves from the latest);
            // skip it rather than surfacing a half-broken record to the library.
            _logger.LogError("Skipping corrupt voice profile {RowKey}: no versions", e.RowKey);
            return null;
        }

        var assignedStations = SafeJson.Deserialize<List<string>>(
            e.AssignedStationsJson, _logger, $"voice profile {e.RowKey} assignedStations", JsonOptions) ?? [];

        return VoiceProfile.Reconstitute(
            e.RowKey ?? string.Empty,
            e.Name ?? string.Empty,
            e.Description,
            e.BaseVoiceId ?? string.Empty,
            e.BaseVoiceName ?? string.Empty,
            string.IsNullOrEmpty(e.Provider) ? "elevenlabs" : e.Provider,
            string.IsNullOrEmpty(e.Status) ? "active" : e.Status,
            e.CreatedAt.UtcDateTime,
            e.UpdatedAt.UtcDateTime,
            e.CreatedBy,
            assignedStations,
            versions);
    }

    private static VoiceProfileEntity MapToEntity(VoiceProfile p, string versionsJson)
    {
        return new VoiceProfileEntity
        {
            PartitionKey = PK,
            RowKey = p.Id,
            Name = p.Name,
            Description = p.Description,
            BaseVoiceId = p.BaseVoiceId,
            BaseVoiceName = p.BaseVoiceName,
            Provider = p.Provider,
            Status = p.Status,
            CurrentVersion = p.CurrentVersion,
            CreatedBy = p.CreatedBy,
            AssignedStationsJson = JsonSerializer.Serialize(p.AssignedStations, JsonOptions),
            VersionsJson = versionsJson,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        };
    }

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class VoiceProfileEntity : ITableEntity
    {
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public string BaseVoiceId { get; set; } = default!;
        public string BaseVoiceName { get; set; } = default!;
        public string Provider { get; set; } = "elevenlabs";
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

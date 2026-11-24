using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// Partition key: "EditorialSettings". Row key: the scope ("Global", "Market:sydney",
// "Station:kiis-90s"). Flat columns rather than a JSON blob — every numeric setting field is nullable,
// so the table's own typing carries them and an operator reading the table sees the values directly.
// DisabledTrafficSources is the one exception: Table storage has no collection column type, so it
// rides a JSON string exactly as FillOrderJson and ContentSelectionJson do elsewhere. Absent or
// unparseable reads back as null ("inherit"), never as an empty denylist, because silently promoting
// a corrupt row to "disable nothing" and to "inherit nothing" are different lies.

public sealed class TableStorageEditorialSettingsRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageEditorialSettingsRepository> logger)
    : TableRepositoryBase(TableName, options, credential, logger), IEditorialSettingsRepository
{
    private const string TableName = "editorialSettings";
    private const string PK = "EditorialSettings";

    public async Task<Result<IReadOnlyList<EditorialSettings>>> GetApplicableAsync(
        string market, Option<string> station, CancellationToken cancellationToken = default)
    {
        var rowKeys = new List<string> { RowKeyFor(EditorialScope.Global, string.Empty) };
        rowKeys.Add(RowKeyFor(EditorialScope.Market, market));
        if (station.HasValue)
            rowKeys.Add(RowKeyFor(EditorialScope.Station, station.Value));

        string filter = $"PartitionKey eq '{PK}' and (" +
                        string.Join(" or ", rowKeys.Select(k => $"RowKey eq '{Escape(k)}'")) + ")";

        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<EditorialSettingsEntity>(filter, cancellationToken);
            return entities
                .Select(MapToDomain)
                .Where(s => s is not null)
                .Select(s => s!)
                .ToList() as IReadOnlyList<EditorialSettings>;
        });
    }

    public async Task<Result<EditorialSettings?>> GetAsync(
        EditorialScope scope, string scopeId, CancellationToken cancellationToken = default)
    {
        var result = await GetEntityOrDefaultAsync<EditorialSettingsEntity>(
            PK, RowKeyFor(scope, scopeId), cancellationToken);
        if (result.IsFailure)
            return Result<EditorialSettings?>.Failure(result.Error);

        return result.Value.Match(
            entity => Result<EditorialSettings?>.Success(MapToDomain(entity)),
            () => Result<EditorialSettings?>.Success(null));
    }

    public Task<Result> SaveAsync(EditorialSettings settings, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(
                MapToEntity(settings), TableUpdateMode.Replace, cancellationToken);
        });
    }

    public Task<Result> DeleteAsync(
        EditorialScope scope, string scopeId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            await TableClient.DeleteEntityAsync(
                PK, RowKeyFor(scope, scopeId), cancellationToken: cancellationToken);
        });
    }

    // ─── Mapping ─────────────────────────────────────────────────────

    private EditorialSettings? MapToDomain(EditorialSettingsEntity e)
    {
        if (!TryParseRowKey(e.RowKey, out var scope, out string scopeId))
        {
            logger.LogError("Ignoring editorial settings row with unparseable scope key {RowKey}", e.RowKey);
            return null;
        }

        return new EditorialSettings(
            scope,
            scopeId,
            e.DriveTimeHeadlineThresholdMinutes,
            e.MaxCorridors,
            e.MajorTierCap,
            e.ModerateTierCap,
            e.MinorTierCap,
            e.ContinuityNewCap,
            e.ContinuityOngoingCap,
            e.ContinuityClearedCap,
            e.ContinuityUpdatedCap,
            e.GuidanceMaxDirectives,
            e.CreatedAt.UtcDateTime,
            e.UpdatedAt.UtcDateTime,
            e.UpdatedBy,
            e.MaxSlowDownFactor,
            e.MaxSpeedUpFactor,
            e.MaxSilencePadSeconds,
            e.MaxSafeOverrunTrimSeconds,
            DeserializeDisabledSources(e.DisabledTrafficSourcesJson, e.RowKey));
    }

    private static EditorialSettingsEntity MapToEntity(EditorialSettings s) => new()
    {
        PartitionKey = PK,
        RowKey = RowKeyFor(s.Scope, s.ScopeId),
        DriveTimeHeadlineThresholdMinutes = s.DriveTimeHeadlineThresholdMinutes,
        MaxCorridors = s.MaxCorridors,
        MajorTierCap = s.MajorTierCap,
        ModerateTierCap = s.ModerateTierCap,
        MinorTierCap = s.MinorTierCap,
        ContinuityNewCap = s.ContinuityNewCap,
        ContinuityOngoingCap = s.ContinuityOngoingCap,
        ContinuityClearedCap = s.ContinuityClearedCap,
        ContinuityUpdatedCap = s.ContinuityUpdatedCap,
        GuidanceMaxDirectives = s.GuidanceMaxDirectives,
        MaxSlowDownFactor = s.MaxSlowDownFactor,
        MaxSpeedUpFactor = s.MaxSpeedUpFactor,
        MaxSilencePadSeconds = s.MaxSilencePadSeconds,
        MaxSafeOverrunTrimSeconds = s.MaxSafeOverrunTrimSeconds,
        DisabledTrafficSourcesJson = s.NormalizeDisabledTrafficSources() is { } disabled
            ? JsonSerializer.Serialize(disabled)
            : null,
        CreatedAt = s.CreatedAtUtc == default ? DateTimeOffset.UtcNow : s.CreatedAtUtc,
        UpdatedAt = s.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : s.UpdatedAtUtc,
        UpdatedBy = s.UpdatedBy
    };

    /// <summary>
    ///     Reads the denylist column. A malformed value is logged and treated as unset rather than
    ///     failing the whole settings read: an unreadable editorial row must not take a station off
    ///     air, and "inherit" is the behaviour that existed before the column did.
    /// </summary>
    private IReadOnlyList<string>? DeserializeDisabledSources(string? json, string? rowKey)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(json);
            if (parsed is null)
                return null;

            return OfficialTrafficSources.All
                .Where(known => parsed.Any(
                    stored => string.Equals(stored, known, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }
        catch (JsonException ex)
        {
            logger.LogError(ex,
                "Editorial settings row {RowKey} has an unparseable DisabledTrafficSourcesJson; treating as inherit",
                rowKey);
            return null;
        }
    }

    internal static string RowKeyFor(EditorialScope scope, string scopeId) =>
        scope == EditorialScope.Global
            ? nameof(EditorialScope.Global)
            : $"{scope}:{EditorialSettings.NormalizeScopeId(scope, scopeId)}";

    private static bool TryParseRowKey(string? rowKey, out EditorialScope scope, out string scopeId)
    {
        scope = EditorialScope.Global;
        scopeId = string.Empty;
        if (string.IsNullOrWhiteSpace(rowKey))
            return false;
        if (string.Equals(rowKey, nameof(EditorialScope.Global), StringComparison.Ordinal))
            return true;

        int separator = rowKey.IndexOf(':');
        if (separator <= 0 || separator == rowKey.Length - 1)
            return false;
        if (!Enum.TryParse(rowKey[..separator], out scope))
            return false;

        scopeId = rowKey[(separator + 1)..];
        return scope != EditorialScope.Global;
    }

    private static string Escape(string value) => value.Replace("'", "''");

    // ─── Entity ──────────────────────────────────────────────────────

    internal sealed class EditorialSettingsEntity : ITableEntity
    {
        public int? DriveTimeHeadlineThresholdMinutes { get; set; }
        public int? MaxCorridors { get; set; }
        public int? MajorTierCap { get; set; }
        public int? ModerateTierCap { get; set; }
        public int? MinorTierCap { get; set; }
        public int? ContinuityNewCap { get; set; }
        public int? ContinuityOngoingCap { get; set; }
        public int? ContinuityClearedCap { get; set; }
        public int? ContinuityUpdatedCap { get; set; }
        public int? GuidanceMaxDirectives { get; set; }
        public double? MaxSlowDownFactor { get; set; }
        public double? MaxSpeedUpFactor { get; set; }
        public double? MaxSilencePadSeconds { get; set; }
        public double? MaxSafeOverrunTrimSeconds { get; set; }
        public string? DisabledTrafficSourcesJson { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public Azure.ETag ETag { get; set; }
    }
}

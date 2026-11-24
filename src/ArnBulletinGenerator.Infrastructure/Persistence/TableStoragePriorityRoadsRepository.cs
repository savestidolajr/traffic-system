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

using ArnBulletinGenerator.Domain.ValueObjects;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// PartitionKey = "PriorityRoads" (single partition for all markets).
// RowKey format: {market} trimmed and lowercased — enables fast lookup by market.

public sealed class TableStoragePriorityRoadsRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStoragePriorityRoadsRepository> logger)
    : TableRepositoryBase("priorityRoads", options, credential, logger), IPriorityRoadsRepository
{
    private const string PK = "PriorityRoads";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    public async Task<Result<IReadOnlyList<PriorityRoad>>> GetRoadsAsync(
        string market, CancellationToken cancellationToken = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<PriorityRoadsEntity>(
            PK, NormalizeKey(market), cancellationToken);

        if (entityResult.IsFailure)
            return Result<IReadOnlyList<PriorityRoad>>.Failure(entityResult.Error);

        return entityResult.Value.Match(
            e => Result<IReadOnlyList<PriorityRoad>>.Success(
                ParseRoads(e.RoadsJson, $"priority roads {market}")),
            () => Result<IReadOnlyList<PriorityRoad>>.Success([]));
    }

    public async Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<PriorityRoadsEntity>(null, cancellationToken);
            return entities.Select(e => e.RowKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                as IReadOnlyList<string>;
        });
    }

    public async Task<Result> SetRoadsAsync(
        string market, IReadOnlyList<PriorityRoad> roads, string updatedBy,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        var normalizedKey = NormalizeKey(market);

        for (int attempt = 0; attempt < maxRetries; attempt++)
            try
            {
                var response =
                    await TableClient.GetEntityAsync<PriorityRoadsEntity>(
                        PK, normalizedKey, cancellationToken: cancellationToken);
                var entity = response.Value;

                entity.RoadsJson = JsonSerializer.Serialize(roads, JsonOptions);
                entity.UpdatedBy = updatedBy;
                entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await TableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity doesn't exist yet — create it. A storage failure here must still
                // fail soft (the outer try's catch blocks do not cover this branch).
                try
                {
                    await TableClient.UpsertEntityAsync(new PriorityRoadsEntity
                    {
                        PartitionKey = PK,
                        RowKey = normalizedKey,
                        RoadsJson = JsonSerializer.Serialize(roads, JsonOptions),
                        UpdatedBy = updatedBy,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    }, TableUpdateMode.Replace, cancellationToken);
                    return Result.Success();
                }
                catch (RequestFailedException createEx)
                {
                    logger.LogError(createEx, "Create failed in SetRoadsAsync for market {Market}", market);
                    return Result.Failure(createEx.Message);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                logger.LogWarning("ETag conflict on SetRoadsAsync for market {Market}, attempt {Attempt}. Retrying.",
                    market, attempt + 1);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                logger.LogWarning(
                    "ETag conflict (retries exhausted) on SetRoadsAsync for market {Market}.", market);
                return Result.Failure(
                    "Conflict: the priority roads were modified by another request. Reload and try again.");
            }
            catch (RequestFailedException ex)
            {
                logger.LogError(ex, "Table Storage operation failed in SetRoadsAsync for market {Market}", market);
                return Result.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in SetRoadsAsync for market {Market}", market);
                return Result.Failure(ex.Message);
            }

        return Result.Failure(
            $"Failed to update priority roads for '{market}' after {maxRetries} attempts due to concurrent modifications");
    }

    private static string NormalizeKey(string market) => MarketKey.Normalize(market);

    /// <summary>
    ///     Tolerant read across three on-disk shapes: a bare string[] (oldest) → boost incidents +
    ///     flow tier High; typed entries with the legacy bool <c>BoostFlowing</c> → map to a tier
    ///     (false → None, else High); current entries with <c>FlowTier</c> → as-is.
    /// </summary>
    private IReadOnlyList<PriorityRoad> ParseRoads(string json, string ctx)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                    return doc.RootElement.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => (e.GetString() ?? string.Empty).Trim())
                        .Where(name => name.Length > 0)
                        .Select(name => new PriorityRoad(name, Enabled: true))
                        .ToList();
                break;
            }
            var stored = SafeJson.Deserialize<StoredRoad[]>(json, logger, ctx, JsonOptions) ?? [];
            return stored
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .Select(s => new PriorityRoad(
                    s.Name!.Trim(),
                    s.BoostIncidents ?? true,
                    s.FlowTier ?? (s.BoostFlowing == false ? FlowPriorityTier.None : FlowPriorityTier.High),
                    s.Enabled ?? true))
                .ToList();
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse priority roads for {Ctx}", ctx);
            return [];
        }
    }

    /// <summary>Tolerant read shape: accepts the current FlowTier and the legacy bool BoostFlowing.</summary>
    private sealed record StoredRoad(
        string? Name,
        bool? BoostIncidents = null,
        bool? BoostFlowing = null,
        FlowPriorityTier? FlowTier = null,
        bool? Enabled = null);

    internal sealed class PriorityRoadsEntity : ITableEntity
    {
        public string RoadsJson { get; set; } = "[]";
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

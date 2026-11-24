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

// PartitionKey = "FillerStatements" (single partition for all markets).
// RowKey format: {market} trimmed and lowercased — enables fast lookup by market.

public sealed class TableStorageFillerStatementsRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageFillerStatementsRepository> logger)
    : TableRepositoryBase("fillerStatements", options, credential, logger), IFillerStatementsRepository
{
    private const string PK = "FillerStatements";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    public async Task<Result<IReadOnlyList<FillerStatement>>> GetStatementsAsync(
        string market, CancellationToken cancellationToken = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<FillerStatementsEntity>(
            PK, NormalizeKey(market), cancellationToken);

        if (entityResult.IsFailure)
            return Result<IReadOnlyList<FillerStatement>>.Failure(entityResult.Error);

        return entityResult.Value.Match(
            e => Result<IReadOnlyList<FillerStatement>>.Success(
                ParseStatements(e.StatementsJson, $"filler statements {market}")),
            () => Result<IReadOnlyList<FillerStatement>>.Success([]));
    }

    public async Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<FillerStatementsEntity>(null, cancellationToken);
            return entities.Select(e => e.RowKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                as IReadOnlyList<string>;
        });
    }

    public async Task<Result> SetStatementsAsync(
        string market, IReadOnlyList<FillerStatement> statements, string updatedBy,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        var normalizedKey = NormalizeKey(market);

        for (int attempt = 0; attempt < maxRetries; attempt++)
            try
            {
                var response =
                    await TableClient.GetEntityAsync<FillerStatementsEntity>(
                        PK, normalizedKey, cancellationToken: cancellationToken);
                var entity = response.Value;

                entity.StatementsJson = JsonSerializer.Serialize(statements, JsonOptions);
                entity.UpdatedBy = updatedBy;
                entity.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await TableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
                return Result.Success();
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity doesn't exist yet — create it.
                try
                {
                    await TableClient.UpsertEntityAsync(new FillerStatementsEntity
                    {
                        PartitionKey = PK,
                        RowKey = normalizedKey,
                        StatementsJson = JsonSerializer.Serialize(statements, JsonOptions),
                        UpdatedBy = updatedBy,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    }, TableUpdateMode.Replace, cancellationToken);
                    return Result.Success();
                }
                catch (RequestFailedException createEx)
                {
                    logger.LogError(createEx, "Create failed in SetStatementsAsync for market {Market}", market);
                    return Result.Failure(createEx.Message);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                logger.LogWarning("ETag conflict on SetStatementsAsync for market {Market}, attempt {Attempt}. Retrying.",
                    market, attempt + 1);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                logger.LogWarning(
                    "ETag conflict (retries exhausted) on SetStatementsAsync for market {Market}.", market);
                return Result.Failure(
                    "Conflict: the filler statements were modified by another request. Reload and try again.");
            }
            catch (RequestFailedException ex)
            {
                logger.LogError(ex, "Table Storage operation failed in SetStatementsAsync for market {Market}", market);
                return Result.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in SetStatementsAsync for market {Market}", market);
                return Result.Failure(ex.Message);
            }

        return Result.Failure(
            $"Failed to update filler statements for '{market}' after {maxRetries} attempts due to concurrent modifications");
    }

    private static string NormalizeKey(string market) => MarketKey.Normalize(market);

    private IReadOnlyList<FillerStatement> ParseStatements(string json, string ctx)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        var stored = SafeJson.Deserialize<FillerStatement[]>(json, logger, ctx, JsonOptions);
        return stored ?? [];
    }

    internal sealed class FillerStatementsEntity : ITableEntity
    {
        public string StatementsJson { get; set; } = "[]";
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

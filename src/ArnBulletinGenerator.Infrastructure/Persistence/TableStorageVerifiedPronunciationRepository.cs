using System.Text.Json;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.ValueObjects;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Serialization;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// Operator "sounded fine" verdicts that mute a recurring audio-pre-review suspect word.
// PartitionKey = "VerifiedPronunciations" (single partition for all markets).
// RowKey = {market} trimmed and lowercased. The word list is one JSON column, mirroring
// TableStoragePriorityRoadsRepository — the list is small and always written as a whole.
public sealed class TableStorageVerifiedPronunciationRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageVerifiedPronunciationRepository> logger)
    : TableRepositoryBase("verifiedPronunciations", options, credential, logger), IVerifiedPronunciationRepository
{
    private const string PK = "VerifiedPronunciations";
    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Storage;

    public async Task<Result<IReadOnlyList<VerifiedPronunciation>>> GetForMarketAsync(
        string market, CancellationToken cancellationToken = default)
    {
        var entityResult = await GetEntityOrDefaultAsync<VerifiedPronunciationsEntity>(
            PK, NormalizeKey(market), cancellationToken);

        if (entityResult.IsFailure)
            return Result<IReadOnlyList<VerifiedPronunciation>>.Failure(entityResult.Error);

        return entityResult.Value.Match(
            e => Result<IReadOnlyList<VerifiedPronunciation>>.Success(Parse(e.WordsJson, market)),
            () => Result<IReadOnlyList<VerifiedPronunciation>>.Success([]));
    }

    public async Task<Result<IReadOnlyList<string>>> ListMarketsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<VerifiedPronunciationsEntity>(null, cancellationToken);
            return entities.Select(e => e.RowKey).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                as IReadOnlyList<string>;
        });
    }

    public async Task<Result> SetForMarketAsync(
        string market, IReadOnlyList<VerifiedPronunciation> words, string updatedBy,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 3;
        string normalizedKey = NormalizeKey(market);

        // Pre-flight: the whole list is ONE WordsJson property, and Azure Table caps a property
        // at 32,768 UTF-16 chars (PronunciationDictionary.StoragePropertyCeiling). Fully-valid
        // input can still exceed the wall (200 entries x (80-char word + 200-char note) ≈ 74K), so
        // serialise ONCE here and refuse with a typed InvalidInput before any storage call — the
        // eventual write would otherwise fail with an opaque 500. Same pre-flight shape the
        // versioned libraries use (VersionHistoryTrimmer / the pronunciation-dictionary service).
        string wordsJson = JsonSerializer.Serialize(words, JsonOptions);
        if (wordsJson.Length > PronunciationDictionary.StoragePropertyCeiling)
            return Result.Failure(OperationalFailure.InvalidInput(
                "verifiedpronunciations.list_too_large",
                $"This verified pronunciation list is too large to save — it is {wordsJson.Length:N0} " +
                $"characters against a {PronunciationDictionary.StoragePropertyCeiling:N0} limit. Remove or " +
                "shorten some entries and save again."));

        for (int attempt = 0; attempt < maxRetries; attempt++)
            try
            {
                var response = await TableClient.GetEntityAsync<VerifiedPronunciationsEntity>(
                    PK, normalizedKey, cancellationToken: cancellationToken);
                var entity = response.Value;

                entity.WordsJson = wordsJson;
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
                    await TableClient.UpsertEntityAsync(new VerifiedPronunciationsEntity
                    {
                        PartitionKey = PK,
                        RowKey = normalizedKey,
                        WordsJson = wordsJson,
                        UpdatedBy = updatedBy,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    }, TableUpdateMode.Replace, cancellationToken);
                    return Result.Success();
                }
                catch (RequestFailedException createEx)
                {
                    logger.LogError(createEx,
                        "Create failed in SetForMarketAsync for market {Market}", market);
                    return Result.Failure(createEx.Message);
                }
            }
            catch (RequestFailedException ex) when (ex.Status == 412 && attempt < maxRetries - 1)
            {
                logger.LogWarning(
                    "ETag conflict on SetForMarketAsync for market {Market}, attempt {Attempt}. Retrying.",
                    market, attempt + 1);
            }
            catch (RequestFailedException ex) when (ex.Status == 412)
            {
                logger.LogWarning(
                    "ETag conflict (retries exhausted) on SetForMarketAsync for market {Market}.", market);
                return Result.Failure(
                    "Conflict: the verified pronunciations were modified by another request. Reload and try again.");
            }
            catch (RequestFailedException ex)
            {
                logger.LogError(ex,
                    "Table Storage operation failed in SetForMarketAsync for market {Market}", market);
                return Result.Failure(ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in SetForMarketAsync for market {Market}", market);
                return Result.Failure(ex.Message);
            }

        return Result.Failure(
            $"Failed to update verified pronunciations for '{market}' after {maxRetries} attempts due to concurrent modifications");
    }

    private static string NormalizeKey(string market) => MarketKey.Normalize(market);

    /// <summary>
    ///     Fail-soft read: a corrupt/unreadable column yields an empty list rather than an
    ///     error, because a lost verdict only restores a warning chip — it never blocks a
    ///     bulletin (this store is off the generation path entirely).
    /// </summary>
    private IReadOnlyList<VerifiedPronunciation> Parse(string json, string market)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var stored = JsonSerializer.Deserialize<List<VerifiedPronunciation>>(json, JsonOptions);
            return stored?.Where(w => !string.IsNullOrWhiteSpace(w.Word)).ToList() ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "Unreadable verified-pronunciation list for market {Market}; treating as empty", market);
            return [];
        }
    }

    internal sealed class VerifiedPronunciationsEntity : ITableEntity
    {
        public string WordsJson { get; set; } = "[]";
        public string UpdatedBy { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

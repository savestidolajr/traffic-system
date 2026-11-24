using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.ValueObjects;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
/// Azure Table Storage implementation of the per-fetch parse coverage ledger. Rows are retained for
/// 180 days: at 96 polls/day this bounds a market to roughly 17,280 source rows while preserving a
/// useful trend window without allowing instrumentation to grow without limit.
/// </summary>
public sealed class TableStorageSourceParseReportRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageSourceParseReportRepository> logger)
    : TableRepositoryBase("sourceParseReports", options, credential, logger), ISourceParseReportRepository
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(180);
    private const int MaxListResults = 10_000;
    private const int MaxTablePropertyChars = 30_000;
    private const string GzipSentinel = "gz:";

    // PartitionKey = normalized market id. RowKey = UTC ticks (D19) followed by a stable source
    // hash. Ticks are fixed-width decimal and therefore lexicographically ordered. Both ends of a
    // coverage request are pushed into this key range so the storage service, rather than the
    // trigger, bounds the read.

    public async Task<Result> SaveAsync(
        string marketId,
        DateTimeOffset atUtc,
        IReadOnlyList<SourceParseReport> reports,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            string partitionKey = MarketKey.Normalize(marketId);
            DateTimeOffset timestamp = atUtc.ToUniversalTime();

            foreach (SourceParseReport report in reports)
            {
                var entity = new SourceParseReportEntity
                {
                    PartitionKey = partitionKey,
                    RowKey = CreateRowKey(timestamp, report.SourceId),
                    MarketId = marketId,
                    AtUtc = timestamp,
                    SourceId = report.SourceId,
                    SchemaVersion = report.SchemaVersion,
                    Records = report.Records,
                    FieldsJson = FitPayload(JsonSerializer.Serialize(report.Fields), "Fields", report.SourceId),
                    UnknownVocabularyJson = FitPayload(
                        JsonSerializer.Serialize(report.UnknownVocabulary), "UnknownVocabulary", report.SourceId)
                };
                await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
            }

            await DeleteExpiredAsync(partitionKey, timestamp - Retention, cancellationToken);
            return;
        });
    }

    /// <summary>
    /// Legacy since-only shape retained for internal source compatibility. The upper edge is the
    /// current UTC instant, so this overload never issues an unbounded partition query.
    /// </summary>
    public Task<Result<IReadOnlyList<StoredParseReport>>> ListAsync(
        string? marketId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        ListAsync(marketId, sinceUtc, DateTimeOffset.UtcNow, cancellationToken);

    public async Task<Result<IReadOnlyList<StoredParseReport>>> ListAsync(
        string? marketId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(marketId))
            return Result<IReadOnlyList<StoredParseReport>>.Failure(
                "A market or an explicit bounded market set is required.");

        return await ListAsync([marketId], fromUtc, toUtc, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<StoredParseReport>>> ListAsync(
        IReadOnlyList<string> marketIds,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default)
    {
        if (fromUtc > toUtc)
            return Result<IReadOnlyList<StoredParseReport>>.Failure("to must not be before from");

        var normalizedMarkets = marketIds
            .Where(market => !string.IsNullOrWhiteSpace(market))
            .Select(MarketKey.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedMarkets.Length == 0)
            return Result<IReadOnlyList<StoredParseReport>>.Success([]);

        return await ExecuteAsync<IReadOnlyList<StoredParseReport>>(async () =>
        {
            string partitionFilter = string.Join(" or ",
                normalizedMarkets.Select(market =>
                    TableClient.CreateQueryFilter($"PartitionKey eq {market}")));
            if (normalizedMarkets.Length > 1)
                partitionFilter = $"({partitionFilter})";

            string lowerBound = CreateBoundaryKey(fromUtc);
            string upperBound = CreateExclusiveUpperBoundaryKey(toUtc);
            string rowRange = TableClient.CreateQueryFilter(
                $"RowKey ge {lowerBound} and RowKey lt {upperBound}");
            string filter = $"{partitionFilter} and {rowRange}";

            // The request is bounded by explicit partitions and both time edges. Do not apply a
            // materialisation cap here: truncating a range would silently move trend/count values.
            var entities = await QueryPagedAsync(filter, cancellationToken);
            DateTimeOffset from = fromUtc.ToUniversalTime();
            DateTimeOffset to = toUtc.ToUniversalTime();
            return entities
                .Where(entity => entity.AtUtc >= from && entity.AtUtc <= to)
                .Select(MapToDomain)
                .OrderBy(report => report.AtUtc)
                .ThenBy(report => report.MarketId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(report => report.Report.SourceId, StringComparer.OrdinalIgnoreCase)
                .ToList() as IReadOnlyList<StoredParseReport>;
        });
    }

    private async Task DeleteExpiredAsync(
        string partitionKey,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        string filter = TableClient.CreateQueryFilter(
            $"PartitionKey eq {partitionKey} and RowKey lt {CreateBoundaryKey(cutoffUtc)}");
        var expired = await QueryPagedAsync(filter, cancellationToken, MaxListResults);
        foreach (SourceParseReportEntity[] batch in expired.Chunk(100))
        {
            var actions = batch
                .Select(entity => new TableTransactionAction(
                    TableTransactionActionType.Delete, entity))
                .ToList();
            try
            {
                await TableClient.SubmitTransactionAsync(actions, cancellationToken);
            }
            catch (RequestFailedException ex)
            {
                logger.LogWarning(ex, "[ParseCoverage] Batch delete failed for partition {PartitionKey}", partitionKey);
                throw;
            }
        }
    }

    private async Task<List<SourceParseReportEntity>> QueryPagedAsync(
        string? filter,
        CancellationToken cancellationToken,
        int? cap = null)
    {
        await EnsureTableExistsAsync(cancellationToken);
        var results = new List<SourceParseReportEntity>();
        var query = TableClient.QueryAsync<SourceParseReportEntity>(filter, cancellationToken: cancellationToken);
        await foreach (var page in query.AsPages())
        {
            foreach (SourceParseReportEntity entity in page.Values)
            {
                results.Add(entity);
                if (cap.HasValue && results.Count >= cap.Value)
                {
                    logger.LogWarning(
                        "[ParseCoverage] Query for {Filter} hit the {Cap}-row cleanup cap; retention cleanup will continue on a later save",
                        filter, cap.Value);
                    return results;
                }
            }
        }
        return results;
    }

    private static string CreateRowKey(DateTimeOffset atUtc, string sourceId)
    {
        long ticks = atUtc.UtcDateTime.Ticks;
        string sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(sourceId))).ToLowerInvariant()[..16];
        return $"{ticks:D19}-{sourceHash}";
    }

    private static string CreateBoundaryKey(DateTimeOffset atUtc) =>
        atUtc.ToUniversalTime().UtcDateTime.Ticks.ToString("D19");

    private static string CreateExclusiveUpperBoundaryKey(DateTimeOffset atUtc)
    {
        DateTimeOffset utc = atUtc.ToUniversalTime();
        return utc.UtcDateTime.Ticks == DateTime.MaxValue.Ticks
            ? $"{DateTime.MaxValue.Ticks:D19}-\uFFFF"
            : CreateBoundaryKey(utc.AddTicks(1));
    }

    private string FitPayload(string json, string column, string sourceId)
    {
        if (json.Length <= MaxTablePropertyChars) return json;

        string compressed;
        using (var buffer = new MemoryStream())
        {
            using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
                gzip.Write(Encoding.UTF8.GetBytes(json));
            compressed = GzipSentinel + Convert.ToBase64String(buffer.ToArray());
        }

        if (compressed.Length <= MaxTablePropertyChars)
        {
            logger.LogInformation(
                "[ParseCoverage] {Column} for {SourceId} is {Length} chars; stored gzip-compressed ({Compressed} chars)",
                column, sourceId, json.Length, compressed.Length);
            return compressed;
        }

        throw new InvalidOperationException(
            $"Parse coverage {column} for source '{sourceId}' exceeds the Table Storage property limit even when compressed.");
    }

    private static string? ExpandPayload(string? stored)
    {
        if (stored is null || !stored.StartsWith(GzipSentinel, StringComparison.Ordinal)) return stored;
        using var gzip = new GZipStream(
            new MemoryStream(Convert.FromBase64String(stored[GzipSentinel.Length..])), CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private StoredParseReport MapToDomain(SourceParseReportEntity entity)
    {
        var fields = SafeJson.Deserialize<List<FieldCoverage>>(
            ExpandPayload(entity.FieldsJson), logger, $"parse report {entity.RowKey} fields") ?? [];
        var unknown = SafeJson.Deserialize<List<string>>(
            ExpandPayload(entity.UnknownVocabularyJson), logger, $"parse report {entity.RowKey} vocabulary") ?? [];
        return new StoredParseReport(
            entity.MarketId ?? entity.PartitionKey,
            entity.AtUtc,
            new SourceParseReport(entity.SourceId ?? string.Empty, entity.SchemaVersion ?? string.Empty,
                entity.Records, fields, unknown));
    }

    internal sealed class SourceParseReportEntity : ITableEntity
    {
        public string? MarketId { get; set; }
        public DateTimeOffset AtUtc { get; set; }
        public string? SourceId { get; set; }
        public string? SchemaVersion { get; set; }
        public int Records { get; set; }
        public string? FieldsJson { get; set; }
        public string? UnknownVocabularyJson { get; set; }
        public string PartitionKey { get; set; } = default!;
        public string RowKey { get; set; } = default!;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

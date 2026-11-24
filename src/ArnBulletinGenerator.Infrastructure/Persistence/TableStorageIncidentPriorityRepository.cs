using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces.Repositories;
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

/// <summary>
///     Azure Table Storage implementation for per-market incident priorities and accumulated landscape occurrences.
///     Legacy rows in <c>incidentLandscape</c> remain the source of truth. A compact per-market top projection
///     in <c>incidentLandscapeTop</c> serves the normal read path and is dual-written when enabled.
/// </summary>
public sealed class TableStorageIncidentPriorityRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageIncidentPriorityRepository> logger,
    IOptions<IncidentLandscapeOptions>? landscapeOptions = null,
    TableClient? landscapeClient = null,
    TableClient? topClient = null)
    : TableRepositoryBase("incidentPriorities", options, credential, logger), IIncidentPriorityRepository
{
    private const string PrioritiesPK = "IncidentPriorities";
    private const string TopPartitionKey = "LandscapeTop";
    private const string TopDataColumn = "EntriesJson";
    private const string TopTotalColumn = "TotalCount";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IncidentLandscapeOptions _landscapeOptions =
        landscapeOptions?.Value ?? new IncidentLandscapeOptions();
    private readonly TableClient _landscapeClient = landscapeClient ??
        TableClientFactory.Create("incidentLandscape", options, credential);
    private readonly TableClient _topClient = topClient ??
        TableClientFactory.Create("incidentLandscapeTop", options, credential);

    public async Task<Result<IReadOnlyList<IncidentPriorityRule>>> GetRulesAsync(
        string market, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync<IReadOnlyList<IncidentPriorityRule>>(async () =>
        {
            string rowKey = MarketKey.Normalize(market);
            try
            {
                var response = await TableClient.GetEntityAsync<TableEntity>(
                    PrioritiesPK, rowKey, cancellationToken: cancellationToken);
                var entity = response.Value;
                if (!entity.TryGetValue("RulesJson", out var raw) || raw is not string json)
                    return [];

                var rules = JsonSerializer.Deserialize<List<IncidentPriorityRule>>(json, JsonOptions) ?? [];
                return rules;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return [];
            }
        });
    }

    public async Task<Result> SetRulesAsync(
        string market,
        IReadOnlyList<IncidentPriorityRule> rules,
        string updatedBy,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            string rowKey = MarketKey.Normalize(market);
            string json = JsonSerializer.Serialize(rules, JsonOptions);
            var entity = new TableEntity(PrioritiesPK, rowKey)
            {
                ["RulesJson"] = json,
                ["UpdatedBy"] = updatedBy,
                ["UpdatedAtUtc"] = DateTime.UtcNow
            };

            await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        });
    }

    public async Task<Result<IncidentLandscapePage>> GetLandscapePageAsync(
        string market,
        int limit,
        string? continuationToken = null,
        bool complete = false,
        CancellationToken cancellationToken = default)
    {
        int pageSize = Math.Clamp(limit, 1, complete
            ? Math.Max(1, _landscapeOptions.LegacyFallbackRows)
            : Math.Max(1, _landscapeOptions.TopN));
        if (!complete && !string.IsNullOrWhiteSpace(continuationToken))
            return Result<IncidentLandscapePage>.Failure("continuation requires complete=true");

        if (!complete && _landscapeOptions.ReadModelEnabled)
        {
            var topResult = await ReadTopPageAsync(market, pageSize, cancellationToken);
            if (topResult.IsSuccess)
                return topResult;

            logger.LogWarning(
                "[Landscape] Top projection unavailable for {Market}; using bounded legacy fallback: {Error}",
                market, topResult.Error);
        }

        return await ReadLegacyPageAsync(
            market, pageSize, continuationToken, complete, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<IncidentLandscapeEntry>>> GetLandscapeAsync(
        string market, CancellationToken cancellationToken = default)
    {
        // This explicit internal shape is deliberately complete. HTTP callers use the bounded page
        // endpoint, while existing internal consumers retain the historical all-entries contract.
        var page = await ReadLegacyPageAsync(
            market,
            Math.Max(1, _landscapeOptions.LegacyFallbackRows),
            continuationToken: null,
            complete: true,
            cancellationToken);
        return page.Map(value => value.Entries);
    }

    public async Task<Result> RecordOccurrencesAsync(
        string market,
        IReadOnlyList<IncidentLandscapeEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
            return Result.Success();

        return await ExecuteAsync(async () =>
        {
            string partitionKey = MarketKey.Normalize(market);
            await _landscapeClient.CreateIfNotExistsAsync(cancellationToken);
            DateTime nowUtc = DateTime.UtcNow;

            // Group/deduplicate the incoming batch first so duplicate signatures merge counts
            // without requiring a partition-wide read.
            var entryMap = new Dictionary<string, IncidentLandscapeEntry>(StringComparer.Ordinal);
            foreach (IncidentLandscapeEntry item in entries)
            {
                string signature = Signature(item);
                int increment = item.Count > 0 ? item.Count : 1;
                if (entryMap.TryGetValue(signature, out IncidentLandscapeEntry? existing))
                    entryMap[signature] = existing with { Count = existing.Count + increment };
                else
                    entryMap[signature] = item with { Count = increment };
            }

            var updates = new List<LandscapeUpdate>(entryMap.Count);
            foreach ((string signature, IncidentLandscapeEntry item) in entryMap)
            {
                string rowKey = ComputeHashKey(signature);
                int currentCount = 0;
                DateTime firstSeen = nowUtc;
                try
                {
                    Response<TableEntity> response = await _landscapeClient.GetEntityAsync<TableEntity>(
                        partitionKey,
                        rowKey,
                        select: ["Count", "FirstSeenUtc", "LastSeenUtc", "DataJson"],
                        cancellationToken: cancellationToken);
                    TableEntity existing = response.Value;
                    currentCount = ReadInt(existing, "Count", 0);
                    firstSeen = ReadDateTime(existing, "FirstSeenUtc", nowUtc);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    // New signature: the incoming item supplies its first-seen timestamp below.
                    firstSeen = item.FirstSeenUtc == default ? nowUtc : item.FirstSeenUtc;
                }

                int newCount = checked(currentCount + item.Count);
                var updatedItem = item with
                {
                    Count = newCount,
                    FirstSeenUtc = firstSeen,
                    LastSeenUtc = nowUtc
                };
                updates.Add(new LandscapeUpdate(rowKey, updatedItem, item.Count));
            }

            var actions = updates.Select(update =>
            {
                var entity = new TableEntity(partitionKey, update.RowKey)
                {
                    ["Count"] = update.Entry.Count,
                    ["FirstSeenUtc"] = update.Entry.FirstSeenUtc,
                    ["LastSeenUtc"] = update.Entry.LastSeenUtc,
                    ["DataJson"] = JsonSerializer.Serialize(update.Entry, JsonOptions)
                };
                return new TableTransactionAction(TableTransactionActionType.UpsertReplace, entity);
            }).ToList();

            // Azure Table transactions accept at most 100 actions, all within one partition.
            const int maxBatchSize = 100;
            for (int i = 0; i < actions.Count; i += maxBatchSize)
            {
                List<TableTransactionAction> chunk = actions.Skip(i).Take(maxBatchSize).ToList();
                try
                {
                    await _landscapeClient.SubmitTransactionAsync(chunk, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "[Landscape] Failed batch transaction of {Count} entries for market {Market}, falling back to individual upserts",
                        chunk.Count, market);
                    foreach (TableTransactionAction action in chunk)
                    {
                        try
                        {
                            await _landscapeClient.UpsertEntityAsync(
                                action.Entity, TableUpdateMode.Replace, cancellationToken);
                        }
                        catch (Exception itemEx)
                        {
                            logger.LogWarning(itemEx,
                                "[Landscape] Failed individual upsert for row {RowKey} in market {Market}",
                                action.Entity.RowKey, market);
                        }
                    }
                }
            }

            if (_landscapeOptions.WriteReadModelEnabled)
                await WriteTopSnapshotAsync(market, updates, cancellationToken);
        });
    }

    private async Task<Result<IncidentLandscapePage>> ReadTopPageAsync(
        string market,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await _topClient.CreateIfNotExistsAsync(cancellationToken);
            Response<TableEntity> response = await _topClient.GetEntityAsync<TableEntity>(
                TopPartitionKey,
                MarketKey.Normalize(market),
                select: [TopDataColumn, TopTotalColumn],
                cancellationToken: cancellationToken);
            TableEntity entity = response.Value;
            if (!entity.TryGetValue(TopDataColumn, out object? raw) || raw is not string json)
                return Result<IncidentLandscapePage>.Failure("top projection is unavailable");

            List<IncidentLandscapeEntry> entries =
                JsonSerializer.Deserialize<List<IncidentLandscapeEntry>>(json, JsonOptions) ?? [];
            int totalCount = ReadInt(entity, TopTotalColumn, entries.Sum(entry => Math.Max(0, entry.Count)));
            entries = OrderEntries(entries).Take(pageSize).ToList();
            return Result<IncidentLandscapePage>.Success(
                new IncidentLandscapePage(entries, totalCount));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return Result<IncidentLandscapePage>.Failure("top projection is unavailable");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Landscape] Top projection read failed for {Market}", market);
            return Result<IncidentLandscapePage>.Failure(ex.Message);
        }
    }

    private async Task<Result<IncidentLandscapePage>> ReadLegacyPageAsync(
        string market,
        int pageSize,
        string? continuationToken,
        bool complete,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync<IncidentLandscapePage>(async () =>
        {
            string partitionKey = MarketKey.Normalize(market);
            await _landscapeClient.CreateIfNotExistsAsync(cancellationToken);
            string filter = TableClient.CreateQueryFilter($"PartitionKey eq {partitionKey}");
            var query = _landscapeClient.QueryAsync<TableEntity>(
                filter,
                select: ["RowKey", "Count", "FirstSeenUtc", "LastSeenUtc", "DataJson"],
                cancellationToken: cancellationToken);
            var all = new List<IncidentLandscapeEntry>();
            int maxRows = complete
                ? int.MaxValue
                : Math.Max(pageSize, Math.Min(_landscapeOptions.LegacyFallbackRows, 1_000));

            await foreach (TableEntity entity in query.WithCancellation(cancellationToken))
            {
                if (TryMapEntry(entity, out IncidentLandscapeEntry? item))
                    all.Add(item!);
                if (all.Count >= maxRows)
                    break;
            }

            List<IncidentLandscapeEntry> ordered = OrderEntries(all).ToList();
            int totalCount = ordered.Sum(entry => Math.Max(0, entry.Count));
            if (!complete)
                return new IncidentLandscapePage(ordered.Take(pageSize).ToList(), totalCount);

            int offset = DecodeContinuationToken(continuationToken, ordered.Count);
            List<IncidentLandscapeEntry> page = ordered.Skip(offset).Take(pageSize).ToList();
            int nextOffset = offset + page.Count;
            string? next = nextOffset < ordered.Count
                ? EncodeContinuationToken(nextOffset)
                : null;
            return new IncidentLandscapePage(page, totalCount, next);
        });
    }

    private async Task WriteTopSnapshotAsync(
        string market,
        IReadOnlyList<LandscapeUpdate> updates,
        CancellationToken cancellationToken)
    {
        try
        {
            await _topClient.CreateIfNotExistsAsync(cancellationToken);
            string rowKey = MarketKey.Normalize(market);
            var entries = new Dictionary<string, IncidentLandscapeEntry>(StringComparer.Ordinal);
            int totalCount = 0;
            try
            {
                Response<TableEntity> response = await _topClient.GetEntityAsync<TableEntity>(
                    TopPartitionKey,
                    rowKey,
                    select: [TopDataColumn, TopTotalColumn],
                    cancellationToken: cancellationToken);
                TableEntity entity = response.Value;
                if (entity.TryGetValue(TopDataColumn, out object? raw) && raw is string json)
                {
                    foreach (IncidentLandscapeEntry entry in
                             JsonSerializer.Deserialize<List<IncidentLandscapeEntry>>(json, JsonOptions) ?? [])
                        entries[Signature(entry)] = entry;
                }
                totalCount = ReadInt(entity, TopTotalColumn, entries.Values.Sum(entry => Math.Max(0, entry.Count)));
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // The projection can be built incrementally after a deployment; a separate backfill
                // can seed historical rows without making this write path scan the legacy partition.
            }

            foreach (LandscapeUpdate update in updates)
                entries[Signature(update.Entry)] = update.Entry;
            totalCount = checked(totalCount + updates.Sum(update => Math.Max(0, update.Increment)));
            List<IncidentLandscapeEntry> top = OrderEntries(entries.Values)
                .Take(Math.Max(1, _landscapeOptions.TopN))
                .ToList();
            var projection = new TableEntity(TopPartitionKey, rowKey)
            {
                [TopDataColumn] = JsonSerializer.Serialize(top, JsonOptions),
                [TopTotalColumn] = totalCount,
                ["UpdatedAtUtc"] = DateTime.UtcNow
            };
            await _topClient.UpsertEntityAsync(projection, TableUpdateMode.Replace, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // This is a derived read model; the legacy landscape rows written immediately before
            // this method remain authoritative. A projection outage must not discard that write.
            logger.LogWarning(
                ex,
                "Failed to update incident landscape top projection for market {Market}; "
                + "legacy landscape rows remain available",
                market);
        }
    }

    private static bool TryMapEntry(TableEntity entity, out IncidentLandscapeEntry? item)
    {
        item = null;
        if (!entity.TryGetValue("DataJson", out object? raw) || raw is not string json)
            return false;

        try
        {
            item = JsonSerializer.Deserialize<IncidentLandscapeEntry>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (item is null)
            return false;

        item = item with
        {
            Count = ReadInt(entity, "Count", item.Count),
            FirstSeenUtc = ReadDateTime(entity, "FirstSeenUtc", item.FirstSeenUtc),
            LastSeenUtc = ReadDateTime(entity, "LastSeenUtc", item.LastSeenUtc)
        };
        return true;
    }

    private static IEnumerable<IncidentLandscapeEntry> OrderEntries(
        IEnumerable<IncidentLandscapeEntry> entries) =>
        entries.OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.CanonicalClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.EventType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Details, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Codes, StringComparer.OrdinalIgnoreCase);

    private static string Signature(IncidentLandscapeEntry item) =>
        $"{item.Provider}|{item.CanonicalClass}|{item.EventType}|{item.Details}|{item.Codes ?? string.Empty}";

    private static string EncodeContinuationToken(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"v1:{offset}"));

    private static int DecodeContinuationToken(string? token, int maxOffset)
    {
        if (string.IsNullOrWhiteSpace(token)) return 0;
        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            if (!decoded.StartsWith("v1:", StringComparison.Ordinal) ||
                !int.TryParse(decoded[3..], out int offset) || offset < 0 || offset > maxOffset)
                throw new FormatException();
            return offset;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
        {
            throw new InvalidOperationException("The landscape continuation token is invalid or outside the current result set.");
        }
    }

    private static int ReadInt(TableEntity entity, string key, int fallback) =>
        entity.TryGetValue(key, out object? value) && value is int number ? number : fallback;

    private static DateTime ReadDateTime(TableEntity entity, string key, DateTime fallback) =>
        entity.TryGetValue(key, out object? value) && value is DateTime timestamp ? timestamp : fallback;

    private static string ComputeHashKey(string input)
    {
        using var sha = SHA256.Create();
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..32];
    }

    private sealed record LandscapeUpdate(string RowKey, IncidentLandscapeEntry Entry, int Increment);
}

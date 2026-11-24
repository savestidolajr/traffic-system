using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Domain.ValueObjects;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

// PartitionKey = "v:{market}" for event verdicts (RowKey = sanitized event key), and
// "spotcheck" for spot-check sign-offs (RowKey = job id). Event keys carry characters Table
// Storage forbids in row keys ('/', '\', '#', '?'), so keys are sanitized on write and the
// original key is stored verbatim in its own column for the read path.

public sealed class TableStorageEventVerdictRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageEventVerdictRepository> logger)
    : TableRepositoryBase("eventVerdicts", options, credential, logger), IEventVerdictRepository
{
    public async Task<Result<IReadOnlyList<EventVerdict>>> GetVerdictsAsync(
        string market, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<EventVerdictEntity>(
                $"PartitionKey eq 'v:{NormalizeKey(market)}'", cancellationToken);
            return entities
                // A row whose stored verdict is not an omission (legacy Confirmed/Flagged, or
                // anything unrecognised) carries no standing judgment, so it is not returned at
                // all rather than surfacing as a verdict that would read as "omitted".
                .Select(e => (Entity: e, Kind: ParseKind(e.Verdict)))
                .Where(x => x.Kind.HasValue)
                .Select(x => new EventVerdict(
                    market,
                    x.Entity.EventKey ?? x.Entity.RowKey,
                    x.Kind!.Value,
                    x.Entity.Notes,
                    x.Entity.By ?? "unknown",
                    x.Entity.AtUtc))
                .ToList() as IReadOnlyList<EventVerdict>;
        });
    }

    public async Task<Result> SetVerdictAsync(
        EventVerdict verdict, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(new EventVerdictEntity
            {
                PartitionKey = $"v:{NormalizeKey(verdict.Market)}",
                RowKey = SanitizeRowKey(verdict.EventKey),
                EventKey = verdict.EventKey,
                Verdict = verdict.Verdict.ToString(),
                Notes = verdict.Notes,
                By = verdict.By,
                AtUtc = verdict.AtUtc
            }, TableUpdateMode.Replace, cancellationToken);
        });
    }

    public async Task<Result> ClearVerdictAsync(
        string market, string eventKey, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            await TableClient.DeleteEntityAsync(
                $"v:{NormalizeKey(market)}", SanitizeRowKey(eventKey),
                cancellationToken: cancellationToken);
        });
    }

    public async Task<Result<IReadOnlyList<SpotCheckReview>>> GetSpotChecksAsync(
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var entities = await QueryEntitiesAsync<EventVerdictEntity>(
                "PartitionKey eq 'spotcheck'", cancellationToken);
            return entities
                .Select(e => new SpotCheckReview(e.RowKey, e.By ?? "unknown", e.AtUtc))
                .ToList() as IReadOnlyList<SpotCheckReview>;
        });
    }

    public async Task<Result> MarkSpotCheckAsync(
        SpotCheckReview review, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            await TableClient.UpsertEntityAsync(new EventVerdictEntity
            {
                PartitionKey = "spotcheck",
                RowKey = SanitizeRowKey(review.JobId),
                By = review.By,
                AtUtc = review.AtUtc
            }, TableUpdateMode.Replace, cancellationToken);
        });
    }

    private static string NormalizeKey(string market) => MarketKey.Normalize(market);

    /// <summary>
    ///     Omission is the only verdict, so anything else stored is not one: trial rows wrote "Killed"
    ///     before the rename, and legacy Confirmed/Flagged rows were written by a trust ladder that no
    ///     longer exists. Null means "not omitted" — an unrecognised string must never be promoted into
    ///     a verdict, because the only thing a verdict does is keep an event off air.
    /// </summary>
    internal static VerdictKind? ParseKind(string? verdict) =>
        string.Equals(verdict, "Killed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(verdict, nameof(VerdictKind.Omitted), StringComparison.OrdinalIgnoreCase)
            ? VerdictKind.Omitted
            : null;

    /// <summary>Table Storage forbids '/', '\', '#', '?' and control chars in row keys.</summary>
    private static string SanitizeRowKey(string key)
    {
        Span<char> buffer = stackalloc char[key.Length];
        for (int i = 0; i < key.Length; i++)
        {
            char c = key[i];
            buffer[i] = c is '/' or '\\' or '#' or '?' || char.IsControl(c) ? '_' : c;
        }

        return new string(buffer);
    }

    private sealed class EventVerdictEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = null!;
        public string RowKey { get; set; } = null!;
        public string? EventKey { get; set; }
        public string? Verdict { get; set; }
        public string? Notes { get; set; }
        public string? By { get; set; }
        public DateTimeOffset AtUtc { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

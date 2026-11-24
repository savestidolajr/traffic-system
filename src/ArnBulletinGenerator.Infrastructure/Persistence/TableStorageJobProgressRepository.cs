using System.Globalization;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Stores one replacement progress document per job and one exponential moving average row per
///     market. The progress writer is the sole owner of read-modify-write state, so job rows use
///     Replace rather than append transactions.
/// </summary>
public sealed class TableStorageJobProgressRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageJobProgressRepository> logger)
    : TableRepositoryBase("radioJobProgress", options, credential, logger), IJobProgressRepository
{
    private const string ProgressPartition = "Progress";
    private const string TypicalPartition = "Typical";
    private const double TypicalAlpha = 0.3;

    public async Task<Result> UpsertAsync(
        JobProgressDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return await ExecuteAsync(async () =>
        {
            JobProgressDto dto = document.ToDto() with { TypicalSeconds = null };
            var entity = new JobProgressEntity
            {
                PartitionKey = ProgressPartition,
                RowKey = document.JobId,
                JobId = document.JobId,
                Market = document.Market,
                Version = document.Version,
                UpdatedAt = ParseDate(dto.UpdatedAt),
                Document = JsonSerializer.Serialize(dto)
            };
            await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        });
    }

    public async Task<Result<Option<JobProgressSnapshot>>> GetAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return Result<Option<JobProgressSnapshot>>.Success(Option<JobProgressSnapshot>.None);

        return await ExecuteAsync(async () =>
        {
            var entityResult = await GetEntityOrDefaultAsync<JobProgressEntity>(
                ProgressPartition, jobId, cancellationToken);
            if (entityResult.IsFailure)
                throw new InvalidOperationException(entityResult.Error);
            if (entityResult.Value.IsNone)
                return Option<JobProgressSnapshot>.None;

            JobProgressEntity entity = entityResult.Value.Value;
            var dto = JsonSerializer.Deserialize<JobProgressDto>(entity.Document);
            if (dto is null)
                throw new InvalidOperationException("Stored job progress document is empty.");

            var document = JobProgressDocument.FromDto(dto, entity.Market ?? string.Empty);
            var typical = await GetTypicalAsync(entity.Market ?? string.Empty, cancellationToken);
            if (typical.IsSuccess && typical.Value.HasValue)
                document.SetTypicalSeconds(typical.Value.Value);
            else if (typical.IsFailure)
                logger.LogWarning(
                    "[JobProgress] Typical duration read failed for market {Market}: {Error}",
                    entity.Market, typical.Error);

            string etag = entity.ETag.ToString();
            return Option<JobProgressSnapshot>.Some(new JobProgressSnapshot(document, etag));
        });
    }

    public async Task<Result<Option<IReadOnlyDictionary<string, double>>>> GetTypicalAsync(
        string market, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(market))
            return Result<Option<IReadOnlyDictionary<string, double>>>.Success(
                Option<IReadOnlyDictionary<string, double>>.None);

        var entityResult = await GetEntityOrDefaultAsync<JobProgressTypicalEntity>(
            TypicalPartition, market.Trim(), cancellationToken);
        if (entityResult.IsFailure)
            return entityResult.PropagateFailure<Option<IReadOnlyDictionary<string, double>>>();
        if (entityResult.Value.IsNone || string.IsNullOrWhiteSpace(entityResult.Value.Value.StageSecondsJson))
            return Result<Option<IReadOnlyDictionary<string, double>>>.Success(
                Option<IReadOnlyDictionary<string, double>>.None);

        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, double>>(
                entityResult.Value.Value.StageSecondsJson!);
            return values is null
                ? Option<IReadOnlyDictionary<string, double>>.None
                : Option<IReadOnlyDictionary<string, double>>.Some(
                    new Dictionary<string, double>(values, StringComparer.Ordinal));
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception,
                "[JobProgress] Typical duration row for market {Market} was invalid", market);
            return Result<Option<IReadOnlyDictionary<string, double>>>.Failure(
                "Stored typical duration row is invalid.");
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, double>>> UpdateTypicalAsync(
        string market,
        IReadOnlyDictionary<string, double> stageSeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(market))
            return Result<IReadOnlyDictionary<string, double>>.Failure("Market is required.");

        var sample = stageSeconds
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key)
                && double.IsFinite(pair.Value)
                && pair.Value >= 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        return await ExecuteAsync(async () =>
        {
            var existingResult = await GetEntityOrDefaultAsync<JobProgressTypicalEntity>(
                TypicalPartition, market.Trim(), cancellationToken);
            if (existingResult.IsFailure)
                throw new InvalidOperationException(existingResult.Error);

            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            JobProgressTypicalEntity? existing = existingResult.Value.HasValue
                ? existingResult.Value.Value
                : null;
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.StageSecondsJson))
            {
                var prior = JsonSerializer.Deserialize<Dictionary<string, double>>(
                    existing.StageSecondsJson!);
                if (prior is not null)
                    foreach (var pair in prior.Where(pair => double.IsFinite(pair.Value) && pair.Value >= 0))
                        values[pair.Key] = pair.Value;
            }

            foreach (var pair in sample)
                values[pair.Key] = values.TryGetValue(pair.Key, out double old)
                    ? old * (1.0 - TypicalAlpha) + pair.Value * TypicalAlpha
                    : pair.Value;

            var entity = new JobProgressTypicalEntity
            {
                PartitionKey = TypicalPartition,
                RowKey = market.Trim(),
                Market = market.Trim(),
                StageSecondsJson = JsonSerializer.Serialize(values),
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await TableClient.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
            return (IReadOnlyDictionary<string, double>)values;
        });
    }

    private static DateTimeOffset ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    internal sealed class JobProgressEntity : ITableEntity
    {
        public string JobId { get; set; } = string.Empty;
        public string Market { get; set; } = string.Empty;
        public int Version { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string Document { get; set; } = string.Empty;
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }

    internal sealed class JobProgressTypicalEntity : ITableEntity
    {
        public string Market { get; set; } = string.Empty;
        public string StageSecondsJson { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAt { get; set; }
        public string PartitionKey { get; set; } = string.Empty;
        public string RowKey { get; set; } = string.Empty;
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
    }
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
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
///     Stores large immutable job payloads as compressed, content-addressed entities. The aggregate
///     row keeps only a stable reference while legacy inline JSON remains readable during migration.
/// </summary>
public sealed class TableStorageJobPayloadRepository(
    IOptions<StorageOptions> options,
    TokenCredential credential,
    ILogger<TableStorageJobPayloadRepository> logger)
    : TableRepositoryBase("jobPayloads", options, credential, logger), IJobPayloadRepository
{
    private const string GzipSentinel = "gz:";
    private const int MaxTablePropertyChars = 30_000;

    public async Task<Result<IReadOnlyDictionary<JobPayloadKind, string>>> ReadAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            var latest = new Dictionary<JobPayloadKind, JobPayloadEntity>();
            var query = TableClient.QueryAsync<JobPayloadEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {Partition(jobId)}"),
                maxPerPage: 500, cancellationToken: cancellationToken);
            await foreach (var entity in query)
            {
                if (!Enum.TryParse<JobPayloadKind>(entity.Kind, out var kind))
                    continue;
                if (!latest.TryGetValue(kind, out var current)
                    || (entity.Timestamp ?? DateTimeOffset.MinValue) > (current.Timestamp ?? DateTimeOffset.MinValue)
                    || (entity.Timestamp ?? DateTimeOffset.MinValue) == (current.Timestamp ?? DateTimeOffset.MinValue)
                        && string.CompareOrdinal(entity.RowKey, current.RowKey) > 0)
                    latest[kind] = entity;
                if (latest.Count >= 500)
                    break;
            }

            var values = new Dictionary<JobPayloadKind, string>();
            foreach (var (kind, entity) in latest)
            {
                var expanded = Expand(entity.Content);
                if (expanded is null)
                {
                    logger.LogWarning(
                        "Payload {Kind} for job {JobId} could not be decompressed; detail reads will use inline data.",
                        kind, jobId);
                    continue;
                }
                if (!HasExpectedHash(expanded, entity.ContentHash))
                {
                    logger.LogWarning(
                        "Payload {Kind} for job {JobId} failed its content-hash check; detail reads will use inline data.",
                        kind, jobId);
                    continue;
                }
                values[kind] = expanded;
            }
            return (IReadOnlyDictionary<JobPayloadKind, string>)values;
        });
    }

    public async Task<Result<IReadOnlyDictionary<JobPayloadKind, string>>> ReadAsync(
        string jobId,
        IReadOnlyDictionary<JobPayloadKind, string> references,
        CancellationToken cancellationToken = default)
    {
        if (references.Count == 0)
            return await ReadAsync(jobId, cancellationToken);

        return await ExecuteAsync(async () =>
        {
            var values = new Dictionary<JobPayloadKind, string>();
            foreach (var (kind, reference) in references)
            {
                string? rowKey = ResolveRowKey(reference, kind);
                if (rowKey is null)
                    continue;
                try
                {
                    var response = await TableClient.GetEntityAsync<JobPayloadEntity>(
                        Partition(jobId), rowKey, cancellationToken: cancellationToken);
                    var payload = response.Value;
                    var expanded = Expand(payload.Content);
                    if (expanded is not null && HasExpectedHash(expanded, payload.ContentHash))
                        values[kind] = expanded;
                    else
                        logger.LogWarning(
                            "Payload {Kind} for job {JobId} failed decompression/hash validation.",
                            kind, jobId);
                }
                catch (RequestFailedException ex) when (ex.Status == 404)
                {
                    logger.LogWarning(
                        "Referenced payload {Kind} for job {JobId} is missing; detail reads will use inline data.",
                        kind, jobId);
                }
            }
            return (IReadOnlyDictionary<JobPayloadKind, string>)values;
        });
    }

    public async Task<Result> SaveAsync(
        string jobId,
        IReadOnlyDictionary<JobPayloadKind, string> payloads,
        CancellationToken cancellationToken = default)
    {
        if (payloads.Count == 0) return Result.Success();
        return await ExecuteAsync(async () =>
        {
            foreach (var payload in payloads)
            {
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.Value)));
                string content = Fit(payload.Value, jobId, payload.Key);
                var entity = new JobPayloadEntity
                {
                    PartitionKey = Partition(jobId),
                    RowKey = $"{payload.Key}|{hash}",
                    JobId = jobId,
                    Kind = payload.Key.ToString(),
                    ContentHash = hash,
                    Content = content
                };
                try
                {
                    await TableClient.AddEntityAsync(entity, cancellationToken);
                }
                catch (RequestFailedException ex) when (ex.Status == 409)
                {
                    // Content-addressed rows are immutable; a replay of the same payload is safe.
                }
            }
        });
    }

    public async Task<Result> DeleteAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(async () =>
        {
            string partition = Partition(jobId);
            var query = TableClient.QueryAsync<JobPayloadEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {partition}"),
                cancellationToken: cancellationToken);
            await foreach (var entity in query)
                await TableClient.DeleteEntityAsync(partition, entity.RowKey, cancellationToken: cancellationToken);
        });
    }

    internal static string Partition(string jobId) => $"JobPayload|{jobId}";
    private static string? ResolveRowKey(string reference, JobPayloadKind kind)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;
        string value = reference.Trim();
        int lastSeparator = value.LastIndexOf(':');
        if (value.StartsWith("jobPayload:", StringComparison.Ordinal)
            && lastSeparator > "jobPayload:".Length)
        {
            int kindSeparator = value.LastIndexOf(':', lastSeparator - 1);
            if (kindSeparator >= 0
                && Enum.TryParse<JobPayloadKind>(
                    value[(kindSeparator + 1)..lastSeparator], out var referencedKind)
                && referencedKind == kind)
                return $"{kind}|{value[(lastSeparator + 1)..]}";
        }
        return value.Contains('|') ? value : null;
    }

    private static bool HasExpectedHash(string payload, string expectedHash) =>
        !string.IsNullOrWhiteSpace(expectedHash)
        && string.Equals(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            expectedHash,
            StringComparison.OrdinalIgnoreCase);

    private string Fit(string payload, string jobId, JobPayloadKind kind)
    {
        if (payload.Length <= MaxTablePropertyChars) return payload;
        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes(payload));
        string compressed = GzipSentinel + Convert.ToBase64String(buffer.ToArray());
        if (compressed.Length <= MaxTablePropertyChars) return compressed;
        throw new InvalidOperationException(
            $"Job payload {kind} for {jobId} exceeds the Table Storage property limit after compression.");
    }

    private static string? Expand(string? stored)
    {
        if (stored is null || !stored.StartsWith(GzipSentinel, StringComparison.Ordinal)) return stored;
        try
        {
            using var gzip = new GZipStream(
                new MemoryStream(Convert.FromBase64String(stored[GzipSentinel.Length..])),
                CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (FormatException)
        {
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}

internal sealed class JobPayloadEntity : ITableEntity
{
    public string JobId { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string ContentHash { get; set; } = null!;
    public string Content { get; set; } = null!;
    public string PartitionKey { get; set; } = null!;
    public string RowKey { get; set; } = null!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

using System.Text;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Persistence;

/// <summary>
///     Blob-backed store for pending configuration imports. One JSON envelope per import under
///     <c>pending/</c>; resolution rewrites the envelope (with the resolver identity appended)
///     under <c>applied/</c> or <c>rejected/</c> and deletes the pending blob. A compact index blob
///     under <c>index/</c> carries the requester, timestamp and review report so pending listing
///     can enumerate metadata without downloading potentially multi-megabyte bundle bodies.
///     Pending envelopes remain the source of truth: missing or stale index entries fall back to
///     the legacy body read and are backfilled opportunistically.
/// </summary>
public sealed class BlobConfigImportStore(
    BlobServiceClient blobServiceClient,
    IOptions<ConfigImportOptions> options,
    TimeProvider timeProvider,
    ILogger<BlobConfigImportStore> logger) : IConfigImportStore
{
    private const string PendingPrefix = "pending/";
    private const string IndexPrefix = "index/";
    private const string MetadataId = "Id";
    private const string MetadataRequestedBy = "RequestedBy";
    private const string MetadataRequestedByObjectId = "RequestedByObjectId";
    private const string MetadataRequestedAtUtc = "RequestedAtUtc";
    private const string MetadataReportJson = "ReportJson";
    private const string MetadataReportInBody = "ReportInBody";
    private const int MaxMetadataBytes = 7 * 1024;

    private readonly ConfigImportOptions _options = options.Value;

    public async Task<Result> SavePendingAsync(
        PendingConfigImport import, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = await GetContainerAsync(cancellationToken);
            var blob = container.GetBlobClient(PendingBlobName(import.Id));
            await blob.UploadAsync(
                BinaryData.FromString(JsonSerializer.Serialize(import)),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                },
                cancellationToken);

            if (!_options.IndexDisabled)
                await TrySaveIndexAsync(container, import, cancellationToken);

            logger.LogInformation("[ConfigImport] Pending import {Id} saved (requested by {By})",
                import.Id, import.RequestedBy);
            return Result.Success();
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "[ConfigImport] Failed to save pending import {Id}", import.Id);
            return Result.Failure($"Failed to save pending import: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<PendingConfigImport>>> ListPendingAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var container = await GetContainerAsync(cancellationToken);
            var pendingNames = await ListPendingNamesAsync(container, cancellationToken);
            if (_options.IndexDisabled)
                return await ListLegacyPendingAsync(container, pendingNames, cancellationToken);

            var importsById = new Dictionary<string, PendingConfigImport>(StringComparer.Ordinal);
            await foreach (var item in container.GetBlobsAsync(
                               BlobTraits.Metadata, BlobStates.None, IndexPrefix, cancellationToken))
            {
                var id = Decode(item.Metadata, MetadataId);
                if (string.IsNullOrWhiteSpace(id) || !pendingNames.Contains(PendingBlobName(id)))
                    continue; // stale index row, normally left by a failed archive cleanup

                var indexed = await ReadIndexedAsync(container, item, id, cancellationToken);
                if (indexed is not null)
                    importsById[id] = indexed;
            }

            // Legacy pending envelopes may pre-date the index or have been written while the
            // index was unavailable. Read only those bodies and backfill their metadata row.
            foreach (var pendingName in pendingNames)
            {
                var id = PendingIdFromName(pendingName);
                if (id is null || importsById.ContainsKey(id))
                    continue;

                var import = await ReadPendingBodyAsync(container, pendingName, cancellationToken);
                if (import is null)
                    continue;

                importsById[import.Id] = import;
                await TrySaveIndexAsync(container, import, cancellationToken);
            }

            var imports = importsById.Values.ToList();
            imports.Sort(static (a, b) =>
            {
                var byTime = a.RequestedAtUtc.CompareTo(b.RequestedAtUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
            });
            return Result<IReadOnlyList<PendingConfigImport>>.Success(imports);
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "[ConfigImport] Failed to list pending imports");
            return Result<IReadOnlyList<PendingConfigImport>>.Failure(
                $"Failed to list pending imports: {ex.Message}");
        }
    }

    public async Task<Result<Option<PendingConfigImport>>> GetPendingAsync(
        string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = await GetContainerAsync(cancellationToken);
            var blob = container.GetBlobClient(PendingBlobName(id));
            if (!await blob.ExistsAsync(cancellationToken))
                return Result<Option<PendingConfigImport>>.Success(Option<PendingConfigImport>.None);

            var content = await blob.DownloadContentAsync(cancellationToken);
            var import = Deserialize(content.Value.Content);
            return Result<Option<PendingConfigImport>>.Success(
                import is null ? Option<PendingConfigImport>.None : Option<PendingConfigImport>.Some(import));
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "[ConfigImport] Failed to read pending import {Id}", id);
            return Result<Option<PendingConfigImport>>.Failure($"Failed to read pending import: {ex.Message}");
        }
    }

    public async Task<Result> ResolveAsync(
        string id, string resolution, string resolvedBy, string resolvedByObjectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var container = await GetContainerAsync(cancellationToken);
            var pending = container.GetBlobClient(PendingBlobName(id));
            if (!await pending.ExistsAsync(cancellationToken))
                return Result.Failure($"Pending import '{id}' not found.");

            var content = await pending.DownloadContentAsync(cancellationToken);
            var envelope = new ResolvedEnvelope(
                content.Value.Content.ToString(),
                resolution,
                resolvedBy,
                resolvedByObjectId,
                timeProvider.GetUtcNow().UtcDateTime);

            var archived = container.GetBlobClient($"{resolution}/{id}.json");
            await archived.UploadAsync(
                BinaryData.FromString(JsonSerializer.Serialize(envelope)),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" }
                },
                cancellationToken);
            await pending.DeleteAsync(cancellationToken: cancellationToken);

            if (!_options.IndexDisabled)
            {
                try
                {
                    await container.GetBlobClient(IndexBlobName(id))
                        .DeleteIfExistsAsync(cancellationToken: cancellationToken);
                }
                catch (RequestFailedException ex)
                {
                    // The pending-name check in ListPendingAsync suppresses a stale index row;
                    // cleanup is therefore best effort and never changes the approval result.
                    logger.LogWarning(ex, "[ConfigImport] Failed to remove index for resolved import {Id}", id);
                }
            }

            logger.LogInformation("[ConfigImport] Pending import {Id} resolved as {Resolution} by {By}",
                id, resolution, resolvedBy);
            return Result.Success();
        }
        catch (RequestFailedException ex)
        {
            logger.LogError(ex, "[ConfigImport] Failed to resolve pending import {Id}", id);
            return Result.Failure($"Failed to resolve pending import: {ex.Message}");
        }
    }

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken)
    {
        var container = blobServiceClient.GetBlobContainerClient(_options.ContainerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return container;
    }

    private static async Task<HashSet<string>> ListPendingNamesAsync(
        BlobContainerClient container, CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var item in container.GetBlobsAsync(
                           BlobTraits.None, BlobStates.None, PendingPrefix, cancellationToken))
            names.Add(item.Name);
        return names;
    }

    private async Task<Result<IReadOnlyList<PendingConfigImport>>> ListLegacyPendingAsync(
        BlobContainerClient container, IReadOnlySet<string> pendingNames,
        CancellationToken cancellationToken)
    {
        var imports = new List<PendingConfigImport>();
        foreach (var pendingName in pendingNames)
        {
            var import = await ReadPendingBodyAsync(container, pendingName, cancellationToken);
            if (import is not null)
                imports.Add(import);
        }

        imports.Sort(static (a, b) =>
        {
            var byTime = a.RequestedAtUtc.CompareTo(b.RequestedAtUtc);
            return byTime != 0 ? byTime : string.CompareOrdinal(a.Id, b.Id);
        });
        return Result<IReadOnlyList<PendingConfigImport>>.Success(imports);
    }

    private async Task<PendingConfigImport?> ReadIndexedAsync(
        BlobContainerClient container, BlobItem item, string id, CancellationToken cancellationToken)
    {
        var metadata = item.Metadata;
        var requestedBy = Decode(metadata, MetadataRequestedBy);
        var requestedByObjectId = Decode(metadata, MetadataRequestedByObjectId);
        var requestedAt = Decode(metadata, MetadataRequestedAtUtc);
        var reportJson = Decode(metadata, MetadataReportJson);

        if (metadata.TryGetValue(MetadataReportInBody, out var inBody)
            && string.Equals(inBody, "1", StringComparison.Ordinal))
        {
            var content = await container.GetBlobClient(item.Name).DownloadContentAsync(cancellationToken);
            try
            {
                var index = JsonSerializer.Deserialize<ConfigImportIndex>(content.Value.Content.ToString());
                if (index is null || !string.Equals(index.Id, id, StringComparison.Ordinal))
                    return null;
                requestedBy = index.RequestedBy;
                requestedByObjectId = index.RequestedByObjectId;
                requestedAt = index.RequestedAtUtc.ToString("O");
                reportJson = index.ReportJson;
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "[ConfigImport] Skipping unreadable pending-import index {Id}", id);
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(requestedAt)
            || !DateTime.TryParse(requestedAt, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var requestedAtUtc)
            || string.IsNullOrWhiteSpace(reportJson))
            return null;

        return new PendingConfigImport(
            id,
            requestedBy ?? string.Empty,
            requestedByObjectId ?? string.Empty,
            requestedAtUtc,
            reportJson,
            string.Empty);
    }

    private async Task<PendingConfigImport?> ReadPendingBodyAsync(
        BlobContainerClient container, string pendingName, CancellationToken cancellationToken)
    {
        try
        {
            var content = await container.GetBlobClient(pendingName).DownloadContentAsync(cancellationToken);
            return Deserialize(content.Value.Content);
        }
        catch (RequestFailedException ex)
        {
            logger.LogWarning(ex, "[ConfigImport] Skipping unreadable pending-import body {Name}", pendingName);
            return null;
        }
    }

    private async Task TrySaveIndexAsync(
        BlobContainerClient container, PendingConfigImport import, CancellationToken cancellationToken)
    {
        try
        {
            var index = new ConfigImportIndex(
                import.Id,
                import.RequestedBy,
                import.RequestedByObjectId,
                import.RequestedAtUtc,
                import.ReportJson);
            var metadata = new Dictionary<string, string>
            {
                [MetadataId] = Encode(index.Id),
                [MetadataRequestedBy] = Encode(index.RequestedBy),
                [MetadataRequestedByObjectId] = Encode(index.RequestedByObjectId),
                [MetadataRequestedAtUtc] = Encode(index.RequestedAtUtc.ToString("O"))
            };

            bool reportInBody = MetadataBytes(metadata) + Encoding.UTF8.GetByteCount(MetadataReportJson)
                + Encoding.UTF8.GetByteCount(Encode(index.ReportJson)) > MaxMetadataBytes;
            if (reportInBody)
            {
                metadata[MetadataReportInBody] = "1";
            }
            else
            {
                metadata[MetadataReportJson] = Encode(index.ReportJson);
            }

            var blob = container.GetBlobClient(IndexBlobName(import.Id));
            await blob.UploadAsync(
                reportInBody ? BinaryData.FromString(JsonSerializer.Serialize(index)) : BinaryData.FromString(string.Empty),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/json" },
                    Metadata = metadata
                },
                cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            // The pending envelope is authoritative. A transient index outage must not turn a
            // successful import submission into a 500; ListPendingAsync will backfill it later.
            logger.LogWarning(ex, "[ConfigImport] Failed to write metadata index for pending import {Id}", import.Id);
        }
    }

    private PendingConfigImport? Deserialize(BinaryData data)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingConfigImport>(data.ToString());
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "[ConfigImport] Skipping unreadable pending-import envelope");
            return null;
        }
    }

    private static string? PendingIdFromName(string name)
    {
        if (!name.StartsWith(PendingPrefix, StringComparison.Ordinal)
            || !name.EndsWith(".json", StringComparison.Ordinal))
            return null;
        var id = name[PendingPrefix.Length..^5];
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }

    private static string PendingBlobName(string id) => $"{PendingPrefix}{id}.json";
    private static string IndexBlobName(string id) => $"{IndexPrefix}{id}.json";

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string? Decode(IDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value))
            return null;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static int MetadataBytes(IReadOnlyDictionary<string, string> metadata) =>
        metadata.Sum(pair => Encoding.UTF8.GetByteCount(pair.Key) + Encoding.UTF8.GetByteCount(pair.Value));

    /// <summary>The archived form: the original pending envelope plus who resolved it and when.</summary>
    private sealed record ResolvedEnvelope(
        string Pending,
        string Resolution,
        string ResolvedBy,
        string ResolvedByObjectId,
        DateTime ResolvedAtUtc);
}

/// <summary>Compact pending-import metadata. BundleJson is deliberately absent from this index.</summary>
internal sealed record ConfigImportIndex(
    string Id,
    string RequestedBy,
    string RequestedByObjectId,
    DateTime RequestedAtUtc,
    string ReportJson);

using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     Uploads synthesized bulletin audio to Azure Blob Storage and tags it published once the
///     bulletin airs, so lifecycle rules can target and retain only aired audio.
/// </summary>
public sealed class AzureBlobBroadcastOutput(
    BlobServiceClient blobServiceClient,
    IOptions<StorageOptions> options,
    ILogger<AzureBlobBroadcastOutput> logger) : IBroadcastOutput
{
    private const string PublishedTagKey = "published";

    private readonly StorageOptions _options = options.Value;

    // Container existence is a once-per-process concern: every bulletin's SaveAsync used to
    // issue CreateIfNotExistsAsync, and each call on the already-existing container logged a
    // 409 ContainerAlreadyExists SDK warning to App Insights — the once-per-minute warning the
    // poller-driven broadcast path produced. Same flag+lock shape as TableRepositoryBase; this
    // class is registered as a singleton so the flag genuinely spans the process. The create
    // still happens on a cold start (first save in a fresh process) and real errors still throw.
    private readonly SemaphoreSlim _ensureContainerLock = new(1, 1);
    private volatile bool _containerEnsured;

    private async Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        if (_containerEnsured) return;

        await _ensureContainerLock.WaitAsync(cancellationToken);
        try
        {
            if (_containerEnsured) return;

            var containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);
            if (blobServiceClient.AccountName == StorageOptions.LocalEmulatorAccount)
            {
                // Local development with Azurite: create public so the browser console can read
                // it without SAS/CORS.
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
            }
            else
            {
                await containerClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }

            _containerEnsured = true;
        }
        finally
        {
            _ensureContainerLock.Release();
        }
    }

    public async Task<Result> SaveAsync(byte[] audioData, string identifier,
        CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken);

        var containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);

        // For local development with Azurite, re-assert public access on every save: Azurite
        // restarts drop container ACLs, and the browser console reads this container without
        // SAS/CORS. Only the create is once-per-process (see EnsureContainerExistsAsync).
        if (blobServiceClient.AccountName == StorageOptions.LocalEmulatorAccount)
        {
            // Ensure it's public even if it already existed
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        }

        var blobClient = containerClient.GetBlobClient($"{identifier}.wav");
        logger.LogInformation("Uploading audio data to blob: {BlobName}", blobClient.Name);

        using var ms = new MemoryStream(audioData);
        await blobClient.UploadAsync(ms, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = "audio/wav"
            },
            // Retention marker (CC-002): only the aired bulletin must be retained long-term.
            // Uploaded as unpublished; MarkPublishedAsync flips it when the bulletin airs, so
            // future lifecycle rules can target never-aired candidates (blobIndexMatch cannot negate).
            Tags = new Dictionary<string, string> { [PublishedTagKey] = "false" }
        }, cancellationToken);

        logger.LogInformation("Successfully uploaded {Size} bytes to blob: {BlobName}", audioData.Length,
            blobClient.Name);

        return Result.Success();
    }

    public async Task<Result> MarkPublishedAsync(string audioFileName,
        CancellationToken cancellationToken = default)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);
        var blobClient = containerClient.GetBlobClient(audioFileName);

        if (!await blobClient.ExistsAsync(cancellationToken))
        {
            // Not our blob (e.g. a filler-cart reference) — nothing to tag.
            logger.LogInformation("Skipping published tag: blob {BlobName} not found in {Container}",
                audioFileName, _options.ContainerName);
            return Result.Success();
        }

        await blobClient.SetTagsAsync(
            new Dictionary<string, string> { [PublishedTagKey] = "true" },
            cancellationToken: cancellationToken);
        logger.LogInformation("Tagged blob {BlobName} as published", audioFileName);
        return Result.Success();
    }
}

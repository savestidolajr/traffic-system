using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>Deletes only safe, job-owned WAV names from the broadcast container.</summary>
public sealed class AzureBlobJobAudioArtifactRepository(
    BlobServiceClient blobServiceClient,
    IOptions<StorageOptions> options,
    ILogger<AzureBlobJobAudioArtifactRepository> logger) : IJobAudioArtifactRepository
{
    public async Task<Result> DeleteAsync(
        string audioReference, CancellationToken cancellationToken = default)
    {
        string? name = SafeBlobName(audioReference);
        if (name is null) return Result.Success();

        try
        {
            var blob = blobServiceClient.GetBlobContainerClient(options.Value.ContainerName).GetBlobClient(name);
            await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete retained job audio artifact {BlobName}", name);
            return Result.Failure(ex.Message);
        }
    }

    private static string? SafeBlobName(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        string value = reference.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
        else
            value = value[(value.LastIndexOf('/') + 1)..];
        if (value.Contains("..", StringComparison.Ordinal)
            || value.Contains('\\')
            || !value.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            || value.Length > 256)
            return null;
        return value;
    }
}

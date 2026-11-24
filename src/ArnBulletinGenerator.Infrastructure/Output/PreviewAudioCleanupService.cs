using System.Globalization;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
/// Cleans up only the short-lived audio artifacts created by synchronous preview endpoints.
/// Published bulletin audio and every non-preview blob are outside this service's deletion surface.
/// Pins are stored as blob tags so cleanup remains safe across worker instances.
/// </summary>
public sealed class PreviewAudioCleanupService(
    BlobServiceClient blobServiceClient,
    IOptions<StorageOptions> storageOptions,
    IOptions<PreviewAudioCleanupOptions> cleanupOptions,
    TimeProvider timeProvider,
    ILogger<PreviewAudioCleanupService> logger) : IPreviewAudioCleanup
{
    internal const string PinTagKey = "previewPinnedUntil";
    internal const string ManagedTagKey = "previewManaged";
    internal const string ManagedAtTagKey = "previewManagedAt";

    private static readonly string[] PreviewPrefixes =
    [
        "synthesize_",
        "audition_",
        "voicepreview_",
        "voiceremix_",
        "deliverypreview_",
        "pronunciationpreview_",
        "pronunciationrulepreview_"
    ];

    public static bool IsPreviewFileName(string? audioFileName)
    {
        if (string.IsNullOrWhiteSpace(audioFileName))
            return false;

        string name = audioFileName.Trim();
        return !name.Contains('/', StringComparison.Ordinal)
            && !name.Contains('\\', StringComparison.Ordinal)
            && name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            && PreviewPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    }

    public async Task<Result> PinAsync(
        string audioFileName,
        DateTimeOffset? pinnedUntil = null,
        CancellationToken cancellationToken = default)
    {
        string? blobName = NormalizePreviewFileName(audioFileName);
        if (blobName is null)
            return Result.Failure("Only preview audio file names can be pinned.");

        try
        {
            var blob = Container().GetBlobClient(blobName);
            if (!await blob.ExistsAsync(cancellationToken).ConfigureAwait(false))
                return Result.Success();

            var tags = (await blob.GetTagsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Value.Tags;
            DateTimeOffset now = timeProvider.GetUtcNow();
            DateTimeOffset until = pinnedUntil ?? now.Add(cleanupOptions.Value.EffectiveActiveUrlPinDuration);
            tags[ManagedTagKey] = "true";
            tags[ManagedAtTagKey] = now.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            tags[PinTagKey] = until.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            await blob.SetTagsAsync(tags, cancellationToken: cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to pin preview audio {BlobName}", blobName);
            return Result.Failure($"Failed to pin preview audio: {ex.Message}");
        }
    }

    public async Task<Result<PreviewAudioCleanupReport>> CleanupAsync(
        CancellationToken cancellationToken = default)
    {
        var options = cleanupOptions.Value;
        if (!options.Enabled)
            return Result<PreviewAudioCleanupReport>.Success(new PreviewAudioCleanupReport(0, 0, 0, 0, false));

        int examined = 0;
        int deleted = 0;
        int pinned = 0;
        int skipped = 0;
        bool reachedLimit = false;
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset cutoff = now.AddDays(-options.EffectiveRetentionDays);

        try
        {
            var container = Container();
            await foreach (var item in container.GetBlobsAsync(
                               BlobTraits.Tags, BlobStates.None, prefix: null, cancellationToken)
                           .ConfigureAwait(false))
            {
                if (examined >= options.EffectiveMaxBlobsPerRun)
                {
                    reachedLimit = true;
                    break;
                }

                examined++;
                if (!IsPreviewFileName(item.Name)
                    || item.Tags is null
                    || !item.Tags.TryGetValue(ManagedTagKey, out string? managed)
                    || !string.Equals(managed, "true", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                DateTimeOffset? managedAt = TryGetTimestamp(item.Tags, ManagedAtTagKey);
                if (managedAt is null || managedAt.Value >= cutoff)
                {
                    skipped++;
                    continue;
                }

                if (TryGetPin(item.Tags, out var pinnedUntil) && pinnedUntil > now)
                {
                    pinned++;
                    continue;
                }

                // DeleteIfExists makes retries and concurrent timer instances harmless.
                await container.DeleteBlobIfExistsAsync(
                    item.Name, DeleteSnapshotsOption.IncludeSnapshots, conditions: null, cancellationToken)
                    .ConfigureAwait(false);
                deleted++;
            }

            logger.LogInformation(
                "Preview audio cleanup examined {Examined}, deleted {Deleted}, pinned {Pinned}, skipped {Skipped}, reached limit {ReachedLimit}",
                examined, deleted, pinned, skipped, reachedLimit);
            return Result<PreviewAudioCleanupReport>.Success(
                new PreviewAudioCleanupReport(examined, deleted, pinned, skipped, reachedLimit));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Preview audio cleanup failed after examining {Examined} blob(s)", examined);
            return Result<PreviewAudioCleanupReport>.Failure(ex.Message);
        }
    }

    private BlobContainerClient Container() =>
        blobServiceClient.GetBlobContainerClient(storageOptions.Value.ContainerName);

    private static string? NormalizePreviewFileName(string? audioFileName)
    {
        if (string.IsNullOrWhiteSpace(audioFileName))
            return null;

        string value = audioFileName.Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..];
        else if (value.Contains('/'))
            value = value[(value.LastIndexOf('/') + 1)..];

        return IsPreviewFileName(value) && !value.Contains("..", StringComparison.Ordinal)
            ? value
            : null;
    }

    private static bool TryGetPin(
        IDictionary<string, string>? tags,
        out DateTimeOffset pinnedUntil) =>
        TryGetTimestamp(tags, PinTagKey, out pinnedUntil);

    private static DateTimeOffset? TryGetTimestamp(
        IDictionary<string, string>? tags,
        string key)
    {
        return TryGetTimestamp(tags, key, out var value) ? value : null;
    }

    private static bool TryGetTimestamp(
        IDictionary<string, string>? tags,
        string key,
        out DateTimeOffset timestamp)
    {
        timestamp = default;
        return tags is not null
            && tags.TryGetValue(key, out string? value)
            && DateTimeOffset.TryParse(
                value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out timestamp);
    }
}

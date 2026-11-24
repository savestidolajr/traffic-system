using System.Security.Cryptography;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     Delivers broadcast audio to the playout ingest. Downloads audio from blob
///     storage and writes it with the market-specific file name
///     (e.g., MEL_TRAIN_TRAFF.wav) via the configured delivery transport.
/// </summary>
public sealed class ZettaFileDeliveryService(
    BlobServiceClient blobServiceClient,
    IOptions<StorageOptions> storageOptions,
    IOptions<ZettaOptions> zettaOptions,
    IDeliveryFileWriter fileWriter,
    TimeProvider timeProvider,
    ILogger<ZettaFileDeliveryService> logger) : IFileDeliveryService
{
    private readonly StorageOptions _storageOptions = storageOptions.Value;
    private readonly ZettaOptions _zettaOptions = zettaOptions.Value;

    public async Task<Result<FileDeliveryStatus>> DeliverAsync(string audioFileName, string marketId,
        string? expectedSha256 = null, DateTimeOffset? slotDeadlineUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (!_zettaOptions.Enabled)
        {
            logger.LogDebug("Zetta delivery disabled — skipping for {MarketId}", marketId);
            return Result<FileDeliveryStatus>.Success(FileDeliveryStatus.SkippedDisabled);
        }

        // Last line before bytes reach the live share. Checked FIRST — before the download, the
        // integrity seal and the name resolution — because audio for a slot that has already
        // passed must not be written under any circumstances, and the cheapest refusal is the
        // one that touches nothing. On 2026-08-11 a restart sweep walked 15 expired slots and
        // wrote their stale audio to the production share; the guards added upstream for that
        // live in the orchestrator and the watchdog, i.e. in the layer that made the mistake.
        // This one is independent of them on purpose.
        var utcNow = timeProvider.GetUtcNow();
        if (slotDeadlineUtc is { } deadline && utcNow > deadline)
        {
            logger.LogError(
                "Zetta delivery REFUSED for {MarketId} — slot deadline {Deadline:O} passed at {Now:O} "
                + "({LateBy} late). Stale audio must never reach the playout share.",
                marketId, deadline, utcNow, utcNow - deadline);
            return Result<FileDeliveryStatus>.Failure(
                $"Refusing to deliver to playout: the slot deadline ({deadline:O}) passed "
                + $"{utcNow - deadline} ago. Audio for an expired slot must not be written to the share.");
        }

        if (slotDeadlineUtc is null)
        {
            logger.LogWarning(
                "Zetta delivery proceeding without a slot deadline for {MarketId} — cannot verify the slot "
                + "is still airable (filler/legacy caller)",
                marketId);
        }


        byte[] audioData;
        try
        {
            audioData = await DownloadFromBlobAsync(audioFileName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Zetta delivery failed for {MarketId} — audio {FileName}", marketId, audioFileName);
            return Result<FileDeliveryStatus>.Failure($"Zetta delivery failed: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(expectedSha256))
        {
            string actualSha256 = Convert.ToHexString(SHA256.HashData(audioData));
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError(
                    "Zetta delivery integrity check failed for {MarketId} — SHA-256 mismatch (expected {Expected}, actual {Actual})",
                    marketId, expectedSha256, actualSha256);
                return Result<FileDeliveryStatus>.Failure(ErrorMessages.Job.AudioIntegrityFailed);
            }
        }
        else
        {
            logger.LogWarning(
                "Zetta delivery proceeding without integrity seal for {MarketId} — no expected SHA-256 provided (filler/legacy)",
                marketId);
        }

        string? marketCode = _zettaOptions.ResolveMarketCode(marketId);
        if (marketCode is null)
        {
            logger.LogError(
                "No Zetta market code configured for {MarketId}; refusing to deliver under a guessed name. "
                + "Add Zetta:MarketCodes:{MarketId} with the code ARN's playout expects.",
                marketId, marketId);
            return Result<FileDeliveryStatus>.Failure(
                $"No Zetta market code is configured for market '{marketId}'. Playout file names must use the "
                + "code ARN's Zetta import expects; the market id is not a substitute.");
        }

        if (!IsSafePathSegment(marketCode))
        {
            logger.LogError(
                "Zetta market code {MarketCode} for {MarketId} contains path-traversal characters; refusing to write.",
                marketCode, marketId);
            return Result<FileDeliveryStatus>.Failure("Resolved Zetta market code contains unsafe path characters.");
        }

        // The code is ARN's own playout identifier, written exactly as supplied. It used to be
        // uppercased, which silently mangled the station part of a name like [SYD]KIIS90s_TRAFF.
        // The legacy codes (SYD, MEL, BNE, ADL, PER, GLD) are already uppercase, so nothing moves
        // for them.
        string zettaFileName = BuildDeliveryFileName(marketCode);

        if (!IsSafePathSegment(zettaFileName))
        {
            logger.LogError(
                "Zetta file name {FileName} contains path-traversal characters; refusing to write.",
                zettaFileName);
            return Result<FileDeliveryStatus>.Failure("Resolved Zetta file name contains unsafe path characters.");
        }

        var writeResult = await fileWriter.WriteAsync(audioData, zettaFileName, cancellationToken);
        if (writeResult.IsFailure)
        {
            logger.LogError("Zetta delivery write failed for {MarketId} — {Error}", marketId, writeResult.Error);
            return Result<FileDeliveryStatus>.Failure($"Zetta delivery failed: {writeResult.Error}");
        }

        logger.LogInformation(
            "Delivered {Bytes} bytes to Zetta as {FileName} via {Transport}",
            audioData.Length, zettaFileName, _zettaOptions.Transport);

        return Result<FileDeliveryStatus>.Success(FileDeliveryStatus.Delivered);
    }

    public async Task<Result<PlayoutTrayStatus>> GetTrayStatusAsync(
        string marketId, CancellationToken cancellationToken = default)
    {
        string? marketCode = _zettaOptions.ResolveMarketCode(marketId);
        if (marketCode is null)
        {
            logger.LogError(
                "No Zetta market code configured for {MarketId}; no playout tray to report. "
                + "Add Zetta:MarketCodes:{MarketId} with the code ARN's playout expects.",
                marketId, marketId);
            return Result<PlayoutTrayStatus>.Failure(
                $"No Zetta market code is configured for market '{marketId}'. Playout file names must use the "
                + "code ARN's Zetta import expects; the market id is not a substitute.");
        }

        if (!IsSafePathSegment(marketCode))
        {
            logger.LogError(
                "Zetta market code {MarketCode} for {MarketId} contains path-traversal characters; refusing to probe.",
                marketCode, marketId);
            return Result<PlayoutTrayStatus>.Failure("Resolved Zetta market code contains unsafe path characters.");
        }

        string fileName = BuildDeliveryFileName(marketCode);

        if (!IsSafePathSegment(fileName))
        {
            logger.LogError(
                "Zetta file name {FileName} for {MarketId} contains path-traversal characters; refusing to probe.",
                fileName, marketId);
            return Result<PlayoutTrayStatus>.Failure("Resolved Zetta file name contains unsafe path characters.");
        }

        if (!_zettaOptions.Enabled)
        {
            logger.LogDebug("Zetta delivery disabled — no playout tray for {MarketId}", marketId);
            return Result<PlayoutTrayStatus>.Success(
                new PlayoutTrayStatus(fileName, Enabled: false, AwaitingPickup: false, WrittenAtUtc: null));
        }

        var probe = await fileWriter.GetPendingWriteTimeAsync(fileName, cancellationToken);
        if (probe.IsFailure)
        {
            logger.LogError("Zetta tray probe failed for {MarketId} — {Error}", marketId, probe.Error);
            return Result<PlayoutTrayStatus>.Failure($"Zetta tray probe failed: {probe.Error}");
        }

        var status = probe.Value.Match(
            some: writtenAt => new PlayoutTrayStatus(fileName, Enabled: true, AwaitingPickup: true, WrittenAtUtc: writtenAt),
            none: () => new PlayoutTrayStatus(fileName, Enabled: true, AwaitingPickup: false, WrittenAtUtc: null));

        logger.LogDebug(
            "Zetta tray for {MarketId}: {FileName} {State}",
            marketId, fileName, status.AwaitingPickup ? "awaiting pickup" : "collected");
        return Result<PlayoutTrayStatus>.Success(status);
    }

    /// <summary>
    ///     Builds the drop file name for a resolved market code exactly as delivery writes it.
    ///     Shared by <see cref="DeliverAsync" /> and <see cref="GetTrayStatusAsync" /> so the
    ///     tray probe can never disagree with delivery about which file to look for — a swap
    ///     decision made on a mismatched name would probe the wrong file.
    /// </summary>
    private string BuildDeliveryFileName(string marketCode)
    {
        return _zettaOptions.FileNamePattern
            .Replace("{MarketId}", marketCode, StringComparison.Ordinal) + ".wav";
    }

    private async Task<byte[]> DownloadFromBlobAsync(string audioFileName, CancellationToken cancellationToken)
    {
        var containerClient = blobServiceClient.GetBlobContainerClient(_storageOptions.ContainerName);
        var blobClient = containerClient.GetBlobClient(audioFileName);
        var response = await blobClient.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToArray();
    }

    private static bool IsSafePathSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        if (segment.Contains("..", StringComparison.Ordinal)) return false;
        if (segment.IndexOfAny(['/', '\\', ':', '\0']) >= 0) return false;
        return segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}

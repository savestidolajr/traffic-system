using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Resolves filler audio: a cache hit downloads the stored blob and its measured
///     duration/word-timings; a cache miss synthesizes once, trims/normalizes exactly like the
///     traffic pipeline, uploads the result, and persists the cache metadata for next time.
/// </summary>
public sealed class FillerAudioCacheService(
    IFillerAudioCacheRepository repository,
    BlobServiceClient blobServiceClient,
    IOptions<StorageOptions> storageOptions,
    IAudioSynthesizer audioSynthesizer,
    WavDurationProcessor wavDurationProcessor,
    WavLoudnessNormalizer loudnessNormalizer,
    ILogger<FillerAudioCacheService> logger) : IFillerAudioCacheService
{
    private readonly StorageOptions _storageOptions = storageOptions.Value;

    // Container existence is ensured once per service instance: every cache-miss upload used to
    // issue CreateIfNotExistsAsync, and each call on the already-existing container logged a
    // 409 ContainerAlreadyExists SDK warning — a bulletin resolving several uncached statements
    // fired one warning per statement. Same flag+lock shape as TableRepositoryBase; the guard is
    // per-instance (this service stays scoped because it depends on scoped audio providers), so
    // it dedupes the repeated creates within one resolution burst while a cold-start create still
    // happens whenever this instance's container has never been touched. Real errors still throw.
    private readonly SemaphoreSlim _ensureContainerLock = new(1, 1);
    private volatile bool _containerEnsured;

    private async Task EnsureContainerExistsAsync(CancellationToken ct)
    {
        if (_containerEnsured) return;

        await _ensureContainerLock.WaitAsync(ct);
        try
        {
            if (_containerEnsured) return;

            var containerClient = blobServiceClient.GetBlobContainerClient(_storageOptions.ContainerName);
            if (blobServiceClient.AccountName == StorageOptions.LocalEmulatorAccount)
            {
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);
            }
            else
            {
                await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);
            }

            _containerEnsured = true;
        }
        finally
        {
            _ensureContainerLock.Release();
        }
    }

    public async Task<Result<ResolvedFillerAudio>> ResolveAsync(
        FillerStatement statement, string voiceId, VoiceSettings settings, string model,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = FillerAudioCacheKey.Build(voiceId, settings, model);

        var clip = await ResolveClipAsync(
            statement.Id, statement.Text, cacheKey, voiceId, settings, model, cancellationToken);
        if (clip.IsFailure)
            return Result<ResolvedFillerAudio>.Failure(clip.Error);

        return Result<ResolvedFillerAudio>.Success(
            new ResolvedFillerAudio(clip.Value.Audio, clip.Value.DurationSeconds, clip.Value.WordTimings));
    }

    public async Task<Result<ResolvedCartClip>> ResolveCartAsync(
        string scriptText, string voiceId, VoiceSettings settings, string model,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = FillerCartAudioKey.Build(scriptText, voiceId, settings, model);

        var clip = await ResolveClipAsync(
            FillerCartAudioKey.CartStatementId, scriptText, cacheKey, voiceId, settings, model, cancellationToken);
        if (clip.IsFailure)
            return Result<ResolvedCartClip>.Failure(clip.Error);

        return Result<ResolvedCartClip>.Success(new ResolvedCartClip(
            BlobUri(FillerCartAudioKey.CartStatementId, cacheKey).AbsoluteUri,
            clip.Value.Audio, clip.Value.DurationSeconds, clip.Value.WordTimings));
    }

    // Shared cache-hit/miss pipeline for filler statements and the filler cart. The cache key is
    // a parameter because the two public paths compute DIFFERENT keys (statement key vs cart key
    // with script hash). The blob URL is NOT materialised here: only the cart caller needs it
    // (and only the cart tests stub the blob client's Uri) — the statement path must not touch
    // the blob client's address just to throw the URL away.
    private async Task<Result<ResolvedClip>> ResolveClipAsync(
        string statementId, string scriptText, string cacheKey,
        string voiceId, VoiceSettings settings, string model, CancellationToken ct)
    {
        var existing = await repository.GetAsync(statementId, cacheKey, ct);
        if (existing.IsFailure)
            return Result<ResolvedClip>.Failure(existing.Error);

        if (existing.Value.HasValue)
        {
            var clip = existing.Value.Value;
            var download = await DownloadBlobAsync(statementId, cacheKey, ct);
            if (download.IsFailure)
                return Result<ResolvedClip>.Failure(download.Error);
            return Result<ResolvedClip>.Success(
                new ResolvedClip(download.Value, clip.DurationSeconds, clip.WordTimings));
        }

        var synth = await audioSynthesizer.SynthesizeAsync(
            scriptText, Option<VoiceSettings>.Some(settings), Option<string>.Some(voiceId),
            Option<string>.Some(model), ct);
        if (synth.IsFailure)
            return Result<ResolvedClip>.Failure(synth.Error);

        // Trim BOTH edges: leading silence adds directly to the stitch joint (the orchestrator
        // inserts its own intended pause between traffic read and filler — untrimmed lead made
        // the on-air gap ~2x every natural sentence boundary; 2026-07-20 job 1700e9c3), and
        // trailing silence pads the measured duration the gap math depends on.
        byte[] leadTrimmed = wavDurationProcessor.TrimLeadingSilence(synth.Value.Audio, out double leadSeconds);
        byte[] trimmed = wavDurationProcessor.TrimTrailingSilence(leadTrimmed);
        byte[] normalized = loudnessNormalizer.Normalize(trimmed);
        double duration = wavDurationProcessor.GetDuration(normalized);

        // Alignment timestamps reference the ORIGINAL synth audio — shift left by the trimmed
        // lead so karaoke highlighting stays anchored to the stitched clip.
        IReadOnlyList<WordTiming> wordTimings = synth.Value.Alignment.HasValue
            ? WordTimingBuilder.Build(synth.Value.Alignment.Value, scale: 1.0, maxDurationSeconds: duration + leadSeconds)
                .Select(wt => new WordTiming(wt.Word,
                    Math.Round(Math.Clamp(wt.StartSeconds - leadSeconds, 0, duration), 3),
                    Math.Round(Math.Clamp(wt.EndSeconds - leadSeconds, 0, duration), 3)))
                .ToList()
            : [];

        var upload = await UploadBlobAsync(statementId, cacheKey, normalized, ct);
        if (upload.IsFailure)
            return Result<ResolvedClip>.Failure(upload.Error);

        var saveResult = await repository.SaveAsync(
            new FillerAudioClip(statementId, cacheKey, duration, wordTimings, DateTime.UtcNow), ct);
        if (saveResult.IsFailure)
            logger.LogWarning(
                "[FillerAudioCache] Synthesized {StatementId}/{CacheKey} but failed to persist cache metadata: {Error} — will re-synthesize next time.",
                statementId, cacheKey, saveResult.Error);

        return Result<ResolvedClip>.Success(new ResolvedClip(normalized, duration, wordTimings));
    }

    /// <summary>The shared clip data both public paths wrap into their own result type — audio,
    /// measured duration, and word timings. The blob URL is added only by the cart caller.</summary>
    private sealed record ResolvedClip(
        byte[] Audio, double DurationSeconds, IReadOnlyList<WordTiming> WordTimings);

    public async Task<Result> InvalidateAsync(string fillerStatementId, CancellationToken cancellationToken = default)
    {
        var deleted = await repository.DeleteAllForStatementAsync(fillerStatementId, cancellationToken);
        if (deleted.IsFailure)
            return Result.Failure(deleted.Error);

        var containerClient = blobServiceClient.GetBlobContainerClient(_storageOptions.ContainerName);
        foreach (string cacheKey in deleted.Value)
        {
            try
            {
                await containerClient.GetBlobClient(BlobName(fillerStatementId, cacheKey))
                    .DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex)
            {
                logger.LogWarning(ex, "[FillerAudioCache] Failed to delete cached blob for {StatementId}/{CacheKey}",
                    fillerStatementId, cacheKey);
            }
        }
        return Result.Success();
    }

    private async Task<Result> UploadBlobAsync(string fillerStatementId, string cacheKey, byte[] audio, CancellationToken ct)
    {
        await EnsureContainerExistsAsync(ct);

        var containerClient = blobServiceClient.GetBlobContainerClient(_storageOptions.ContainerName);
        if (blobServiceClient.AccountName == StorageOptions.LocalEmulatorAccount)
        {
            // Re-assert public access on every upload: Azurite restarts drop container ACLs.
            // Only the create is once-per-process (see EnsureContainerExistsAsync).
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob, cancellationToken: ct);
        }

        using var ms = new MemoryStream(audio);
        await containerClient.GetBlobClient(BlobName(fillerStatementId, cacheKey)).UploadAsync(ms, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "audio/wav" }
        }, ct);
        return Result.Success();
    }

    private async Task<Result<byte[]>> DownloadBlobAsync(string fillerStatementId, string cacheKey, CancellationToken ct)
    {
        var blobClient = blobServiceClient.GetBlobContainerClient(_storageOptions.ContainerName)
            .GetBlobClient(BlobName(fillerStatementId, cacheKey));
        var download = await blobClient.DownloadContentAsync(ct);
        return Result<byte[]>.Success(download.Value.Content.ToArray());
    }

    private Uri BlobUri(string fillerStatementId, string cacheKey) =>
        blobServiceClient.GetBlobContainerClient(_storageOptions.ContainerName)
            .GetBlobClient(BlobName(fillerStatementId, cacheKey)).Uri;

    private static string BlobName(string fillerStatementId, string cacheKey) => $"filler-cache/{fillerStatementId}/{cacheKey}.wav";
}

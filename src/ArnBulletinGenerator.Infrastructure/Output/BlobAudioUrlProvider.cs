using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.Output;

/// <summary>
///     Provides audio URLs with User Delegation SAS tokens for secure blob access.
///     Blob existence/properties and delegation keys are cached independently. Cache state is keyed
///     by the thread-safe <see cref="BlobServiceClient" /> instance, so the scoped provider shares
///     bounded state across HTTP requests without retaining arbitrary provider instances.
/// </summary>
public sealed class BlobAudioUrlProvider(
    BlobServiceClient blobServiceClient,
    IOptions<StorageOptions> options,
    ILogger<BlobAudioUrlProvider> logger,
    IOptions<BlobAudioCacheOptions>? cacheOptions = null,
    TimeProvider? timeProvider = null) : IAudioUrlProvider
{
    private static readonly ConditionalWeakTable<BlobServiceClient, BlobAudioCacheState> CacheByClient = new();

    private readonly StorageOptions _options = options.Value;
    private readonly BlobAudioCacheOptions _cacheOptions = cacheOptions?.Value ?? new();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly BlobAudioCacheState _cache = CacheByClient.GetValue(
        blobServiceClient, _ => new BlobAudioCacheState(cacheOptions?.Value ?? new()));

    public async Task<Result<string>> GetAudioUrlAsync(
        string audioFileName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(audioFileName))
            return Result<string>.Failure("Audio filename is empty");

        var containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);

        // Fallback bulletins (filler cart, and jobs sourced from one) persist an ABSOLUTE URL
        // in Job.AudioUrl rather than a bare blob name (QT-003, 2026-07-05). When the URL points
        // into our own private container, reduce it to the blob name so SAS generation works and
        // the console can play the audio; a genuinely external URL is returned untouched (we
        // cannot sign what we do not own).
        var ownBlobName = ResolveOwnBlobName(audioFileName);
        if (ownBlobName.IsNone)
        {
            logger.LogDebug("Audio URL is external — returning as-is: {Url}", audioFileName);
            return Result<string>.Success(audioFileName);
        }

        audioFileName = ownBlobName.Value;
        var blobClient = containerClient.GetBlobClient(audioFileName);

        // Keep the existence probe separate from the richer stream metadata probe. The former is
        // used by URL generation and the latter by range handling; both are bounded and single-flight.
        if (!await GetBlobExistsAsync(blobClient, audioFileName, cancellationToken))
        {
            logger.LogWarning("Audio blob not found: {BlobName}", audioFileName);
            return Result<string>.Failure($"Audio file not found: {audioFileName}");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset startsOn = now.AddMinutes(-5); // Allow for clock skew
        DateTimeOffset expiresOn = now.AddHours(1); // Keep the public token lifetime unchanged

        Uri sasUri;
        if (_options.IsLocalDevelopment)
        {
            // Azurite only supports SharedKey auth — User Delegation SAS requires Azure AD
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _options.ContainerName,
                BlobName = audioFileName,
                Resource = "b",
                StartsOn = startsOn,
                ExpiresOn = expiresOn
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            // Azurite well-known development credentials — safe for local dev only.
            // These are publicly documented and intentionally identical across all Azurite instances.
            var credential = new StorageSharedKeyCredential(
                "devstoreaccount1",
                "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==");

            var sasToken = sasBuilder.ToSasQueryParameters(credential);
            sasUri = new Uri($"{blobClient.Uri}?{sasToken}");
        }
        else
        {
            // Azure: use a cached User Delegation SAS signing key. The key cache refreshes before
            // the service-issued expiry, while each blob still receives a fresh short-lived token.
            var userDelegationKey = await GetUserDelegationKeyAsync(
                now, startsOn, expiresOn, cancellationToken);
            startsOn = userDelegationKey.SignedStartsOn > startsOn
                ? userDelegationKey.SignedStartsOn
                : startsOn;
            expiresOn = userDelegationKey.SignedExpiresOn < expiresOn
                ? userDelegationKey.SignedExpiresOn
                : expiresOn;
            if (expiresOn <= startsOn)
                return Result<string>.Failure("User delegation key is not valid for an audio SAS token");

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _options.ContainerName,
                BlobName = audioFileName,
                Resource = "b",
                StartsOn = startsOn,
                ExpiresOn = expiresOn
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasToken = sasBuilder.ToSasQueryParameters(
                userDelegationKey,
                blobServiceClient.AccountName);

            sasUri = new Uri($"{blobClient.Uri}?{sasToken}");

            logger.LogDebug("Generated User Delegation SAS URL for blob: {BlobName}, expires: {Expiry}",
                audioFileName, expiresOn);
        }

        return Result<string>.Success(sasUri.ToString());
    }

    public Option<string> ResolveOwnBlobName(string audioUrl)
    {
        if (string.IsNullOrEmpty(audioUrl))
            return Option<string>.None;

        if (!Uri.TryCreate(audioUrl, UriKind.Absolute, out var absoluteUrl) ||
            absoluteUrl.Scheme is not ("http" or "https"))
            return Option<string>.Some(audioUrl); // bare blob name

        var containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);
        string containerPath = containerClient.Uri.AbsolutePath.TrimEnd('/') + "/";
        bool ownContainer =
            string.Equals(absoluteUrl.Host, containerClient.Uri.Host, StringComparison.OrdinalIgnoreCase) &&
            absoluteUrl.AbsolutePath.StartsWith(containerPath, StringComparison.OrdinalIgnoreCase) &&
            absoluteUrl.AbsolutePath.Length > containerPath.Length;
        if (!ownContainer)
            return Option<string>.None;

        return Option<string>.Some(Uri.UnescapeDataString(absoluteUrl.AbsolutePath[containerPath.Length..]));
    }

    public async Task<Result<AudioStreamHandle>> OpenAudioStreamAsync(
        string audioUrl, AudioByteRange? range, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(audioUrl))
            return Result<AudioStreamHandle>.Failure("Audio filename is empty");

        var ownBlobName = ResolveOwnBlobName(audioUrl);
        if (ownBlobName.IsNone)
            return Result<AudioStreamHandle>.Failure("Audio URL is external to the audio container");

        string blobName = ownBlobName.Value;
        var blobClient = blobServiceClient
            .GetBlobContainerClient(_options.ContainerName)
            .GetBlobClient(blobName);

        try
        {
            var properties = await GetBlobPropertiesAsync(blobClient, blobName, cancellationToken);
            long totalLength = properties.ContentLength;
            string contentType = properties.ContentType;

            if (range is not { } requested)
            {
                var full = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
                return Result<AudioStreamHandle>.Success(
                    new AudioStreamHandle(full.Value.Content, 0, totalLength, totalLength, contentType));
            }

            if (requested.From >= totalLength)
                return Result<AudioStreamHandle>.Failure("Requested range not satisfiable");

            long from = requested.From;
            long toInclusive = Math.Min(requested.To ?? totalLength - 1, totalLength - 1);
            if (totalLength == 0 || toInclusive < from)
                return Result<AudioStreamHandle>.Failure("Requested range not satisfiable");

            long length = toInclusive - from + 1;
            var partial = await blobClient.DownloadStreamingAsync(
                new BlobDownloadOptions { Range = new HttpRange(from, length) }, cancellationToken);
            return Result<AudioStreamHandle>.Success(
                new AudioStreamHandle(partial.Value.Content, from, length, totalLength, contentType));
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            if (_cacheOptions.MetadataCacheEnabled)
                _cache.SetExists(BlobCacheKey(blobName), false, MetadataExpiry(_timeProvider.GetUtcNow()));
            logger.LogWarning("Audio blob not found for streaming: {BlobName}", blobName);
            return Result<AudioStreamHandle>.Failure($"Audio file '{blobName}' not found");
        }
    }

    private async Task<bool> GetBlobExistsAsync(
        BlobClient blobClient, string blobName, CancellationToken cancellationToken)
    {
        if (!_cacheOptions.MetadataCacheEnabled)
            return (await blobClient.ExistsAsync(cancellationToken)).Value;

        string cacheKey = BlobCacheKey(blobName);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_cache.TryGetExists(cacheKey, now, out bool cached))
            return cached;

        var flight = _cache.ExistenceFlights.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<bool>>(
                () => FetchBlobExistsAsync(blobClient, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            bool exists = await flight.Value;
            _cache.SetExists(cacheKey, exists, MetadataExpiry(now));
            return exists;
        }
        finally
        {
            _cache.ExistenceFlights.TryRemove(new KeyValuePair<string, Lazy<Task<bool>>>(cacheKey, flight));
        }
    }

    private static async Task<bool> FetchBlobExistsAsync(BlobClient blobClient, CancellationToken cancellationToken) =>
        (await blobClient.ExistsAsync(cancellationToken)).Value;

    private async Task<BlobMetadata> GetBlobPropertiesAsync(
        BlobClient blobClient, string blobName, CancellationToken cancellationToken)
    {
        if (!_cacheOptions.MetadataCacheEnabled)
            return await FetchBlobPropertiesAsync(blobClient, cancellationToken);

        string cacheKey = BlobCacheKey(blobName);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (_cache.TryGetProperties(cacheKey, now, out var cached))
            return cached;

        var flight = _cache.PropertiesFlights.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<BlobMetadata>>(
                () => FetchBlobPropertiesAsync(blobClient, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var properties = await flight.Value;
            _cache.SetProperties(cacheKey, properties, MetadataExpiry(now));
            _cache.SetExists(cacheKey, true, MetadataExpiry(now));
            return properties;
        }
        finally
        {
            _cache.PropertiesFlights.TryRemove(
                new KeyValuePair<string, Lazy<Task<BlobMetadata>>>(cacheKey, flight));
        }
    }

    private static async Task<BlobMetadata> FetchBlobPropertiesAsync(
        BlobClient blobClient, CancellationToken cancellationToken)
    {
        var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        string contentType = string.IsNullOrEmpty(properties.Value.ContentType)
            ? "audio/wav"
            : properties.Value.ContentType;
        return new BlobMetadata(properties.Value.ContentLength, contentType);
    }

    private async Task<UserDelegationKey> GetUserDelegationKeyAsync(
        DateTimeOffset now,
        DateTimeOffset startsOn,
        DateTimeOffset expiresOn,
        CancellationToken cancellationToken)
    {
        string key = blobServiceClient.AccountName;
        if (_cacheOptions.DelegationKeyCacheEnabled &&
            _cache.TryGetDelegationKey(key, now, DelegationRefreshWindow(), out var cached))
            return cached;

        if (!_cacheOptions.DelegationKeyCacheEnabled)
            return (await blobServiceClient.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken)).Value;

        var flight = _cache.DelegationFlights.GetOrAdd(
            key,
            _ => new Lazy<Task<UserDelegationKey>>(
                () => FetchDelegationKeyAsync(startsOn, expiresOn, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            var delegationKey = await flight.Value;
            DateTimeOffset cacheExpiry = delegationKey.SignedExpiresOn;
            DateTimeOffset configuredExpiry = now.AddMinutes(Math.Max(1, _cacheOptions.DelegationKeyTtlMinutes));
            if (configuredExpiry < cacheExpiry)
                cacheExpiry = configuredExpiry;
            _cache.SetDelegationKey(key, delegationKey, cacheExpiry);
            return delegationKey;
        }
        finally
        {
            _cache.DelegationFlights.TryRemove(
                new KeyValuePair<string, Lazy<Task<UserDelegationKey>>>(key, flight));
        }
    }

    private async Task<UserDelegationKey> FetchDelegationKeyAsync(
        DateTimeOffset startsOn,
        DateTimeOffset expiresOn,
        CancellationToken cancellationToken)
    {
        // Keep the provider's original one-hour service request; options govern cache retention,
        // while the key's own expiry always bounds the generated SAS.
        return (await blobServiceClient.GetUserDelegationKeyAsync(startsOn, expiresOn, cancellationToken)).Value;
    }

    private TimeSpan MetadataTtl() => TimeSpan.FromSeconds(Math.Max(1, _cacheOptions.MetadataTtlSeconds));
    private TimeSpan DelegationRefreshWindow() =>
        TimeSpan.FromSeconds(Math.Max(0, _cacheOptions.DelegationKeyRefreshBeforeExpirySeconds));
    private DateTimeOffset MetadataExpiry(DateTimeOffset now) => now + MetadataTtl();

    private string BlobCacheKey(string blobName) => $"{_options.ContainerName}:{blobName}";

    private readonly record struct BlobMetadata(long ContentLength, string ContentType);

    private sealed class BlobAudioCacheState(BlobAudioCacheOptions options)
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, TimedValue<bool>> _exists = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TimedValue<BlobMetadata>> _properties = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TimedValue<UserDelegationKey>> _delegationKeys = new(StringComparer.Ordinal);
        private readonly int _metadataLimit = Math.Max(1, options.MetadataCacheSizeLimit);
        private readonly int _delegationLimit = Math.Max(1, options.DelegationKeyCacheSizeLimit);
        private long _sequence;

        internal ConcurrentDictionary<string, Lazy<Task<bool>>> ExistenceFlights { get; } = new(StringComparer.Ordinal);
        internal ConcurrentDictionary<string, Lazy<Task<BlobMetadata>>> PropertiesFlights { get; } = new(StringComparer.Ordinal);
        internal ConcurrentDictionary<string, Lazy<Task<UserDelegationKey>>> DelegationFlights { get; } = new(StringComparer.Ordinal);

        internal bool TryGetExists(string key, DateTimeOffset now, out bool value)
        {
            lock (_gate)
            {
                if (_exists.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
                {
                    _exists[key] = entry with { Sequence = ++_sequence };
                    value = entry.Value;
                    return true;
                }

                _exists.Remove(key);
                value = default;
                return false;
            }
        }

        internal void SetExists(string key, bool value, DateTimeOffset expiresAt)
        {
            lock (_gate)
            {
                _exists[key] = new TimedValue<bool>(value, expiresAt, ++_sequence);
                Trim(_exists, _metadataLimit);
            }
        }

        internal bool TryGetProperties(string key, DateTimeOffset now, out BlobMetadata value)
        {
            lock (_gate)
            {
                if (_properties.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
                {
                    _properties[key] = entry with { Sequence = ++_sequence };
                    value = entry.Value;
                    return true;
                }

                _properties.Remove(key);
                value = default;
                return false;
            }
        }

        internal void SetProperties(string key, BlobMetadata value, DateTimeOffset expiresAt)
        {
            lock (_gate)
            {
                _properties[key] = new TimedValue<BlobMetadata>(value, expiresAt, ++_sequence);
                Trim(_properties, _metadataLimit);
            }
        }

        internal bool TryGetDelegationKey(
            string key, DateTimeOffset now, TimeSpan refreshWindow, out UserDelegationKey value)
        {
            lock (_gate)
            {
                DateTimeOffset refreshAt = now + refreshWindow;
                if (_delegationKeys.TryGetValue(key, out var entry) && entry.ExpiresAt > refreshAt)
                {
                    _delegationKeys[key] = entry with { Sequence = ++_sequence };
                    value = entry.Value;
                    return true;
                }

                _delegationKeys.Remove(key);
                value = default!;
                return false;
            }
        }

        internal void SetDelegationKey(string key, UserDelegationKey value, DateTimeOffset expiresAt)
        {
            lock (_gate)
            {
                _delegationKeys[key] = new TimedValue<UserDelegationKey>(value, expiresAt, ++_sequence);
                Trim(_delegationKeys, _delegationLimit);
            }
        }

        private void Trim<T>(Dictionary<string, TimedValue<T>> values, int limit)
        {
            while (values.Count > limit)
            {
                string? oldest = values.MinBy(pair => pair.Value.Sequence).Key;
                if (oldest is null)
                    return;
                values.Remove(oldest);
            }
        }

        private readonly record struct TimedValue<T>(T Value, DateTimeOffset ExpiresAt, long Sequence);
    }
}

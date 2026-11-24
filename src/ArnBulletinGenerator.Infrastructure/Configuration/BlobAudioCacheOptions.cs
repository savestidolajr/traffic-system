namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Process-local bounds for the audio provider's blob metadata and user-delegation-key caches.
///     The caches are deliberately independent so either one can be disabled during rollout without
///     changing private-blob authorization or the audio response contract.
/// </summary>
public sealed class BlobAudioCacheOptions
{
    public const string SectionName = "BlobAudioCache";

    /// <summary>Enables the bounded existence/properties cache used by audio URL and stream reads.</summary>
    public bool MetadataCacheEnabled { get; set; } = true;

    /// <summary>Enables reuse of a user delegation key until the refresh window before expiry.</summary>
    public bool DelegationKeyCacheEnabled { get; set; } = true;

    /// <summary>Absolute TTL for blob existence/properties entries.</summary>
    public int MetadataTtlSeconds { get; set; } = 60;

    /// <summary>Maximum number of existence/properties entries retained by one provider client.</summary>
    public int MetadataCacheSizeLimit { get; set; } = 2_048;

    /// <summary>Maximum TTL for a cached delegation key; the key's own expiry is always honoured.</summary>
    public int DelegationKeyTtlMinutes { get; set; } = 60;

    /// <summary>Refreshes the delegation key before its service-issued expiry.</summary>
    public int DelegationKeyRefreshBeforeExpirySeconds { get; set; } = 300;

    /// <summary>Maximum number of delegation keys retained by one provider client.</summary>
    public int DelegationKeyCacheSizeLimit { get; set; } = 4;
}

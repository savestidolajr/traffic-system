using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>Controls the short-lived market-scope cache used by operator authorization.</summary>
public sealed record MarketAccessOptions
{
    public const string SectionName = "MarketAccess";

    /// <summary>Disables both positive and negative scope caching when true.</summary>
    public bool CacheDisabled { get; init; }

    /// <summary>Absolute lifetime for successfully resolved scopes, in seconds.</summary>
    [Range(1, 300)]
    public int CacheTtlSeconds { get; init; } = 45;

    /// <summary>
    ///     Lifetime for an unrestricted result caused by an absent assignment row. This is
    ///     deliberately shorter than a positive scope: a newly-created restriction should become
    ///     visible quickly even if a write path outside this process cannot evict the local cache.
    ///     A present assignment with an empty market list is unrestricted but is never cached.
    /// </summary>
    [Range(1, 60)]
    public int EmptyScopeCacheTtlSeconds { get; init; } = 5;

    /// <summary>Builds the canonical cache key for a caller identity.</summary>
    public static string ScopeCacheKey(string objectId)
        => $"{SectionName}:Scope:{objectId.Trim().ToUpperInvariant()}";

    /// <summary>Builds the canonical cache key for an SWA user-id → Entra object-id mapping.</summary>
    public static string AliasCacheKey(string swaUserId)
        => $"{SectionName}:Alias:{swaUserId.Trim().ToUpperInvariant()}";
}

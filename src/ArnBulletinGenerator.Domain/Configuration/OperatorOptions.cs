using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Controls the bounded operator-directory display cache and verified sign-in registration.
///     These switches affect display/telemetry work only; Entra role claims and market assignment
///     storage remain the authorization boundary.
/// </summary>
public sealed record OperatorOptions
{
    public const string SectionName = "Operators";

    /// <summary>Disables the Graph role-holder display cache and stale-on-error fallback.</summary>
    public bool GraphCacheDisabled { get; init; }

    /// <summary>Disables reuse of the registrar's assignment result by <c>/operators/me</c>.</summary>
    public bool RequestScopeReuseDisabled { get; init; }

    /// <summary>Disables per-object-id registration coalescing.</summary>
    public bool RegistrationSingleFlightDisabled { get; init; }

    /// <summary>Disables the short registration burst debounce while retaining single-flight coalescing.</summary>
    public bool RegistrationDebounceDisabled { get; init; }

    /// <summary>Fresh lifetime of a successful Graph role-holder snapshot.</summary>
    [Range(1, 3600)]
    public int GraphCacheTtlSeconds { get; init; } = 60;

    /// <summary>Maximum age of a Graph snapshot that may be shown after a later Graph failure.</summary>
    [Range(0, 86400)]
    public int GraphCacheStaleOnErrorSeconds { get; init; } = 300;

    /// <summary>Maximum distinct Entra groups expanded in one directory snapshot.</summary>
    [Range(1, 10000)]
    public int MaxGroupExpansions { get; init; } = 100;

    /// <summary>Maximum transitive user members accepted from one Entra group.</summary>
    [Range(1, 100000)]
    public int MaxMembersPerGroup { get; init; } = 5000;

    /// <summary>Delay used to gather simultaneous sign-in payloads for one object id.</summary>
    [Range(0, 5000)]
    public int RegistrationDebounceMilliseconds { get; init; } = 25;

    /// <summary>
    ///     Minimum interval between durable timestamp-only sign-in updates. Name, UPN and role
    ///     changes bypass this interval and are persisted synchronously.
    /// </summary>
    [Range(0, 86400)]
    public int SignInTimestampRefreshSeconds { get; init; } = 60;
}

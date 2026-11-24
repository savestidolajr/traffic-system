namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>Bounds and expiry settings for the two-level HERE map attributes cache.</summary>
public sealed class MapAttributesCacheOptions
{
    public const string SectionName = "MapAttributesCache";

    /// <summary>Maximum number of segment entries retained by a process-local L1 cache.</summary>
    public int L1SizeLimit { get; set; } = 20_000;

    /// <summary>Maximum approximate bytes retained by a process-local L1 cache.</summary>
    public long L1ByteLimit { get; set; } = 16 * 1024 * 1024;

    /// <summary>Positive cache lifetime. Map attributes are static facts for this period.</summary>
    public int PositiveTtlHours { get; set; } = 24;

    /// <summary>Lifetime for a confirmed absent segment.</summary>
    public int NegativeTtlMinutes { get; set; } = 10;

}

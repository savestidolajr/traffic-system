namespace ArnBulletinGenerator.Domain.ValueObjects;

/// <summary>
///     The single canonical normalization for market identifiers and market-scoped storage keys:
///     trimmed, lower-invariant. Every Table Storage partition key, market lookup, and
///     market-scoped configuration key MUST normalize through here — scattered ad-hoc
///     normalization is how a free-text market value silently missed every per-market lookup
///     (filler statements, priority roads, guidance) for an entire station (mildura, 2026-07-08).
/// </summary>
public static class MarketKey
{
    /// <summary>Canonical form of a market id or market-scoped key: trimmed, lower-invariant.</summary>
    public static string Normalize(string market) => market.Trim().ToLowerInvariant();
}

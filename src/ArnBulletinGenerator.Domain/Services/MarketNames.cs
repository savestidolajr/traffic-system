using System.Collections.Frozen;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The single canonical market-id → human-readable place-name mapping ("what is this market
///     called in English"). Both the LLM prompt layer (<c>LlmScriptGenerator</c>) and the bookend
///     token resolver (<see cref="BookendTokenResolver" />) consume this ONE table, so a market
///     added or renamed in one place cannot silently drift away from the other — the defect class
///     that put "Goldcoast", "Cairns-4ca", "Portaugusta", "Portlincoln" and "Demo" into live
///     output (found 2026-07-29, fixed by keying every live MarketId here).
///     <para>
///         A market id is a storage key, not a place name: "portaugusta" does not recover "Port
///         Augusta" by slug munging, and "cairns-4ca" carries a call-sign suffix that must never
///         air. The scheduled poller passes the schedule's <c>MarketId</c> everywhere, so every
///         live MarketId appears as an exact key; short codes ("syd", "mel") and the spaced
///         "gold coast" form exist for the free-text location path. Deliberately NO short codes
///         for the regional markets: the prompt path falls back to a substring scan, and a
///         two-letter key would match far too much.
///     </para>
///     <para>
///         The authoritative set is <c>GET /api/schedules</c> — when adding a market, add it here
///         and take the place name from that schedule's data rather than from memory.
///     </para>
/// </summary>
public static class MarketNames
{
    /// <summary>Every known market id / alias mapped to its human-readable place name.</summary>
    public static FrozenDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sydney"] = "Sydney",
            ["syd"] = "Sydney",
            ["melbourne"] = "Melbourne",
            ["mel"] = "Melbourne",
            ["brisbane"] = "Brisbane",
            ["bne"] = "Brisbane",
            ["perth"] = "Perth",
            ["per"] = "Perth",
            ["adelaide"] = "Adelaide",
            ["adl"] = "Adelaide",
            ["darwin"] = "Darwin",
            ["drw"] = "Darwin",
            ["gold coast"] = "Gold Coast",
            ["goldcoast"] = "Gold Coast",
            ["gc"] = "Gold Coast",
            // Regional and secondary markets, keyed by their live MarketId.
            ["cairns-4ca"] = "Cairns",
            ["cairns"] = "Cairns",
            ["mildura"] = "Mildura",
            ["portaugusta"] = "Port Augusta",
            ["portlincoln"] = "Port Lincoln",
            ["queenstown"] = "Queenstown",
            ["demo-coast"] = "Demo Coast"
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}

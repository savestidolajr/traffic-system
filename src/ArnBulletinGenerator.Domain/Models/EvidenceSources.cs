namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Canonical provenance identifiers for evidence records. Every freshly-fetched record carries
///     its sources explicitly — HERE included — so no feed is structurally implicit. A null
///     <see cref="TrafficIncident.Sources" /> appears only on legacy persisted rows and fixtures,
///     and every consumer resolves it through <see cref="Of" />.
/// </summary>
public static class EvidenceSources
{
    public const string Here = "here";

    /// <summary>Shared instance: stamping HERE provenance on every record costs one reference.</summary>
    public static readonly IReadOnlyList<string> HereOnly = [Here];

    /// <summary>The record's provenance, resolving legacy null to HERE (the only pre-fusion source).</summary>
    public static IReadOnlyList<string> Of(IReadOnlyList<string>? sources) =>
        sources is { Count: > 0 } ? sources : HereOnly;

    /// <summary>Length-weighted union of the given records' provenance, deterministic order.</summary>
    public static IReadOnlyList<string> Union(IEnumerable<TrafficIncident> incidents)
    {
        var union = new List<string>();
        foreach (var incident in incidents)
        foreach (string source in Of(incident.Sources))
        {
            if (!union.Contains(source, StringComparer.OrdinalIgnoreCase))
                union.Add(source);
        }

        return union.Count == 0 ? HereOnly : union;
    }
}

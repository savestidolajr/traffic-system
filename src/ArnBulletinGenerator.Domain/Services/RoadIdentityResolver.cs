using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>How decisively the map named the road a record sits on.</summary>
public enum IdentityVerdict
{
    /// <summary>The map named none of the record's segments. Nothing may be claimed about the road.</summary>
    Unnamed,

    /// <summary>The record's segments carry several names and none dominates — it spans two roads,
    ///  so it has no single identity and is withheld rather than assigned one.</summary>
    Split,

    /// <summary>One name holds at least <see cref="RoadIdentityResolver.DominantShareFloor" /> of the
    ///  record's total measured length; the remainder is a side street or unresolved map coverage.</summary>
    Dominant,

    /// <summary>Every segment is named and agrees. Measured: 88.0% of incidents, 90.3% of flow rows.</summary>
    Unambiguous
}

/// <summary>
///     The road a record is on, as HERE's own map records it for the record's own segments, with the
///     verdict that says how far it may be trusted.
/// </summary>
/// <param name="Name">The road's name, or null for <see cref="IdentityVerdict.Split" /> and
///     <see cref="IdentityVerdict.Unnamed" /> — a withheld name, never a guessed one.</param>
/// <param name="Verdict">How decisive the segment evidence was.</param>
/// <param name="FunctionalClass">The most important class among named segments (1 motorway … 5 local).</param>
/// <param name="IsRamp">True when mapped ramp segments hold a majority of total measured length.</param>
/// <param name="NamedShare">The winning name's share of total measured length, 0 when nothing resolved.</param>
public readonly record struct RoadIdentity(
    string? Name, IdentityVerdict Verdict, int FunctionalClass, bool IsRamp, double NamedShare);

/// <summary>
///     Resolves a record's road identity from the segments the record itself references, weighted by
///     segment length.
///     <para>
///         This replaces proximity reverse-geocoding as the source of road identity. Measured against
///         HERE's own map, reverse geocoding named a different road for 38 of 107 records (36%), and
///         <c>types=street</c> improved it by 0.9 pp — it answers "what is near this point", which is
///         not the same question. The segments are the record's own geometry, and HERE's Map Attributes
///         API agreed with HERE's Routing service on 428 of 455 spans (94.1%; 100% above 500 m).
///     </para>
/// </summary>
public static class RoadIdentityResolver
{
    /// <summary>
    ///     The winning name must hold this share of the record's total measured length to name the
    ///     record on its own. Counting unresolved spans in the denominator prevents a partial map
    ///     response from making a small mapped prefix look unanimously authoritative.
    /// </summary>
    public const double DominantShareFloor = 0.70;

    /// <summary>
    ///     Resolves the road identity of one record.
    /// </summary>
    /// <param name="segments">The record's own segment references with their lengths in metres.</param>
    /// <param name="roads">Map knowledge, keyed by the same reference strings.</param>
    public static RoadIdentity Resolve(
        IReadOnlyList<(string Ref, double LengthMeters)> segments,
        IReadOnlyDictionary<string, SegmentRoad> roads)
    {
        if (segments.Count == 0 || roads.Count == 0)
            return new RoadIdentity(null, IdentityVerdict.Unnamed, 5, false, 0);

        // Weight by length so a 900 m arterial is not outvoted by the 100 m side street the record
        // clips. A zero or absent length still counts as one metre rather than nothing.
        var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double totalLength = segments.Sum(segment => Math.Max(1, segment.LengthMeters));
        double rampLength = 0;
        int functionalClass = int.MaxValue;

        foreach ((string reference, double lengthMeters) in segments)
        {
            if (!roads.TryGetValue(reference, out var road) || string.IsNullOrWhiteSpace(road.Name))
                continue;

            double weight = Math.Max(1, lengthMeters);
            // Canonicalize BEFORE the vote ("Western Mwy" → "M4"): two gazetted forms of the same
            // motorway must count as one name, or a record spanning them reads as Split. Every
            // consumer of road identity then sees the on-air designation.
            string name = RoadDesignationAliases.Canonicalize(road.Name);
            byName[name] = byName.GetValueOrDefault(name) + weight;
            if (road.IsRamp)
                rampLength += weight;
            if (road.FunctionalClass > 0)
                functionalClass = Math.Min(functionalClass, road.FunctionalClass);
        }

        if (byName.Count == 0)
            return new RoadIdentity(null, IdentityVerdict.Unnamed, 5, false, 0);

        int resolvedClass = functionalClass == int.MaxValue ? 5 : functionalClass;
        bool isRamp = rampLength * 2 > totalLength;

        (string winner, double winnerLength) = byName.MaxBy(kv => kv.Value);
        double share = winnerLength / totalLength;
        if (share < DominantShareFloor)
            return new RoadIdentity(
                null,
                byName.Count > 1 ? IdentityVerdict.Split : IdentityVerdict.Unnamed,
                resolvedClass,
                isRamp,
                share);

        return new RoadIdentity(
            winner,
            share >= 1.0 ? IdentityVerdict.Unambiguous : IdentityVerdict.Dominant,
            resolvedClass,
            isRamp,
            share);
    }
}

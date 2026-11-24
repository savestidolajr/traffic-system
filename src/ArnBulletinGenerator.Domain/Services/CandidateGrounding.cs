using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The single blocking invariant for candidate grounding. This is deliberately judgement-free
///     set membership over our own vocabulary in both directions: the writer-visible candidate
///     vocabulary is assembled from every road collection it legitimately received, and the full
///     fetched vocabulary is reduced to names that were deliberately left out of that candidate
///     set before the server-owned matcher checks the script. Neither side is inferred from prose.
///     That makes this safe to block on. On test job <c>d4db0a5a</c> (2026-08-28), Forest Way was
///     present in the fetched pool but ranked 18 of 23 and was not a candidate; it was nevertheless
///     voiced, and its persisted sentence had no corridor keys or evidence ids. This check catches
///     that wrong-collection defect without making a stylistic judgement about the script.
///     <para>
///         "Every road collection" has to include the ones that name a road WITHOUT its bare name
///         ever entering the candidate set. A monitored route arrives as "the M4 from Parramatta to
///         the city", and a candidate's own incident text names the cross street that locates it -
///         "South Road at James Congdon Drive" is how traffic radio states a position. Missing
///         these cost dev jobs <c>5e613a74</c> (M4) and <c>cf352c34</c> (James Congdon Drive) all
///         four generation attempts and produced no audio on 2026-08-29, rejecting reads that were
///         voicing exactly what the brief supplied. Any NEW channel that reaches the writer must be
///         added to the supplied-evidence corpus or it becomes a false rejection.
///     </para>
///     <para>
///         This intentionally cannot detect a road invented from world knowledge that exists nowhere
///         in the fetched data. That is a different fabrication defect class, covered by the
///         advisory fidelity detectors; blocking on it would require the prose name extractor that
///         proved unsafe by flagging ordinary sentence-initial words and the market name.
///     </para>
/// </summary>
public static class CandidateGrounding
{
    /// <summary>
    ///     Reports fetched roads that the script named despite not being in any collection handed to
    ///     the writer as a legitimate candidate vocabulary. Road names are trimmed and compared
    ///     case-insensitively; matching itself remains the server-owned, alias-aware and
    ///     abbreviation-aware <see cref="ScriptProvenanceMapper.MapRoadPhraseSpans" /> contract.
    /// </summary>
    public static CandidateGroundingResult Check(string script, BroadcastWithTraffic context)
    {
        var candidateNames = context.Corridors
            .Select(corridor => corridor.RoadName)
            .Concat(context.GoodRunRoads)
            .Concat(context.FlowPool.Select(road => road.RoadName))
            .Concat(context.MonitoredRoutes.Select(route => route.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fetchedOnlyNames = context.AllCorridors
            .Select(corridor => corridor.RoadName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => !candidateNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Nothing was cut from the fetched pool, so there can be no non-candidate road to reject.
        if (fetchedOnlyNames.Count == 0)
            return new CandidateGroundingResult(false, []);

        // A road can be handed to the writer WITHOUT its bare name ever entering the candidate set,
        // and then voicing it is correct rather than a fault. Two measured channels do this:
        //
        //   * a monitored route arrives as a PHRASE - "the M4 from Parramatta to the city" - so the
        //     corridor it covers is never matched by name. Dev job 5e613a74 (2026-08-29) failed all
        //     four generation attempts on "M4" for exactly this reason.
        //   * a candidate's own incident text names the CROSS STREET that locates it. Dev job
        //     cf352c34 (2026-08-29) failed all four attempts on "James Congdon Drive", which the
        //     evidence supplied as location "At James Congdon Drive" on both South Road and Port
        //     Road. "South Road at James Congdon Drive" is how traffic radio states a position, so
        //     rejecting it rejected the correct read; cross streets are pervasive, so this class
        //     fires constantly and cost that market its audio.
        //
        // Both are reduced the same way: gather the text the writer was actually given for its
        // candidates and drop any fetched-only road that text already names. Still judgement-free
        // set work over our own vocabulary through the same server-owned matcher the script side
        // uses - the corpus is our evidence, never the model's prose.
        string suppliedEvidence = string.Join(
            '\n',
            context.MonitoredRoutes
                .Select(route => route.Name)
                .Concat(context.Corridors.SelectMany(corridor => corridor.Incidents
                    .SelectMany(incident => new[]
                    {
                        incident.Description,
                        incident.Summary.GetValueOr(string.Empty)
                    })))
                .Where(text => !string.IsNullOrWhiteSpace(text)));

        if (suppliedEvidence.Length > 0)
        {
            var namedInEvidence = ScriptProvenanceMapper
                .MapRoadPhraseSpans(suppliedEvidence, fetchedOnlyNames)
                .Select(match => match.RoadName.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            fetchedOnlyNames = fetchedOnlyNames
                .Where(name => !namedInEvidence.Contains(name))
                .ToList();

            if (fetchedOnlyNames.Count == 0)
                return new CandidateGroundingResult(false, []);
        }

        var nonCandidateRoads = ScriptProvenanceMapper
            .MapRoadPhraseSpans(script, fetchedOnlyNames)
            .Select(match => match.RoadName.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CandidateGroundingResult(nonCandidateRoads.Count > 0, nonCandidateRoads);
    }
}

/// <summary>
///     The result of checking whether a script voiced a fetched road outside the candidate set.
/// </summary>
public readonly record struct CandidateGroundingResult(
    bool HasNonCandidateRoad,
    IReadOnlyList<string> NonCandidateRoads);

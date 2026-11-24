using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Selects the small set of raw HERE flow rows whose road identity can affect a bulletin.
///     Free-text labels never participate; callers must resolve every selected row against Map
///     Attributes before treating its road name as evidence.
/// </summary>
internal sealed class HereFlowCandidateSelector(
    int candidateFloor = 20,
    int candidateCeiling = 40,
    int referenceCeiling = 128,
    int oversizedRowReferenceCeiling = 320,
    int inspectionCeiling = 320)
{
    internal FlowCandidateRanking Rank(
        IReadOnlyList<FlowIdentityCandidate> rows,
        FlowCandidateBand band,
        double congestionJamFactorThreshold)
    {
        if (rows.Count == 0)
            return FlowCandidateRanking.Empty;
        var ranked = new PriorityQueue<RankedFlowCandidate, FlowRank>();
        int eligibleRows = 0;

        foreach (var row in rows)
        {
            int functionalClass = row.FunctionalClass is > 0 and <= 5 ? row.FunctionalClass : 5;
            double score;
            bool admissionCandidate = false;
            switch (band)
            {
                case FlowCandidateBand.FreeFlow:
                    if (!row.IsRealTime || row.EffectiveJamFactor >= 4.0)
                        continue;
                    score = CorridorAggregator.CalculateFlowScore(functionalClass, row.LengthMeters);
                    break;
                case FlowCandidateBand.Congestion:
                    if (!row.IsRealTime || row.EffectiveJamFactor < congestionJamFactorThreshold)
                        continue;
                    admissionCandidate =
                        row.JamFactorMeters / Math.Max(1, row.LengthMeters) >= CongestionSegment.HeavyJamFactor
                        || row.StandstillMeters >= CongestionSegment.MinHotSpotMeters;
                    score = (admissionCandidate ? 1_000_000 : 0)
                            + row.EffectiveJamFactor * 1_000
                            + (6 - functionalClass) * 100
                            + Math.Log10(Math.Max(1, row.LengthMeters));
                    break;
                case FlowCandidateBand.FreeFlowContradiction:
                    if (row.IsRealTime && row.EffectiveJamFactor < 4.0)
                        continue;
                    score = (6 - functionalClass) * 100
                            + Math.Log10(Math.Max(1, row.LengthMeters))
                            + (row.IsRealTime ? row.EffectiveJamFactor : 10.0) * 1_000;
                    break;
                default:
                    continue;
            }
            eligibleRows++;

            var rankedCandidate = new RankedFlowCandidate(row, admissionCandidate);
            var rank = new FlowRank(score, row.LengthMeters, row.Index);
            if (ranked.Count < inspectionCeiling)
            {
                ranked.Enqueue(rankedCandidate, rank);
            }
            else
            {
                ranked.TryPeek(out _, out var worst);
                if (rank.CompareTo(worst) > 0)
                    ranked.EnqueueDequeue(rankedCandidate, rank);
            }
        }

        // HERE's description is deliberately absent from this ordering. Live probes showed that it
        // can be a suburb, depot or neighbouring road, so using it to group rows can silently discard
        // distinct roads before their authoritative segment identities have been resolved. The bounded
        // heap keeps broad markets O(rows × log 320), not O(rows × log rows).
        var candidates = ranked.UnorderedItems
            .Select(item => item.Element)
            .OrderByDescending(candidate => candidate.AdmissionCandidate)
            .ThenByDescending(candidate => candidate.RankScore(band, congestionJamFactorThreshold))
            .ThenByDescending(candidate => candidate.Row.LengthMeters)
            .ThenBy(candidate => candidate.Row.Index)
            .ToList();
        return new FlowCandidateRanking(candidates, eligibleRows > inspectionCeiling);
    }

    internal FlowIdentitySelection SelectRanked(
        IReadOnlyList<RankedFlowCandidate> rankedCandidates,
        int desiredRoads,
        int candidateOffset,
        int remainingStageReferences)
    {
        if (desiredRoads <= 0 || candidateOffset >= rankedCandidates.Count)
            return FlowIdentitySelection.Empty;

        int candidateLimit = Math.Min(
            candidateCeiling,
            Math.Max(candidateFloor, desiredRoads > candidateCeiling / 2 ? candidateCeiling : desiredRoads * 2));
        var window = rankedCandidates
            .Skip(candidateOffset)
            .Take(candidateLimit)
            .ToList();

        var selectedIndexes = new List<int>();
        var selectedRefs = new List<string>();
        int inspectedRows = 0;
        int uninspectableRows = 0;
        var canonicalIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rankedCandidate in window)
        {
            var candidate = rankedCandidate.Row;
            var representatives = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string reference in candidate.SegmentRefs)
                representatives.TryAdd(HereSegmentReference.CacheIdentity(reference), reference);
            var newRepresentatives = representatives
                .Where(pair => !canonicalIdentities.Contains(pair.Key))
                .ToList();
            int waveReferenceLimit = newRepresentatives.Count > referenceCeiling
                ? oversizedRowReferenceCeiling
                : referenceCeiling;
            int remainingReferences = Math.Min(
                waveReferenceLimit - canonicalIdentities.Count,
                remainingStageReferences - canonicalIdentities.Count);
            if (newRepresentatives.Count > remainingReferences)
            {
                if (newRepresentatives.Count > waveReferenceLimit
                    || newRepresentatives.Count > remainingStageReferences)
                {
                    uninspectableRows++;
                    inspectedRows++;
                    continue;
                }

                break;
            }
            var admittedRepresentatives = newRepresentatives;

            selectedIndexes.Add(candidate.Index);
            foreach ((string identity, string reference) in admittedRepresentatives)
            {
                if (canonicalIdentities.Add(identity))
                    selectedRefs.Add(reference);
            }
            inspectedRows++;
            if (newRepresentatives.Count > referenceCeiling)
                break;
        }

        return new FlowIdentitySelection(
            selectedIndexes,
            selectedRefs,
            canonicalIdentities.Count,
            inspectedRows,
            uninspectableRows);
    }

}

internal enum FlowCandidateBand
{
    Congestion,
    FreeFlow,
    FreeFlowContradiction
}

internal readonly record struct FlowIdentityCandidate(
    int Index,
    double LengthMeters,
    int FunctionalClass,
    bool IsRealTime,
    double EffectiveJamFactor,
    double JamFactorMeters,
    double StandstillMeters,
    IReadOnlyList<string> SegmentRefs);

internal readonly record struct RankedFlowCandidate(
    FlowIdentityCandidate Row,
    bool AdmissionCandidate)
{
    internal double RankScore(FlowCandidateBand band, double congestionJamFactorThreshold)
    {
        int functionalClass = Row.FunctionalClass is > 0 and <= 5 ? Row.FunctionalClass : 5;
        return band switch
        {
            FlowCandidateBand.FreeFlow =>
                CorridorAggregator.CalculateFlowScore(functionalClass, Row.LengthMeters),
            FlowCandidateBand.Congestion =>
                (AdmissionCandidate ? 1_000_000 : 0)
                + Row.EffectiveJamFactor * 1_000
                + (6 - functionalClass) * 100
                + Math.Log10(Math.Max(1, Row.LengthMeters)),
            FlowCandidateBand.FreeFlowContradiction =>
                (6 - functionalClass) * 100
                + Math.Log10(Math.Max(1, Row.LengthMeters))
                + (Row.IsRealTime ? Row.EffectiveJamFactor : 10.0) * 1_000,
            _ => 0
        };
    }
}

internal sealed record FlowCandidateRanking(
    IReadOnlyList<RankedFlowCandidate> Candidates,
    bool Truncated)
{
    internal static FlowCandidateRanking Empty { get; } = new([], false);
}

internal readonly record struct FlowRank(double Score, double LengthMeters, int Index) : IComparable<FlowRank>
{
    public int CompareTo(FlowRank other)
    {
        int score = Score.CompareTo(other.Score);
        if (score != 0)
            return score;
        int length = LengthMeters.CompareTo(other.LengthMeters);
        return length != 0 ? length : other.Index.CompareTo(Index);
    }
}

internal sealed record FlowIdentitySelection(
    IReadOnlyList<int> RowIndexes,
    IReadOnlyList<string> SegmentRefs,
    int CanonicalReferenceCount,
    int InspectedRows,
    int UninspectableRows)
{
    internal static FlowIdentitySelection Empty { get; } = new(
        [],
        [],
        0,
        0,
        0);
}

using System.Globalization;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The records an operator has ruled out of a bulletin, in the one place the fetch can honour
///     them: BEFORE aggregation.
///     <para>
///         This exists because filtering afterwards is not enough, and that was proven on air.
///         <c>DeskOmissions.Apply</c> used to drop the incident from a fetched corridor with
///         <c>corridor with { Incidents = surviving }</c>, but every corridor-level field —
///         <see cref="TrafficCorridor.Direction" />, <see cref="TrafficCorridor.TotalLengthMeters" />,
///         <see cref="TrafficCorridor.ImpactScore" />, <see cref="TrafficCorridor.Description" />,
///         <see cref="TrafficCorridor.OfficialEvidences" /> — was derived by
///         <see cref="Services.CorridorAggregator" /> from ALL members and was never re-derived. Dev
///         job <c>e234aa5a</c>, 2026-08-24: one of Church Street's three records was omitted, the two
///         survivors carried <c>Direction: None</c> between them, and the corridor still said
///         <c>northbound</c> — so the bulletin aired "Northbound on Church Street at Granville" on the
///         strength of the record the operator had killed. <c>ClaimGate</c> never saw it, because the
///         claim was made from the corridor header rather than from a surviving fact. The same
///         corridor also kept the omitted record's 639 m of length (of 679 m total), its unchanged
///         impact score, and its authority receipt.
///     </para>
///     <para>
///         So exclusion is an input to the fetch, not a filter over its output — the same rule the
///         disabled-source denylist follows in the corridor cache key. Excluded records never reach
///         <see cref="Services.CorridorAggregator" />, so no derived field, enrichment or impact
///         score can describe one.
///     </para>
///     <para>
///         Two key spaces, deliberately, because the two operator surfaces name records differently:
///         the Desk's verdicts are ledger event keys
///         (<see cref="SourceRecordIdentity.EventKeyFor" />, stable across HERE's re-stamping), while
///         the job rundown's per-regeneration excludes are the raw provider record ids the console
///         was showing at the time.
///     </para>
///     <para>
///         An instance is built per fetch and is NOT shared between bulletins: it accumulates
///         <see cref="Drops" /> as it filters, so the caller can report exactly which records were
///         held back and why. A cached corridor set is only served to a fetch whose
///         <see cref="FetchToken" /> matches, so a cache hit means an identical exclusion set.
///     </para>
/// </summary>
public sealed class IncidentExclusions
{
    /// <summary>Which operator surface ruled the record out.</summary>
    public enum Reason
    {
        /// <summary>An Omitted verdict on the Desk, standing until cleared.</summary>
        DeskVerdict,

        /// <summary>Unticked on this job's rundown when the operator regenerated it.</summary>
        OperatorExcluded
    }

    /// <param name="Reason">Which surface excluded it.</param>
    /// <param name="Description">Road and record wording, for the operator-facing log.</param>
    public sealed record Drop(Reason Reason, string Description);

    /// <summary>Nothing is excluded — every fetched record aggregates.</summary>
    public static readonly IncidentExclusions None = new([], []);

    private readonly IReadOnlySet<string> _deskEventKeys;
    private readonly IReadOnlySet<string> _operatorRecordIds;
    private readonly List<Drop> _drops = [];
    private readonly HashSet<string> _dropped = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <param name="deskEventKeys">
    ///     Ledger event keys carrying a standing Omitted verdict (<see cref="Reason.DeskVerdict" />).
    /// </param>
    /// <param name="operatorRecordIds">
    ///     Raw provider record ids the operator unticked on this job's rundown
    ///     (<see cref="Reason.OperatorExcluded" />). Matched case-insensitively, as the rundown always has.
    /// </param>
    public IncidentExclusions(
        IEnumerable<string> deskEventKeys, IEnumerable<string> operatorRecordIds)
    {
        _deskEventKeys = deskEventKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.Ordinal);
        _operatorRecordIds = operatorRecordIds
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsEmpty => _deskEventKeys.Count == 0 && _operatorRecordIds.Count == 0;

    /// <summary>Standing Desk verdicts in this set, whether or not any matched.</summary>
    public int DeskVerdictCount => _deskEventKeys.Count;

    /// <summary>Rundown exclusions carried by the regenerating job.</summary>
    public int OperatorExcludedCount => _operatorRecordIds.Count;

    /// <summary>Records actually held back during this fetch, in the order they were filtered.</summary>
    public IReadOnlyList<Drop> Drops
    {
        get
        {
            lock (_gate) return _drops.ToList();
        }
    }

    /// <summary>
    ///     Whether this record must be kept out of the evidence, recording the drop for the caller's
    ///     log. The same record filtered twice (official events are normalized more than once during
    ///     fusion) is recorded once.
    /// </summary>
    public bool Excludes(TrafficIncident incident)
    {
        if (IsEmpty) return false;

        string eventKey = SourceRecordIdentity.EventKeyFor(incident.SourceRecordIdentities, incident.Id);
        bool desk = _deskEventKeys.Contains(eventKey);
        if (!desk && !_operatorRecordIds.Contains(incident.Id))
            return false;

        Record(
            desk ? Reason.DeskVerdict : Reason.OperatorExcluded,
            string.IsNullOrWhiteSpace(incident.Location)
                ? incident.Description
                : $"{incident.Location}: {incident.Description}",
            eventKey);
        return true;
    }

    /// <summary>
    ///     A short, stable identity for this exclusion set, for the corridor cache key. Two stations
    ///     can share a market, a coverage shape and a cache bucket while disagreeing about what is
    ///     omitted, and a cached corridor set built with one exclusion set must never be served to a
    ///     fetch with another. Deterministic across processes: FNV-1a, never <c>string.GetHashCode</c>.
    /// </summary>
    public string FetchToken
    {
        get
        {
            if (IsEmpty) return "-";

            ulong hash = 14695981039346656037UL;
            foreach (string key in _deskEventKeys.OrderBy(k => k, StringComparer.Ordinal))
                hash = Mix(hash, "d:" + key);
            foreach (string key in _operatorRecordIds
                         .Select(k => k.ToLowerInvariant())
                         .OrderBy(k => k, StringComparer.Ordinal))
                hash = Mix(hash, "o:" + key);
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    private void Record(Reason reason, string description, string dedupeKey)
    {
        lock (_gate)
        {
            if (_dropped.Add(dedupeKey))
                _drops.Add(new Drop(reason, description));
        }
    }

    private static ulong Mix(ulong hash, string text)
    {
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}

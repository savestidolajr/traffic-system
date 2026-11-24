using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Folds a market's stored jobs into the Event Ledger: persistent identity per physical road
///     event, its observation history, and its airing history. Pure — the ledger is a derived
///     read-model over data the pipeline already persists (job incident records with stable keys),
///     so it is rebuildable at any time and can never corrupt the pipeline.
/// </summary>
public static class EventLedgerFold
{
    /// <summary>Active events first seen this long ago with no recent change read as rolling re-issues.</summary>
    public static readonly TimeSpan StaleAge = TimeSpan.FromDays(21);

    /// <summary>A change within this window keeps a long-lived event out of the stale lane.</summary>
    public static readonly TimeSpan StaleQuietPeriod = TimeSpan.FromDays(7);

    /// <summary>Statuses whose evidence reflects a real fetch (the job got as far as incidents).</summary>
    private static readonly JobStatus[] FetchedStatuses =
    [
        JobStatus.IncidentsSelected, JobStatus.AwaitingCuration, JobStatus.ScriptReady,
        JobStatus.SynthesizingAudio,
        JobStatus.AudioReady, JobStatus.WaitingApproval, JobStatus.Approved,
        JobStatus.Published, JobStatus.Rejected, JobStatus.FallbackUsed
    ];

    /// <summary>
    ///     Folds jobs (any order; internally sorted oldest-first by creation) into ledger events.
    ///     Jobs that never fetched traffic are skipped — an empty evidence set from a failed job
    ///     must not read as "everything cleared".
    /// </summary>
    public static IReadOnlyList<LedgerEvent> Fold(IReadOnlyList<Job> jobs, DateTimeOffset nowUtc)
    {
        var fetched = jobs
            .Where(j => FetchedStatuses.Contains(j.Status) && j.AllIncidents.Count > 0)
            .OrderBy(j => j.CreatedAt)
            .ToList();
        if (fetched.Count == 0)
            return [];

        DateTimeOffset newestFetch = fetched[^1].CreatedAt;
        var events = new Dictionary<string, Builder>(StringComparer.Ordinal);

        foreach (var job in fetched)
        {
            var at = new DateTimeOffset(job.CreatedAt, TimeSpan.Zero);
            var corridorByKey = CorridorByIncidentKey(job);

            foreach (var incident in job.AllIncidents)
            {
                string eventKey = EventKey(incident);
                if (string.IsNullOrWhiteSpace(eventKey))
                    continue;

                if (!events.TryGetValue(eventKey, out var b))
                {
                    b = new Builder(eventKey, at);
                    events[eventKey] = b;
                }

                var corridor = corridorByKey.GetValueOrDefault(eventKey)
                    ?? corridorByKey.GetValueOrDefault(incident.Key);
                b.Observe(incident, corridor?.RoadName, corridor?.OfficialEvidences, at, job.JobId);
            }

            // Airings: a published bulletin voiced the event when a sentence's corridor
            // (matched by road-name key) carries the incident's key.
            if (job.Status is JobStatus.Published or JobStatus.FallbackUsed)
                RecordAirings(job, at, events);
        }

        return events.Values
            .Select(b => b.Build(newestFetch, nowUtc))
            .OrderByDescending(e => e.LastSeenUtc)
            .ThenBy(e => e.RoadName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    ///     Per-event delta between a bulletin and the market's previous fetched bulletin: the
    ///     diff-review strip. No previous bulletin → everything is New (an honest cold start).
    /// </summary>
    public static IReadOnlyList<EventDelta> ComputeDelta(Job current, Job? previous)
    {
        var prevByKey = (previous?.AllIncidents ?? [])
            .Select(i => (Incident: i, Key: EventKey(i)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key, x => x.Incident, StringComparer.Ordinal);
        var currentRoads = CorridorByIncidentKey(current);
        var previousRoads = previous is null
            ? new Dictionary<string, JobCorridor>(StringComparer.Ordinal)
            : CorridorByIncidentKey(previous);

        var deltas = new List<EventDelta>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var incident in current.AllIncidents)
        {
            string eventKey = EventKey(incident);
            if (string.IsNullOrWhiteSpace(eventKey) || !seen.Add(eventKey))
                continue;

            string road = currentRoads.GetValueOrDefault(eventKey)?.RoadName
                ?? currentRoads.GetValueOrDefault(incident.Key)?.RoadName
                ?? incident.Location;

            if (!prevByKey.TryGetValue(eventKey, out var prior))
            {
                deltas.Add(new EventDelta(eventKey, road, incident.Headline, DeltaStatus.New, null));
                continue;
            }

            string? change = DescribeChange(prior, incident);
            deltas.Add(new EventDelta(
                eventKey, road, incident.Headline,
                change is null ? DeltaStatus.Unchanged : DeltaStatus.Changed, change));
        }

        foreach (var entry in prevByKey)
        {
            string key = entry.Key;
            JobIncident prior = entry.Value;
            if (seen.Contains(key))
                continue;
            deltas.Add(new EventDelta(
                key, previousRoads.GetValueOrDefault(key)?.RoadName ?? prior.Location, prior.Headline,
                DeltaStatus.Cleared, null));
        }

        return deltas;
    }

    /// <summary>Per-feed contribution over the folded window, from corridor provenance.</summary>
    public static IReadOnlyList<FeedHealth> FoldFeedHealth(IReadOnlyList<Job> jobs)
    {
        // Denominator: provenance-era jobs only. Jobs written before corridors carried
        // Sources would otherwise read as "every feed silent" and poison the ratio.
        var fetched = jobs
            .Where(j => j.Corridors.Any(c => c.Sources is { Count: > 0 }))
            .OrderBy(j => j.CreatedAt)
            .ToList();
        var lastSeen = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        var jobCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in fetched)
        {
            var at = new DateTimeOffset(job.CreatedAt, TimeSpan.Zero);
            var sourcesInJob = job.Corridors
                .SelectMany(c => c.Sources ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string src in sourcesInJob)
            {
                lastSeen[src] = at;
                jobCounts[src] = jobCounts.GetValueOrDefault(src) + 1;
            }
        }

        return lastSeen.Keys
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(s => new FeedHealth(s, lastSeen[s], jobCounts[s], fetched.Count))
            .ToList();
    }

    private static void RecordAirings(Job job, DateTimeOffset at, Dictionary<string, Builder> events)
    {
        var eventKeysByLegacyKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var incident in job.AllIncidents)
        {
            if (!string.IsNullOrWhiteSpace(incident.Key))
                eventKeysByLegacyKey.TryAdd(incident.Key, EventKey(incident));
        }

        foreach (var sentence in job.ScriptSentences)
        {
            foreach (string roadKey in sentence.CorridorKeys)
            {
                var corridor = job.Corridors.FirstOrDefault(c =>
                    string.Equals(c.RoadName, roadKey, StringComparison.OrdinalIgnoreCase));
                foreach (string incidentKey in corridor?.IncidentKeys ?? [])
                {
                    string eventKey = eventKeysByLegacyKey.GetValueOrDefault(incidentKey) ?? incidentKey;
                    if (events.TryGetValue(eventKey, out var b))
                        b.Air(job.JobId, at, sentence.Text);
                }
            }
        }
    }

    private static Dictionary<string, JobCorridor> CorridorByIncidentKey(Job job)
    {
        var incidentByLegacyKey = job.AllIncidents
            .Where(i => !string.IsNullOrWhiteSpace(i.Key))
            .GroupBy(i => i.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var map = new Dictionary<string, JobCorridor>(StringComparer.Ordinal);
        foreach (var corridor in job.Corridors)
        {
            foreach (string key in corridor.IncidentKeys ?? [])
            {
                map.TryAdd(key, corridor);
                if (incidentByLegacyKey.TryGetValue(key, out var incident))
                    map.TryAdd(EventKey(incident), corridor);
            }
        }
        return map;
    }

    private static string EventKey(JobIncident incident) =>
        SourceRecordIdentity.EventKeyFor(incident.SourceRecordIdentities, incident.Key);

    /// <summary>A window move below this is a rolling re-issue (HERE re-stamps worksites daily), not news.</summary>
    private static readonly TimeSpan WindowChangeFloor = TimeSpan.FromHours(24);

    private static string? DescribeChange(JobIncident before, JobIncident after)
    {
        var changes = new List<string>();
        if (!string.Equals(before.Headline, after.Headline, StringComparison.Ordinal))
            changes.Add("reworded");
        if (before.RoadClosed != after.RoadClosed)
            changes.Add(after.RoadClosed ? "now closed" : "reopened");
        // Only a genuine reschedule is a change: HERE rolls a worksite's window forward on
        // every re-issue (observed live — the Mitchell Rd class), so sub-day end moves are
        // identity noise, as is an end appearing/disappearing between re-issues.
        if (before.EndTimeUtc is { } be && after.EndTimeUtc is { } ae)
        {
            var moved = ae - be;
            if (moved >= WindowChangeFloor)
                changes.Add("extended");
            else if (moved <= -WindowChangeFloor)
                changes.Add("shortened");
        }

        // Sources: normalized — a record with no provenance stamp IS a HERE record (older
        // schema-era jobs predate stamping), so null/empty vs ["here"] is not a change.
        var beforeSources = NormalizedSources(before);
        var afterSources = NormalizedSources(after);
        if (!beforeSources.SequenceEqual(afterSources, StringComparer.OrdinalIgnoreCase))
            changes.Add(afterSources.Length > beforeSources.Length ? "corroboration gained" : "sources changed");
        return changes.Count > 0 ? string.Join(", ", changes) : null;
    }

    private static string[] NormalizedSources(JobIncident incident) =>
        incident.Sources is { Count: > 0 }
            ? incident.Sources.Order(StringComparer.OrdinalIgnoreCase).ToArray()
            : ["here"];

    /// <summary>Accumulates one event's sightings; snapshot fields always reflect the newest.</summary>
    private sealed class Builder(string key, DateTimeOffset firstSeen)
    {
        private readonly DateTimeOffset _firstSeen = firstSeen;
        private readonly List<LedgerObservation> _observations = [];
        private readonly List<LedgerAiring> _airings = [];
        private JobIncident _latest = null!;
        private string? _road;
        private IReadOnlyList<OfficialEvidence>? _officialEvidences;
        private DateTimeOffset _lastSeen = firstSeen;
        private DateTimeOffset _lastChanged = firstSeen;
        private int _sightings;

        public void Observe(
            JobIncident incident, string? road, IReadOnlyList<OfficialEvidence>? officialEvidences,
            DateTimeOffset at, string jobId)
        {
            _sightings++;
            if (_latest is not null)
            {
                string? change = DescribeChange(_latest, incident);
                if (change is not null)
                {
                    _observations.Add(new LedgerObservation(at, jobId, change));
                    _lastChanged = at;
                }
            }

            _latest = incident;
            _road = road ?? _road;
            _officialEvidences = officialEvidences;
            _lastSeen = at;
        }

        public void Air(string jobId, DateTimeOffset at, string sentence)
        {
            if (_airings.Any(a => a.JobId == jobId && a.Sentence == sentence))
                return;
            _airings.Add(new LedgerAiring(jobId, at, sentence));
        }

        public LedgerEvent Build(DateTimeOffset newestFetch, DateTimeOffset nowUtc)
        {
            // Changed is a NEWS lane, not an archive: only a change observed at the newest
            // sighting keeps an event there — an old change decays back to Active.
            var lane = _lastSeen < newestFetch ? LedgerLane.Cleared
                : _firstSeen == _lastSeen ? LedgerLane.New
                : nowUtc - _firstSeen > StaleAge && nowUtc - _lastChanged > StaleQuietPeriod
                    ? LedgerLane.Stale
                    : _observations.Count > 0 && _lastChanged == _lastSeen ? LedgerLane.Changed
                    : LedgerLane.Active;

            return new LedgerEvent(
                key,
                _road ?? _latest.Location,
                _latest.Headline,
                _latest.Detail.Match(d => d, () => (string?)null),
                _latest.Sources ?? ["here"],
                _firstSeen,
                _lastSeen,
                _latest.StartTimeUtc,
                _latest.EndTimeUtc,
                _latest.RoadClosed,
                lane,
                _sightings,
                _observations,
                _airings,
                _latest,
                _officialEvidences);
        }
    }
}

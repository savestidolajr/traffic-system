using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Services;
namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A complete, round-trippable snapshot of the writer-relevant input that produced a bulletin
///     script — everything <see cref="BroadcastWithTraffic" /> feeds the script generator (corridors,
///     monitored routes, weather, fuel, good-runs, flow pool, content selection, fill order,
///     guidance, feedback).
///     Persisted per job (<c>GeneratorInputJson</c>) and promoted into the version-controlled
///     regression corpus (<c>test/corpus/regressions/</c>) so a script we were unhappy with can be
///     <b>reproduced deterministically</b> and re-scored after a prompt/code change — proving whether
///     the change actually improved it. Routing-only coordinates (corridor start/end points) are
///     dropped: the writer never reads them and they don't serialise cleanly.
/// </summary>
/// <remarks>
///     This snapshot is the source from which regression fixtures and job diagnostics are rebuilt.
///     It previously dropped station configuration silently — most importantly <c>Bookends</c>,
///     which has approximately 30 references in the generation path and was absent from the snapshot
///     entirely. A fixture rebuilt from a snapshot missing these values cannot reproduce what the
///     station actually aired, and nothing failed to indicate the loss. <c>RetryContext</c>,
///     <c>RevoiceOnly</c>, <c>OperatorAttended</c>, and <c>TargetWordCountOverride</c> remain
///     deliberately excluded because they are runtime/per-attempt state, not writer input.
///     <c>Incidents</c> and <c>Traffic</c> remain excluded because incidents are reconstructed from
///     corridors.
/// </remarks>
public sealed record GeneratorInputSnapshot
{
    public required double TargetDurationSeconds { get; init; }
    public required string Location { get; init; }
    public Option<string> City { get; init; }
    public Option<string> MarketId { get; init; }
    public BulletinContentSelection Content { get; init; } = BulletinContentSelection.Default;

    public Option<ResolvedBookends> Bookends { get; init; }
    public IReadOnlyList<FillerStatement> FillerStatements { get; init; } = [];
    public EditorialSettings EditorialSettings { get; init; } = EditorialSettings.Empty;
    public Option<IReadOnlyList<string>> CuratedCorridorRoads { get; init; }
    public Option<IReadOnlyList<string>> CuratedGoodRunRoads { get; init; }
    public Option<IReadOnlyList<string>> ExcludedIncidentKeys { get; init; }

    /// <summary>
    ///     Per-station order remaining-slot fill sources are tried in (BFN-003) once traffic alone
    ///     doesn't reach the target duration; the relative Weather-versus-Fuel order is also their
    ///     ON-AIR read order (resolved by <c>ContentSectionOrder</c>). Persisted so an aired bulletin
    ///     can be audited against the station's configured order. Serialised as the enum names so the
    ///     stored snapshot stays human-readable; an old snapshot without the property falls back to
    ///     the <c>BroadcastContextBase</c> default (weather, fuel, minor traffic, filler).
    /// </summary>
    [JsonConverter(typeof(FillOrderJsonConverter))]
    public IReadOnlyList<FillSource> FillOrder { get; init; } =
        [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler];

    public IReadOnlyList<TrafficCorridor> Corridors { get; init; } = [];
    public IReadOnlyList<MonitoredRouteStatus> MonitoredRoutes { get; init; } = [];
    public IReadOnlyList<string> GoodRunRoads { get; init; } = [];
    public IReadOnlyList<FlowRoad> FlowPool { get; init; } = [];
    public Option<WeatherReport> Weather { get; init; }
    public Option<FuelPriceReport> Fuel { get; init; }
    public Option<IncidentDeltaContext> IncidentChanges { get; init; }
    public ComposedGuidance Guidance { get; init; } = ComposedGuidance.Empty;
    /// <summary>Operator curation decisions persisted before generation resumes.</summary>
    public IReadOnlyList<EditedIncidentDto> EditedIncidents { get; init; } = [];
    public IReadOnlyList<FutureLessonEntry> FutureLessons { get; init; } = [];

    /// <summary>Capture the writer input from a generated context (routing coords stripped).</summary>
    public static GeneratorInputSnapshot FromContext(BroadcastWithTraffic c) => new()
    {
        TargetDurationSeconds = c.TargetDurationSeconds,
        Location = c.Location,
        City = c.City,
        MarketId = c.MarketId,
        Content = c.Content,
        Bookends = c.Bookends,
        FillerStatements = c.FillerStatements,
        EditorialSettings = c.EditorialSettings,
        CuratedCorridorRoads = c.CuratedCorridorRoads,
        CuratedGoodRunRoads = c.CuratedGoodRunRoads,
        ExcludedIncidentKeys = c.ExcludedIncidentKeys,
        FillOrder = c.FillOrder,
        Corridors = c.Corridors.Select(x => x with { StartPoint = default, EndPoint = default }).ToList(),
        MonitoredRoutes = c.MonitoredRoutes,
        GoodRunRoads = c.GoodRunRoads,
        FlowPool = c.FlowPool,
        Weather = c.Weather,
        Fuel = c.Fuel,
        IncidentChanges = c.IncidentChanges,
        Guidance = c.Guidance,
        FutureLessons = c.FutureLessons
    };

    /// <summary>
    ///     Rebuild the generation context for a deterministic replay. <paramref name="jobId" /> labels
    ///     the replay run; <see cref="BroadcastContextBase.ScriptOnly" /> is forced (no synthesis).
    public BroadcastWithTraffic ToContext(string jobId)
    {
        var edited = EditedIncidents.ToDictionary(x => x.IncidentId, StringComparer.OrdinalIgnoreCase);
        var corridors = Corridors
            // Keep incident-free (flow-derived) corridors regardless of curation edits. For
            // incident-backed corridors, retain only those with at least one included incident;
            // applying this predicate before projection distinguishes a flow corridor from one
            // whose incidents were all excluded.
            .Where(c => c.Incidents.Count == 0
                || c.Incidents.Any(i => !edited.TryGetValue(i.Id, out var e) || e.Included))
            .Select(c => c with
            {
                // Operator priority is an ordering override only. Never write it into
                // Severity: severity is an authority measurement used by impact/magnitude
                // scoring.
                Incidents = c.Incidents
                    .Where(i => !edited.TryGetValue(i.Id, out var e) || e.Included)
                    .OrderBy(i => edited.TryGetValue(i.Id, out var e) && e.Priority > 0
                        ? e.Priority
                        : int.MaxValue)
                    .ThenBy(i => i.Severity)
                    .ToList()
            })
            // Operator-prioritised incidents lead; pure-flow corridors remain after incident
            // corridors. Do not synthesize or append from FlowPool: Corridors is already the
            // bounded writer candidate set, and doing so would duplicate incident-backed roads.
            .OrderBy(c => c.Incidents.Count == 0
                ? int.MaxValue
                : c.Incidents.Min(i =>
                    edited.TryGetValue(i.Id, out var e) && e.Priority > 0 ? e.Priority : int.MaxValue))
            .ToList();

        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = TargetDurationSeconds,
            JobId = jobId,
            Location = Location,
            ScriptOnly = true,
            MarketId = MarketId,
            City = City,
            Content = Content,
            Bookends = Bookends,
            FillerStatements = FillerStatements,
            EditorialSettings = EditorialSettings,
            CuratedCorridorRoads = CuratedCorridorRoads,
            CuratedGoodRunRoads = CuratedGoodRunRoads,
            ExcludedIncidentKeys = ExcludedIncidentKeys,
            FillOrder = FillOrder,
            Traffic = CorridorFormatter.Format(corridors),
            Incidents = corridors.SelectMany(c => c.Incidents).ToList(),
            Corridors = corridors,
            MonitoredRoutes = MonitoredRoutes,
            GoodRunRoads = GoodRunRoads,
            FlowPool = FlowPool,
            Weather = Weather,
            Fuel = Fuel,
            IncidentChanges = IncidentChanges,
            Guidance = Guidance,
            FutureLessons = FutureLessons
        };
    }
}

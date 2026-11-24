using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.Location;

/// <summary>
///     Live HERE Traffic v7 adapter that fetches incident and flow-tile data, classifies roads
///     against a curated priority corridor list, and enriches results with drive times and
///     geocoded place names before they reach the bulletin pipeline.
/// </summary>
public sealed partial class HereTrafficProvider(
    HttpClient httpClient,
    IOptions<HereOptions> options,
    IGeocodingProvider geocodingProvider,
    IRoutingProvider routingProvider,
    IOptions<BroadcastOptions> broadcastOptions,
    ILogger<HereTrafficProvider> logger,
    IMapAttributesProvider? mapAttributes = null) : ITrafficProvider
{
    /// <summary>The generic marker carried by an incident HERE gives no usable road name for.</summary>
    private const string UnidentifiedRoad = "Unidentified Road";

    /// <summary>Incidents retained per fetch before corridor aggregation (the diversity-filter cap).</summary>
    private const int CorridorIncidentCap = 50;


    private static readonly FrozenDictionary<string, int> KnownRoadFunctionalClass =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // Sydney
            ["M1"] = 1,
            ["M2"] = 1,
            ["M4"] = 1,
            ["M5"] = 1,
            ["M7"] = 1,
            ["M8"] = 1,
            ["M9"] = 1,
            ["M12"] = 1,
            ["Warringah Freeway"] = 1,
            ["Warringah Fwy"] = 1,
            ["Eastern Distributor"] = 1,
            ["Cross City Tunnel"] = 1,
            ["Sydney Harbour Bridge"] = 1,
            ["Sydney Harbour Tunnel"] = 1,
            ["Anzac Bridge"] = 1,
            ["WestConnex"] = 1,
            ["NorthConnex"] = 1,
            ["Prospect Highway"] = 2,
            ["Hume Highway"] = 2,
            ["Princes Highway"] = 2,
            ["Pacific Highway"] = 2,
            ["Great Western Highway"] = 2,
            ["Parramatta Road"] = 2,
            ["Victoria Road"] = 2,
            // Melbourne
            ["Monash Freeway"] = 1,
            ["Eastern Freeway"] = 1,
            ["Western Freeway"] = 1,
            ["West Gate Freeway"] = 1,
            ["West Gate Bridge"] = 1,
            ["CityLink"] = 1,
            ["EastLink"] = 1,
            ["Tullamarine Freeway"] = 1,
            ["Calder Freeway"] = 1,
            ["South Eastern Freeway"] = 1,
            ["Bolte Bridge"] = 1,
            // Brisbane
            ["Pacific Motorway"] = 1,
            ["Gateway Motorway"] = 1,
            ["Ipswich Motorway"] = 1,
            ["Logan Motorway"] = 1,
            ["Bruce Highway"] = 1
        }.ToFrozenDictionary();

    private readonly HereOptions _options = options.Value;
    private readonly BroadcastOptions _broadcastOptions = broadcastOptions.Value;
    private readonly HereFlowCandidateSelector _flowCandidateSelector = new();
    private readonly ConcurrentDictionary<FlowFetchKey, Lazy<Task<FlowTiles>>> _flowFetches = new();


    public async Task<Result<IEnumerable<TrafficIncident>>> GetIncidentsAsync(CoverageArea area,
        CancellationToken cancellationToken = default)
    {
        var fetchResult = await FetchFromHereApiAsync(area, cancellationToken);
        if (fetchResult.IsFailure)
        {
            logger.LogError(
                "Failed to fetch traffic incidents for area. Lat: {Lat}, Lng: {Lng}, RadiusKm: {Radius}, Error: {Error}",
                area.Latitude, area.Longitude, area.RadiusKm, fetchResult.Error);
            return Result<IEnumerable<TrafficIncident>>.Failure(fetchResult.Error);
        }

        var incidents = FilterAndSortIncidents(fetchResult.Value, 7, area);
        var enriched = await EnrichIncidentsAsync(incidents, cancellationToken);

        logger.LogInformation("Found {Count} traffic incidents (top set)", enriched.Count);
        return Result<IEnumerable<TrafficIncident>>.Success(enriched);
    }

    public async Task<Result<IEnumerable<TrafficCorridor>>> GetCorridorsAsync(
        CoverageArea area,
        int maxCorridors = 5,
        CancellationToken cancellationToken = default,
        IncidentExclusions? exclusions = null)
    {
        // Wave 1: Fetch raw incidents and flow tiles concurrently
        var incidentsFetchTask = FetchFromHereApiAsync(area, cancellationToken);
        var flowFetchTask = FetchFlowTilesAsync(area, cancellationToken);

        Result<HereTrafficResponse> fetchResult;
        try
        {
            fetchResult = await incidentsFetchTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                await flowFetchTask;
            }
            catch (OperationCanceledException)
            {
                // Both concurrently-started fetches have now been observed.
            }

            return Result<IEnumerable<TrafficCorridor>>.Failure("HERE traffic request was cancelled.");
        }
        if (fetchResult.IsFailure)
        {
            // Observe the concurrently-started flow task before returning. Otherwise a failed
            // incident wave can outlive this request and continue consuming HERE work after the
            // bulletin deadline/cancellation has fired.
            await flowFetchTask;
            logger.LogError(
                "Failed to fetch traffic corridors for area. Lat: {Lat}, Lng: {Lng}, RadiusKm: {Radius}, MaxCorridors: {MaxCorridors}, Error: {Error}",
                area.Latitude, area.Longitude, area.RadiusKm, maxCorridors, fetchResult.Error);
            return Result<IEnumerable<TrafficCorridor>>.Failure(fetchResult.Error);
        }

        var utcNow = DateTime.UtcNow;
        var effectiveTypes = GetEffectiveAllowedTypes(utcNow, _broadcastOptions.DefaultTimezone);
        var incidents = FilterAndSortIncidents(fetchResult.Value, 50, area, effectiveTypes);
        var enriched = await EnrichIncidentsAsync(incidents, cancellationToken);

        // Incident corridors (may be empty — flow can still surface incident-free congestion).
        // Operator exclusions are applied HERE, with the eligibility gate, because everything below
        // this line derives corridor fields from whatever survives: filter after aggregation and the
        // corridor keeps the excluded record's direction, extent, length, impact and provenance.
        var voiceable = enriched
            .Where(i => !IncidentEligibility.IsWithheld(i) && !(exclusions?.Excludes(i) ?? false))
            .ToList();
        IReadOnlyList<TrafficCorridor> corridors = voiceable.Count > 0
            ? CorridorAggregator.AggregateByRoad(
                voiceable, maxCorridors, utcNow, ResolveDefaultTimeZone(),
                _broadcastOptions.CorridorGroupingMaxGapMeters)
            : [];
        var enrichedCorridors = await EnrichCorridorsAsync(corridors, cancellationToken);

        // Flow enriches incident corridors AND derives standalone congestion corridors.
        var flows = await flowFetchTask;
        var withFlow = await EnrichCorridorsWithFlowTilesAsync(
            enrichedCorridors, flows, maxCorridors, cancellationToken);
        var ranked = withFlow
            .OrderByDescending(c => c.ImpactScore + CorridorAggregator.CalculateDelayScore(c.DelayMinutes))
            .Take(maxCorridors)
            .ToList();

        // Name WHERE each corridor is before naming how far it runs: ARN reviewed a Sydney read on
        // 2026-08-24 in which all five voiced roads were flow-derived and therefore carried no
        // suburb at all ("Haig Ave is a good one - I'm not sure which it is"). Suburb first, then
        // extent, because the extent pass only fills a corridor whose Extent is still None.
        ranked = await EnrichCorridorsWithSuburbAsync(ranked, cancellationToken);

        // Voice magnitude as WHERE the stretch runs ("from A to B").
        ranked = await EnrichCorridorsWithExtentAsync(ranked, cancellationToken);

        logger.LogInformation("Created {Count} corridors ({IncidentCount} incident-sourced)",
            ranked.Count, enriched.Count);
        return Result<IEnumerable<TrafficCorridor>>.Success(ranked);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> GetIncidentRoadNamesAsync(
        CoverageArea area, int maxCorridors = 5, CancellationToken cancellationToken = default)
    {
        var fetchResult = await FetchFromHereApiAsync(area, cancellationToken);
        if (fetchResult.IsFailure)
            return Result<IReadOnlyList<string>>.Failure(fetchResult.Error);

        var utcNow = DateTime.UtcNow;
        var effectiveTypes = GetEffectiveAllowedTypes(utcNow, _broadcastOptions.DefaultTimezone);
        var incidents = FilterAndSortIncidents(fetchResult.Value, CorridorIncidentCap, area, effectiveTypes);
        var enriched = await EnrichIncidentsAsync(incidents, cancellationToken);

        // Resolved the same way the bulletin resolves them, but WITHOUT the corridor cap, so the
        // caller can keep every one of these roads out of the "moving nicely" pool.
        var names = CorridorAggregator
            .AggregateByRoad(enriched, int.MaxValue, utcNow, ResolveDefaultTimeZone(),
                _broadcastOptions.CorridorGroupingMaxGapMeters)
            .Select(c => c.RoadName)
            .Concat(enriched.Select(i => i.Location))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<IReadOnlyList<string>>.Success(names);
    }

    public async Task<Result<IReadOnlyList<FlowRoad>>> GetFreeFlowingRoadsAsync(
        CoverageArea area,
        int max = 5,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? excludedRoadNames = null)
    {
        var flows = await FetchFlowTilesAsync(area, cancellationToken);
        if (flows.Count == 0)
            return Result<IReadOnlyList<FlowRoad>>.Success([]);

        var staged = await StageFlowSegmentsAsync(
            flows.Prepared,
            FlowCandidateBand.FreeFlow,
            max,
            includeAllRowsForIncidentMatching: false,
            resolveAllRankedCandidates: false,
            quotaExcludedRoads: excludedRoadNames,
            cancellationToken);
        var safetyStage = await StageFlowSegmentsAsync(
            flows.Prepared,
            FlowCandidateBand.FreeFlowContradiction,
            max,
            includeAllRowsForIncidentMatching: false,
            resolveAllRankedCandidates: true,
            quotaExcludedRoads: null,
            cancellationToken);
        if (safetyStage.MapFailed || safetyStage.Incomplete)
        {
            logger.LogWarning("Free-flowing roads withheld because contradiction safety could not inspect every candidate.");
            return Result<IReadOnlyList<FlowRoad>>.Success([]);
        }

        var unresolvedSafetyRefs = safetyStage.UnresolvedCanonicalReferences;
        var segments = staged.IdentityCandidates
            .Where(segment => segment.SegmentRefs?.Any(
                reference => unresolvedSafetyRefs.Contains(HereSegmentReference.CacheIdentity(reference))) != true)
            .Concat(safetyStage.IdentityCandidates)
            .Where(segment => excludedRoadNames?.Any(
                road => CorridorAggregator.RoadNamesOverlap(road, segment.RoadName)) != true)
            .ToList();
        var ranked = CorridorAggregator.RankFreeFlowingRoads(segments, [], Math.Min(24, max));
        // HERE's own class for the row wins. The name-based known-road override remains only for rows
        // HERE reported no class for at all: deriving importance from the NAME is how a local road
        // beside a motorway ended up scored as the motorway.
        var freeFlowing = ranked
            .Select(f =>
            {
                int fc = f.FunctionalClass is > 0 and <= 5
                    ? f.FunctionalClass
                    : ApplyKnownRoadOverride(3, f.RoadName);
                return f with
                {
                    FunctionalClass = fc,
                    Score = CorridorAggregator.CalculateFlowScore(fc, f.LengthMeters)
                };
            })
            .ToList();

        freeFlowing = await EnrichFlowRoadsWithSuburbAsync(freeFlowing, cancellationToken);

        logger.LogInformation("Free-flowing roads for good-run fill: {Count} — [{Roads}]",
            freeFlowing.Count, string.Join(", ", freeFlowing.Select(f => f.RoadName)));
        return Result<IReadOnlyList<FlowRoad>>.Success(freeFlowing);
    }

    /// <summary>
    ///     Projects every flow row once into the cheap numeric/reference representations reused by
    ///     each ranked evidence band.
    /// </summary>
    private static PreparedFlowRows PrepareFlowRows(
        IReadOnlyList<HereFlowItem> flows,
        IReadOnlyDictionary<string, string>? refReplacements)
    {
        var rows = flows
            .Select((flow, index) => new FlowRow(
                index,
                flow,
                ToSegmentSpans(flow.Location.SegmentRef, refReplacements)))
            .ToList();
        var candidates = rows
            .Select(row =>
            {
                var (jamFactorMeters, standstillMeters) = WeighRow(row.Flow);
                return new FlowIdentityCandidate(
                    row.Index,
                    row.Flow.Location.Length,
                    FlowFunctionalClass(row.Flow, default),
                    (row.Flow.CurrentFlow.Confidence ?? 0.0) >= CongestionSegment.RealTimeConfidenceFloor,
                    Math.Max(
                        row.Flow.CurrentFlow.JamFactor,
                        row.Flow.CurrentFlow.SubSegments is { Count: > 0 } subs
                            ? subs.Max(sub => sub.JamFactor)
                            : 0.0),
                    jamFactorMeters,
                    standstillMeters,
                    row.Spans.Select(span => span.Ref).ToList());
            })
            .ToList();

        return new PreparedFlowRows(rows, candidates);
    }

    /// <summary>
    ///     Resolves road identity in ranked waves until enough distinct, authoritative road names can
    ///     fill the caller's result. A misleading HERE description cannot group, rank, or name a candidate.
    /// </summary>
    private async Task<StagedFlowSegments> StageFlowSegmentsAsync(
        PreparedFlowRows prepared,
        FlowCandidateBand band,
        int desiredRoads,
        bool includeAllRowsForIncidentMatching,
        bool resolveAllRankedCandidates,
        IReadOnlyList<string>? quotaExcludedRoads,
        CancellationToken cancellationToken)
    {
        var rows = prepared.Rows;
        var candidates = prepared.Candidates;

        var allSegments = new List<CongestionSegment>(
            includeAllRowsForIncidentMatching ? rows.Count : 0);
        int closedRows = 0;
        foreach (var row in rows)
        {
            string? label = row.Flow.Location.Description;
            if (includeAllRowsForIncidentMatching &&
                string.Equals(
                    row.Flow.CurrentFlow.Traversability,
                    "closed",
                    StringComparison.OrdinalIgnoreCase))
            {
                closedRows++;
                logger.LogInformation(
                    "[FlowClosure] segment {Segment} on {Road} reports traversability=closed",
                    row.Spans.Count > 0 ? row.Spans[0].Ref : "(none)", label);
            }

            if (includeAllRowsForIncidentMatching)
                allSegments.Add(CreateFlowSegment(row, candidates[row.Index], label ?? UnidentifiedRoad, default));
        }

        int targetRoads = Math.Min(24, Math.Max(1, desiredRoads));
        int candidateOffset = 0;
        int inspectedRows = 0;
        int selectedRows = 0;
        int canonicalReferences = 0;
        int unnamedCandidates = 0;
        const int maxCanonicalReferencesPerBand = 2_048;
        bool mapFailed = false;
        var unresolvedSafetyRefs = new HashSet<string>(StringComparer.Ordinal);
        var identityCandidates = new List<CongestionSegment>();
        var namedRoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranking = _flowCandidateSelector.Rank(
            candidates,
            band,
            _options.CongestionJamFactorThreshold);
        bool incomplete = resolveAllRankedCandidates && ranking.Truncated;
        var rankedCandidates = ranking.Candidates;

        while (candidateOffset < rankedCandidates.Count
               && canonicalReferences < maxCanonicalReferencesPerBand
               && (resolveAllRankedCandidates || namedRoads.Count < targetRoads))
        {
            var selection = _flowCandidateSelector.SelectRanked(
                rankedCandidates,
                desiredRoads,
                candidateOffset,
                maxCanonicalReferencesPerBand - canonicalReferences);
            if (selection.InspectedRows == 0)
                break;

            candidateOffset += selection.InspectedRows;
            inspectedRows += selection.InspectedRows;
            selectedRows += selection.RowIndexes.Count;
            canonicalReferences += selection.CanonicalReferenceCount;
            if (resolveAllRankedCandidates && selection.UninspectableRows > 0)
                incomplete = true;
            if (selection.RowIndexes.Count == 0)
                continue;

            var mapResolution = await ResolveSegmentRoadsAsync(selection.SegmentRefs, cancellationToken);
            if (mapResolution.Failed)
            {
                mapFailed = true;
                break;
            }
            var roadsByIdentity = mapResolution.Roads
                .GroupBy(pair => HereSegmentReference.CacheIdentity(pair.Key), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);


            foreach (int rowIndex in selection.RowIndexes)
            {
                var row = rows[rowIndex];
                var candidate = candidates[rowIndex];
                var rowRoads = row.Spans
                    .Where(span => roadsByIdentity.ContainsKey(HereSegmentReference.CacheIdentity(span.Ref)))
                    .ToDictionary(
                        span => span.Ref,
                        span => roadsByIdentity[HereSegmentReference.CacheIdentity(span.Ref)],
                        StringComparer.Ordinal);
                RoadIdentity identity = RoadIdentityResolver.Resolve(
                    row.Spans.Select(span => (span.Ref, span.LengthMeters)).ToList(), rowRoads);

                // A description is the only available name for a row with no segment references.
                // Otherwise Map Attributes must name it; an outage omits optional flow evidence
                // rather than promoting a depot, suburb, or neighbouring-road label on air.
                string? voiceableRoad = identity.Name
                                        ?? (row.Spans.Count == 0
                                            ? row.Flow.Location.Description
                                            : null);
                string internalRoad = voiceableRoad
                                      ?? row.Flow.Location.Description
                                      ?? UnidentifiedRoad;
                var segment = CreateFlowSegment(row, candidate, internalRoad, identity);

                if (includeAllRowsForIncidentMatching)
                    allSegments[rowIndex] = segment;
                if (string.IsNullOrWhiteSpace(voiceableRoad))
                {
                    unnamedCandidates++;
                    if (band == FlowCandidateBand.FreeFlowContradiction)
                    {
                        foreach (var span in row.Spans)
                            unresolvedSafetyRefs.Add(HereSegmentReference.CacheIdentity(span.Ref));
                    }
                    continue;
                }


                bool countsTowardQuota = band != FlowCandidateBand.Congestion
                                         || candidate.JamFactorMeters / Math.Max(1, candidate.LengthMeters)
                                         >= CongestionSegment.HeavyJamFactor
                                         || candidate.StandstillMeters >= CongestionSegment.MinHotSpotMeters;
                bool excludedFromQuota = quotaExcludedRoads?.Any(
                    road => CorridorAggregator.RoadNamesOverlap(road, voiceableRoad)) == true;
                string roadKey = CorridorAggregator.CanonicalRoadKey(voiceableRoad);
                bool fillsQuota = RoadNamePlausibility.IsPlausible(voiceableRoad)
                                  && countsTowardQuota
                                  && !excludedFromQuota;
                if (!resolveAllRankedCandidates
                    && fillsQuota
                    && !namedRoads.Contains(roadKey)
                    && namedRoads.Count >= targetRoads)
                    continue;

                identityCandidates.Add(segment);
                if (fillsQuota)
                    namedRoads.Add(roadKey);
            }
        }

        if (resolveAllRankedCandidates && candidateOffset < rankedCandidates.Count)
            incomplete = true;

        logger.LogInformation(
            "Flow identity staged: {Selected} of {Inspected}/{Rows} ranked rows, "
            + "{References} canonical segment ids, {Roads} named roads, band {Band}",
            selectedRows, inspectedRows, rows.Count, canonicalReferences, namedRoads.Count, band);
        if (closedRows > 0 || unnamedCandidates > 0)
            logger.LogInformation(
                "Flow identity: {Resolved} shortlisted rows named, {Unnamed} withheld, "
                + "{Closed} of {Total} rows reporting traversability=closed",
                identityCandidates.Count, unnamedCandidates, closedRows, rows.Count);
        return new StagedFlowSegments(
            allSegments,
            identityCandidates,
            mapFailed,
            incomplete,
            unresolvedSafetyRefs);
    }


    private static CongestionSegment CreateFlowSegment(
        FlowRow row,
        FlowIdentityCandidate candidate,
        string road,
        RoadIdentity identity)
    {
        int hereFunctionalClass = FlowFunctionalClass(row.Flow, identity);
        double jamFactorMeters = candidate.JamFactorMeters;
        double standstillMeters = candidate.StandstillMeters;
        var (rowStart, rowEnd) = FlowRowEndpoints(row.Flow.Location.Shape);

        return new CongestionSegment(
            road,
            row.Flow.CurrentFlow.JamFactor,
            row.Flow.Location.Length,
            row.Flow.CurrentFlow.Speed,
            row.Flow.CurrentFlow.FreeFlow,
            Confidence: row.Flow.CurrentFlow.Confidence ?? 0.0,
            MaxSubSegmentJamFactor: row.Flow.CurrentFlow.SubSegments is { Count: > 0 } subs
                ? subs.Max(sub => sub.JamFactor)
                : 0.0,
            HereFunctionalClass: hereFunctionalClass,
            SegmentRefs: row.Spans.Select(span => span.Ref).ToList(),
            ResolvedRoadName: identity.Name,
            Verdict: identity.Verdict,
            JamFactorMeters: jamFactorMeters,
            StandstillMeters: standstillMeters,
            Traversability: row.Flow.CurrentFlow.Traversability,
            StartPoint: rowStart,
            EndPoint: rowEnd);
    }


    private sealed record FlowRow(
        int Index,
        HereFlowItem Flow,
        IReadOnlyList<SegmentSpan> Spans);

    private sealed record StagedFlowSegments(
        IReadOnlyList<CongestionSegment> All,
        IReadOnlyList<CongestionSegment> IdentityCandidates,
        bool MapFailed,
        bool Incomplete,
        IReadOnlySet<string> UnresolvedCanonicalReferences);

    /// <summary>
    ///     The first and last coordinate of a flow row's shape. HERE returns the polyline as an
    ///     ordered list of links, each with its own point list, so the row's ends are the first
    ///     point of the first non-empty link and the last point of the last non-empty link.
    ///     Null when the response carried no usable shape — every consumer treats the geometry as
    ///     optional garnish, exactly as the incident path does.
    /// </summary>
    private static ((double Lat, double Lng)? Start, (double Lat, double Lng)? End) FlowRowEndpoints(
        HereShapeLocation? shape)
    {
        if (shape?.Links is not { Count: > 0 } links)
            return (null, null);

        var first = links.FirstOrDefault(l => l.Points is { Count: > 0 })?.Points;
        var last = links.LastOrDefault(l => l.Points is { Count: > 0 })?.Points;
        if (first is null || last is null)
            return (null, null);

        return ((first[0].Lat, first[0].Lng), (last[^1].Lat, last[^1].Lng));
    }

    /// <summary>
    ///     Resolves one flow row into the two lengths a band decision needs: Σ(jamFactor × length) and
    ///     the length actually at a standstill. HERE's <c>subSegments</c> are the finer readings inside
    ///     a parent row, so where they exist they carry the weight and the parent covers only whatever
    ///     length they leave over; where they don't, the parent reading covers its whole length.
    /// </summary>
    private static (double JamFactorMeters, double StandstillMeters) WeighRow(HereFlowItem flow)
    {
        double rowLength = flow.Location.Length;
        double parentJam = flow.CurrentFlow.JamFactor;
        var subs = flow.CurrentFlow.SubSegments;

        if (subs is not { Count: > 0 })
            return (parentJam * rowLength,
                parentJam >= CongestionSegment.StandstillJamFactor ? rowLength : 0.0);

        double weighted = 0, standstill = 0, covered = 0;
        foreach (var sub in subs)
        {
            weighted += sub.JamFactor * sub.Length;
            covered += sub.Length;
            if (sub.JamFactor >= CongestionSegment.StandstillJamFactor)
                standstill += sub.Length;
        }

        double remainder = rowLength - covered;
        if (remainder > 0)
        {
            weighted += parentJam * remainder;
            if (parentJam >= CongestionSegment.StandstillJamFactor)
                standstill += remainder;
        }

        return (weighted, standstill);
    }

    /// <summary>
    ///     HERE's own class for a flow row: the map's class for its segments when the map resolved
    ///     them, else the minimum class across the row's own shape links. Zero when HERE said neither —
    ///     guessing the class from the road NAME is what put 219 of 235 corridors at FC3/Severity1.
    /// </summary>
    private static int FlowFunctionalClass(HereFlowItem flow, RoadIdentity identity)
    {
        if (identity.Name is not null && identity.FunctionalClass is > 0 and <= 5)
            return identity.FunctionalClass;

        var links = flow.Location.Shape?.Links;
        return links is { Count: > 0 } && links.Any(l => l.FunctionalClass > 0)
            ? links.Where(l => l.FunctionalClass > 0).Min(l => l.FunctionalClass)
            : 0;
    }

    /// <summary>
    ///     Fetches the request-scoped broad HERE flow snapshot for a coverage area, tiling into
    ///     multiple requests when the area's enclosing box exceeds HERE's 1° bbox limit (see
    ///     <see cref="HereGeoFilter.BuildTiles" />). Congestion and free-flow selection share this
    ///     one snapshot rather than downloading differently filtered copies.
    ///     Flow items carry no id, so tiles are de-duplicated on the row's own segment reference where
    ///     HERE gave one and on the (description, length, speed) triple where it did not — HERE's
    ///     bbox-intersection matching means the same segment straddling a tile boundary comes back with
    ///     identical values from every tile it intersects. Failed tiles degrade silently (flow is
    ///     fail-soft garnish); returns empty when every tile fails.
    /// </summary>
    private async Task<FlowTiles> FetchFlowTilesAsync(
        CoverageArea area,
        CancellationToken cancellationToken)
    {
        var key = new FlowFetchKey(area, cancellationToken);
        var pending = _flowFetches.GetOrAdd(
            key,
            _ => new Lazy<Task<FlowTiles>>(
                () => FetchFlowTilesCoreAsync(area, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            return await pending.Value;
        }
        catch
        {
            _flowFetches.TryRemove(new KeyValuePair<FlowFetchKey, Lazy<Task<FlowTiles>>>(key, pending));
            throw;
        }
    }

    private async Task<FlowTiles> FetchFlowTilesCoreAsync(
        CoverageArea area, CancellationToken cancellationToken)
    {
        var tiles = HereGeoFilter.BuildTiles(area);
        var tileResults = await Task.WhenAll(tiles.Select(tile =>
        {
            string url =
                $"{_options.TrafficBaseUrl}/{_options.FlowPath}?in={tile}" +
                $"&locationReferencing=shape,segmentRef&functionalClasses=1,2,3&minJamFactor=0" +
                $"&lang=en-GB&apiKey={_options.ApiKey}";
            return HereApiHelper.ExecuteGetAsync<HereFlowResponse>(httpClient, url, logger, "Flow", cancellationToken);
        }));

        int failed = tileResults.Count(r => r.IsFailure);
        if (failed > 0)
            logger.LogWarning("Flow fetch: {Failed} of {Total} tiles failed for a multi-tile coverage area",
                failed, tileResults.Length);

        var rows = tileResults
            .Where(r => r.IsSuccess)
            .SelectMany(r => r.Value.Results ?? [])
            // HERE omits currentFlow for some rows and STJ leaves the record null; a row with no
            // reading measures nothing. The DESCRIPTION is no longer a filter: 370 of 400 Sydney and
            // 145 of 163 Cairns unlabelled rows resolve to a real road through their own segments.
            .Where(f => f.CurrentFlow is not null)
            .DistinctBy(f => f.Location.SegmentRef?.Segments is { Count: > 0 } segs
                ? segs[0].Ref
                : $"{f.Location.Description}|{f.Location.Length}|{f.CurrentFlow.Speed}")
            .ToList();

        // The flow feed factors the catalog and segment prefixes out of every ref into its OWN
        // refReplacements map, exactly as the incidents feed does. Without expanding them the refs
        // carry no "here:cm:segment:" and the Map Attributes lookup rejects every one, which silently
        // put flow identity back on HERE's free-text label (observed live: a suburb, "Terrey Hills",
        // in the good-run pool).
        var refReplacements = tileResults
            .Where(r => r.IsSuccess)
            .Select(r => r.Value.RefReplacements)
            .FirstOrDefault(m => m is { Count: > 0 });

        return new FlowTiles(PrepareFlowRows(rows, refReplacements));
    }

    /// <summary>One request-scoped broad flow snapshot, projected once for every consumer.</summary>
    private sealed record FlowTiles(PreparedFlowRows Prepared)
    {
        public int Count => Prepared.Rows.Count;
    }

    /// <summary>
    ///     Fetches incidents for a coverage area, tiling into multiple HERE
    ///     requests when the area's enclosing box exceeds HERE's 1° bbox
    ///     limit (see <see cref="HereGeoFilter.BuildTiles" />). Results are
    ///     merged and de-duplicated by incident id. Fails only when every
    ///     tile fails; a partial tile failure degrades to the tiles that
    ///     succeeded (logged), matching the fail-soft policy elsewhere in
    ///     the pipeline.
    /// </summary>
    private async Task<Result<HereTrafficResponse>> FetchFromHereApiAsync(CoverageArea area,
        CancellationToken cancellationToken)
    {
        var tiles = HereGeoFilter.BuildTiles(area);
        var tileResults = await Task.WhenAll(tiles.Select(tile => FetchIncidentsTileAsync(tile, cancellationToken)));

        var successes = tileResults.Where(r => r.IsSuccess).ToList();
        if (successes.Count == 0)
            return Result<HereTrafficResponse>.Failure(tileResults[0].Error);

        if (successes.Count < tileResults.Length)
            logger.LogWarning(
                "Incidents fetch: {Failed} of {Total} tiles failed for a multi-tile coverage area",
                tileResults.Length - successes.Count, tileResults.Length);

        var merged = successes
            .SelectMany(r => r.Value.Results ?? [])
            .DistinctBy(item => item.IncidentDetails.Id)
            .ToList();

        // Oldest tile wins: the merged set is only as fresh as its stalest contributor.
        var sourceUpdated = successes
            .Select(r => r.Value.SourceUpdated)
            .Where(t => t.HasValue)
            .DefaultIfEmpty(null)
            .Min();

        var refReplacements = successes
            .Select(r => r.Value.RefReplacements)
            .FirstOrDefault(m => m is { Count: > 0 });

        return Result<HereTrafficResponse>.Success(
            new HereTrafficResponse(merged, sourceUpdated, refReplacements));
    }

    private async Task<Result<HereTrafficResponse>> FetchIncidentsTileAsync(string tileFilter,
        CancellationToken cancellationToken)
    {
        // `lang` is PINNED. Unset, HERE answers en-US; `en-AU` is accepted and silently downgraded to
        // en-US (measured); `en-GB` returns Australian idiom ("Roadworks", "Queuing traffic"). Nothing
        // keys on the English wording — the condition comes from `codes[0]` — but an unpinned language
        // means HERE could change the register under us without notice.
        string url =
            $"{_options.TrafficBaseUrl}/{_options.IncidentsPath}?in={tileFilter}" +
            $"&locationReferencing=shape,segmentRef&lang=en-GB&apiKey={_options.ApiKey}";

        var result =
            await HereApiHelper.ExecuteGetAsync<HereTrafficResponse>(httpClient, url, logger, "Traffic",
                cancellationToken);
        if (result.IsFailure)
            return result;

        return result.Value.Results is null
            ? Result<HereTrafficResponse>.Failure("No results structure returned")
            : result;
    }

    private List<TrafficIncident> FilterAndSortIncidents(HereTrafficResponse response, int maxCount,
        CoverageArea area, HashSet<string>? allowedSet = null)
    {
        allowedSet ??= _options.AllowedIncidentTypes.Count > 0
            ? new HashSet<string>(_options.AllowedIncidentTypes, StringComparer.OrdinalIgnoreCase)
            : null;

        var raw = response.Results
            .Where(item => allowedSet is null || allowedSet.Contains(item.IncidentDetails.Type))
            .ToList();

        // Polygon post-filter: HERE has no native polygon filter, so we fetch
        // the enclosing bbox and narrow results against the exact polygon here.
        if (area.ShapeType == CoverageShapeType.Polygon)
        {
            int before = raw.Count;
            raw = raw
                .Where(item => area.IntersectsPolygon(
                    GetShapePoints(item)
                        .Select(p => new CoveragePoint(p.Lat, p.Lng))
                        .ToList()))
                .ToList();
            int removed = before - raw.Count;
            if (removed > 0)
                logger.LogInformation(
                    "Polygon post-filter kept {Kept} of {Total} incidents", raw.Count, before);
        }

        var allFiltered = raw
            .Select(item =>
            {
                try
                {
                    return MapToDomain(item, response.SourceUpdated, response.RefReplacements);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Skipping incident {Id} due to mapping error",
                        item.IncidentDetails?.Id ?? "unknown");
                    return null;
                }
            })
            .OfType<TrafficIncident>()
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.FunctionalClass)
            .ThenByDescending(i => i.LengthMeters)
            .ToList();

        // Non-vehicular incident filter: remove pedestrian/cyclist infrastructure
        // works (PSP upgrades, cycleway closures) that HERE misclassifies as road
        // incidents — they waste bulletin airtime and dilute genuine traffic.
        var filterResult = NonVehicularIncidentFilter.Filter(allFiltered);
        if (filterResult.Removed.Count > 0)
        {
            foreach (var (incident, token) in filterResult.Removed)
                logger.LogInformation(
                    "[NonVehicularFilter] Dropped incident {Id} on {Road}: matched token '{Token}'",
                    incident.Id, incident.Location, token);
            logger.LogInformation(
                "[NonVehicularFilter] Filtered {Removed} non-vehicular incident(s) from {Total}",
                filterResult.Removed.Count, allFiltered.Count);
        }

        return IncidentDiversityFilter.EnsureDiversity(filterResult.Kept, maxCount);
    }

    /// <summary>
    ///     Enumerates all shape points from a HERE incident item. Returns
    ///     empty when the item has no shape data (rare but valid — the item
    ///     is kept rather than discarded).
    /// </summary>
    private static IEnumerable<(double Lat, double Lng)> GetShapePoints(HereIncidentItem item)
    {
        return item.Location.Shape?.Links
            ?.Where(l => l.Points is { Count: > 0 })
            .SelectMany(l => l.Points)
            .Select(p => (p.Lat, p.Lng))
            ?? [];
    }

    /// <summary>Max shape points persisted per incident — caps the job-record
    ///  size while keeping a recognisable road polyline for the review map.</summary>
    private const int MaxShapePoints = 16;

    /// <summary>The incident's road geometry as a capped, evenly-sampled polyline
    ///  (first + last always kept) for the map. Null for point/shapeless incidents.</summary>
    private static IReadOnlyList<CoveragePoint>? BuildIncidentShape(HereIncidentItem item)
    {
        var pts = GetShapePoints(item).ToList();
        if (pts.Count < 2)
            return null;
        if (pts.Count <= MaxShapePoints)
            return pts.Select(p => new CoveragePoint(p.Lat, p.Lng)).ToList();

        var sampled = new List<CoveragePoint>(MaxShapePoints);
        double step = (pts.Count - 1) / (double)(MaxShapePoints - 1);
        for (int i = 0; i < MaxShapePoints; i++)
        {
            int idx = Math.Min((int)Math.Round(i * step), pts.Count - 1);
            sampled.Add(new CoveragePoint(pts[idx].Lat, pts[idx].Lng));
        }
        return sampled;
    }

    /// <summary>
    ///     Resolves what each incident IS and WHERE it is, and reduces it to an
    ///     <see cref="IncidentFact" /> — the atomic unit anything downstream may claim from.
    ///     <para>
    ///         Road identity comes from the map's own record of the incident's OWN segments, never from
    ///         proximity: reverse geocoding named a different road than HERE's map for 38 of 107
    ///         measured records (36%), and <c>types=street</c> moved that by 0.9 pp. The geocoder is
    ///         still called, for the SUBURB only — which is the question it actually answers.
    ///     </para>
    /// </summary>
    private async Task<List<TrafficIncident>> EnrichIncidentsAsync(List<TrafficIncident> incidents,
        CancellationToken cancellationToken)
    {
        if (incidents.Count == 0)
            return incidents;

        // The map lookup and the locality lookup are independent; neither blocks the other.
        var segmentRoadsTask = ResolveSegmentRoadsAsync(incidents, cancellationToken);
        var suburbsTask = ResolveSuburbsAsync(incidents, cancellationToken);
        var segmentRoads = await segmentRoadsTask;
        var suburbs = await suburbsTask;

        var now = DateTimeOffset.UtcNow;
        var enriched = new List<TrafficIncident>(incidents.Count);
        for (int i = 0; i < incidents.Count; i++)
        {
            var incident = incidents[i];
            if (suburbs.TryGetValue(i, out var suburb))
                incident = incident with { Suburb = suburb };

            enriched.Add(ApplySegmentRoadIdentity(incident, segmentRoads, now));
        }

        // The data boundary: claims the facts cannot support are demoted or withheld HERE, so
        // corridors, the writer context, and the stored job all see the same already-vetted record.
        var outcome = IncidentEligibility.Transform(enriched);
        foreach (var action in outcome.Ledger)
            logger.LogInformation(
                "[Eligibility] {Action} {Id} on {Road}: {Reason}",
                action.Action, action.IncidentId, action.Road, action.Reason);

        return outcome.Incidents.ToList();
    }

    /// <summary>
    ///     Names the incident from HERE's own map for the segments the incident itself references, and
    ///     attaches the record's <see cref="IncidentFact" />.
    ///     <para>
    ///         No geocoder corroboration is required or accepted: the geocoder is wrong about the road
    ///         36% of the time and so cannot arbitrate. A <see cref="IdentityVerdict.Split" /> or
    ///         <see cref="IdentityVerdict.Unnamed" /> record keeps the generic marker and is withheld by
    ///         <see cref="ClaimGate" /> rather than given a name we cannot support.
    ///     </para>
    /// </summary>
    private TrafficIncident ApplySegmentRoadIdentity(
        TrafficIncident incident,
        IReadOnlyDictionary<string, SegmentRoad> segmentRoads,
        DateTimeOffset now)
    {
        var spans = incident.HereSegmentSpans ?? [];
        var identity = RoadIdentityResolver.Resolve(
            spans.Select(s => (s.Ref, s.LengthMeters)).ToList(), segmentRoads);

        bool named = identity.Name is not null;
        var located = named
            ? incident with { Location = identity.Name!, RoadNameVerified = true, SegmentIsRamp = identity.IsRamp }
            : incident with { RoadNameVerified = false, SegmentIsRamp = identity.IsRamp };

        var built = IncidentFactBuilder.Build(located, identity, now);

        // HERE's roadClosed flag agreed with the condition vocabulary on 793 of 793 measured records,
        // so a disagreement is a data anomaly rather than a nuance: it is logged here, and the fact
        // itself already reports the record as a non-closure — the safe direction to be wrong in.
        if (built.ClosureFlagDisagreement)
        {
            logger.LogWarning(
                "[ClaimGate] closure-flag disagreement on {Id}: roadClosed={RoadClosed} but codes[0] " +
                "reports {Condition}. Treating the record as non-closure.",
                located.Id, located.RoadClosed, built.Fact.Condition.Kind);
        }

        return located with { ClosedAsset = built.ClosedAsset, Fact = built.Fact };
    }

    /// <summary>
    ///     Reverse geocodes each incident's position for its LOCALITY only. The road name is never
    ///     taken from here: collapsing the two let a locality air as a road ("Arundel",
    ///     "Greensborough", "Laverton"), and even the street answer belongs to whatever is nearest the
    ///     point rather than to the incident's own road.
    /// </summary>
    private async Task<Dictionary<int, Option<string>>> ResolveSuburbsAsync(
        List<TrafficIncident> incidents, CancellationToken cancellationToken)
    {
        var located = incidents
            .Select((incident, index) => (incident, index))
            .Where(x => x.incident.Latitude.HasValue && x.incident.Longitude.HasValue)
            .ToList();

        var suburbs = new Dictionary<int, Option<string>>();
        if (located.Count == 0)
            return suburbs;

        var coordinates = located.Select(x => (x.incident.Latitude.Value, x.incident.Longitude.Value));
        var batchResult = await geocodingProvider.BatchReverseGeocodePlacesAsync(coordinates, cancellationToken);
        if (batchResult.IsFailure)
        {
            logger.LogWarning("Batch geocoding failed for {Count} incidents: {Error}", located.Count,
                batchResult.Error);
            return suburbs;
        }

        foreach ((int batchIndex, var place) in batchResult.Value)
            if (batchIndex >= 0 && batchIndex < located.Count && place.Suburb.HasValue)
                suburbs[located[batchIndex].index] = place.Suburb;

        return suburbs;
    }

    /// <summary>
    ///     Looks up the map's road for every segment the incidents reference. Fail-soft: no map
    ///     knowledge means the records stay unnamed and are withheld, never renamed by guesswork.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, SegmentRoad>> ResolveSegmentRoadsAsync(
        List<TrafficIncident> incidents, CancellationToken cancellationToken)
    {
        if (mapAttributes is null)
            return new Dictionary<string, SegmentRoad>();

        var refs = incidents
            .SelectMany(i => i.HereSegmentSpans ?? [])
            .Select(s => s.Ref)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (refs.Count == 0)
            return new Dictionary<string, SegmentRoad>();

        return (await ResolveSegmentRoadsAsync(refs, cancellationToken)).Roads;
    }

    /// <summary>Map lookup for an explicit reference set (incidents or flow rows), fail-soft.</summary>
    private async Task<SegmentRoadResolution> ResolveSegmentRoadsAsync(
        IReadOnlyList<string> refs, CancellationToken cancellationToken)
    {
        if (refs.Count == 0)
            return new SegmentRoadResolution(new Dictionary<string, SegmentRoad>(), false);
        if (mapAttributes is null)
            return new SegmentRoadResolution(new Dictionary<string, SegmentRoad>(), true);

        try
        {
            var result = await mapAttributes.GetSegmentRoadsAsync(refs, cancellationToken);
            if (result.IsSuccess)
            {
                var rejected = result.Value
                    .Where(pair => RoadNameFormatter.IsPlaceholderName(pair.Value.Name)
                                   || RoadNameFormatter.IsPointOfInterestName(pair.Value.Name))
                    .ToList();
                if (rejected.Count > 0)
                {
                    logger.LogWarning(
                        "[RoadNameFilter] Rejected {Count} placeholder/POI map road names in one fetch: {Names}",
                        rejected.Count,
                        string.Join(", ", rejected.Select(pair => pair.Value.Name)));
                }

                var roads = result.Value
                    .Where(pair => !RoadNameFormatter.IsPlaceholderName(pair.Value.Name)
                                   && !RoadNameFormatter.IsPointOfInterestName(pair.Value.Name))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                return new SegmentRoadResolution(roads, false);
            }

            logger.LogWarning("[RoadIdentity] Segment lookup failed ({Error}); records stay unnamed.",
                result.Error);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[RoadIdentity] Segment lookup threw; records stay unnamed.");
        }

        return new SegmentRoadResolution(new Dictionary<string, SegmentRoad>(), true);
    }

    private async Task<List<TrafficCorridor>> EnrichCorridorsAsync(IEnumerable<TrafficCorridor> corridors,
        CancellationToken cancellationToken)
    {
        var corridorList = corridors.ToList();
        var tasks = corridorList.Select(c => TryEnrichCorridorDriveTimeAsync(c, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<TrafficCorridor> TryEnrichCorridorDriveTimeAsync(TrafficCorridor corridor,
        CancellationToken cancellationToken)
    {
        if (corridor.StartPoint.IsNone || corridor.EndPoint.IsNone)
            return corridor;

        var driveTimeResult = await routingProvider.GetDriveTimeAsync(
            corridor.StartPoint.Value,
            corridor.EndPoint.Value,
            cancellationToken);

        if (driveTimeResult.IsFailure)
        {
            logger.LogDebug("Could not get drive time for corridor {Road}: {Error}", corridor.RoadName,
                driveTimeResult.Error);
            return corridor;
        }

        var driveTime = driveTimeResult.Value;
        logger.LogDebug("Drive time for {Road}: {Duration}min (typical: {Typical}min, delay: {Delay}min)",
            corridor.RoadName, driveTime.DurationMinutes, driveTime.TypicalDurationMinutes, driveTime.DelayMinutes);

        Option<DetourSuggestion> detour = default;
        if (routingProvider is IDetourRoutingProvider detourProvider && driveTime.DelayMinutes > 0)
        {
            var avoidSegments = corridor.Incidents
                .SelectMany(i => i.SegmentRefs ?? [])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (avoidSegments.Count > 0)
            {
                var detourResult = await detourProvider.GetSafeDetourAsync(
                    corridor.StartPoint.Value, corridor.EndPoint.Value, avoidSegments, driveTime, cancellationToken);
                if (detourResult.IsSuccess)
                    detour = Option<DetourSuggestion>.Some(detourResult.Value);
            }
        }

        return corridor with
        {
            DriveTimeMinutes = Option<int>.Some(driveTime.DurationMinutes),
            NormalDriveTimeMinutes = Option<int>.Some(driveTime.TypicalDurationMinutes),
            Detour = detour
        };
    }

    /// <summary>
    ///     Enriches corridors with live flow data: congestion trend
    ///     (building/easing) and a flow-derived delay for corridors that have
    ///     no routed drive time. Flow failures degrade silently — flow is
    ///     garnish, never a reason to fail a bulletin.
    /// </summary>

    private async Task<List<TrafficCorridor>> EnrichCorridorsWithFlowTilesAsync(
        List<TrafficCorridor> corridors,
        FlowTiles flows,
        int desiredRoads,
        CancellationToken cancellationToken)
    {
        if (flows.Count == 0)
            return corridors;

        var staged = await StageFlowSegmentsAsync(
            flows.Prepared,
            FlowCandidateBand.Congestion,
            Math.Max(1, desiredRoads),
            includeAllRowsForIncidentMatching: true,
            resolveAllRankedCandidates: false,
            quotaExcludedRoads: corridors.Select(corridor => corridor.RoadName).ToList(),
            cancellationToken);
        var segments = staged.All;
        var enriched = new List<TrafficCorridor>(corridors.Count);
        foreach (var corridor in corridors)
        {
            var corridorSegments = corridor.Incidents
                .SelectMany(i => i.HereSegmentSpans ?? [])
                .Select(s => StripSegmentOffsets(s.Ref))
                .ToHashSet(StringComparer.Ordinal);

            // Segment identity first — the strongest match there is, and free now that flow rows carry
            // their own references. Then the row's RESOLVED road name, abbreviation-aware and never a
            // substring test ("Bay Street" once matched HERE's "Botany Bay Street" and took its delay).
            // The row's label takes no part in matching at all.
            var matches = segments
                .Where(s => SharesSegment(s, corridorSegments)
                            || (s.ResolvedRoadName is not null
                                && RoadNameFormatter.IsSameRoad(s.ResolvedRoadName, corridor.RoadName)))
                .ToList();

            if (matches.Count == 0)
            {
                enriched.Add(corridor);
                continue;
            }

            // Flow-derived delay: sum of (time at current speed − time at free flow) over congested
            // matched segments. Only readings HERE vouches for as real-time may produce a delay.
            double delaySeconds = matches
                .Where(m => m.IsRealTime && m.SpeedMps > 0.1 && m.FreeFlowMps > m.SpeedMps)
                .Sum(m => m.LengthMeters / m.SpeedMps - m.LengthMeters / m.FreeFlowMps);
            int delayMinutes = (int)Math.Round(delaySeconds / 60.0);

            enriched.Add(corridor with
            {
                FlowDelayMinutes = delayMinutes >= 1 ? Option<int>.Some(delayMinutes) : default
            });
        }

        // Derive standalone congestion corridors from high-jamFactor rows with no incident.
        var congestion = CorridorAggregator.BuildCongestionCorridors(
            staged.IdentityCandidates, enriched, _options.CongestionJamFactorThreshold);
        if (congestion.Count > 0)
            enriched.AddRange(congestion);

        logger.LogInformation(
            "Flow: {FlowCount} rows, {Segments} usable segments, {CorridorCount} incident corridors, " +
            "{Congestion} congestion corridors",
            flows.Count, segments.Count, corridors.Count, congestion.Count);
        return enriched;
    }

    /// <summary>
    ///     True when a flow row and a corridor's incidents share a topology segment. Offsets and the
    ///     direction sign are stripped on both sides: the same physical stretch is reported with
    ///     different offset ranges on the flow and incident feeds, and per carriageway.
    /// </summary>
    private static bool SharesSegment(CongestionSegment segment, HashSet<string> corridorSegments)
    {
        if (corridorSegments.Count == 0 || segment.SegmentRefs is null)
            return false;
        foreach (string reference in segment.SegmentRefs)
            if (corridorSegments.Contains(StripSegmentOffsets(reference)))
                return true;
        return false;
    }

    /// <summary>The segment reference with its direction sign and offset range removed.</summary>
    private static string StripSegmentOffsets(string segmentRef)
    {
        int hash = segmentRef.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? segmentRef : segmentRef[..hash];
    }

    /// <summary>Below this the affected stretch is a single spot, not an honest "from A to B".</summary>
    private const double ExtentMinLengthMeters = 800;

    internal readonly record struct CorridorEndpoints(
        (double Lat, double Lng) Start, (double Lat, double Lng) End);

    private static readonly FrozenSet<string> ExtentPlaceholderNames =
        new HashSet<string> { "Unidentified Road", "Unknown Location" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Names the suburb of every ranked corridor that has coordinates but no suburb yet.
    ///     <para>
    ///         An incident corridor inherits its suburb from the leading incident, which got one
    ///         from the authority feed or from <see cref="ResolveSuburbsAsync" />. A flow-derived
    ///         congestion corridor has no incident to inherit from, so before this pass it reached
    ///         the writer as a bare road name — and on a congestion-led morning that is EVERY
    ///         voiced item. ARN reviewed exactly that read on 2026-08-24: five roads, no suburb on
    ///         any of them, and a "Haig Avenue" the reviewer could not place.
    ///     </para>
    ///     <para>
    ///         Deliberately the Places variant, not the collapsed one the extent pass uses:
    ///         <see cref="IGeocodingProvider.BatchReverseGeocodeAsync" /> returns a street-or-locality
    ///         string, while only <see cref="IGeocodingProvider.BatchReverseGeocodePlacesAsync" />
    ///         separates the suburb out. One batch for the whole ranked set; any failure degrades to
    ///         no suburb, never a bulletin error.
    ///     </para>
    /// </summary>
    private async Task<List<TrafficCorridor>> EnrichCorridorsWithSuburbAsync(
        List<TrafficCorridor> corridors, CancellationToken cancellationToken)
    {
        try
        {
            var pending = new List<int>();
            var coords = new List<(double Lat, double Lng)>();
            for (int i = 0; i < corridors.Count; i++)
            {
                var corridor = corridors[i];
                if (corridor.Suburb.HasValue)
                    continue;
                if (corridor.StartPoint.HasValue)
                    coords.Add(corridor.StartPoint.Value);
                else if (corridor.Latitude.HasValue && corridor.Longitude.HasValue)
                    coords.Add((corridor.Latitude.Value, corridor.Longitude.Value));
                else
                    continue;
                pending.Add(i);
            }

            if (pending.Count == 0)
                return corridors;

            var places = await geocodingProvider.BatchReverseGeocodePlacesAsync(coords, cancellationToken);
            if (places.IsFailure)
            {
                logger.LogWarning("Corridor suburb lookup failed for {Count} corridor(s): {Error}",
                    pending.Count, places.Error);
                return corridors;
            }

            int named = 0;
            foreach ((int batchIndex, var place) in places.Value)
            {
                if (batchIndex < 0 || batchIndex >= pending.Count || !place.Suburb.HasValue)
                    continue;
                int target = pending[batchIndex];
                // A suburb that just restates the road locates nothing ("Wiley Park" is both).
                if (corridors[target].RoadName.Contains(place.Suburb.Value, StringComparison.OrdinalIgnoreCase))
                    continue;
                corridors[target] = corridors[target] with { Suburb = place.Suburb };
                named++;
            }

            logger.LogInformation("Corridor suburbs resolved: {Named} of {Pending} unlocated corridor(s)",
                named, pending.Count);
            return corridors;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Corridor suburb enrichment skipped: {Error}", ex.Message);
            return corridors;
        }
    }

    /// <summary>
    ///     Names the suburb of each good-run candidate, from the coordinate its longest flowing
    ///     reading carries.
    ///     <para>
    ///         The clear-run list is where a bare road name hurts most: an incident at least gets a
    ///         cross-street from its description, while a good run is only ever the name. ARN heard
    ///         "another clear run on the M4 Western Motorway, its service centre, Victoria Road and
    ///         Elizabeth Drive" on 2026-08-24 and could not place either street — Sydney has many of
    ///         both. Same Places lookup and same fail-soft contract as
    ///         <see cref="EnrichCorridorsWithSuburbAsync" />; a suburb that merely restates the road
    ///         is dropped.
    ///     </para>
    /// </summary>
    private async Task<List<FlowRoad>> EnrichFlowRoadsWithSuburbAsync(
        List<FlowRoad> roads, CancellationToken cancellationToken)
    {
        try
        {
            var pending = new List<int>();
            var coords = new List<(double Lat, double Lng)>();
            for (int i = 0; i < roads.Count; i++)
            {
                if (roads[i].Suburb is not null || roads[i].Point is not { } point)
                    continue;
                coords.Add(point);
                pending.Add(i);
            }

            if (pending.Count == 0)
                return roads;

            var places = await geocodingProvider.BatchReverseGeocodePlacesAsync(coords, cancellationToken);
            if (places.IsFailure)
            {
                logger.LogWarning("Good-run suburb lookup failed for {Count} road(s): {Error}",
                    pending.Count, places.Error);
                return roads;
            }

            foreach ((int batchIndex, var place) in places.Value)
            {
                if (batchIndex < 0 || batchIndex >= pending.Count || !place.Suburb.HasValue)
                    continue;
                int target = pending[batchIndex];
                if (roads[target].RoadName.Contains(place.Suburb.Value, StringComparison.OrdinalIgnoreCase))
                    continue;
                roads[target] = roads[target] with { Suburb = place.Suburb.Value };
            }

            return roads;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Good-run suburb enrichment skipped: {Error}", ex.Message);
            return roads;
        }
    }

    /// <summary>
    ///     Populates each corridor's <see cref="TrafficCorridor.Extent" /> ("from A to B") by
    ///     reverse-geocoding the two ends of the affected stretch — the routed StartPoint/EndPoint
    ///     when set, else the primary incident's shape ends. This is the ATN "magnitude is where the
    ///     queue reaches" device. Set ONLY when both ends resolve to distinct, speakable names that
    ///     aren't just the corridor's own road; otherwise left None. Extent is garnish — any failure
    ///     (incl. a replay 404 for un-recorded coords) degrades to no extent, never a bulletin error.
    /// </summary>
    private async Task<List<TrafficCorridor>> EnrichCorridorsWithExtentAsync(
        List<TrafficCorridor> corridors, CancellationToken cancellationToken)
    {
        try
        {
            var endpoints = new Dictionary<int, CorridorEndpoints>();
            for (int i = 0; i < corridors.Count; i++)
                if (ResolveCorridorEndpoints(corridors[i]) is { } ends)
                    endpoints[i] = ends;
            if (endpoints.Count == 0)
                return corridors;

            // One batch revgeocode for all endpoints; the coord list index is the dictionary key.
            var coords = new List<(double Lat, double Lng)>();
            var slots = new List<(int Corridor, bool IsStart)>();
            foreach ((int idx, var pair) in endpoints)
            {
                coords.Add(pair.Start); slots.Add((idx, true));
                coords.Add(pair.End); slots.Add((idx, false));
            }

            var geo = await geocodingProvider.BatchReverseGeocodeAsync(coords, cancellationToken);
            if (geo.IsFailure)
                return corridors;

            var startName = new Dictionary<int, string>();
            var endName = new Dictionary<int, string>();
            for (int k = 0; k < slots.Count; k++)
            {
                if (!geo.Value.TryGetValue(k, out string? name))
                    continue;
                string clean = CleanEndpointName(name);
                if (clean.Length == 0)
                    continue;
                (slots[k].IsStart ? startName : endName)[slots[k].Corridor] = clean;
            }

            var result = new List<TrafficCorridor>(corridors.Count);
            for (int i = 0; i < corridors.Count; i++)
            {
                var corridor = corridors[i];
                if (corridor.Extent.IsNone)
                {
                    startName.TryGetValue(i, out string? a);
                    endName.TryGetValue(i, out string? b);
                    var extent = ComposeExtent(a, b, corridor.RoadName);
                    if (extent.HasValue)
                        corridor = corridor with { Extent = extent };
                }
                result.Add(corridor);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogDebug("Extent enrichment skipped: {Error}", ex.Message);
            return corridors;
        }
    }

    /// <summary>
    ///     The two ends of a corridor's affected stretch: the routed Start/End points when present
    ///     (multi-incident corridors), else the primary incident's shape ends when the stretch is
    ///     long enough to be an extent rather than a single spot.
    /// </summary>
    internal static CorridorEndpoints? ResolveCorridorEndpoints(TrafficCorridor corridor)
    {
        if (corridor.StartPoint.HasValue && corridor.EndPoint.HasValue)
            return new CorridorEndpoints(corridor.StartPoint.Value, corridor.EndPoint.Value);

        if (corridor.TotalLengthMeters < ExtentMinLengthMeters)
            return null;

        var shape = corridor.Incidents
            .OrderBy(i => i.Severity)
            .FirstOrDefault(i => i.Shape is { Count: >= 2 })?.Shape;
        return shape is { Count: >= 2 }
            ? new CorridorEndpoints((shape[0].Lat, shape[0].Lng), (shape[^1].Lat, shape[^1].Lng))
            : null;
    }

    /// <summary>Reduces a reverse-geocode result to a speakable street/place name, or empty.</summary>
    internal static string CleanEndpointName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        string name = raw.Split(',')[0].Trim();
        return ExtentPlaceholderNames.Contains(name) || !name.Any(char.IsLetter)
            ? string.Empty
            : name;
    }

    /// <summary>
    ///     Builds "from A to B" from two reverse-geocoded endpoint names, or None when they don't
    ///     make an honest extent — a missing/empty end, the two ends the same place, or an end that
    ///     just restates the corridor's own road (which locates nothing). Road-name comparison is
    ///     abbreviation-aware (<see cref="RoadNameFormatter.IsSameRoad" />) — a reverse-geocoded
    ///     endpoint often comes back abbreviated ("Helensvale Rd") against the corridor's spelled-out
    ///     name ("Helensvale Road"), which an exact-string compare misses entirely (observed live:
    ///     shipped as "closed from Helensvale Road to Country Club Drive" — the corridor's own road
    ///     used as its own Extent start).
    /// </summary>
    internal static Option<string> ComposeExtent(string? startName, string? endName, string roadName)
    {
        if (string.IsNullOrWhiteSpace(startName) || string.IsNullOrWhiteSpace(endName))
            return default;
        if (RoadNameFormatter.IsSameRoad(startName, endName)
            || RoadNameFormatter.IsSameRoad(startName, roadName)
            || RoadNameFormatter.IsSameRoad(endName, roadName))
            return default;
        return Option<string>.Some($"from {startName} to {endName}");
    }

    private static TrafficIncident MapToDomain(
        HereIncidentItem item, DateTimeOffset? sourceUpdated,
        IReadOnlyDictionary<string, string>? refReplacements)
    {
        var details = item.IncidentDetails;
        var links = item.Location.Shape?.Links;
        var firstLink = links?.FirstOrDefault();

        (double? lat, double? lng) = firstLink?.Points?.Count > 0
            ? (firstLink.Points[0].Lat, firstLink.Points[0].Lng)
            : ((double?)null, (double?)null);

        // HERE's own road class for this incident's links. It is used AS REPORTED: deriving road
        // importance from words in the description let a name match promote a local road to motorway
        // class (Sydney 2026-08-13, two FC5 links aired as a closed M7).
        (double totalLength, int hereFunctionalClass) = links is { Count: > 0 }
            ? (links.Sum(l => l.Length), links.Min(l => l.FunctionalClass > 0 ? l.FunctionalClass : 5))
            : (0.0, 5);

        string? descriptionValue = details.Description?.Value;
        var parsed = IncidentDescriptionGrammar.Parse(descriptionValue);

        IReadOnlyList<string> segmentRefs = item.Location.SegmentRef?.Segments?
            .Select(s => ToRoutingAvoidSegmentId(s.Ref))
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        // HERE's refs kept INTACT — tile, direction sign and length. The Routing form above strips the
        // tile, and without it a segment cannot be resolved back to the map, which is why historical
        // incidents could not be checked against HERE's own topology at all.
        IReadOnlyList<SegmentSpan> spans = ToSegmentSpans(item.Location.SegmentRef, refReplacements);

        (var direction, var directionSource) = ResolveDirection(parsed, spans, links, totalLength);

        // The summary repeats the event without location ("Construction
        // work. Lane blocked.") — only worth carrying when it adds detail
        // the description doesn't already contain (typically lane info).
        Option<string> summary = default;
        string? summaryText = details.Summary?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(summaryText) &&
            !string.IsNullOrEmpty(descriptionValue) &&
            !descriptionValue.Contains(summaryText.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
        {
            summary = Option<string>.Some(summaryText);
        }

        Option<string> junctions = string.IsNullOrWhiteSpace(details.JunctionTraversability)
            ? default
            : Option<string>.Some(details.JunctionTraversability);

        // Operator free-text from the road authority — richer cross-street/direction/lane
        // detail than the structured description, but advisory-laden and source-varying, so
        // it is decoded, advisory-stripped, and length-capped before it can reach a script.
        Option<string> comment = IncidentCommentNormalizer.Normalize(
            details.Comment, descriptionValue ?? string.Empty);

        return new TrafficIncident(
            details.Id,
            Type: details.Type,
            Severity: DeriveSeverity(hereFunctionalClass, details.Type, totalLength),
            Description: descriptionValue ?? "Unknown traffic event",
            Location: UnidentifiedRoad,
            LengthMeters: totalLength,
            FunctionalClass: hereFunctionalClass,
            Latitude: lat.ToOption(),
            Longitude: lng.ToOption(),
            // HERE's per-incident revision time when it gives one, else the response's own
            // `sourceUpdated`. Never null when HERE told us anything about the data's age.
            DataTimestampUtc: details.LastUpdateTime ?? sourceUpdated,
            Direction: direction,
            Summary: summary,
            JunctionTraversability: junctions,
            SegmentRefs: segmentRefs,
            Comment: comment,
            Shape: BuildIncidentShape(item),
            RoadClosed: details.RoadClosed,
            EntryTimeUtc: details.EntryTime,
            OriginalId: details.OriginalId ?? string.Empty,
            HereFunctionalClass: hereFunctionalClass,
            HereSegmentSpans: spans,
            StartTimeUtc: details.StartTime,
            EndTimeUtc: details.EndTime,
            Codes: details.Codes,
            DirectionSource: directionSource,
            AuthorityPriority: Option.OfWhitespace(details.Criticality),
            // Explicit provenance (source-neutral fusion, 2026-08-18): HERE records say so
            // themselves rather than being the implicit default. Shared instance — one reference.
            Sources: EvidenceSources.HereOnly,
            SourceRecordIdentities:
            [
                new SourceRecordIdentity(EvidenceSources.Here, details.Id, details.OriginalId)
            ]);
    }

    /// <summary>
    ///     HERE's segment references expanded to their full form and paired with the length HERE gave
    ///     each one — the weight <see cref="RoadIdentityResolver" /> resolves road identity on.
    /// </summary>
    private static IReadOnlyList<SegmentSpan> ToSegmentSpans(
        HereSegmentRefLocation? segmentRef, IReadOnlyDictionary<string, string>? refReplacements) =>
        segmentRef?.Segments?
            .Select(s => new SegmentSpan(ExpandSegmentRef(s.Ref, refReplacements), s.Length ?? 0))
            .Where(s => !string.IsNullOrWhiteSpace(s.Ref))
            .DistinctBy(s => s.Ref, StringComparer.Ordinal)
            .ToList() ?? [];

    /// <summary>
    ///     Direction of travel, in order of authority: the words HERE itself put in the description,
    ///     then the shape's bearing but ONLY where every one of the record's segment references agrees
    ///     on a direction sign, then nothing at all.
    ///     <para>
    ///         The bearing was previously taken relative to the coverage area's centre, which made
    ///         direction a property of WHICH STATION fetched the record rather than of the road: one
    ///         457 m closure aired "citybound" 15 times and "outbound" once from byte-identical input.
    ///         Measured on 94 live Sydney incidents, 47 references were unanimously '-', 38 unanimously
    ///         '+', and 9 mixed — the mixed ones are exactly the records that must go directionless.
    ///     </para>
    /// </summary>
    private static (Option<string> Direction, DirectionSource Source) ResolveDirection(
        ParsedDescription parsed,
        IReadOnlyList<SegmentSpan> spans,
        List<HereShapeLink>? links,
        double totalLength)
    {
        if (parsed.StatedDirection.HasValue)
            return (parsed.StatedDirection, DirectionSource.HereDescription);

        if (spans.Count == 0 || totalLength < CompassDirection.MinShapeLengthMeters)
            return (default, DirectionSource.None);

        var signs = spans.Select(s => s.DirectionSign).Distinct().ToList();
        if (signs.Count != 1 || signs[0] is not ('+' or '-'))
            return (default, DirectionSource.None);

        var first = links?.FirstOrDefault(l => l.Points is { Count: > 0 })?.Points[0];
        var last = links?.LastOrDefault(l => l.Points is { Count: > 0 })?.Points[^1];
        if (first is null || last is null)
            return (default, DirectionSource.None);

        // A '-' reference runs AGAINST the segment's digitisation, so travel is the reverse of the
        // order HERE returned the shape in.
        var direction = signs[0] == '-'
            ? CompassDirection.ResolveCompass((last.Lat, last.Lng), (first.Lat, first.Lng))
            : CompassDirection.ResolveCompass((first.Lat, first.Lng), (last.Lat, last.Lng));

        return direction.HasValue
            ? (direction, DirectionSource.SegmentOriented)
            : (default, DirectionSource.None);
    }

    /// <summary>
    ///     Derives a meaningful severity (1-4) from road importance, incident type, and extent.
    ///     HERE's criticality is unreliable — it marks almost all closures as "critical"
    ///     regardless of road importance.
    /// </summary>
    private static int DeriveSeverity(int functionalClass, string type, double lengthMeters)
    {
        // Base severity from road importance: major roads = more severe
        int baseSeverity = functionalClass switch
        {
            1 => 1, // Freeway — always high impact
            2 => 1, // Highway — always high impact
            3 => 2, // Arterial — major impact
            4 => 3, // Collector — moderate impact
            _ => 4 // Local — low impact unless other factors
        };

        // Upgrade severity for high-impact types on any road
        if (type.Equals("accident", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("congestion", StringComparison.OrdinalIgnoreCase))
            baseSeverity = Math.Max(1, baseSeverity - 1);

        // Upgrade severity for long incidents (> 1km on local, > 2km on collector)
        if ((functionalClass >= 5 && lengthMeters > 1000) ||
            (functionalClass == 4 && lengthMeters > 2000))
            baseSeverity = Math.Max(1, baseSeverity - 1);

        return baseSeverity;
    }

    /// <summary>
    ///     Checks if the incident description contains a known major road name and returns its true FC.
    ///     HERE API assigns FC per link segment, which misclassifies ramps and merge lanes.
    /// </summary>
    private static int ApplyKnownRoadOverride(int linkFunctionalClass, string description)
    {
        foreach ((string roadName, int knownFc) in KnownRoadFunctionalClass)
            if (description.Contains(roadName, StringComparison.OrdinalIgnoreCase))
                return Math.Min(linkFunctionalClass, knownFc);
        return linkFunctionalClass;
    }

    internal HashSet<string>? GetEffectiveAllowedTypes(DateTime utcNow, string? timezoneId)
    {
        var baseTypes = _options.AllowedIncidentTypes.Count > 0
            ? new HashSet<string>(_options.AllowedIncidentTypes, StringComparer.OrdinalIgnoreCase)
            : null;

        if (baseTypes is null) return null;

        // Convert to the coverage area's local time so the overnight suppression window
        // (10pm-5am) correctly reflects local night, not UTC night.
        int hour = utcNow.Hour;
        if (!string.IsNullOrEmpty(timezoneId))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
                hour = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz).Hour;
            }
            catch (TimeZoneNotFoundException ex)
            {
                logger.LogWarning(ex,
                    "Timezone '{TimezoneId}' not found; falling back to UTC for overnight suppression",
                    timezoneId);
            }
            catch (InvalidTimeZoneException ex)
            {
                logger.LogWarning(ex,
                    "Timezone '{TimezoneId}' is invalid; falling back to UTC for overnight suppression",
                    timezoneId);
            }
        }

        // Suppress low-value types during overnight hours (10pm-5am local)
        if (hour is >= 22 or < 5)
        {
            baseTypes.Remove("plannedEvent");
            baseTypes.Remove("construction");
        }

        return baseTypes;
    }

    /// <summary>
    ///     Resolves the platform-default IANA timezone (<see cref="BroadcastOptions.DefaultTimezone" />)
    ///     to a <see cref="TimeZoneInfo" /> for corridor peak-hour weighting, or null when it can't
    ///     resolve — in which case <see cref="CorridorAggregator" /> skips time-weighting (unchanged
    ///     from the prior always-null behaviour). Mirrors the resolution/fallback in
    ///     <see cref="GetEffectiveAllowedTypes" />.
    /// </summary>
    internal TimeZoneInfo? ResolveDefaultTimeZone()
    {
        string? id = _broadcastOptions.DefaultTimezone;
        if (string.IsNullOrWhiteSpace(id))
            return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(ex,
                "Default timezone '{TimezoneId}' could not be resolved; corridor peak-hour weighting skipped for this fetch",
                id);
            return null;
        }
    }

    // `sourceUpdated` stamps the whole response with HERE's own data age. Not reading it left
    // DataTimestampUtc null on every incident in all 212 corpus jobs, so TrafficDataMaxAgeMinutes
    // filtered nothing and a superseded incident could still air.
    private sealed record HereTrafficResponse(
        List<HereIncidentItem> Results,
        DateTimeOffset? SourceUpdated = null,
        // HERE factors the catalog and segment prefixes out of every segmentRef into this map ($0, $1).
        // Without it the refs cannot be resolved back to the map at all.
        Dictionary<string, string>? RefReplacements = null);

    private sealed record HereIncidentItem(HereIncidentLocation Location, HereIncidentDetails IncidentDetails);

    private sealed record HereIncidentLocation(HereShapeLocation? Shape, HereSegmentRefLocation? SegmentRef);

    private sealed record HereShapeLocation(List<HereShapeLink> Links);

    private sealed record HereSegmentRefLocation(List<HereSegmentRef>? Segments);

    // `length` is HERE's own length for the segment and the weight road identity is resolved on.
    // It is bound as a nullable double deliberately: HERE documents a fractional value, live
    // responses returned integers (6, 8, 24, 522), and binding it to int threw FormatException
    // mid-array and failed the whole traffic fetch (dev, 2026-08-05). A double parses both shapes.
    private sealed record HereSegmentRef(string Ref, double? Length = null);

    /// <summary>
    ///     Converts a HERE Traffic API v7 <c>segmentRef</c> into the identifier format the
    ///     Routing API v8 <c>avoid[segments]</c> parameter accepts:
    ///     <c>here:cm:segment:&lt;id&gt;[#&lt;direction&gt;]</c> (direction <c>+</c>/<c>-</c>/<c>*</c>,
    ///     no offsets).
    ///     The Traffic API returns HMC topology refs in two shapes — compact
    ///     (<c>$0:&lt;tile&gt;:$1:&lt;segment&gt;#&lt;dir&gt;&lt;start&gt;..&lt;end&gt;</c>) or full
    ///     (<c>hrn:here:data::olp-here:rib-2:&lt;ver&gt;::&lt;tile&gt;:here:cm:segment:&lt;id&gt;#&lt;dir&gt;&lt;start&gt;..&lt;end&gt;</c>) —
    ///     and Routing v8 rejects both as "Invalid topology segment" (E605001) because its
    ///     segment IDs carry no catalog prefix or offset range. This strips both, preserving the
    ///     direction, and passes already-correct identifiers through unchanged.
    /// </summary>
    private static string ToRoutingAvoidSegmentId(string segmentRef)
    {
        var catalog = CatalogSegmentPattern().Match(segmentRef);
        if (catalog.Success)
            return catalog.Groups[2].Success
                ? $"here:cm:segment:{catalog.Groups[1].Value}#{catalog.Groups[2].Value}"
                : $"here:cm:segment:{catalog.Groups[1].Value}";

        var compact = CompactSegmentPattern().Match(segmentRef);
        if (compact.Success)
            return compact.Groups[2].Success
                ? $"here:cm:segment:{compact.Groups[1].Value}#{compact.Groups[2].Value}"
                : $"here:cm:segment:{compact.Groups[1].Value}";

        // Unknown shape — leave untouched rather than mangle; the request error (now logged with
        // HERE's cause) will name it.
        return segmentRef;
    }

    /// <summary>
    ///     Restores a compact HERE segment reference to its full form using the response's own
    ///     <c>refReplacements</c> map ("$0:367219725:$1:221129749#+0.3..0.9" →
    ///     "hrn:here:data::olp-here:rib-2:9203::367219725:here:cm:segment:221129749#+0.3..0.9").
    ///     The compact form cannot be looked up against the map, so an unexpanded ref silently disables
    ///     segment-based road identity — observed on dev, 2026-08-13.
    /// </summary>
    private static string ExpandSegmentRef(string reference, IReadOnlyDictionary<string, string>? replacements)
    {
        if (replacements is null || replacements.Count == 0 || !reference.Contains('$'))
            return reference;

        string expanded = reference;
        foreach ((string token, string value) in replacements)
            expanded = expanded.Replace($"${token}", value, StringComparison.Ordinal);
        return expanded;
    }

    [GeneratedRegex(@"here:cm:segment:(\d+)(?:#([+\-*]))?")]
    private static partial Regex CatalogSegmentPattern();

    [GeneratedRegex(@"\$1:(\d+)(?:#([+\-*]))?")]
    private static partial Regex CompactSegmentPattern();

    private readonly record struct FlowFetchKey(
        CoverageArea Area,
        CancellationToken CancellationToken);

    private sealed record SegmentRoadResolution(
        IReadOnlyDictionary<string, SegmentRoad> Roads,
        bool Failed);

    private sealed record HereShapeLink(List<HerePoint> Points, double Length, int FunctionalClass);

    private sealed record PreparedFlowRows(
        IReadOnlyList<FlowRow> Rows,
        IReadOnlyList<FlowIdentityCandidate> Candidates);

    private sealed record HerePoint(double Lat, double Lng);

    private sealed record HereIncidentDetails(
        string Id,
        string Type,
        string Criticality,
        HereDescription Description,
        DateTimeOffset? LastUpdateTime = null,
        HereDescription? Summary = null,
        string? JunctionTraversability = null,
        string? Comment = null,
        bool RoadClosed = false,
        DateTimeOffset? EntryTime = null,
        // HERE's stable handle on the event: `Id` changes with every revision HERE publishes
        // (measured live — one Kennedy Hwy hazard changed id between fetches three hours apart), so
        // only this can tell an UPDATE from a NEW event, or collapse one event reported twice.
        string? OriginalId = null,
        // HERE's validity window. Present on 100% of measured incidents; a 693-day window is a
        // worksite, not this morning's news (see IncidentWindow).
        DateTimeOffset? StartTime = null,
        DateTimeOffset? EndTime = null,
        // The AlertC cause list (ISO 14819-2), most significant first. `codes[0]` is the condition
        // key — the English description is a rendering of it, never the source of truth.
        List<int>? Codes = null);

    private sealed record HereDescription(string Value);

    // ── Flow API (jam tendency + flow-derived delay) ───────────────────

    // `refReplacements` matters as much here as on incidents: the flow feed also factors the catalog
    // and segment prefixes out of every segmentRef, and an unexpanded ref cannot be resolved against
    // the map at all.
    private sealed record HereFlowResponse(
        List<HereFlowItem>? Results,
        Dictionary<string, string>? RefReplacements = null);

    private sealed record HereFlowItem(HereFlowLocation Location, HereCurrentFlow CurrentFlow);

    // A flow row's own identity and geometry. `description` is a LABEL, not a road: 225 of 260
    // Sydney rows and 22 of 26 Cairns rows named a different road than HERE's own map for the
    // row's own segments, and the label class also produced "Wiley Park" (a suburb) and
    // "Australian Post Delivery Centre" (a depot) as on-air road names.
    private sealed record HereFlowLocation(
        string? Description,
        double Length,
        HereSegmentRefLocation? SegmentRef = null,
        HereShapeLocation? Shape = null);

    private sealed record HereCurrentFlow(
        double Speed,
        double FreeFlow,
        double JamFactor,
        string? Traversability = null,
        // HERE's confidence in this reading: >=0.7 real-time probe data, lower is a historical or
        // speed-limit estimate. Measured live on Sydney: 41 segments historical, 33 with none — all
        // voiced as current conditions.
        double? Confidence = null,
        // A parent segment averages over its length, so a standstill inside it reads as free-flowing.
        // Measured live: 21 Sydney roads carried ~9.9 km of high-jam subSegments inside parents we
        // put in the good-run pool.
        List<HereFlowSubSegment>? SubSegments = null);

    private sealed record HereFlowSubSegment(double Length, double JamFactor);
}

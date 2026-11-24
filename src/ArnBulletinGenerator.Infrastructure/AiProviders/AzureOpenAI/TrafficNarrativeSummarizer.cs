using System.Text;
using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Formats structured traffic corridor data into concise factual bullets
///     for LLM script generation. Outputs raw data — no conversational prose,
///     no editorial framing. The LLM is responsible for creative writing.
/// </summary>
internal static partial class TrafficNarrativeSummarizer
{
    /// <summary>
    ///     Shipped bar for crowning a monitored drive-time run as a headline figure. ARN can raise or
    ///     lower it per market via <see cref="EditorialSettings.DriveTimeHeadlineThresholdMinutes" />;
    ///     this is the fallback when they have not.
    /// </summary>
    private const int DefaultHeadlineDelayThresholdMinutes = 5;

    /// <summary>Vivid pace phrases in a corridor Description that already convey how the road
    /// drives — a mild Impact category label beside one reads as a contradiction.</summary>
    [GeneratedRegex(@"standstill|bumper to bumper|not moving|crawl", RegexOptions.IgnoreCase)]
    private static partial Regex StrongPaceRegex();

    /// <summary>
    ///     Builds a structured traffic data summary from corridors and incidents.
    ///     Falls back to the raw traffic string when no structured corridor data exists.
    /// </summary>
    public static string Summarize(BroadcastWithTraffic context)
    {
        // All-clear: when there's nothing congested but good-run roads ARE present, hand the writer
        // an EMPTY traffic block — the good-run section conveys the clear state. Returning the
        // "No significant traffic incidents to report." status line here invites the writer to read it
        // VERBATIM on air (a data-scaffolding echo). Keep the status line only when there is no good-run
        // content either (a genuinely empty market → content-limited → fallback).
        if (context.Corridors.Count == 0 && context.MonitoredRoutes.Count == 0)
            return context.GoodRunRoads.Count > 0 ? string.Empty : context.Traffic;

        var sb = new StringBuilder();
        var corridors = MergeBidirectionalClosures(context.Corridors);

        string city = context.City.HasValue ? context.City.Value : context.Location;
        sb.AppendLine($"Traffic data for {city} ({corridors.Count} corridor{(corridors.Count == 1 ? "" : "s")}):");
        sb.AppendLine();
        AppendMonitoredRouteBullets(sb, context.MonitoredRoutes, corridors, context.EditorialSettings);

        if (context.CuratedCorridorRoads.HasValue)
            AppendCuratedCorridorBullets(sb, corridors);
        else
            AppendCorridorBullets(sb, corridors, context.FillOrder.Contains(FillSource.MinorTraffic));
        AppendClearedSection(sb, context);

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    ///     One road closed in BOTH directions arrives as two corridors (one per direction) and the
    ///     writer voices whichever survives selection — observed live (Mildura, 2026-07-06): Walker
    ///     Road closed citybound AND outbound in the data, aired as "Walker Road citybound is shut",
    ///     inviting listeners to try the closed outbound side. Merge same-road closure corridors with
    ///     differing directions into ONE corridor marked "both directions" so the data itself states
    ///     the only broadcastable truth.
    /// </summary>
    private static IReadOnlyList<TrafficCorridor> MergeBidirectionalClosures(
        IReadOnlyList<TrafficCorridor> corridors)
    {
        var merged = new List<TrafficCorridor>(corridors.Count);
        var consumed = new HashSet<int>();
        for (int i = 0; i < corridors.Count; i++)
        {
            if (consumed.Contains(i))
                continue;
            var current = corridors[i];
            if (!IncidentTypeClassifier.IsClosure(current.PrimaryType) || !current.Direction.HasValue)
            {
                merged.Add(current);
                continue;
            }

            var partners = new List<int>();
            for (int k = i + 1; k < corridors.Count; k++)
            {
                var other = corridors[k];
                if (!consumed.Contains(k)
                    && IncidentTypeClassifier.IsClosure(other.PrimaryType)
                    && other.Direction.HasValue
                    && string.Equals(other.RoadName, current.RoadName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(other.Direction.Value, current.Direction.Value, StringComparison.OrdinalIgnoreCase))
                    partners.Add(k);
            }

            if (partners.Count == 0)
            {
                merged.Add(current);
                continue;
            }

            var allIncidents = current.Incidents
                .Concat(partners.SelectMany(p => corridors[p].Incidents))
                .DistinctBy(x => x.Id)
                .ToList();
            merged.Add(current with
            {
                Direction = Option<string>.Some("both directions"),
                Incidents = allIncidents,
                ImpactScore = Math.Max(current.ImpactScore, partners.Max(p => corridors[p].ImpactScore))
            });
            foreach (int p in partners)
                consumed.Add(p);
        }

        return merged;
    }

    private static void AppendMonitoredRouteBullets(
        StringBuilder sb,
        IReadOnlyList<MonitoredRouteStatus> routes,
        IReadOnlyList<TrafficCorridor> corridors,
        EditorialSettings? settings)
    {
        if (routes.Count == 0)
            return;

        // Reconcile against closure corridors BEFORE rendering: a monitored route voiced as
        // "running to time" while a closure corridor shuts the same road is an on-air
        // self-contradiction (frozen-eval baseline, Sydney: "Warringah Freeway citybound is closed…
        // the Warringah Freeway into the city is only a touch slow"). The closure is the story;
        // the monitored line adds nothing a listener can use, so it is suppressed entirely rather
        // than left for the writer to reconcile (it can't — it has no way to know which is fresher).
        var closureRoads = corridors
            .Where(c => IncidentTypeClassifier.IsClosure(c.PrimaryType)
                        || c.Incidents.Any(i => IncidentTypeClassifier.IsClosure(i.Type)))
            .Select(c => c.RoadName)
            .ToList();
        if (closureRoads.Count > 0)
            routes = routes
                .Where(r => !closureRoads.Any(road => RoadNameFormatter.ContainsRoad(r.Name, road)))
                .ToList();
        if (routes.Count == 0)
            return;


        // A monitored route is a HEADLINE candidate only when genuinely delayed (>= several minutes).
        // At or near typical it must NOT be crowned the lead nor voiced as a pointless "X-min run,
        // normally Y" — so split it out with qualitative, number-free framing the model can't turn
        // into a headline figure.
        // ARN-tunable: below this many minutes a monitored route is not worth a headline figure
        // (a "22-minute run, normally 20" spends airtime saying nothing). Unset falls back to 5.
        int headlineThreshold = settings?.DriveTimeHeadlineThresholdMinutes ?? DefaultHeadlineDelayThresholdMinutes;
        var delayed = routes.Where(r => r.DelayMinutes >= headlineThreshold).ToList();
        var onTime = routes.Where(r => r.DelayMinutes < headlineThreshold).ToList();

        if (delayed.Count > 0)
        {
            sb.AppendLine("Monitored drive-time routes — lead with one ONLY when it is the worst item on the board:");
            foreach (var route in delayed)
                sb.AppendLine(
                    $"\u2022 {RoadNameFormatter.ToSpeakable(route.Name)} \u2014 about {route.DelayMinutes} min slower than usual");
            sb.AppendLine();
        }

        // On-time asides are suppressed CLASS-WIDE when the board carries any closure or
        // standstill/heavy corridor. Same-road name-matching cannot be trusted here: HERE names
        // the corridor by gazetted name ("Western Motorway") while ARN configures the monitored
        // route by colloquial name ("the M4 from Parramatta to the city") — observed live
        // (Sydney, 2026-07-06): the M4 corridor sat "at a standstill" in the flow data while the
        // routing API's on-time aside for the same road aired as "only a touch slow". A
        // reassuring aside is only worth airtime on a quiet board; on a hot board it is at best
        // wasted words and at worst a direct contradiction of the flow data.
        bool hotBoard = corridors.Any(c =>
            IncidentTypeClassifier.IsClosure(c.PrimaryType)
            || c.Incidents.Any(i => IncidentTypeClassifier.IsClosure(i.Type))
            || (!string.IsNullOrWhiteSpace(c.Description) && StrongPaceRegex().IsMatch(c.Description)));
        if (onTime.Count > 0 && !hotBoard)
        {
            sb.AppendLine("Commuter routes running to time (NEVER the lead, and NEVER voice an \"X-minute run, normally Y\" figure — a brief aside at most):");
            foreach (var route in onTime)
                sb.AppendLine(route.DelayMinutes > 0
                    ? $"\u2022 {RoadNameFormatter.ToSpeakable(route.Name)} \u2014 running close to normal, only a touch slow"
                    : $"\u2022 {RoadNameFormatter.ToSpeakable(route.Name)} \u2014 running to time");
            sb.AppendLine();
        }
    }

    // ── Structured corridor bullets ─────────────────────────────────────

    private static void AppendCorridorBullets(
        StringBuilder sb,
        IReadOnlyList<TrafficCorridor> corridors,
        bool includeMinorTier)
    {
        // Tier by commuter-facing magnitude (measured delay / real queue), NOT HERE's flat
        // severity — which floors every freeway corridor at "1" and collapses the whole market
        // into one undifferentiated "Major" block. Within a tier, lead with the worst delay so
        // the model can see, and voice, the single worst corridor.
        var selection = SelectTieredCorridors(corridors, includeMinorTier);

        // Hard cap on individually-named corridors per tier — a busy market can return 10+ corridors
        // in one tier, and the writer's "name at most two, sweep the rest" instruction proved
        // unreliable under that load (the same class of failure RollCallDetector exists to catch
        // downstream). Capping the DATA itself makes a roll-call structurally impossible: the writer
        // cannot voice a road it was never given. Worst-first ordering (already applied above) means
        // the cap always keeps the corridors that matter most.
        //
        // When a Major-or-worse item leads the bulletin (a closure, or a blocking/heavy corridor),
        // the minor tier is sub-threshold noise: a "minor delays" side street changes no listener's
        // route once a road is SHUT elsewhere. Naming them individually is the fluff that buries
        // the headline (observed live: Warringah Freeway shut, then "O'Dea Avenue has minor delays,
        // Lister Avenue also running behind"). With a Major headline the minor tier is NOT rendered
        // at all. With NO Major headline the minor items ARE the story (a quiet market's only content),
        // so keep naming them at the cap.
        //
        // EXCEPTION: when the station's FillOrder (BFN-003) includes MinorTraffic, the pipeline is
        // explicitly asked to escalate into this tier as real-content fill BEFORE reaching Filler or
        // a stand-by bulletin — a genuinely fillable market should never fall back purely because a
        // marathon closure suppressed two other legitimately distinct roadworks items.
        var namedCorridors = selection.FullBullets
            .Concat(selection.MinorBullets)
            .ToList();
        var siblingTokens = BuildSiblingTokens(namedCorridors);

        AppendTierBullets(sb, "Major:", selection.Major, selection.MajorCap, siblingTokens, condensed: false);
        AppendTierBullets(sb, "Moderate:", selection.Moderate, selection.ModerateCap, siblingTokens, condensed: false);
        if (selection.MinorEnabled)
            AppendTierBullets(sb, "Minor:", selection.Minor, selection.MinorCap, siblingTokens, condensed: true);
    }

    /// <summary>
    ///     The one tier/cap decision shared by prose and evidence. Keep the rendered corridor sets
    ///     here rather than deriving Minor/full membership a second time in the evidence builder:
    ///     the AdditionalFactLines projection must receive the same condensed flag the prose used.
    /// </summary>
    private static TieredCorridorSelection SelectTieredCorridors(
        IReadOnlyList<TrafficCorridor> corridors,
        bool includeMinorTier)
    {
        var (major, moderate, minor) = ClassifyByTier(corridors);
        bool majorHeadline = major.Count > 0 && !includeMinorTier;

        int totalBudget = MajorTierCap + ModerateTierCap + MinorTierCap;
        int majorTake = Math.Min(major.Count, MajorTierCap);
        int moderateCap = ModerateTierCap + (MajorTierCap - majorTake);
        int moderateTake = Math.Min(moderate.Count, moderateCap);
        int minorCap = Math.Max(0, totalBudget - majorTake - moderateTake);
        int effectiveMinorCap = majorHeadline ? 0 : minorCap;

        return new TieredCorridorSelection(
            major,
            moderate,
            minor,
            majorTake,
            moderateCap,
            minorCap,
            majorHeadline,
            major.Take(majorTake).ToList()
                .Concat(moderate.Take(moderateTake))
                .ToList(),
            minor.Take(effectiveMinorCap).ToList());
    }

    private sealed record TieredCorridorSelection(
        List<TrafficCorridor> Major,
        List<TrafficCorridor> Moderate,
        List<TrafficCorridor> Minor,
        int MajorCap,
        int ModerateCap,
        int MinorCap,
        bool MajorHeadline,
        List<TrafficCorridor> FullBullets,
        List<TrafficCorridor> MinorBullets)
    {
        public bool MinorEnabled => !MajorHeadline;
    }

    /// <summary>Tier caps and voiceable selection live in <see cref="CorridorTiering" /> (Domain) —
    /// the single source of truth shared with the filler gap estimate, the good-run fill decision,
    /// and the eval's fabrication vocabulary.</summary>
    private const int MajorTierCap = CorridorTiering.MajorTierCap;
    private const int ModerateTierCap = CorridorTiering.ModerateTierCap;
    private const int MinorTierCap = CorridorTiering.MinorTierCap;

    private static (List<TrafficCorridor> Major, List<TrafficCorridor> Moderate, List<TrafficCorridor> Minor)
        ClassifyByTier(IReadOnlyList<TrafficCorridor> corridors) => CorridorTiering.ClassifyByTier(corridors);

    /// <summary>
    ///     One significant-token set per individually-named corridor's own road, keyed for exact
    ///     self-exclusion by name — feeds the cross-anchor suppression in <see cref="CorridorAnchors" />
    ///     (an anchor that only restates ANOTHER corridor's own road, which is ALSO being voiced as
    ///     its own item in this same bulletin, is redundant — observed live: "Fig Tree Pocket Road"
    ///     voiced as its own standstill AND as Centenary Highway's cross-street anchor; "Tonkin
    ///     Highway" voiced as its own item AND as Reid Highway's cross-street anchor).
    /// </summary>
    private static List<(string RoadName, HashSet<string> Tokens)> BuildSiblingTokens(
        IReadOnlyList<TrafficCorridor> namedCorridors) =>
        namedCorridors
            .Select(c => (c.RoadName, SignificantTokens(c.RoadName)))
            .Where(x => x.Item2.Count > 0)
            .ToList();

    private static void AppendTierBullets(
        StringBuilder sb, string header, List<TrafficCorridor> corridors, int cap,
        List<(string RoadName, HashSet<string> Tokens)> siblingTokens, bool condensed)
    {
        if (corridors.Count == 0)
            return;

        sb.AppendLine(header);
        foreach (var corridor in corridors.Take(cap))
        {
            if (condensed)
                AppendCondensedCorridorBullet(sb, corridor, siblingTokens);
            else
                AppendCorridorBullet(sb, corridor, siblingTokens);
        }

        int remaining = corridors.Count - cap;
        if (remaining > 0)
            sb.AppendLine(
                $"- (plus {remaining} more corridor{(remaining == 1 ? "" : "s")} at this severity, below the " +
                "named cut — leave them out entirely; never voice a vague summary of them)");

        sb.AppendLine();
    }

    /// <summary>
    ///     Curated regeneration: the operator hand-ordered these corridors, so emit them
    ///     flat in that order instead of regrouping by severity tier.
    /// </summary>
    private static void AppendCuratedCorridorBullets(
        StringBuilder sb,
        IReadOnlyList<TrafficCorridor> corridors)
    {
        sb.AppendLine("Producer-selected corridors (in priority order — cover in this order):");
        var siblingTokens = BuildSiblingTokens(corridors);
        foreach (var corridor in corridors)
            AppendCorridorBullet(sb, corridor, siblingTokens);
        sb.AppendLine();
    }


    /// <summary>
    ///     Writes a single corridor as a structured data bullet with sub-lines for
    ///     incidents, impact, drive time, extent, and per-incident location anchors.
    ///     All measurements are emitted numerically — the LLM's style rules decide
    ///     which (at most two) to voice; the rest it renders qualitatively. Location
    ///     lines carry the cross-street anchors ("Between X and Y", "At Z") that the
    ///     ATN style is built on; without them the model cannot anchor incidents.
    /// </summary>
    private static void AppendCorridorBullet(
        StringBuilder sb, TrafficCorridor corridor,
        List<(string RoadName, HashSet<string> Tokens)> siblingTokens)
    {
        string direction = corridor.Direction.HasValue ? $" {corridor.Direction.Value}" : "";
        // Suppress the suburb anchor when road-name extraction has fallen back to
        // the suburb itself — otherwise the bullet reads "Neranwood near Neranwood".
        string suburb = corridor.Suburb.HasValue &&
                        !corridor.RoadName.Contains(corridor.Suburb.Value, StringComparison.OrdinalIgnoreCase)
            ? $" near {corridor.Suburb.Value}"
            : "";
        string incidentLabel = MapIncidentType(corridor.PrimaryType);
        string newFlag = corridor.Incidents.Any(i => i.IsNew) ? " [NEW]" : "";

        // Primary bullet: road, direction, suburb, type. The magnitude word is intentionally NOT
        // repeated here — the section header (Major:/Moderate:/Minor:) already conveys the ranking
        // tier, and the Impact line below carries the honest, delay-driven magnitude. Repeating the
        // tier word produced contradictions ("congestion, major" beside "delays ~5 min (minor delays)")
        // and let the writer escalate a tier word into an invented time.
        sb.AppendLine(
            $"\u2022 {RoadNameFormatter.ToSpeakable(corridor.RoadName)}{direction}{suburb} \u2014 {incidentLabel}{newFlag}");
        AppendMagnitudeFields(sb, corridor);

        // Incident types if multiple distinct types — capped at 2, and EXCLUDING the corridor's own
        // header label: "road closure" on the bullet line plus "road closure, construction" on the
        // Incidents line is the same fact twice, and the writer voiced both ("closed ... due to road
        // closure work", fresh-live eval Sydney CBD). Only types ADDING information survive.
        var distinctTypes = corridor.Incidents
            .Select(i => MapIncidentType(i.Type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => !string.Equals(t, incidentLabel, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        if (distinctTypes.Count > 0)
            sb.AppendLine($"  Incidents: {string.Join(", ", distinctTypes)}");

        // Impact and Detail can genuinely disagree: a flow-derived congestion corridor can carry a
        // small measured delay ("minor delays") while its jam-factor description says "at a
        // standstill". Handing the writer both produced the on-air stack "at a standstill, with
        // minor delays" (frozen-eval iter4, Sydney) — two worlds in one breath. The vivid pace
        // phrase is the information a listener acts on; suppress the mild category label under it.
        // A closure's "road closed" impact likewise restates the header label ("road closure") —
        // and the writer voiced the label verbatim ("Coast Highway southbound is road closed",
        // fresh-live eval Gold Coast). The header alone carries the closure.
        string impact = FormatImpact(corridor);
        bool detailCarriesPace = !string.IsNullOrWhiteSpace(corridor.Description)
                                 && StrongPaceRegex().IsMatch(corridor.Description);
        bool impactIsMildLabel = impact is "minor delays" or "moderate delays";
        bool impactRestatesClosureLabel = string.Equals(impact, "road closed", StringComparison.OrdinalIgnoreCase)
                                          && IncidentTypeClassifier.IsClosure(corridor.PrimaryType);
        if (!(detailCarriesPace && impactIsMildLabel) && !impactRestatesClosureLabel)
            sb.AppendLine($"  Impact: {impact}");

        if (corridor.Detour.HasValue)
        {
            var detour = corridor.Detour.Value;
            sb.AppendLine(
                $"  Detour: safe avoided-segment alternative {detour.DurationMinutes} min, saves {detour.SavedMinutes} min, {detour.DistanceKm:F1}km");
        }


        // Extent
        if (corridor.Extent.HasValue)
            sb.AppendLine($"  Extent: {RoadNameFormatter.ToSpeakable(corridor.Extent.Value)}");

        // Lane detail from incident summaries ("Lane blocked") that the
        // descriptions don't already carry
        var laneDetails = corridor.Incidents
            .Where(i => i.Summary.HasValue)
            .Select(i => i.Summary.Value.TrimEnd('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2);
        foreach (string laneDetail in laneDetails)
            sb.AppendLine($"  Lanes: {laneDetail}");

        // Side-street status along closures
        if (corridor.Incidents.Any(i =>
                i.JunctionTraversability.HasValue &&
                i.JunctionTraversability.Value.Equals("allClosed", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("  Junctions: side streets along the closure are also closed");
        }

        // Per-incident location anchors — the cross-street detail the ATN
        // style anchors on. Deduplicated, capped, and skipped when the
        // description carries no information beyond the incident type.
        int locationCap = corridor.Extent.HasValue ? 1 : 2;
        var anchors = CorridorAnchors(corridor, siblingTokens).Take(locationCap).ToList();
        // Two anchors = two DISTINCT incidents at different points on the road. Rendered as bare
        // twin "Location:" lines the writer welded them into one composite place — observed live
        // (Brisbane, 2026-07-06): construction "between Bruce Hwy and Sandgate Rd" plus a breakdown
        // "at Steve Irwin Way" (~45 km apart) aired as "a breakdown near Sandgate Road at Steve
        // Irwin Way". Label the plurality explicitly so the data states they are separate spots.
        if (anchors.Count > 1)
        {
            sb.AppendLine("  Locations (SEPARATE spots along this road — voice separately or pick the worst; never combine into one place):");
            foreach (string anchor in anchors)
                sb.AppendLine($"    - {anchor}");
        }
        else if (anchors.Count == 1)
        {
            sb.AppendLine($"  Location: {anchors[0]}");
        }

        // Description (raw factual detail from source)
        if (!string.IsNullOrWhiteSpace(corridor.Description))
            sb.AppendLine($"  Detail: {corridor.Description.TrimEnd('.')}");

        // Operator source detail (cross-streets, direction, ops status) the structured
        // description omits — normalized and advisory-stripped in IncidentCommentNormalizer.
        // Major/Moderate only: this method is never called for the condensed minor bullet, so a
        // busy market's standing-works comments ("Vms Trailer", maintenance windows) stay out.
        var sourceDetails = corridor.Incidents
            .Where(i => i.Comment.HasValue)
            .Select(i => i.Comment.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2);
        foreach (string sourceDetail in sourceDetails)
            sb.AppendLine($"  Source detail: {sourceDetail}");

        // Provenance, source-neutrally (2026-08-18): the feeds this corridor's evidence came
        // from, plus the authority's own detail line when one contributed. No feed is "the base";
        // multi-source agreement is stated as corroboration, an official-only event as an alert.
        var sources = EvidenceSources.Of(corridor.Sources);
        bool hasOfficial = sources.Any(s => !s.Equals(EvidenceSources.Here, StringComparison.OrdinalIgnoreCase));
        if (hasOfficial && sources.Contains(EvidenceSources.Here, StringComparer.OrdinalIgnoreCase))
        {
            string officials = string.Join(" + ", sources.Where(s =>
                !s.Equals(EvidenceSources.Here, StringComparison.OrdinalIgnoreCase)));
            string cross = corridor.OfficialCrossStreet.HasValue ? $" at {corridor.OfficialCrossStreet.Value}" : "";
            sb.AppendLine($"  Corroborated by {officials}{cross}");
        }
        else if (hasOfficial)
        {
            sb.AppendLine($"  Official alert ({string.Join(" + ", sources)}): "
                          + $"{corridor.OfficialHeadline.GetValueOr(corridor.Description)}");
        }

        // The authority's own operational detail ("Traffic control on scene") — enrichment a road
        // authority knows and a telematics feed cannot, source-generic via OfficialEvidences so
        // every current and future feed flows through this one line. Vetted through the SAME
        // advisory-stripping normalizer as operator comments: raw feed text never reaches the
        // prompt, and descriptions remain verbatim-as-parsed in the persisted receipts.
        foreach (var evidence in corridor.OfficialEvidences ?? [])
        {
            if (evidence.Description is not { Length: > 0 })
                continue;
            var authorityDetail = IncidentCommentNormalizer.Normalize(
                evidence.Description, $"{corridor.Description} {evidence.Headline}");
            if (!authorityDetail.HasValue)
                continue;
            sb.AppendLine($"  Authority detail ({evidence.SourceId}): {authorityDetail.Value}");
            break;
        }
        foreach (var line in AdditionalFactLines(corridor))
            sb.AppendLine(line.Text);


        // A closure claim the boundary reduced still explains itself to the writer.
        if (corridor.Incidents.Any(i => i.WithheldReason.HasValue
                && i.WithheldReason.Value.StartsWith("demoted:uncorroborated", StringComparison.Ordinal)))
        {
            sb.AppendLine("  Status: closure not corroborated by any road authority — voiced as standing roadworks, never as a closure");
        }
    }

    /// <summary>
    ///     Optional source attributes carried by each incident's atomic fact. This is the single
    ///     selection and wording projection shared by the prose and evidence-unit builders; changing
    ///     either side independently would let the writer cite a fact it never saw (or voice an
    ///     uncitable fact). Every value is source-owned and absent values stay absent.
    ///
    ///     Full bullets receive every available projection. Condensed Minor bullets receive only
    ///     voicing facts: lane counts and any separately affected asset. This split is intentional:
    ///     237 of 849 live prose records (28%) carry lane facts that are absent from structured fields,
    ///     with 68% of those concentrated in NSW roadworks. Minor is exactly where generic "minor
    ///     delays" wording otherwise loses that useful detail. Other assets stay because a second
    ///     closure (for example, the Sydney Harbour Tunnel beside Warringah Freeway) must not vanish
    ///     merely because the corridor is condensed. Corroboration, canonical class, and authority
    ///     priority are ranking/provenance signals, so they remain full-bullet-only to protect the
    ///     condensed tier's token budget.
    /// </summary>
    private static IEnumerable<(string Field, string Value, string Text)> AdditionalFactLines(
        TrafficCorridor corridor,
        bool condensed = false)
    {
        if (!condensed && corridor.Sources is { Count: > 1 } && corridor.CorroborationCount > 1)
        {
            string value = $"{corridor.CorroborationCount} authorities agree";
            yield return ("corroboration", value, $"  Corroboration: {value}");
        }

        var incidents = corridor.Incidents
            .Where(i => i.Fact is not null)
            .Select(i => (Incident: i, Fact: i.Fact!))
            .ToList();

        var laneClosed = incidents
            .Where(x => x.Fact.LanesClosed.HasValue && x.Fact.LanesClosed.Value > 0)
            .Select(x => x.Fact.LanesClosed.Value.ToString(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .Take(2);
        foreach (string value in laneClosed)
            yield return ("lanesClosed", value, $"  Lanes closed: {value}");

        var lanesTotal = incidents
            .Where(x => x.Fact.LanesTotal.HasValue && x.Fact.LanesTotal.Value > 0)
            .Select(x => x.Fact.LanesTotal.Value.ToString(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .Take(2);
        foreach (string value in lanesTotal)
            yield return ("lanesTotal", value, $"  Total lanes: {value}");

        var otherAssets = incidents
            .SelectMany(x => x.Fact.OtherAssets ?? [])
            .Where(asset => !string.IsNullOrWhiteSpace(asset))
            .Select(asset => asset.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2);
        foreach (string value in otherAssets)
            yield return ("otherAsset", value,
                $"  Other affected asset (separate from {corridor.RoadName}): {value}");

        // ── Facts we already held and never voiced (2026-08-25) ─────────────
        // Both are yielded HERE rather than straight into the evidence builder because this method
        // is the shared source for the prose block and the citable units: a unit with no prose line
        // lets the writer voice something an operator never saw, which is the divergence the file
        // header forbids.

        // The authority's own headline is populated on 31% of live dev corridors and earned zero
        // citations, because nothing ever emitted it. It is PROSE, so it is never passed through
        // whole - that is the "40 through the works" defect, where a rendered phrase in an evidence
        // value was read back almost verbatim. Only the closed-form works window is lifted out:
        // "At Gympie and Webster Road intersection, 9.30pm to 4am between Murphy Road and Ledbury
        // Street" -> "9.30pm to 4am". That fact exists nowhere else in our payload, and without it
        // every roadworks item in a read collapses into the same sentence shape.
        if (corridor.OfficialHeadline.HasValue
            && !string.IsNullOrWhiteSpace(corridor.OfficialHeadline.Value))
        {
            var window = WorksWindowRegex().Match(corridor.OfficialHeadline.Value);
            if (window.Success)
            {
                string value = $"{window.Groups[1].Value.Trim()} to {window.Groups[2].Value.Trim()}";
                yield return ("worksWindow", value, $"  Works window: {value}");
            }
        }

        // HERE reports intermediateClosedEdgeOpen on effectively every closure (11 of 11 in the
        // replay corpus); it describes the closed edge's junction topology, not local access, and
        // voicing it put unsupported access claims on air (dev perth a409dbd4 and goldcoast
        // e55c3619, 2026-09-03), so it is no longer evidence.

        if (condensed)
            yield break;

        var canonicalClasses = incidents
            .Where(x => x.Fact.CanonicalClass.HasValue)
            .Select(x => x.Fact.CanonicalClass.Value.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2);
        foreach (string value in canonicalClasses)
            yield return ("canonicalClass", value, $"  Canonical class: {value}");

        var priorities = incidents
            .Where(x => x.Fact.AuthorityPriority.HasValue)
            .Select(x =>
            {
                string source = EvidenceSources.Of(x.Incident.Sources)
                    .FirstOrDefault(s => !s.Equals(EvidenceSources.Here, StringComparison.OrdinalIgnoreCase))
                    ?? EvidenceSources.Of(x.Incident.Sources)[0];
                return (Value: x.Fact.AuthorityPriority.Value.Trim(), Text: $"  Authority priority ({source}): {x.Fact.AuthorityPriority.Value.Trim()}");
            })
            .Where(x => x.Value.Length > 0)
            .DistinctBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
            .Take(2);
        foreach (var item in priorities)
            yield return ("authorityPriority", item.Value, item.Text);

        var speedLimits = incidents
            .Where(x => x.Fact.SpeedLimitKph.HasValue && x.Fact.SpeedLimitKph.Value > 0)
            .Select(x => x.Fact.SpeedLimitKph.Value.ToString(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .Take(2);
        foreach (string value in speedLimits)
            yield return ("speedLimitKph", value, $"  Reduced speed limit: {value} km/h");

        var vehicleDetails = incidents
            .Where(x => x.Fact.SubCategory.HasValue && IsVehicleDetail(x.Fact.SubCategory.Value))
            .Select(x => x.Fact.SubCategory.Value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2);
        foreach (string value in vehicleDetails)
            yield return ("vehicleDetail", value, $"  Vehicle detail: {value}");
    }

    private static bool IsVehicleDetail(string value) =>
        value.Equals("Semi-trailer", StringComparison.OrdinalIgnoreCase)
        || value.Equals("2 trucks", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Oversize vehicle", StringComparison.OrdinalIgnoreCase)
        || value.Equals("2 cars", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Car", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Van", StringComparison.OrdinalIgnoreCase)
        || value.Equals("Stationary vehicle", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Renders a minor corridor as a compact line (road, direction, suburb, type, short impact,
    ///     and one anchor), followed only by the voicing facts selected by AdditionalFactLines.
    /// </summary>
    private static void AppendCondensedCorridorBullet(
        StringBuilder sb, TrafficCorridor corridor,
        List<(string RoadName, HashSet<string> Tokens)> siblingTokens)
    {
        string direction = corridor.Direction.HasValue ? $" {corridor.Direction.Value}" : "";
        string suburb = corridor.Suburb.HasValue &&
                        !corridor.RoadName.Contains(corridor.Suburb.Value, StringComparison.OrdinalIgnoreCase)
            ? $" near {corridor.Suburb.Value}"
            : "";
        string incidentLabel = MapIncidentType(corridor.PrimaryType);
        string newFlag = corridor.Incidents.Any(i => i.IsNew) ? " [NEW]" : "";
        // One inline cross-street anchor so a minor item still tells listeners WHERE it is —
        // a bare road name is the clearest data-reading tell. Major bullets carry up to three
        // as Location sub-lines; a condensed minor line gets the single best one, inline.
        string anchor = CorridorAnchors(corridor, siblingTokens).FirstOrDefault() is { } a ? $", {a}" : "";
        // A closure's impact ("road closed") restates its own type label ("road closure") — one
        // fact, two spellings. Handing the writer both produced "Mitchell Street outbound is shut,
        // with the road closed" (frozen-eval iter6, Newcastle). Voice the label alone.
        string impact = FormatImpact(corridor);
        string impactPart = string.Equals(impact, "road closed", StringComparison.OrdinalIgnoreCase)
                            && IncidentTypeClassifier.IsClosure(corridor.PrimaryType)
            ? ""
            : $", {impact}";
        sb.AppendLine(
            $"\u2022 {RoadNameFormatter.ToSpeakable(corridor.RoadName)}{direction}{suburb} \u2014 {incidentLabel}{impactPart}{anchor}{newFlag}");
        AppendMagnitudeFields(sb, corridor);
        foreach (var line in AdditionalFactLines(corridor, condensed: true))
            sb.AppendLine(line.Text);

    }
    /// <summary>
    ///     The cross-street anchors carried by a corridor's incident descriptions — the spots the
    ///     ATN style pins every item to ("At Pine Street", "approaching Mowbray Road", "between X
    ///     and Y"). Deduplicated, "Closed"-only stripped, and self-reference suppressed (an anchor
    ///     that only restates the corridor's own road name locates nothing) — AND cross-anchor
    ///     suppressed: a narrower anchor that only restates a place already covered by the Extent
    ///     span OR by an earlier, richer anchor from the SAME corridor ("At Tamborine Mountain Road"
    ///     after "between Tamborine Mountain Road and Henri Robert Drive" — observed live, Gold
    ///     Coast), OR a place that is itself ANOTHER corridor's own road ALSO being voiced as its
    ///     own item in this bulletin ("Fig Tree Pocket Road" as its own standstill item AND as
    ///     Centenary Highway's cross-street anchor; "Tonkin Highway" as its own item AND as Reid
    ///     Highway's cross-street anchor — both observed live) — is redundant, not new information;
    ///     Location: is for a DIFFERENT spot, never a second voicing of a place the listener already
    ///     heard, whether in this item or an earlier one in the same bulletin.
    /// </summary>
    private static List<string> CorridorAnchors(
        TrafficCorridor corridor, List<(string RoadName, HashSet<string> Tokens)> siblingTokens)
    {
        var extentTokens = corridor.Extent.HasValue ? SignificantTokens(corridor.Extent.Value) : null;

        var candidates = corridor.Incidents
            .Select(i => i.Description.Trim().TrimEnd('.'))
            .Where(d => d.Length > 12 && !d.Equals("Closed", StringComparison.OrdinalIgnoreCase))
            .Select(d => CleanAnchor(d, corridor.RoadName))
            .Where(a => !string.IsNullOrEmpty(a))
            .Select(a => a!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(a => (Anchor: a, Tokens: SignificantTokens(a)))
            // Richer/more-specific anchors first ("between A and B" before a lone "at A") so a
            // later, narrower anchor naming a place already covered is the one dropped.
            .OrderByDescending(x => x.Tokens.Count)
            .ToList();

        var covered = extentTokens is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(extentTokens, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var (anchor, tokens) in candidates)
        {
            if (tokens.IsSubsetOf(covered))
                continue;
            // Sibling suppression: an anchor that only restates ANOTHER individually-named
            // corridor's own road (excluding this corridor itself, matched by name) adds no new
            // location — that road is already being voiced as its own item.
            if (siblingTokens.Any(s =>
                    !string.Equals(s.RoadName, corridor.RoadName, StringComparison.OrdinalIgnoreCase)
                    && tokens.IsSubsetOf(s.Tokens)))
                continue;
            result.Add(RoadNameFormatter.ToSpeakable(anchor));
            covered.UnionWith(tokens);
        }

        return result;
    }

    /// <summary>
    ///     The cleared block in the writer's data. Gated and capped identically to
    ///     <c>AppendClearedEvidence</c> and <see cref="IncidentChangeFormatter" /> — three paths, one
    ///     answer. They previously disagreed: the formatter capped at 2, these two were unlimited.
    /// </summary>
    private static void AppendClearedSection(StringBuilder sb, BroadcastWithTraffic context)
    {
        if (!context.IncidentChanges.HasValue)
            return;

        int cap = context.EditorialSettings.ContinuityClearedCap ?? IncidentChangeFormatter.DefaultClearedCap;
        var cleared = context.IncidentChanges.Value.ClearedIncidents
            .Where(c => !RoadNameFormatter.LooksLikeRawAddress(c.RoadName.GetValueOr("unknown road")))
            .Where(IncidentChangeFormatter.IsClearedWorthVoicing)
            .Take(cap)
            .ToList();
        if (cleared.Count == 0)
            return;

        sb.AppendLine("Cleared:");
        foreach (var item in cleared)
        {
            string road = item.RoadName.GetValueOr("unknown road");
            string type = MapIncidentType(item.Type);
            string suburbInfo = item.Suburb.HasValue ? $" near {item.Suburb.Value}" : "";
            sb.AppendLine($"\u2022 {type} on {RoadNameFormatter.ToSpeakable(road)}{suburbInfo} has cleared");
        }
    }
    /// <summary>
    ///     Appends machine-readable magnitude fields beside a corridor's qualitative prose. These
    ///     labels are part of the draft data block, not words for the bulletin: the writer receives
    ///     the measured band without being invited to echo a per-corridor minute figure.
    /// </summary>
    private static void AppendMagnitudeFields(StringBuilder sb, TrafficCorridor corridor)
    {
        sb.AppendLine($"  magnitudeTier: {CorridorMagnitude.Classify(corridor)}");
        sb.AppendLine($"  voiceability: {VoiceabilityClassifier.Classify(corridor)}");
        if (corridor.DelayMinutes.HasValue && corridor.DelayMinutes.Value > 0)
            sb.AppendLine($"  delayMinutes: {corridor.DelayMinutes.Value}");
    }


    // ── Impact formatting ───────────────────────────────────────────────

    /// <summary>
    ///     Formats a corridor's impact as a QUALITATIVE label. No number leaves this method: the
    ///     only measured magnitudes a bulletin may voice are a monitored route's drive time
    ///     (<see cref="AppendMonitoredRouteBullets" />) and a routed/flow-derived delay, and both
    ///     are rendered as bands here rather than figures. Every distance this method used to emit
    ///     was derived by us (a sum of overlapping HERE records), not reported by HERE.
    /// </summary>
    private static string FormatImpact(TrafficCorridor corridor)
    {
        // A closure is its own magnitude — the road (or ramp) being shut is the headline, never a
        // residual "minor delays" figure HERE still reports for the through-lanes. Subordinating it
        // ("shut ... so minor delays there") buries the one thing a listener must hear, so skip the
        // delay-minute branch entirely and hand the writer the closure as the lead.
        if (IncidentTypeClassifier.IsClosure(corridor.PrimaryType))
            return "road closed";
        if (corridor.DelayMinutes.HasValue && corridor.DelayMinutes.Value > 0)
            // Magnitude is voiced QUALITATIVELY — never a minute count. ATN quotes a time only on a
            // monitored point-to-point run (see AppendMonitoredRouteBullets), the one numbers channel;
            // a per-corridor "~N min" is what produced the stacked minute-counts the corpus never uses.
            return corridor.DelayMinutes.Value switch
            {
                <= 5 => "minor delays",
                <= 15 => "moderate delays",
                <= 30 => "heavy delays",
                _ => "severe delays"
            };

        double extentMeters = corridor.VoicedExtentMeters;
        if (extentMeters > 500)
        {
            // Extent is voiced QUALITATIVELY ONLY. Any number here is a measurement claim, and the
            // only distance HERE gives us per incident is that incident's own affected length —
            // never a queue. Cairns 2026-08-13: six overlapping Kennedy Highway records (two
            // carriageways of the same works plus hazard records over shared segments) summed to
            // 12,169 m and rendered "queue ~10km (long queue)" while EVERY measured delay on that
            // corridor was 0; the writer faithfully aired "queues back about ten kays" and the
            // real backup was ~700 m. Distances now come only from a measured source: the routed
            // delay branch above and the monitored point-to-point runs in
            // AppendMonitoredRouteBullets. The extent picks the honest label and nothing more.
            if (IncidentTypeClassifier.IsStandingFootprint(corridor.PrimaryType))
                return extentMeters < 1000 ? "short work zone" : "extended work zone";

            return extentMeters switch
            {
                <= 1500 => "short queue",
                <= 3000 => "moderate queue",
                _ => "long queue"
            };
        }

        // No measured delay and no real queue. A Major/Severe tier here can only come from the
        // incident-type floor (a closure or blocking crash on a significant road), so describe the
        // CONDITION — never invent a time magnitude. Saying "heavy/severe delays" for a closure with
        // no routed delay is what let the writer voice "the best part of an hour" off a +1-min road.
        return CorridorMagnitude.Classify(corridor) switch
        {
            MagnitudeTier.Severe or MagnitudeTier.Major => "blocking — no measured delay",
            MagnitudeTier.Moderate => "slow but moving",
            _ => "minor impact"
        };
    }


    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    ///     Maps a HERE incident type code to the words a presenter says.
    ///     <para>
    ///         This used to be a second copy of <see cref="TrafficTypeMapper" />'s table, and the
    ///         two had drifted: this one said "planned event", "lane restriction", "road hazard" and
    ///         "construction" where the other said "Planned event", "Lane closure", "Hazard on road"
    ///         and "Roadworks". Fixing only this copy on the 2026-08-25 run left "planned event"
    ///         still airing, because the corridor Description is built from the OTHER one. One
    ///         table, one set of words.
    ///     </para>
    /// </summary>
    private static string MapIncidentType(string primaryType) =>
        TrafficTypeMapper.MapType(primaryType).ToLowerInvariant();

    // ── Self-referential anchor suppression ─────────────────────────────

    /// <summary>Anchor token separators; apostrophes are kept for names like O'Riordan.</summary>
    private static readonly char[] TokenSeparators =
        [' ', ',', '-', '\u2013', '\u2014', '/', '(', ')', '.', ':', ';', '\t'];

    /// <summary>
    ///     Connectives, directions, and road-type words that locate nothing on their own. A
    ///     description built only from these plus the corridor's own road name is a self-reference,
    ///     not a cross-street anchor.
    /// </summary>
    private static readonly FrozenSet<string> AnchorFillerWords =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "at", "on", "near", "the", "to", "of", "in", "a", "an", "ramp", "exit", "onto", "off",
            "approaching", "before", "after", "between", "and", "from", "towards", "toward",
            "bound", "city", "citybound", "inbound", "outbound", "eastbound", "westbound",
            "northbound", "southbound",
            "road", "rd", "street", "st", "highway", "hwy", "freeway", "fwy", "motorway", "mwy",
            "mtwy", "bridge", "br", "tunnel", "avenue", "ave", "terrace", "tce", "parade", "pde",
            "drive", "dr", "place", "pl", "crescent", "cres", "court", "ct", "lane", "ln",
            "boulevard", "blvd", "close", "cl", "grove", "gr", "circuit", "cct", "way", "bypass",
            "byp", "ring", "connector", "link",
            "closed", "closure", "blocked", "blocking", "shut", "open"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);


    /// <summary>
    ///     Cleans a raw incident description into a speakable cross-street anchor, or null when it
    ///     only restates the corridor's OWN road — which airs as a tautology ("Kessels Road ... at
    ///     Kessels Road", "Bruce Highway ... between the Bruce Highway and Sandgate Road"). For a
    ///     "between A and B" span where one endpoint IS the corridor road, keeps only the genuine
    ///     cross-street ("near Sandgate Road"). A "between A and B" where NEITHER end is the road is
    ///     a real segment anchor and is kept as-is ("Epping Road between Herring and Lane Cove").
    [GeneratedRegex(@"^(?<prefix>.*?\b)?between\s+(?<a>.+?)\s+and\s+(?<b>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BetweenAnchorRegex();
    /// </summary>
    private static string? CleanAnchor(string description, string roadName)
    {
        // The anchor is the location phrase before any " - <detail>" tail.
        int dash = description.IndexOf(" - ", StringComparison.Ordinal);
        string loc = (dash >= 0 ? description[..dash] : description).Trim();
        if (loc.Length == 0)
            return null;

        var roadTokens = SignificantTokens(roadName);

        var between = BetweenAnchorRegex().Match(loc);
        if (between.Success)
        {
            string a = between.Groups["a"].Value.Trim();
            string b = between.Groups["b"].Value.Trim();
            bool aSelf = IsRoadToken(a, roadTokens);
            bool bSelf = IsRoadToken(b, roadTokens);
            if (aSelf && bSelf) return null;       // both ends just restate the road
            if (aSelf) return $"near {b}";         // keep the real cross-street only
            if (bSelf) return $"near {a}";
            return loc;                            // genuine "between A and B" segment
        }

        // Plain location: drop when it only restates the road, carries no proper noun, or is a
        // STATUS phrase mis-slotted as a location. HERE puts a bare status in the whole description
        // when no cross-street is supplied ("Closed due to road construction" on a corridor the
        // structured feed classifies as construction, RoadClosed=false) — rendered as a Location
        // anchor it leaks "Closed" into the bullet and the writer voices a non-closure as closed
        // (observed live, Brisbane 2026-07-13: Gilchrist Avenue roadworks aired as "closed").
        var locTokens = SignificantTokens(loc);
        if (locTokens.Count == 0 || IsRoadToken(loc, roadTokens) || LeadingStatusWordRegex().IsMatch(loc))
            return null;
        return loc;
    }

    /// <summary>A bare route designation ("M1", "A4", "B23") — letters then digits, nothing else.</summary>
    [GeneratedRegex(@"^[A-Za-z]{1,3}\d{1,4}$")]
    private static partial Regex RouteCodeToken();

    /// <summary>
    ///     A description that LEADS with an incident-status word is a status, not a location — a
    ///     genuine anchor leads with a place preposition ("At", "Between", "near") or a proper-noun
    ///     place. Guards <see cref="CleanAnchor" /> against HERE status strings mis-slotted as the
    ///     location field ("Closed due to road construction", "Roadworks ...", "Lane closed ...").
    /// </summary>
    [GeneratedRegex(
        @"^\s*(closed|shut|blocked|closure|road\s?works?|construction|reduced|lanes?|delays?|queu|slow|broken|disabled|maintenance|works|caution|hazard|breakdown|incident|accident|crash|smash|prang|congest|restrict)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex LeadingStatusWordRegex();

    /// <summary>
    ///     True when <paramref name="fragment" /> is a self-reference to the corridor's own road —
    ///     either its tokens are entirely contained in the road's tokens (the anchor names nothing
    ///     beyond the road itself), OR the road's tokens are entirely contained in the fragment's
    ///     AND every extra token is a bare route code (observed live: HERE prefixes a corridor's own
    ///     route designation onto its name in an incident description, "M1 Eastern Distributor" on
    ///     the "Eastern Distributor" corridor — that is the SAME road, not a different, longer name
    ///     like "Smith Street Motorway" vs "Smith Street").
    /// </summary>
    private static bool IsRoadToken(string fragment, HashSet<string> roadTokens)
    {
        var tokens = SignificantTokens(fragment);
        if (tokens.Count == 0)
            return false;
        if (tokens.IsSubsetOf(roadTokens))
            return true;
        return roadTokens.IsSubsetOf(tokens) && tokens.Except(roadTokens).All(t => RouteCodeToken().IsMatch(t));
    }

    private static HashSet<string> SignificantTokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string token in text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
            if (token.Length > 1 && !AnchorFillerWords.Contains(token))
                tokens.Add(token);
        return tokens;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;

/// <summary>
///     Evidence-unit projection of the traffic narrative. Every <see cref="EvidenceUnit"/> here
///     corresponds to a fact ALSO rendered into the Draft prompt's prose data blocks — by
///     <see cref="Summarize"/> (traffic) or <see cref="ContentFormatters"/> (weather/fuel/good-runs).
///     The two must never diverge: a cited fact the prose never showed lets the model voice
///     something it wasn't given, and a prose fact with no citable id leaves a voiceable sentence
///     unable to cite anything. To guarantee the equivalence this file reuses the partial class's
///     own selection/formatting members (merge, tiering, caps, sibling-token anchor suppression,
///     impact suppression) rather than re-deriving them, and gates weather/fuel on the SAME
///     eligibility flags <c>LlmScriptGenerator.InvokeDraftAsync</c> uses to render each source.
/// </summary>
internal static partial class TrafficNarrativeSummarizer
{
    /// <summary>
    ///     Fields the PROSE may carry for an operator but the writer must never receive: provenance
    ///     and classification, none of it sayable on air. Kept as a deny-list at the evidence
    ///     boundary because <see cref="AdditionalFactLines" /> serves both consumers.
    /// </summary>
    private static readonly HashSet<string> NonVoiceableEvidenceFields =
        new(StringComparer.Ordinal) { "corroboration", "canonicalClass", "authorityPriority" };
    /// <summary>
    ///     Closure wording in a provider's free text. Matched only to SUPPRESS a description whose
    ///     closure claim the typed fields deny — never to assert a closure.
    /// </summary>
    [GeneratedRegex(@"\bclos(ed|ure|ing)\b|\bshut\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnsupportedClosureWordingRegex();


    /// <summary>
    ///     Below this, an extent is noise on air: a 200 m work zone is "at" a spot, not a stretch.
    ///     Set at 300 m because that is the shortest extent the ATN reference reads voices as a
    ///     distance ("works for about three hundred metres") rather than a point location.
    /// </summary>
    private const double MinVoicedExtentMeters = 300.0;

    /// <summary>
    ///     Renders a voiced distance the way an Australian presenter says it — in kays, never in
    ///     metres. ARN, 2026-08-25: "we should never say 500 metres, we should say half a kay."
    ///     <para>
    ///     The previous rendering rounded to whole hundreds of metres below a kilometre, which put
    ///     "a 500-metre road closure" to air on the adelaide read (Salisbury Highway, 476m + 96m).
    ///     Metres are a map unit; a driver hears distance in kays and fractions of one. The ATN
    ///     corpus confirms the register — "around seven kays", "about a kilometre" — and never once
    ///     voices a metre count across 180 reads.
    ///     </para>
    ///     <para>
    ///     Deliberately vague below half a kay: the source shapes are HERE polylines, so the
    ///     precision was always spurious, and "a short stretch" is both honest and sayable. Values
    ///     under <see cref="MinVoicedExtentMeters" /> never reach here.
    ///     </para>
    /// </summary>
    private static string FormatExtentLength(double meters) => meters switch
    {
        < 400.0 => "a short stretch",
        < 750.0 => "about half a kay",
        < 1_250.0 => "about a kay",
        < 1_750.0 => "about one and a half kays",
        // Spelled, not printed. The first cut of this emitted "about {n:0.#} kays", which put
        // "roadworks span about 1.5 kays" to air on the 2026-08-25 panel — a numeral in a field
        // whose whole purpose is to be spoken, which is the same defect as the "$2.23" fuel price.
        _ => $"about {SpellWholeNumber((int)Math.Round(meters / 1000.0))} kays",
    };

    /// <summary>
    ///     Spells a whole number for speech. Distinct from <see cref="SpellSmallNumber" />, which
    ///     stops at four on purpose: above four lanes the authority is describing a carriageway and
    ///     the numeral is what a presenter says. A distance has no such ceiling — "about 23 kays"
    ///     is still read aloud, so it is still spelled. Beyond thirty the figure is a corridor
    ///     length no bulletin voices, and the numeral is harmless.
    /// </summary>
    private static string SpellWholeNumber(int value)
    {
        string[] ones =
        [
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
            "eighteen", "nineteen"
        ];
        string[] tens = ["", "", "twenty", "thirty"];

        if (value is < 0 or > 39)
            return value.ToString(CultureInfo.InvariantCulture);
        if (value < 20)
            return ones[value];
        int unit = value % 10;
        return unit == 0 ? tens[value / 10] : $"{tens[value / 10]}-{ones[unit]}";
    }

    /// <summary>
    ///     Builds the flat, ordered list of atomic evidence units the Draft call cites. Pure data
    ///     transformation — no LLM call, deterministic given <paramref name="context"/>. Ordered to
    ///     match the prose reading order: traffic (monitored routes, corridors, cleared), then
    ///     weather, fuel, good-runs.
    /// </summary>
    /// <param name="mirrorProseSelection">
    ///     True for the structured matrix, whose evidence MUST mirror the prose bullets
    ///     <see cref="Summarize" /> renders, so a citation resolves to something the read actually
    ///     said. False for the conversational pipeline, which renders no prose: its writers decide
    ///     the story list themselves, so pre-cutting the board to the prose tier budget hands an
    ///     expert a shortlist somebody else already chose from.
    ///     <para>
    ///         Measured on dev job 39b22098 (cairns): 17 corridors fetched, 5 reached the writer.
    ///         The tier budget is Major 4 + Moderate 3 + Minor 3, and the minor tier is dropped
    ///         entirely under a major headline, so a busy board can never present more than ten
    ///         items however much is happening. The bulletin talked about two small delays while a
    ///         dozen roads it had never been shown sat below the cut.
    ///     </para>
    /// </param>
    public static IReadOnlyList<EvidenceUnit> BuildEvidenceUnits(
        BroadcastWithTraffic context, bool mirrorProseSelection = true)
    {
        var units = new List<EvidenceUnit>();

        // ── Traffic block (mirrors Summarize) ──────────────────────────────
        // Summarize only renders a "Traffic:" block when Content.Traffic is set; otherwise the
        // draft is news-style (noTraffic) and there is no traffic prose to cite.
        if (context.Content.Traffic)
        {
            // Same bidirectional-closure merge Summarize applies before tiering — so a road shut
            // both ways is ONE corridor in evidence, exactly as in the prose.
            // Writers that choose their own stories brief from everything the fetch returned;
            // the matrix voices what it is handed, so it keeps the slot-capacity selection.
            var source = mirrorProseSelection && context.AllCorridors.Count > 0
                ? context.AllCorridors
                : context.Corridors;
            var corridors = MergeBidirectionalClosures(source);

            AppendRouteEvidence(units, context.MonitoredRoutes, corridors, context.EditorialSettings);

            if (context.CuratedCorridorRoads.HasValue || !mirrorProseSelection)
            {
                // Every corridor, full field set, in supplied order. Curated regeneration wants this
                // because an operator picked the roads; the conversational writers want it because
                // picking the roads is their job.
                var siblingTokens = BuildSiblingTokens(corridors);
                var corridorIds = BuildCorridorDiscriminators(corridors);
                foreach (var corridor in corridors)
                    AppendFullCorridorEvidence(
                        units, corridor, !mirrorProseSelection, siblingTokens, corridorIds);
            }
            else
                AppendTieredCorridorEvidence(units, corridors,
                    context.FillOrder.Contains(FillSource.MinorTraffic));
            AppendClearedEvidence(units, context);
        }

        // ── Weather / Fuel / Good-runs (same gates as InvokeDraftAsync) ─────
        bool weatherEligible = context.FillOrder.Contains(FillSource.Weather);
        bool fuelEligible = context.FillOrder.Contains(FillSource.Fuel);

        if (context.Weather.HasValue && (weatherEligible || context.Weather.Value.HasMajorAlert))
            AppendWeatherEvidence(units, context.Weather.Value);

        if (context.Fuel.HasValue && fuelEligible)
            AppendFuelEvidence(units, context.Fuel.Value);

        if (context.GoodRunRoads.Count > 0)
            AppendGoodRunEvidence(units, context.GoodRunRoads,
                CorridorTiering.CountVoiceable(context.Corridors, context.EditorialSettings),
                context.FlowPool);

        return units;
    }

    /// <summary>
    ///     Monitored drive-time routes actually rendered by <see cref="AppendMonitoredRouteBullets"/>:
    ///     delayed routes always, on-time asides only on a non-hot board, after the same closure-road
    ///     suppression. driveTime is free-paraphrase prose, so its exact wording is not citation-checked.
    /// </summary>
    private static void AppendRouteEvidence(
        List<EvidenceUnit> units,
        IReadOnlyList<MonitoredRouteStatus> routes,
        IReadOnlyList<TrafficCorridor> corridors,
        EditorialSettings? settings)
    {
        if (routes.Count == 0)
            return;

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

        // A route is "running to time" only when it is. Anything with a real delay carries its
        // figure, because the corridor evidence beside it voices delays of ONE minute - so a
        // threshold of five here meant the board's biggest measured delay could be the one thing
        // the writer never saw. Job ffcad4af: the M4 from Parramatta ran 31 against a typical 27,
        // +4, and was erased as "on time" while Seven Hills at one minute went to air.
        int headlineThreshold = settings?.DriveTimeHeadlineThresholdMinutes ?? DefaultHeadlineDelayThresholdMinutes;
        var delayed = routes.Where(r => r.DelayMinutes > 0).ToList();
        var onTime = routes.Where(r => r.DelayMinutes <= 0).ToList();

        foreach (var route in delayed)
        {
            string id = $"route:{route.Name}";

            // Above the headline threshold the run is worth the full figure. Below it, the delay
            // is still real and still rankable, but the drive time itself is not news: "31 minutes
            // against a typical 27" earns its airtime, "22 against 20" does not.
            units.Add(new EvidenceUnit(
                $"{id}:driveTime", id, "driveTime",
                route.DelayMinutes >= headlineThreshold
                    ? $"{RoadNameFormatter.ToSpeakable(route.Name)} \u2014 {route.DriveTimeMinutes} min run (typical {route.NormalDriveTimeMinutes} min, {route.DelayMinutes} min slower than typical), {route.DistanceKm:F1}km"
                    : $"{RoadNameFormatter.ToSpeakable(route.Name)} \u2014 {route.DelayMinutes} min slower than typical"));

            // Emitted separately so the delay is a NUMBER the ranker can band, not prose. Without
            // it every monitored route scored as an unmeasured minor and sorted below a one-minute
            // suburban queue.
            units.Add(new EvidenceUnit(
                $"{id}:delayMinutes", id, "delayMinutes", SpellSmallNumber(route.DelayMinutes)));
        }

        // An on-time aside is filler, so it yields to a board that has real news on it. A DELAYED
        // route never does - that is the news.
        bool hotBoard = corridors.Any(c =>
            IncidentTypeClassifier.IsClosure(c.PrimaryType)
            || c.Incidents.Any(i => IncidentTypeClassifier.IsClosure(i.Type))
            || (!string.IsNullOrWhiteSpace(c.Description) && StrongPaceRegex().IsMatch(c.Description)));
        if (!hotBoard)
            foreach (var route in onTime)
                units.Add(new EvidenceUnit(
                    $"route:{route.Name}:driveTime", $"route:{route.Name}", "driveTime",
                    $"{RoadNameFormatter.ToSpeakable(route.Name)} \u2014 running to time"));
    }

    /// <summary>
    ///     Corridors selected exactly as <see cref="AppendCorridorBullets"/> does — pooled tier
    ///     budget, minor tier suppressed under a Major headline, condensed rendering for the minor
    ///     tier — so evidence exists ONLY for corridors the prose actually voices.
    /// </summary>
    private static void AppendTieredCorridorEvidence(
        List<EvidenceUnit> units, IReadOnlyList<TrafficCorridor> corridors, bool includeMinorTier)
    {
        // Reuse the same selection/cap decision as AppendCorridorBullets. In particular, a
        // corridor in Minor is condensed only when the prose selected it for that tier; this keeps
        // the voicing-only AdditionalFactLines projection equivalent on both sides.
        var selection = SelectTieredCorridors(corridors, includeMinorTier);

        // siblingTokens must be built from the SAME named set the prose uses, so cross-anchor
        // sibling suppression in CorridorAnchors matches the bullets exactly.
        var siblingTokens = BuildSiblingTokens(
            selection.FullBullets.Concat(selection.MinorBullets).ToList());
        var corridorIds = BuildCorridorDiscriminators(corridors);

        // Every selected corridor now gets the FULL field set. Condensing the minor tier mirrored
        // the prose bullet renderer's one-line minor bullet, but the structured draft path is not
        // the prose renderer: it is a data payload. Measured 2026-08-20 on live jobs, condensation
        // starved 5 of 7 cairns corridors and both Sydney minor items of `detail`, `extent`,
        // `lanes` and source `comment` — so an item like Mitchell Road reached the writer as a
        // bare location with no incident and nothing to say, which is exactly what drives the
        // vagueness and hazard detectors. The fields exist on the corridor; withholding them from
        // the items that most need help was the defect.
        foreach (var corridor in selection.FullBullets.Concat(selection.MinorBullets))
            AppendFullCorridorEvidence(units, corridor, discriminateSites: false, siblingTokens, corridorIds);
    }

    /// <summary>Mirrors <see cref="AppendCorridorBullet"/>'s field-to-bullet mapping and caps.</summary>
    private static void AppendFullCorridorEvidence(
        List<EvidenceUnit> units, TrafficCorridor corridor,
        bool discriminateSites,
        List<(string RoadName, HashSet<string> Tokens)> siblingTokens,
        IReadOnlyList<(TrafficCorridor Corridor, string IdPrefix)> corridorIds)
    {
        // Road name alone is not an identity. Cairns 8383b04c returned the Bruce Highway at
        // Gordonvale AND at Edmonton, Captain Cook Highway at three separate places, and a hazard
        // plus roadworks both at Kuranda. Keyed on the road, Normalizer grouped each set into ONE
        // event and poured two sites' facts into a single bag - the same ownership hazard that put
        // a four-minute delay and a two-minute delay in one brief with nothing saying which road
        // owned which. Place is part of the identity.
        //
        // Matrix keeps the road-only key: LlmScriptGenerator.CorridorKeysFor slices this string to
        // recover a canonical road name for provenance mapping, and a suffix would break it.
        string sourceKey = discriminateSites && corridor.Suburb.HasValue
                           && !string.IsNullOrWhiteSpace(corridor.Suburb.Value)
            ? $"corridor:{corridor.RoadName} at {corridor.Suburb.Value.Trim()}"
            : $"corridor:{corridor.RoadName}";
        string idPrefix = FindCorridorIdPrefix(corridor, corridorIds);
        AppendCorridorPayloadEvidence(units, corridor, sourceKey, idPrefix);
        string incidentLabel = MapIncidentType(corridor.PrimaryType);

        var distinctTypes = corridor.Incidents
            .Select(i => MapIncidentType(i.Type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(t => !string.Equals(t, incidentLabel, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        for (int i = 0; i < distinctTypes.Count; i++)
            units.Add(new EvidenceUnit($"{idPrefix}:incident:{i}", sourceKey, "incident", distinctTypes[i]));

        string impact = FormatImpact(corridor);
        bool detailCarriesPace = !string.IsNullOrWhiteSpace(corridor.Description)
                                 && StrongPaceRegex().IsMatch(corridor.Description);
        bool impactIsMildLabel = impact is "minor delays" or "moderate delays";
        bool impactRestatesClosureLabel = string.Equals(impact, "road closed", StringComparison.OrdinalIgnoreCase)
                                          && IncidentTypeClassifier.IsClosure(corridor.PrimaryType);

        // A MEASUREMENT OUTRANKS OUR DERIVATION OF IT, so a road with a delay figure does not also
        // get road-feel. The two are different scales and they disagree: FormatImpact calls
        // anything up to five minutes "minor delays", which speaks as "a touch slower", while the
        // station palette reserves that for one or two and wants "a slower run" from three. A
        // fifteen minute delay - genuinely bad - reached the writer as "a slower run" while the
        // palette called it "crawling". Measured across today's captures: 25 corridors carried both
        // fields and 5 disagreed, all of them understating.
        //
        // Kept where there is NO figure (74 corridors today): with nothing measured to reason from,
        // the band is the only pace signal there is, and it cannot contradict a number that is
        // absent. The writer bands from delayMinutes when it has one, which is ARN's own scale,
        // editable in the console, and the only one the palette agrees with.
        bool hasMeasuredDelay = corridor.DelayMinutes.HasValue && corridor.DelayMinutes.Value > 0;

        if (!(detailCarriesPace && impactIsMildLabel) && !impactRestatesClosureLabel && !hasMeasuredDelay)
        {
            // The band is translated to speech HERE rather than asked for in the prompt. Both the
            // field name and the value used to be leak sources — the writer echoed the value
            // ("moderate delays") and the field ("minor impacts are reported") — so both change:
            // the id keeps its ":impact" suffix, because citations and review reference it, while
            // what the writer READS is road-feel under a field name that is safe to say aloud.
            units.Add(new EvidenceUnit(
                $"{idPrefix}:impact", sourceKey, "road feel",
                RoadFeelTranslator.Speak(impact, corridor.RoadName)));
        }

        if (corridor.Extent.HasValue)
            units.Add(new EvidenceUnit($"{idPrefix}:extent", sourceKey, "extent",
                RoadNameFormatter.ToSpeakable(corridor.Extent.Value)));

        // ToSpeakable, same as extent above: this is raw HERE prose bound for a voiced read, and
        // it carries the same defects. The 2026-08-25 panel aired "the closed ramp to Alfred
        // StreetNorth" from the Summary "Ramp to Alfred StreetNorth - Closed" — the glued-road-type
        // repair that fixes exactly this already existed, but this field never went through it.
        var laneDetails = corridor.Incidents
            .Where(i => i.Summary.HasValue)
            .Select(i => RoadNameFormatter.ToSpeakable(i.Summary.Value).TrimEnd('.'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        for (int i = 0; i < laneDetails.Count; i++)
            units.Add(new EvidenceUnit($"{idPrefix}:lanes:{i}", sourceKey, "lanes", laneDetails[i]));

        // Each anchor is its own location unit, so citing two of them for one road in a single
        // sentence is a detectable duplicate-anchor defect rather than a plausible read.
        int locationCap = corridor.Extent.HasValue ? 1 : 2;
        var anchors = CorridorAnchors(corridor, siblingTokens).Take(locationCap).ToList();
        for (int i = 0; i < anchors.Count; i++)
            units.Add(new EvidenceUnit($"{idPrefix}:location:{i}", sourceKey, "location", anchors[i]));

        // A description that asserts a closure the typed data denies must never reach the writer:
        // it is the one fact FalseClosureDetector will reject the read for. Live 2026-08-20,
        // Heathcote Road arrived as type=construction / roadClosed=false with
        // Description="road closure and roadworks", so the draft voiced a closure and was flagged
        // for it. Both sides now consult IncidentTypeClassifier.DataMarksClosure, so the writer is
        // never offered a closure that the check would reject.
        //
        // This is a CONTRADICTION check, not a wording filter: the description claims something the
        // typed fields deny. The pace banlist that used to sit beside it - suppressing "at a
        // standstill" / "slow-moving" and, after a rename it never learned about, failing to
        // suppress "crawling" - is gone. Withholding words upstream cannot work: the list goes
        // stale the moment a provider or our own aggregator picks a new one, and every stale entry
        // is a live defect nobody sees. A description is an OBSERVATION, sometimes an unreliable
        // one, so it is supplied as evidence and the writer is told to weigh it against the
        // measured figure rather than repeat it.
        bool assertsUnsupportedClosure =
            !IncidentTypeClassifier.DataMarksClosure(corridor)
            && UnsupportedClosureWordingRegex().IsMatch(corridor.Description ?? string.Empty);

        // A DERIVED description is our own classifier speaking, so it is not evidence and the
        // writer never sees it: severity comes from delayMinutes, which is measured. An OBSERVED one
        // ("breakdown Eastbound", "roadworks westbound") says what actually happened and is exactly
        // what the writer needs. The test is provenance, never wording - a word list cannot survive
        // the next rename, and this already proved it once.
        if (!string.IsNullOrWhiteSpace(corridor.Description)
            && !corridor.DescriptionIsDerived
            && !assertsUnsupportedClosure)
        {
            units.Add(new EvidenceUnit($"{idPrefix}:detail", sourceKey, "detail",
                corridor.Description.TrimEnd('.')));
        }
        var sourceDetails = corridor.Incidents
            .Where(i => i.Comment.HasValue)
            .Select(i => i.Comment.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
        for (int i = 0; i < sourceDetails.Count; i++)
            units.Add(new EvidenceUnit($"{idPrefix}:sourceDetail:{i}", sourceKey, "sourceDetail", sourceDetails[i]));
        var additionalIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in AdditionalFactLines(corridor))
        {
            // AdditionalFactLines is shared with the PROSE renderer, which legitimately shows an
            // operator our provenance and classification. The writer must not see them: measured
            // live 2026-08-20, `corroboration` ("2 authorities agree"), `canonicalClass`
            // ("Closure") and `authorityPriority` ("critical") earned ZERO citations between them
            // across four draft captures, and none of the three is a phrase a presenter can say.
            // Filtered here rather than removed upstream so the prose keeps them.
            if (NonVoiceableEvidenceFields.Contains(line.Field))
                continue;

            int index = additionalIndexes.GetValueOrDefault(line.Field);
            additionalIndexes[line.Field] = index + 1;
            units.Add(new EvidenceUnit(
                $"{idPrefix}:{line.Field}:{index}", sourceKey, line.Field, line.Value));
        }
    }

    private static IReadOnlyList<(TrafficCorridor Corridor, string IdPrefix)> BuildCorridorDiscriminators(
        IReadOnlyList<TrafficCorridor> corridors)
    {
        // Sort by stable corridor facts rather than input position. The poller, draft, and refine
        // paths can rebuild the list in different orders; the same corridor must retain its id.
        var ordered = corridors
            .Select((corridor, index) => (corridor, index, key: StableCorridorKey(corridor)))
            .OrderBy(x => x.key, StringComparer.Ordinal)
            .ThenBy(x => x.index)
            .ToList();
        // Short, stable, road-free ids: "r1", "r2", ... assigned over the SAME stable ordering as
        // before, so a corridor keeps its id across the poller/draft/refine rebuilds. The former
        // "corridor:<road>#<n>" prefix restated the road on every fact and put our own bucket word
        // in front of the writer 88 times in one measured 5,915-char prompt — the exact token
        // MetaLeakDetector exists to catch coming back out. The road is named ONCE, by the group
        // heading the renderer emits; the id only has to be unique and copyable.
        var result = new List<(TrafficCorridor Corridor, string IdPrefix)>(ordered.Count);
        int roadIndex = 0;
        foreach (var (corridor, _, _) in ordered)
        {
            roadIndex++;
            result.Add((corridor, $"r{roadIndex.ToString(CultureInfo.InvariantCulture)}"));
        }
        return result;
    }

    private static string FindCorridorIdPrefix(
        TrafficCorridor corridor,
        IReadOnlyList<(TrafficCorridor Corridor, string IdPrefix)> corridorIds) =>
        corridorIds.First(x => ReferenceEquals(x.Corridor, corridor)).IdPrefix;

    private static string StableCorridorKey(TrafficCorridor corridor) =>
        string.Join('\u001f',
            corridor.RoadName,
            corridor.Suburb.GetValueOr(string.Empty),
            corridor.Direction.GetValueOr(string.Empty),
            corridor.Extent.GetValueOr(string.Empty),
            corridor.PrimaryType,
            corridor.Description,
            string.Join('\u001e', corridor.Incidents.Select(i => i.Id).OrderBy(id => id, StringComparer.Ordinal)));


    private static void AppendCorridorPayloadEvidence(
        List<EvidenceUnit> units, TrafficCorridor corridor, string sourceKey, string idPrefix)
    {
        // THE ROAD NAME IS A FACT. It reached the writer only as the event's label, and the
        // composer is told it may voice the available facts "and nothing else" - so on job
        // ffcad4af it obeyed and wrote "Around Greenacre, the affected stretch is about a kay"
        // and "Mascot has about half a kay of traffic". Roberts Road and Southern Cross Drive
        // were never said. That is unairable (a listener who cannot place a hazard cannot act on
        // it) and it is also why only 1 of 3 traffic sentences carried a citation: a sentence
        // naming no road matches no corridor. Lane Cove Tunnel survived only because the composer
        // lifted it out of the story subject, which was never a fact either.
        if (!string.IsNullOrWhiteSpace(corridor.RoadName))
        {
            units.Add(new EvidenceUnit(
                $"{idPrefix}:road", sourceKey, "road", RoadNameFormatter.ToSpeakable(corridor.RoadName)));
        }

        if (corridor.Suburb.HasValue
            && !string.IsNullOrWhiteSpace(corridor.Suburb.Value)
            && !corridor.RoadName.Contains(corridor.Suburb.Value, StringComparison.OrdinalIgnoreCase))
        {
            units.Add(new EvidenceUnit($"{idPrefix}:suburb", sourceKey, "suburb", corridor.Suburb.Value.Trim()));
        }
        // `magnitudeTier` and `voiceability` are gone. Measured 2026-08-20 across four live draft
        // captures: `voiceability` earned 0 citations from 783 chars and `magnitudeTier` 2 from
        // 776. Both are OUR classifier outputs — `Leadable` and `Major` are not words a presenter
        // can say, and `magnitudeTier` restated a magnitude the payload already carries as
        // delayMinutes. `direction` and the voiced extent replace them: both are sayable, both
        // already sit on the corridor, and neither was ever emitted.
        if (corridor.Direction.HasValue && !string.IsNullOrWhiteSpace(corridor.Direction.Value))
            units.Add(new EvidenceUnit(
                $"{idPrefix}:direction", sourceKey, "direction", corridor.Direction.Value.Trim()));

        // Scale the listener can picture. VoicedExtentMeters is deliberately the honest figure —
        // see its own summary — where TotalLengthMeters sums both directions and would overstate.
        double extentMeters = corridor.VoicedExtentMeters;
        if (extentMeters >= MinVoicedExtentMeters)
            units.Add(new EvidenceUnit(
                $"{idPrefix}:extentLength", sourceKey, "extentLength", FormatExtentLength(extentMeters)));

        if (corridor.DelayMinutes.HasValue && corridor.DelayMinutes.Value > 0)
            units.Add(new EvidenceUnit(
                $"{idPrefix}:delayMinutes",
                sourceKey,
                "delayMinutes",
                corridor.DelayMinutes.Value.ToString(CultureInfo.InvariantCulture)));
        AppendOfficialContentEvidence(units, corridor, sourceKey, idPrefix);
    }

    /// <summary>
    ///     Lane counts are read aloud, not displayed, so they are spelled: "two of three lanes
    ///     closed". Small range only — above four the authority is describing a motorway
    ///     carriageway, where the numeral is what a presenter would actually say.
    /// </summary>
    private static string SpellSmallNumber(int value) => value switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    ///     The authority's own CONTENT, as distinct from its control labels. Before 2026-08-20 the
    ///     emitter reached into <see cref="TrafficCorridor.OfficialEvidences" /> and took exactly
    ///     two things — <c>authorityPriority</c> and the corroboration count — while discarding
    ///     advice, lanes, the reduced limit and the authority's impact class. Both of the fields it
    ///     did take earned zero citations across four live draft captures, so the writer received
    ///     the official feed's plumbing and none of its journalism.
    /// </summary>
    private static void AppendOfficialContentEvidence(
        List<EvidenceUnit> units, TrafficCorridor corridor, string sourceKey, string idPrefix)
    {
        var evidences = corridor.OfficialEvidences;
        if (evidences is null || evidences.Count == 0)
            return;

        // Lane counts read as "two of three lanes closed" only when BOTH are known and the closure
        // is partial; a closed count alone is still worth voicing, a total alone is not.
        var lanes = evidences.FirstOrDefault(e => e.LanesClosed is > 0);
        if (lanes is not null)
        {
            string value = lanes.LanesTotal is > 0 && lanes.LanesClosed < lanes.LanesTotal
                ? $"{SpellSmallNumber(lanes.LanesClosed!.Value)} of "
                  + $"{SpellSmallNumber(lanes.LanesTotal.Value)} lanes closed"
                : $"{SpellSmallNumber(lanes.LanesClosed!.Value)} "
                  + $"{(lanes.LanesClosed == 1 ? "lane" : "lanes")} closed";
            units.Add(new EvidenceUnit($"{idPrefix}:lanesClosed", sourceKey, "lanesClosed", value));
        }

        // A FACT with its unit, never a phrasing. This first shipped as "40 through the works",
        // which is a sentence fragment rather than a datum: the writer read it back almost verbatim
        // and padded it into "It's 40 through the works, so check the signs" — and "check the signs"
        // appears in no adviceTags, so it was invented and carried no citable id. Baking prose into
        // an evidence value puts words in the writer's mouth, which is the same defect as the old
        // pre-chewed `impact = heavy delays`. Spoken-ready units, because the value is read aloud.
        var limit = evidences.FirstOrDefault(e => e.SpeedLimitKph is > 0);
        if (limit is not null)
            units.Add(new EvidenceUnit(
                $"{idPrefix}:speedLimit", sourceKey, "speedLimit",
                $"{limit.SpeedLimitKph!.Value.ToString(CultureInfo.InvariantCulture)} kilometres an hour"));

        // Closed vocabulary, so it maps to station wording by lookup and can never be a
        // fabrication. ONE tag only: every item carrying "find another way around" is a machine
        // tell, and the authority orders these by usefulness already.
        string? advice = evidences
            .SelectMany(e => e.AdviceTags ?? [])
            .Select(tag => tag.Trim())
            .FirstOrDefault(tag => tag.Length > 0);
        if (advice is not null)
            units.Add(new EvidenceUnit($"{idPrefix}:advice", sourceKey, "advice", advice.ToLowerInvariant()));
    }

    /// <summary>
    ///     A works window as the authority writes it: "9.30pm to 4am", "8pm - 5am". Both halves must
    ///     carry a meridiem so a bare cross-street number can never be mistaken for a time.
    /// </summary>
    [GeneratedRegex(@"(\d{1,2}(?:[.:]\d{2})?\s*(?:am|pm))\s*(?:to|until|-|–|—)\s*(\d{1,2}(?:[.:]\d{2})?\s*(?:am|pm))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WorksWindowRegex();

    /// <summary>
    ///     Mirrors <see cref="TrafficNarrativeSummarizer.AppendClearedSection"/> — cleared items are
    ///     voiced, so citable. Both now honour the SAME worthiness gate and the SAME cap.
    /// </summary>
    /// <remarks>
    ///     This is the path production runs, and it was uncapped and ungated: it declared
    ///     <c>int emitted</c>, incremented it, and never compared it to anything, so every cleared
    ///     incident became citable evidence. Meanwhile <c>IncidentChangeFormatter</c> capped the same
    ///     category at 2 with the documented rationale "the delta is context, not content". Measured
    ///     on a live Sydney 22s read (2026-08-23): nine cleared items in, seven roads voiced, ~40% of
    ///     the slot spent on things that were no longer happening while the one live story — a
    ///     Harbour Tunnel closure — got a single sentence.
    /// </remarks>
    private static void AppendClearedEvidence(List<EvidenceUnit> units, BroadcastWithTraffic context)
    {
        if (!context.IncidentChanges.HasValue)
            return;

        int cap = context.EditorialSettings.ContinuityClearedCap ?? IncidentChangeFormatter.DefaultClearedCap;
        var cleared = context.IncidentChanges.Value.ClearedIncidents
            .Where(IncidentChangeFormatter.IsClearedWorthVoicing);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int emitted = 0;
        foreach (var item in cleared)
        {
            string road = item.RoadName.GetValueOr("unknown road");
            if (RoadNameFormatter.LooksLikeRawAddress(road))
                continue; // never cite a raw postal address as a cleared road (mirrors AppendClearedSection)
            string type = MapIncidentType(item.Type);
            string dedupeKey = $"{road}\u001f{type}";
            if (!seen.Add(dedupeKey))
                continue;
            string suburbInfo = item.Suburb.HasValue ? $" near {item.Suburb.Value}" : "";
            units.Add(new EvidenceUnit(
                $"cleared:{emitted}", $"cleared:{road}", "cleared",
                $"{type} on {RoadNameFormatter.ToSpeakable(road)}{suburbInfo} has cleared"));
            emitted++;
            if (emitted >= cap)
                return; // the cap the counter was always for
        }
    }

    /// <summary>
    ///     Weather condition, temperature, and alerts — parsed back out of the exact prose
    ///     <see cref="ContentFormatters.FormatWeather"/> renders, so the evidence values are the
    ///     rendered strings verbatim (no risk of the naturalized-condition table drifting between
    ///     the two). condition and temperature are voiced near-verbatim, so they are citation-checked.
    /// </summary>
    private static void AppendWeatherEvidence(List<EvidenceUnit> units, WeatherReport weather)
    {
        const string condPrefix = "- Conditions: ";
        const string alertPrefix = "- ALERT: ";
        int alertIndex = 0;
        foreach (string rawLine in ContentFormatters.FormatWeather(weather).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.StartsWith(condPrefix, StringComparison.Ordinal))
            {
                string body = line[condPrefix.Length..]; // "{condition}, {temp} degrees"
                int split = body.LastIndexOf(", ", StringComparison.Ordinal);
                if (split < 0)
                    continue;
                units.Add(new EvidenceUnit("weather:condition", "weather", "condition", body[..split]));
                units.Add(new EvidenceUnit("weather:temperature", "weather", "temperature", body[(split + 2)..]));
            }
            else if (line.StartsWith(alertPrefix, StringComparison.Ordinal))
            {
                units.Add(new EvidenceUnit($"weather:alert:{alertIndex}", "weather", "alert", line[alertPrefix.Length..]));
                alertIndex++;
            }
        }
    }

    /// <summary>
    ///     Cheapest price per fuel type, already in the form we want said. The checkable value is the
    ///     price alone — the fixed, fabrication-prone fact — not the whole rendered phrase: the
    ///     surrounding wording ("cheapest at", "there too", suburb) is voiced as free paraphrase, so
    ///     value-checking the full phrase would false-positive on every fuel line. The suburb still
    ///     reaches the model via the "Fuel price data:" prose block, so the evidence value stays a
    ///     strict subset of the prose.
    ///     <para>
    ///     Spoken, not numeric. This used to hand over <c>$2.23</c> and rely on the writer to say it
    ///     properly, with a prompt section teaching the conversion. On the 2026-08-25 panel the
    ///     port lincoln read aired "The cheapest unleaded is $2.23 at Cummins" — ElevenLabs does not
    ///     reliably speak a bare currency token, so that is dead air or a misread, not a style slip.
    ///     Giving the model the spoken form removes the conversion from its job entirely.
    ///     </para>
    /// </summary>
    private static void AppendFuelEvidence(List<EvidenceUnit> units, FuelPriceReport fuel)
    {
        // ONE source, not one per fuel type. A separate source per type is read as a separate
        // subject, so the writer produced a separate ITEM per type — two fuel items, each with
        // three or four phrasings, for a fixture whose content is fully determined (both types,
        // each with its price and suburb, every time). ARN, 2026-08-25: "fuel should only be one
        // section, not two... there's no point in having an output like this."
        //
        // Grouping them also removes the duplicate-suburb read the split produced when both types
        // are cheapest in the same place ("In Edge Hill, unleaded ... Diesel is ... in Edge Hill").
        const string sourceKey = "fuel";
        foreach (var summary in fuel.Summaries)
        {
            string type = SpeakableFuelType(summary.FuelType);
            units.Add(new EvidenceUnit($"{sourceKey}:{type}:price", sourceKey, $"{type} price",
                SpokenPriceNormalizer.Normalize(
                    "$" + summary.MinPrice.ToString("F2", CultureInfo.InvariantCulture))));
            if (!string.IsNullOrWhiteSpace(summary.CheapestStationSuburb))
            {
                units.Add(new EvidenceUnit($"{sourceKey}:{type}:suburb", sourceKey, $"{type} suburb",
                    summary.CheapestStationSuburb));
            }
        }
    }

    /// <summary>
    ///     The grade as a presenter says it. Delegates to <see cref="SpokenFuelGrade" />, which owns
    ///     this for every path — this method and the one in ContentFormatters were hand-copied, a
    ///     third copy in FixtureLineComposer was missed, and it aired the octane on dev brisbane
    ///     <c>e91805be</c> (2026-08-25).
    /// </summary>
    private static string SpeakableFuelType(string fuelType) => SpokenFuelGrade.Of(fuelType);

    /// <summary>
    ///     Good-run roads the prose names — mirrors <see cref="ContentFormatters.FormatGoodRuns"/>'s cap
    ///     AND its suburb, which is the whole point: a clear run carries no cross-street and no detail,
    ///     so the road name is all the writer gets. ARN, 2026-08-24: "Victoria and Elizabeth are still
    ///     a bit of a curly" — Sydney has many of each. The suburb comes from the flow pool the
    ///     good-run names were drawn from, so the two representations cite the same fact.
    /// </summary>
    private static void AppendGoodRunEvidence(
        List<EvidenceUnit> units,
        IReadOnlyList<string> goodRunRoads,
        int voiceableIncidentCount,
        IReadOnlyList<FlowRoad> flowPool)
    {
        // ONE source, not one per road — the same defect the fuel split had, and it produces the
        // same shape of read. A source per road is a subject per road, so the writer emitted an
        // item per road and the fitter voiced several of them: measured 2026-08-25 on the sydney
        // panel fixture, "The M8 is clear for motorists. Traffic is running clear through Sydney
        // Gateway. It's a similar story on Cleveland Street, with clear traffic. There's also a
        // clear run through the M8, Sydney Gateway and Cleveland Street" — four sentences, one
        // fact, and the last re-listing roads already named.
        //
        // A good run carries no per-road detail by definition, so the roads belong together under
        // one state. Grouped, the writer has a single subject and writes the sweep the house style
        // asks for: "Bishop Street, White Flat Road and Hallet Place are all running clear."
        const string sourceKey = "goodrun";
        int maxNamed = voiceableIncidentCount >= 3 ? 2 : 4;
        foreach (string road in goodRunRoads.Take(maxNamed))
        {
            // ToSpeakable: a good-run road name is the VALUE here, not a bullet heading, so it never
            // met the normaliser the headings go through. Dev brisbane 58bd9459 (2026-08-25) aired
            // "Logan Mwy in Loganlea and Bruce Hwy in Kallangur" — abbreviations ElevenLabs has no
            // reading for. The id keeps the raw road so citations still match.
            units.Add(new EvidenceUnit(
                $"{sourceKey}:{road}", sourceKey, "clear road", RoadNameFormatter.ToSpeakable(road)));

            string? suburb = flowPool
                .FirstOrDefault(f => RoadNameFormatter.IsSameRoad(f.RoadName, road))?.Suburb;
            if (!string.IsNullOrWhiteSpace(suburb))
                units.Add(new EvidenceUnit($"{sourceKey}:{road}:suburb", sourceKey, "suburb", suburb));
        }
    }
}

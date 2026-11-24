using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders;

public class TrafficNarrativeSummarizerTests
{
    private static BroadcastWithTraffic CreateContext(
        IReadOnlyList<TrafficCorridor>? corridors = null,
        string traffic = "No incidents reported",
        string location = "Sydney",
        Option<string> city = default,
        Option<IncidentDeltaContext> incidentChanges = default,
        IReadOnlyList<MonitoredRouteStatus>? monitoredRoutes = null,
        IReadOnlyList<FillSource>? fillOrder = null)
    {
        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "test-job",
            Location = location,
            Traffic = traffic,
            Incidents = corridors?.SelectMany(c => c.Incidents).ToList() ?? [],
            Corridors = corridors ?? [],
            City = city,
            IncidentChanges = incidentChanges,
            MonitoredRoutes = monitoredRoutes ?? [],
            FillOrder = fillOrder ?? [FillSource.Weather, FillSource.Fuel, FillSource.Filler]
        };
    }

    private static TrafficCorridor CreateCorridor(
        string road = "M4",
        int severity = 1,
        double impactScore = 80,
        string primaryType = "accident",
        string description = "Multi-vehicle crash",
        IReadOnlyList<TrafficIncident>? incidents = null,
        Option<string> suburb = default,
        Option<string> direction = default,
        Option<string> extent = default,
        Option<int> driveTime = default,
        Option<int> normalDriveTime = default,
        Option<DetourSuggestion> detour = default,
        double totalLengthMeters = 2000,
        Option<int> flowDelay = default)
    {
        return new TrafficCorridor(
            road,
            severity,
            1,
            totalLengthMeters,
            impactScore,
            primaryType,
            description,
            incidents ?? [new TrafficIncident("i1", "crash", severity, road, primaryType)],
            suburb,
            direction,
            extent,
            driveTime,
            normalDriveTime,
            FlowDelayMinutes: flowDelay,
            Detour: detour);
    }

    [Fact]
    public void Summarize_NoCorridors_ReturnsFallbackTrafficString()
    {
        var context = CreateContext([], "All clear on Sydney roads");

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldBe("All clear on Sydney roads");
    }

    [Fact]
    public void Summarize_MonitoredRoutesWithoutCorridors_ProducesDriveTimeSection()
    {
        var route = new MonitoredRouteStatus(
            "M4 Parramatta to Sydney CBD",
            new CoveragePoint(-33.8179, 150.9999),
            new CoveragePoint(-33.8688, 151.2093),
            28,
            18,
            22.4);
        var context = CreateContext([], "All clear on Sydney roads", monitoredRoutes: [route]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Monitored drive-time routes");
        result.ShouldContain("M4 Parramatta to Sydney CBD");
        result.ShouldContain("10 min slower than usual");
        result.ShouldNotContain("min run");
    }

    [Fact]
    public void Summarize_MonitoredRouteNearTypical_NotHeadlineFramedNoMinuteFigure()
    {
        var route = new MonitoredRouteStatus(
            "M4 Parramatta to Sydney CBD",
            new CoveragePoint(-33.8179, 150.9999),
            new CoveragePoint(-33.8688, 151.2093),
            26,
            25,
            22.4);
        var context = CreateContext([], "All clear on Sydney roads", monitoredRoutes: [route]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("running to time");
        result.ShouldContain("NEVER the lead");
        result.ShouldNotContain("min run");
        result.ShouldNotContain("slower than usual");
    }

    [Fact]
    public void Summarize_MonitoredRouteOnClosedRoad_IsSuppressed()
    {
        // Frozen-eval baseline (Sydney): "Warringah Freeway citybound is closed" and "the
        // Warringah Freeway into the city is only a touch slow" aired in ONE bulletin — the same
        // road voiced as both shut and near-normal. A monitored route sharing a closure corridor's
        // road must not reach the writer at all.
        var closure = CreateCorridor(
            road: "Warringah Freeway",
            primaryType: "roadClosure",
            description: "Road closed citybound",
            direction: "citybound");
        var monitored = new MonitoredRouteStatus(
            "the Warringah Freeway into the city",
            new CoveragePoint(-33.84, 151.21),
            new CoveragePoint(-33.87, 151.21),
            12,
            11,
            6.0);
        // Kept genuinely DELAYED (not on-time): an on-time route here would now ALSO be
        // suppressed by the separate class-wide hot-board rule (any closure on the board
        // suppresses ALL on-time asides), which would confound this test's actual target —
        // that suppression is road-name-specific, not global.
        var untouched = new MonitoredRouteStatus(
            "M4 Parramatta to Sydney CBD",
            new CoveragePoint(-33.8179, 150.9999),
            new CoveragePoint(-33.8688, 151.2093),
            30,
            20,
            22.4);
        var context = CreateContext([closure], monitoredRoutes: [monitored, untouched]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("into the city");
        result.ShouldContain("M4 Parramatta to Sydney CBD");
        result.ShouldContain("Warringah Freeway citybound");
    }

    [Fact]
    public void Summarize_MonitoredRouteOnClosedRoad_SuppressedWhenClosureIsIncidentLevel()
    {
        // The closure may live on a non-closure corridor as an incident (PrimaryType "construction",
        // incidents include a roadClosure) — suppression must look at both levels.
        var corridor = CreateCorridor(
            road: "Reid Highway",
            primaryType: "construction",
            incidents:
            [
                new TrafficIncident("i1", "roadworks", 1, "Reid Highway", "construction"),
                new TrafficIncident("i2", "road closed", 1, "Reid Highway", "roadClosure")
            ]);
        var monitored = new MonitoredRouteStatus(
            "Reid Highway east run",
            new CoveragePoint(-31.9, 115.9),
            new CoveragePoint(-31.9, 116.0),
            9,
            9,
            8.0);
        var context = CreateContext([corridor], monitoredRoutes: [monitored]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("east run");
    }

    [Fact]
    public void Summarize_MonitoredRouteOnNonClosedCorridorRoad_IsKept()
    {
        // A congestion (non-closure) corridor on the same road is NOT a contradiction — "the M4 is
        // heavy AND the run is 28 minutes" is coherent, so the monitored line stays.
        var congestion = CreateCorridor(
            road: "M4",
            primaryType: "congestion",
            description: "Heavy congestion citybound");
        var monitored = new MonitoredRouteStatus(
            "M4 Parramatta to Sydney CBD",
            new CoveragePoint(-33.8179, 150.9999),
            new CoveragePoint(-33.8688, 151.2093),
            28,
            18,
            22.4);
        var context = CreateContext([congestion], monitoredRoutes: [monitored]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("M4 Parramatta to Sydney CBD");
        result.ShouldContain("10 min slower than usual");
    }

    [Fact]
    public void Summarize_SingleCorridor_ProducesStructuredBullet()
    {
        var corridor = CreateCorridor(suburb: "Parramatta",
            direction: "citybound",
            description: "Multi-vehicle crash blocking two lanes");
        var context = CreateContext([corridor], city: "Sydney");

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Traffic data for Sydney (1 corridor):");
        result.ShouldContain("Major:");
        result.ShouldContain("M4 citybound near Parramatta");
        result.ShouldContain("crash"); // TrafficTypeMapper speaks accident as "Crash" (2026-08-25 single-source cutover)
        result.ShouldContain("Multi-vehicle crash blocking two lanes");
    }

    [Fact]
    public void Summarize_MultipleCorridors_GroupBySpannedMagnitudeTiers()
    {
        // Magnitude — measured delay/queue, not HERE's flat severity — drives the tiers: a severe
        // delay and a moderate delay land in distinct buckets, and once that Major-or-worse item
        // leads, the quiet minor hazard vanishes from the writer's view entirely (not even a count).
        var major = CreateCorridor("M1", primaryType: "accident", driveTime: 45, normalDriveTime: 10);
        var moderate = CreateCorridor("Pacific Highway", primaryType: "congestion",
            driveTime: 22, normalDriveTime: 12, totalLengthMeters: 1200);
        var minor = CreateCorridor("King Street", primaryType: "roadHazard", totalLengthMeters: 300);
        var context = CreateContext([major, moderate, minor], city: "Sydney");

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Major:");
        result.ShouldContain("Moderate:");
        result.ShouldNotContain("Minor");             // minor tier absent once a Major-or-worse item leads
        result.ShouldContain("M1");
        result.ShouldContain("Pacific Highway");
        result.ShouldNotContain("King Street");       // minor road name suppressed under the headline
    }

    [Fact]
    public void Summarize_LongStandstillMinorDelay_SuppressesContradictoryMildImpact()
    {
        // A 4km standstill ranks Major (length) with a measured delay of only 5 min. Handing the
        // writer BOTH "at a standstill" (Detail) and "minor delays" (Impact) produced the on-air
        // stack "at a standstill, with minor delays" — two contradictory worlds in one breath
        // (frozen-eval iter4, Sydney). The vivid pace phrase carries the information; the mild
        // category label under it is suppressed. The tier word must still not leak into the bullet.
        var corridor = CreateCorridor("Reedy Creek Road", primaryType: "congestion",
            description: "at a standstill", driveTime: 17, normalDriveTime: 12, totalLengthMeters: 4000);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Major:");                          // ranks high (long standstill)
        result.ShouldContain("at a standstill");                 // the vivid pace phrase survives
        result.ShouldNotContain("minor delays");                 // the contradictory mild label does not
        result.ShouldNotContain("congestion, major");            // no contradictory tier word in the bullet
    }

    [Fact]
    public void Summarize_ClosureNoMeasuredDelay_StatesConditionNotInventedDelay()
    {
        // A closure is voiced through its "road closure" label alone — never an invented time
        // magnitude ("heavy/severe delays" here is what let the writer voice "the best part of
        // an hour"), and never an "Impact: road closed" line restating the label.
        var corridor = CreateCorridor("Reid Highway", primaryType: "roadClosure",
            description: "road closure", totalLengthMeters: 200);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("road closure");
        result.ShouldNotContain("heavy delays");
        result.ShouldNotContain("severe delays");
    }

    [Fact]
    public void Summarize_ClosureWithResidualDelay_LeadsOnClosureNotDelay()
    {
        // A closure HERE also reports a small routed delay for the through-lanes must STILL read as
        // shut — never "minor delays" subordinating the closure ("shut ... so minor delays there").
        var corridor = CreateCorridor("Coast Highway", primaryType: "roadClosure",
            driveTime: 17, normalDriveTime: 12); // delay 5 min -> would have been "minor delays"
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("road closure");
        result.ShouldNotContain("minor delays");
    }

    [Fact]
    public void Summarize_AllCapsRoadName_IsTitleCased_RouteCodePreserved()
    {
        // HERE sometimes emits SHOUTING road names; they must never reach the writer as caps.
        // The route code (M4) stays upper; the words are title-cased.
        var corridor = CreateCorridor("M4 WESTERN MOTORWAY", primaryType: "accident");
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("M4 Western Motorway", Case.Sensitive);
        result.ShouldNotContain("WESTERN MOTORWAY", Case.Sensitive);
    }

    [Fact]
    public void Summarize_CorridorPayload_ExposesTierAndStrictPositiveDelayOnly()
    {
        var measured = CreateCorridor("Eastern Freeway", primaryType: "congestion",
            driveTime: 25, normalDriveTime: 5);
        string measuredSummary = TrafficNarrativeSummarizer.Summarize(CreateContext([measured]));

        measuredSummary.ShouldContain("magnitudeTier: Major");
        measuredSummary.ShouldContain("voiceability: Leadable");
        measuredSummary.ShouldContain("delayMinutes: 20");
        measuredSummary.ShouldContain("heavy delays");
        measuredSummary.ShouldNotContain("Impact: 20");

        var unknown = CreateCorridor("M4", primaryType: "congestion",
            totalLengthMeters: 0, flowDelay: Option<int>.Some(0));
        string unknownSummary = TrafficNarrativeSummarizer.Summarize(CreateContext([unknown]));

        unknownSummary.ShouldContain("magnitudeTier: Minor");
        unknownSummary.ShouldContain("voiceability: GroupedOnly");
        unknownSummary.ShouldNotContain("delayMinutes:");
    }
    [Fact]
    public void Summarize_CorridorWithDriveTime_DoesNotVoicePerCorridorMinute()
    {
        // Per-corridor drive times are NOT voiced — a time belongs only to the monitored
        // point-to-point route channel (ATN quotes a figure only on a named run).
        var corridor = CreateCorridor(
            "Eastern Freeway",
            driveTime: 25,
            normalDriveTime: 12);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Eastern Freeway");
        result.ShouldContain("moderate delays");   // magnitude is qualitative
        result.ShouldNotContain("Drive time:");
        result.ShouldNotContain("25 min");
    }

    [Fact]
    public void Summarize_CorridorWithDetour_IncludesSafeDetourInfo()
    {
        var corridor = CreateCorridor(
            "M4",
            driveTime: 30,
            normalDriveTime: 20,
            detour: Option<DetourSuggestion>.Some(new DetourSuggestion(24, 6, 18.2)));
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Detour: safe avoided-segment alternative 24 min");
        result.ShouldContain("saves 6 min");
    }

    [Fact]
    public void Summarize_RoadNameEqualsSuburb_OmitsRedundantSuburbAnchor()
    {
        var corridor = CreateCorridor("Neranwood", suburb: "Neranwood", direction: "outbound");
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Neranwood outbound");
        result.ShouldNotContain("near Neranwood");
    }

    [Fact]
    public void Summarize_StandingFootprintExtent_IsQualitativeNeverAKmFigure()
    {
        var corridor = CreateCorridor(
            "Warringah Freeway",
            primaryType: "construction",
            totalLengthMeters: 1600);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        // A works extent is described, never measured. HERE gives us an affected length per
        // incident, not a queue, and our own km rendering of it aired as a queue claim
        // (Cairns 2026-08-13, "queues back about ten kays" off a 12.2 km sum of overlapping
        // records). No distance may leave the corridor channel.
        result.ShouldContain("extended work zone");
        result.ShouldNotContain("stretch affected");
        result.ShouldNotContain("~2km");
        result.ShouldNotContain("~5km");
    }

    [Fact]
    public void Summarize_CorridorWithExtent_IncludesExtent()
    {
        var corridor = CreateCorridor(
            "M5",
            extent: "from King Georges Road to the airport");
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Extent: from King Georges Road to the airport");
    }

    [Fact]
    public void Summarize_MultipleDelayedCorridors_AllQualitative_NoVoicedMinutes()
    {
        var first = CreateCorridor(
            "M4",
            1,
            90,
            driveTime: 30,
            normalDriveTime: 10);
        var second = CreateCorridor(
            "M5",
            driveTime: 25,
            normalDriveTime: 10);
        var context = CreateContext([first, second]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("heavy delays");      // first: 20-min delta -> heavy, qualitative
        result.ShouldContain("moderate delays");   // second: 15-min delta -> moderate, qualitative
        result.ShouldNotContain(" min");           // no voiced minute count anywhere
        result.ShouldNotContain("Drive time:");
    }

    [Fact]
    public void Summarize_CorridorWithQueueLength_LabelsQueueWithoutAKmFigure()
    {
        var corridor = CreateCorridor(
            "Warringah Freeway",
            2,
            totalLengthMeters: 3500,
            driveTime: default,
            normalDriveTime: default);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        // The band is kept; the figure is not. A queue DISTANCE is a measurement HERE never
        // supplies for an incident corridor — the old "queue ~4km" was our own arithmetic.
        result.ShouldContain("long queue");
        result.ShouldNotContain("queue ~");
        result.ShouldNotContain("4km");
    }

    [Fact]
    public void Summarize_MergedOverlappingObservations_NeverInflatesTheVoicedExtent()
    {
        // Cairns 2026-08-13 regression: HERE reported the same Kennedy Highway works on both
        // carriageways (5665 m + 5523 m over four shared segments) plus four short hazard/works
        // records over one more shared segment. Summing them produced 12,169 m — voiced as
        // "queues back about ten kays" with every measured delay at 0. The corridor may only ever
        // voice its longest SINGLE observation, and the reported hazard's own extent is 268 m.
        var incidents = new[]
        {
            new TrafficIncident("k1", "At Captain Cook Highway - Road construction", 1, "Kennedy Hwy",
                "construction", LengthMeters: 5665),
            new TrafficIncident("k2", "At Myola Road - Road construction", 1, "Kennedy Hwy",
                "construction", LengthMeters: 5523),
            new TrafficIncident("k3", "At Captain Cook Highway - Hazardous driving conditions", 1,
                "Kennedy Hwy", "roadHazard", LengthMeters: 268),
            new TrafficIncident("k4", "At Myola Road - Hazardous driving conditions", 1, "Kennedy Hwy",
                "roadHazard", LengthMeters: 247)
        };
        var corridor = CreateCorridor("Kennedy Highway", primaryType: "roadHazard",
            incidents: incidents, totalLengthMeters: 12169);

        corridor.VoicedExtentMeters.ShouldBe(268);

        string result = TrafficNarrativeSummarizer.Summarize(CreateContext([corridor]));

        result.ShouldNotContain("queue");
        result.ShouldNotContain("kays");
        result.ShouldNotContain("10km");
        result.ShouldNotContain("12km");
    }

    [Fact]
    public void Summarize_SubKilometreExtent_GetsQualitativeLabelNotNumber()
    {
        var corridor = CreateCorridor(
            "Pacific Highway",
            primaryType: "construction",
            totalLengthMeters: 600);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        // A 600 m work zone must never be voiced with a fabricated km figure —
        // no number beats a false one.
        result.ShouldContain("short work zone");
        result.ShouldNotContain("km stretch affected");
    }

    [Fact]
    public void Summarize_IncidentTypeMappings_ProducesSpeakableLabels()
    {
        // Intent: incident-type -> SPEAKABLE label mapping. "Human-readable" was not a high enough
        // bar - these reach the writer as evidence, and "planned event" aired verbatim twice on the
        // 2026-08-25 run. R2/R3 carry a measured delay so they stay Moderate (individually named) -
        // otherwise R1's roadClosure Major floor would sweep them.
        var corridors = new[]
        {
            CreateCorridor("R1", primaryType: "roadClosure", impactScore: 90),
            CreateCorridor("R2", 2, primaryType: "disabledVehicle", impactScore: 50,
                driveTime: 20, normalDriveTime: 10),
            CreateCorridor("R3", 3, primaryType: "construction", impactScore: 20,
                driveTime: 20, normalDriveTime: 10)
        };
        var context = CreateContext(corridors);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("road closure");
        result.ShouldContain("breakdown");
        // "construction" is the American register for this, and the raw HERE code besides.
        result.ShouldContain("roadworks");
        result.ShouldNotContain("construction");
    }

    [Fact]
    public void Summarize_NewIncident_ShowsNewFlag()
    {
        var incident = new TrafficIncident("i1", "crash", 1, "M4", "accident", IsNew: true);
        var corridor = CreateCorridor(incidents: [incident]);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("[NEW]");
    }

    [Fact]
    public void Summarize_WithClearedIncidents_AppendsClearedSection()
    {
        var corridor = CreateCorridor();
        var clearedItem = new IncidentDeltaItem(
            "c1",
            "accident",
            1,
            "Harbour Bridge",
            "Milsons Point",
            default,
            0,
            default);
        var delta = new IncidentDeltaContext([], [], [clearedItem], []);
        var context = CreateContext([corridor], incidentChanges: delta);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Cleared:");
        result.ShouldContain("crash on Harbour Bridge near Milsons Point has cleared");
    }

    [Fact]
    public void Summarize_CityFromOption_UsedInHeader()
    {
        var corridor = CreateCorridor();
        var context = CreateContext([corridor], city: "Melbourne", location: "mel");

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Traffic data for Melbourne");
    }

    [Fact]
    public void Summarize_CityFallbackToLocation_WhenNoCityOption()
    {
        var corridor = CreateCorridor();
        var context = CreateContext([corridor], location: "Brisbane");

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Traffic data for Brisbane");
    }

    // ── Curated regeneration ordering (WS2 / SDD §4.2) ───────────────

    [Fact]
    public void Summarize_Curated_PreservesOperatorOrder_NoSeverityTiers()
    {
        // Minor-severity corridor first by operator choice; severity tiers would put it last.
        var corridors = new List<TrafficCorridor>
        {
            CreateCorridor("Punt Road", severity: 3, impactScore: 10),
            CreateCorridor("M4", severity: 1, impactScore: 90)
        };
        var context = CreateContext(corridors) with
        {
            CuratedCorridorRoads = Option<IReadOnlyList<string>>.Some(["Punt Road", "M4"])
        };

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Producer-selected corridors");
        result.ShouldNotContain("Major:");
        result.ShouldNotContain("Minor:");
        result.IndexOf("Punt Road", StringComparison.Ordinal)
            .ShouldBeLessThan(result.IndexOf("M4", StringComparison.Ordinal));
    }

    [Fact]
    public void Summarize_NotCurated_GroupsByMagnitudeTier()
    {
        var corridors = new List<TrafficCorridor>
        {
            CreateCorridor("Punt Road", primaryType: "roadHazard", totalLengthMeters: 300),
            CreateCorridor("M4", primaryType: "accident", driveTime: 40, normalDriveTime: 10)
        };
        var context = CreateContext(corridors);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Major:");
        result.ShouldContain("M4");
        result.ShouldNotContain("Minor");             // Punt Road (and the whole tier) absent under the M4 headline
        result.ShouldNotContain("Punt Road");
    }

    [Fact]
    public void Summarize_StandingFootprint_PresentsWorkZoneNotQueue()
    {
        // A roadworks corridor's length is a works-zone extent, not a queue. Labelling it a
        // "long queue" used to pull the LLM into leading with rural roadworks over a real jam.
        var roadworks = CreateCorridor("Mountain Road", severity: 2, primaryType: "construction",
            totalLengthMeters: 7200);
        var context = CreateContext([roadworks]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("queue");
        result.ShouldContain("work zone");
        result.ShouldNotContain("km");
    }

    [Fact]
    public void Summarize_CongestionWithLength_StillPresentsQueue()
    {
        // A live congestion queue keeps its queue framing — only standing footprints change.
        var jam = CreateCorridor("M1", severity: 2, primaryType: "congestion", totalLengthMeters: 2000);
        var context = CreateContext([jam]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("queue");
    }

    // ── Data-layer quality fixes: self-reference, magnitude, condensed minor ──

    [Fact]
    public void Summarize_AnchorRestatingRoadName_IsSuppressed()
    {
        // "At Epping Road" on the Epping Road corridor is a self-reference, not a cross-street;
        // emitting it makes the model write the tautology "Epping Road ... at Epping Road".
        var incident = new TrafficIncident("i1", "At Epping Road", 1, "Epping Road", "accident");
        var corridor = CreateCorridor("Epping Road", incidents: [incident]);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Location: At Epping Road");
    }

    [Fact]
    public void Summarize_AnchorWithCrossStreet_IsKept()
    {
        var incident = new TrafficIncident(
            "i1", "Epping Road between Herring Road and Lane Cove Road", 1, "Epping Road", "accident");
        var corridor = CreateCorridor("Epping Road", incidents: [incident]);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Location: Epping Road between Herring Road and Lane Cove Road");
    }

    [Fact]
    public void Summarize_BetweenAnchorNamingOwnRoad_KeepsOnlyCrossStreet()
    {
        // "Between Bruce Highway and Sandgate Road" on the Bruce Highway corridor aired as the
        // tautology "Bruce Highway ... between the Bruce Highway and Sandgate Road" (a send-back).
        // The self-reference must be stripped to the real cross-street.
        var incident = new TrafficIncident(
            "i1", "Between Bruce Highway and Sandgate Road - Lane blocked", 1, "Bruce Highway", "construction");
        var corridor = CreateCorridor(
            "Bruce Highway", primaryType: "construction", incidents: [incident], totalLengthMeters: 200);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("near Sandgate Road");
        result.ShouldNotContain("between Bruce Highway and Sandgate Road");
    }

    [Fact]
    public void Summarize_RouteCodePrefixedSelfReference_IsSuppressed()
    {
        // Observed live (Sydney): HERE's incident description named the corridor's OWN road with
        // its route code prefixed — "M1 Eastern Distributor" — on the "Eastern Distributor"
        // corridor. This is the SAME road, not a genuine cross-street, but the old subset check
        // only caught anchor tokens contained in road tokens, not the reverse (road tokens
        // contained in anchor tokens with only a route-code token added).
        var incident = new TrafficIncident(
            "i1", "M1 Eastern Distributor", 1, "Eastern Distributor", "congestion");
        var corridor = CreateCorridor("Eastern Distributor", incidents: [incident]);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Location:");
    }

    [Fact]
    public void Summarize_RouteCodePrefixedAnchorWithGenuineCrossStreet_KeepsCrossStreet()
    {
        // "M1 Eastern Distributor at Cathedral Street" carries a REAL cross-street beyond the
        // route code + road name — must not be over-suppressed by the route-code fix.
        var incident = new TrafficIncident(
            "i1", "M1 Eastern Distributor at Cathedral Street", 1, "Eastern Distributor", "congestion");
        var corridor = CreateCorridor("Eastern Distributor", incidents: [incident]);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Cathedral Street");
    }

    [Fact]
    public void Summarize_AnchorMatchingSiblingCorridorsOwnRoad_IsSuppressed()
    {
        // Observed live (Brisbane): "Fig Tree Pocket Road" was voiced as its own standstill item
        // AND, separately, as Centenary Highway's cross-street anchor ("Centenary Highway is also
        // delayed at Fig Tree Pocket Road") — the same place named twice across DIFFERENT
        // sentences. DuplicateAnchorDetector only catches repetition within ONE sentence; this
        // must be suppressed at the data layer so the writer is never handed the redundant anchor.
        // Both corridors must be individually NAMED for cross-sibling anchor suppression to fire,
        // so figTreePocket is a Major accident too (a swept minor is dropped from siblingTokens).
        var figTreePocket = CreateCorridor(
            "Fig Tree Pocket Road", severity: 2, primaryType: "accident", totalLengthMeters: 200);
        var centenaryIncident = new TrafficIncident(
            "c1", "At Fig Tree Pocket Road - Heavy congestion", 2, "Centenary Highway", "congestion");
        var centenaryHighway = CreateCorridor(
            "Centenary Highway", incidents: [centenaryIncident], totalLengthMeters: 200);
        var context = CreateContext([figTreePocket, centenaryHighway]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Location:");
        // Fig Tree Pocket Road is still voiced ONCE — as its own corridor's headline, never twice.
        int count = 0, idx = 0;
        while ((idx = result.IndexOf("Fig Tree Pocket Road", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += "Fig Tree Pocket Road".Length;
        }
        count.ShouldBe(1);
    }

    [Fact]
    public void Summarize_AnchorMatchingSweptOverflowCorridor_IsNotSuppressed()
    {
        // A sibling that gets SWEPT (beyond the tier cap, never individually named) is never
        // actually voiced as its own item — an anchor matching it is genuine information, not a
        // duplicate, and must survive.
        var mainRoad = new TrafficIncident("m1", "At Overflow Road - Minor delay", 3, "Main Road", "congestion");
        var mainCorridor = CreateCorridor("Main Road", incidents: [mainRoad], totalLengthMeters: 200);
        // 3 more minor corridors push "Overflow Road" past the MinorTierCap of 3, so it is swept,
        // never individually named — its name is still genuine NEW information as an anchor.
        var minors = new[] { "Overflow Road", "Filler Road A", "Filler Road B", "Filler Road C" }
            .Select(r => CreateCorridor(r, severity: 3, totalLengthMeters: 200))
            .ToList();
        var context = CreateContext([mainCorridor, .. minors]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Overflow Road");
    }

    [Fact]
    public void Summarize_AnchorMatchingSelfIsNotTreatedAsSibling()
    {
        // A corridor's own incident anchor referencing ITS OWN road name is handled by the
        // existing self-reference logic (CleanAnchor) — the sibling check must not double-count
        // the corridor against itself and accidentally suppress a genuine cross-street.
        var incident = new TrafficIncident(
            "i1", "Epping Road between Herring Road and Lane Cove Road", 1, "Epping Road", "accident");
        var corridor = CreateCorridor("Epping Road", incidents: [incident]);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Location: Epping Road between Herring Road and Lane Cove Road");
    }

    [Fact]
    public void Summarize_SecondAnchorRestatingFirstAnchorsPlace_IsSuppressed()
    {
        // Observed live (Gold Coast): two incidents on the same corridor produced TWO Location
        // lines both naming "Tamborine Mountain Road" — "Between Tamborine Mountain Road and
        // Henri Robert Drive" (a genuine two-endpoint anchor) AND a separate, narrower "At
        // Tamborine Mountain Road". Extent was unset, so the existing Extent-overlap filter never
        // saw it; the writer then read the same place name twice in one breath. The richer
        // "between" anchor must survive; the narrower duplicate must be dropped.
        var incidents = new[]
        {
            new TrafficIncident(
                "i1", "Between Tamborine Mountain Road and Henri Robert Drive", 2, "Mountain Road", "roadHazard"),
            new TrafficIncident("i2", "At Tamborine Mountain Road - hazard on road", 2, "Mountain Road", "construction")
        };
        var corridor = CreateCorridor("Mountain Road", incidents: incidents, totalLengthMeters: 5000);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Location: Between Tamborine Mountain Road and Henri Robert Drive");
        result.ShouldNotContain("Location: At Tamborine Mountain Road");
        // "Tamborine Mountain Road" is voiced ONCE across all Location lines for this corridor.
        int count = 0, idx = 0;
        while ((idx = result.IndexOf("Tamborine Mountain Road", idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            idx += "Tamborine Mountain Road".Length;
        }
        count.ShouldBe(1);
    }

    [Fact]
    public void Summarize_TwoIncidentsWithGenuinelyDistinctAnchors_BothSurvive()
    {
        var incidents = new[]
        {
            new TrafficIncident("i1", "Between Alpha Street and Beta Road", 2, "Main Road", "accident"),
            new TrafficIncident("i2", "At Gamma Avenue - broken down vehicle", 2, "Main Road", "disabledVehicle")
        };
        var corridor = CreateCorridor("Main Road", incidents: incidents, totalLengthMeters: 5000);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Locations (SEPARATE spots along this road");
        result.ShouldContain("Between Alpha Street and Beta Road");
        result.ShouldContain("At Gamma Avenue");
    }

    [Fact]
    public void Summarize_AtOwnRoadAnchorWithExtraDetail_IsDropped()
    {
        // "At Kessels Road - Major event" on the Kessels Road corridor aired as "Kessels Road has a
        // major event at Kessels Road". The extra "Major event" tokens used to defeat the subset
        // check; the location anchor must now be dropped entirely (it locates nothing new).
        var incident = new TrafficIncident(
            "i1", "At Kessels Road - Major event", 1, "Kessels Road", "plannedEvent");
        var corridor = CreateCorridor(
            "Kessels Road", primaryType: "plannedEvent", incidents: [incident], totalLengthMeters: 200);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("at Kessels Road");
        result.ShouldNotContain("Kessels Road - Major event");
    }

    [Fact]
    public void Summarize_Bullet_OmitsTierWord_MagnitudeInImpactLine()
    {
        var corridor = CreateCorridor("M1", primaryType: "accident", driveTime: 45, normalDriveTime: 10);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("accident, severe");            // tier word no longer repeated in the bullet
        result.ShouldContain("severe delays");                  // magnitude qualitative in the Impact line (no minute)
        result.ShouldNotContain("severity 1");                  // never leak raw HERE severity
    }

    [Fact]
    public void Summarize_MinorCorridor_RendersAsSingleCondensedLine()
    {
        // A minor corridor gets a one-line mention, not the full Impact/Drive/Location briefing.
        var minor = CreateCorridor("Side Street", primaryType: "roadHazard", totalLengthMeters: 200);
        var context = CreateContext([minor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Minor:");
        result.ShouldContain("Side Street");
        result.ShouldNotContain("Impact:");
    }

    [Fact]
    public void Summarize_WithinTier_LeadsWithWorstDelay()
    {
        var worse = CreateCorridor("M1", primaryType: "accident", driveTime: 50, normalDriveTime: 10);
        var bad = CreateCorridor("M2", primaryType: "accident", driveTime: 45, normalDriveTime: 10);
        // Listed worst-last to prove the summarizer re-orders within the tier by delay.
        var context = CreateContext([bad, worse]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.IndexOf("M1", StringComparison.Ordinal)
            .ShouldBeLessThan(result.IndexOf("M2", StringComparison.Ordinal));
    }

    [Fact]
    public void Summarize_MajorCorridor_EmitsIncidentSourceDetail()
    {
        var incident = new TrafficIncident(
            "i1", "Road closed due to accidents", 1, "Pilatus St", "accident",
            Comment: Option<string>.Some(
                "Pilatus St eastbound between Centurion Pl and Berrigan D, Jandakot - Road Crash Road closed"));
        // accident on FC1 floors the corridor at Major, so it gets the full bullet.
        var corridor = CreateCorridor("Pilatus St", primaryType: "accident", incidents: [incident]);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain(
            "Source detail: Pilatus St eastbound between Centurion Pl and Berrigan D, Jandakot");
    }

    [Fact]
    public void Summarize_MinorCorridor_SuppressesIncidentSourceDetail()
    {
        var incident = new TrafficIncident(
            "i1", "Gas leak", 3, "South St", "roadHazard",
            Comment: Option<string>.Some(
                "South St both directions at Fifth Av, White Gum Valley - All Lanes Open"));
        // A non-blocking hazard on a short stretch stays Minor -> condensed bullet, no sub-lines.
        var corridor = CreateCorridor(
            "South St", primaryType: "roadHazard", incidents: [incident], totalLengthMeters: 500);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Source detail:");
    }
    // ── Drive-time threshold: suppress zero-delta comparison ──

    [Fact]
    public void Summarize_DriveTimeEqualToNormal_OmitsDriveTimeLine()
    {
        // A closure / no-delay corridor where drive time matches normal — the pointless
        // "5 min instead of the usual 5" comparison must be suppressed entirely.
        var corridor = CreateCorridor(
            "Reid Highway",
            primaryType: "roadClosure",
            driveTime: 5,
            normalDriveTime: 5);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Drive time:");
    }

    [Fact]
    public void Summarize_DriveTimeOneMinAboveNormal_OmitsDriveTimeLine()
    {
        // A +1 min difference is within noise — suppress.
        var corridor = CreateCorridor(
            "Eastern Freeway",
            driveTime: 13,
            normalDriveTime: 12);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Drive time:");
    }

    [Fact]
    public void Summarize_PerCorridorDriveTime_NeverVoiced()
    {
        // Even a meaningful +2 min delta no longer emits a per-corridor drive-time line — a
        // corridor's magnitude is qualitative; times live only on the monitored route.
        var corridor = CreateCorridor(
            "Eastern Freeway",
            driveTime: 14,
            normalDriveTime: 12);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Drive time:");
    }

    [Fact]
    public void Summarize_DriveTimeOnly_OmitsDriveTimeLine()
    {
        // Drive time present but no normal — can't compute delta, omit.
        var corridor = CreateCorridor(
            "M4",
            driveTime: 25);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Drive time:");
    }

    // ── Self-anchor: "the X area near X" suppression ──

    [Fact]
    public void Summarize_RoadNameResolvesToSuburbArea_OmitsRedundantSuburbAnchor()
    {
        // When ResolveDisplayRoadName turns a placeholder into "the Neranwood area",
        // the bullet must NOT add "near Neranwood" — that's the self-anchor bug.
        var corridor = CreateCorridor("the Neranwood area", suburb: "Neranwood", direction: "outbound");
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Neranwood area outbound");
        result.ShouldNotContain("near Neranwood");
    }

    [Fact]
    public void Summarize_RoadNameContainsSuburb_OmitsRedundantSuburbAnchor()
    {
        // "Nerang Street near Nerang" is redundant — the road name already contains the suburb.
        var corridor = CreateCorridor("Nerang Street", suburb: "Nerang", direction: "outbound");
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Nerang Street outbound");
        result.ShouldNotContain("near Nerang");
    }

    [Fact]
    public void Summarize_RoadNameDifferentFromSuburb_KeepsSuburbAnchor()
    {
        // "Pacific Highway near Nerang" is a valid cross-reference — keep it.
        var corridor = CreateCorridor("Pacific Highway", suburb: "Nerang", direction: "outbound");
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Pacific Highway outbound near Nerang");
    }
    [Fact]
    public void Summarize_MinorCorridor_VoicesInlineCrossStreetAnchor()
    {
        // A minor corridor still gets ONE inline cross-street anchor (not a bare road name) so the
        // writer can tell listeners WHERE it is — minor-only reads previously scored zero anchors.
        // It stays a condensed single line (no Impact:/Location: sub-lines).
        var incident = new TrafficIncident("i1", "At Druitt Street - hazard", 1, "King Street", "roadHazard");
        var minor = CreateCorridor(
            "King Street", primaryType: "roadHazard", incidents: [incident], totalLengthMeters: 200);
        var context = CreateContext([minor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Minor:");
        result.ShouldContain("At Druitt Street");   // anchor present, inline
        result.ShouldNotContain("Impact:");          // still condensed — no sub-lines
    }

    // ── Tier cap (roll-call prevention at the data layer) ─────────────

    [Fact]
    public void Summarize_ManyMinorCorridors_PooledBudgetNamesTenAndOverflows()
    {
        // 12 minor corridors (congestion, short length -> Minor tier); the pooled naming budget
        // is 10 (MajorTierCap 4 + ModerateTierCap 3 + MinorTierCap 3), so corridors 1-10 are
        // individually named and corridors 11-12 are collapsed into a single overflow line.
        var corridors = Enumerable.Range(1, 12)
            .Select(i => CreateCorridor($"Minor Road {i}", primaryType: "congestion", totalLengthMeters: 400))
            .ToList();
        var context = CreateContext(corridors);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Minor Road 1");
        result.ShouldContain("Minor Road 10");
        result.ShouldNotContain("Minor Road 11");
        result.ShouldNotContain("Minor Road 12");
        result.ShouldContain("plus 2 more corridors at this severity");
    }

    [Fact]
    public void Summarize_ManyModerateCorridors_PooledBudgetNamesTenAndOverflows()
    {
        // 12 moderate corridors (congestion + flowDelay 10 min -> Moderate tier via DelayTier);
        // the pooled naming budget allocates 7 slots to Moderate (3 base + 4 unused Major),
        // so corridors 1-7 are individually named and corridors 8-12 are collapsed into a
        // single overflow line — proving the cap mechanism still exists under pooled budgeting.
        var corridors = Enumerable.Range(1, 12)
            .Select(i => CreateCorridor($"Moderate Road {i}", primaryType: "congestion",
                flowDelay: Option<int>.Some(10), totalLengthMeters: 800))
            .ToList();
        var context = CreateContext(corridors);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Moderate Road 1");
        result.ShouldContain("Moderate Road 7");
        result.ShouldNotContain("Moderate Road 8");
        result.ShouldNotContain("Moderate Road 12");
        result.ShouldContain("plus 5 more corridors at this severity");
    }

    [Fact]
    public void Summarize_FewMinorCorridors_NoAggregateLineBelowCap()
    {
        var corridors = Enumerable.Range(1, 2)
            .Select(i => CreateCorridor($"Small Road {i}", primaryType: "congestion", totalLengthMeters: 400))
            .ToList();
        var context = CreateContext(corridors);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("plus");
        result.ShouldNotContain("more corridors at this severity");
    }

    // ── Closure impact never restates the header label ───

    [Fact]
    public void Summarize_Closure_ImpactLineSuppressedUnderClosureLabel()
    {
        // "road closure" on the bullet header plus "Impact: road closed" is ONE fact rendered
        // twice, and the writer voiced the label verbatim ("Coast Highway southbound is road
        // closed", fresh-live eval Gold Coast). The header alone carries the closure; the Impact
        // line must not restate it — and must never carry an invented delay magnitude either.
        var corridor = CreateCorridor("Test Freeway", primaryType: "roadClosure", severity: 1);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("road closure");
        result.ShouldNotContain("Impact: road closed");
        result.ShouldNotContain("headline");
        result.ShouldNotContain("residual delay");
    }

    // ── Location cap tightens when Extent already conveys the span ────

    [Fact]
    public void Summarize_CorridorWithExtent_CapsLocationAnchorsToOne()
    {
        var incidents = new List<TrafficIncident>
        {
            new("i1", "Roadworks between Alpha Street and Beta Street - lane closed", 2, "Test Road", "construction"),
            new("i2", "Near Gamma Avenue - detour signposted", 2, "Test Road", "construction"),
            new("i3", "Approaching Delta Lane - reduced speed", 2, "Test Road", "construction")
        };
        var corridor = CreateCorridor(
            "Test Road", primaryType: "roadClosure", incidents: incidents,
            extent: Option<string>.Some("from Alpha Street to Delta Lane"), totalLengthMeters: 4000);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Extent: from Alpha Street to Delta Lane");
        int locationLineCount = result.Split('\n').Count(l => l.TrimStart().StartsWith("Location:"));
        locationLineCount.ShouldBe(1);
    }

    // ── Minor-tier context-aware suppression (major headline present) ──

    [Fact]
    public void Summarize_MajorHeadline_SuppressesMinorRoadNames()
    {
        // A closure floors Warringah Freeway at Major. Once a Major-or-worse item leads, the minor
        // tier is NOT rendered at all — no names, no count. An earlier count-only "leave these OUT"
        // line was voiced anyway under length pressure ("a few minor hold-ups around town",
        // frozen-eval iter3 Canberra): data the writer never sees is data it cannot voice.
        var major = CreateCorridor("Warringah Freeway", primaryType: "roadClosure");
        var minorA = CreateCorridor("Odea Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var minorB = CreateCorridor("Lister Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var context = CreateContext([major, minorA, minorB]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Warringah Freeway");   // the headline is still named
        result.ShouldNotContain("Minor");            // the tier is gone entirely — no header, no count
        result.ShouldNotContain("more road");
        result.ShouldNotContain("Odea Avenue");      // side-street names never reach the writer
        result.ShouldNotContain("Lister Avenue");
    }

    [Fact]
    public void Summarize_MajorHeadline_NoMinorResidueAtAnyCount()
    {
        // Even a large suppressed-minor pool leaves NO residue in the writer's view — no count
        // line to voice, no names to echo.
        var minorRoads = new[]
        {
            "Odea Avenue", "Lister Avenue", "Monara Street", "Petrie Plaza", "Bourke Street"
        };
        var major = CreateCorridor("Warringah Freeway", primaryType: "roadClosure");
        var minors = minorRoads
            .Select(r => CreateCorridor(r, primaryType: "congestion", totalLengthMeters: 800))
            .ToList();
        var context = CreateContext([major, .. minors]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("more road");
        foreach (string road in minorRoads)
            result.ShouldNotContain(road);
    }

    [Fact]
    public void Summarize_NoMajorHeadline_KeepsMinorRoadNames()
    {
        // Worst item is Moderate (a 10-min congestion delay), so there is NO major headline: the
        // minor items are still part of the story and are named individually under the normal
        // "Minor:" section, NOT swept into the count-only background line.
        var moderate = CreateCorridor("Kingsway", primaryType: "congestion",
            driveTime: Option<int>.Some(20), normalDriveTime: Option<int>.Some(10));
        var minorA = CreateCorridor("Odea Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var minorB = CreateCorridor("Lister Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var context = CreateContext([moderate, minorA, minorB]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Odea Avenue");         // minors named individually...
        result.ShouldContain("Lister Avenue");
        result.ShouldNotContain("Minor (background"); // ...not the suppressed-sweep header
    }

    [Fact]
    public void Summarize_MinorOnlyMarket_KeepsMinorRoadNames()
    {
        // A quiet market with only minor items: the minors ARE the whole bulletin, so they are
        // named individually — the suppression rule only fires when something bigger leads.
        var minorA = CreateCorridor("Odea Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var minorB = CreateCorridor("Monara Street", primaryType: "congestion", totalLengthMeters: 800);
        var context = CreateContext([minorA, minorB]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Odea Avenue");
        result.ShouldContain("Monara Street");
        result.ShouldNotContain("Minor (background");
    }

    [Fact]
    public void Summarize_SevereHeadline_AlsoSuppressesMinorRoadNames()
    {
        // Severe is 'major-or-worse' (Tier <= Major): a 50-min delay-driven Severe corridor leads,
        // so the minor tier is absent entirely, just as for a closure.
        var severe = CreateCorridor("Southern Expressway", primaryType: "congestion",
            driveTime: Option<int>.Some(60), normalDriveTime: Option<int>.Some(10));
        var minorA = CreateCorridor("Odea Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var minorB = CreateCorridor("Lister Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var context = CreateContext([severe, minorA, minorB]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Minor");            // no header, no count — the tier is gone
        result.ShouldNotContain("Odea Avenue");
        result.ShouldNotContain("Lister Avenue");
    }
    // ── MinorTraffic escalation: Minor tier visible when FillOrder requests it ──

    [Fact]
    public void Summarize_MajorHeadline_WithMinorTrafficFillOrder_ShowsMinorTier()
    {
        // When MinorTraffic is in the FillOrder, a Major-tier headline must NOT suppress the Minor
        // tier — the Minor section and its road names DO appear, because the station opted into
        // minor-traffic escalation.
        var major = CreateCorridor("Warringah Freeway", primaryType: "roadClosure");
        var minorA = CreateCorridor("Odea Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var minorB = CreateCorridor("Lister Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var context = CreateContext(
            [major, minorA, minorB],
            fillOrder: [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Warringah Freeway");   // headline still named
        result.ShouldContain("Minor");               // minor tier is present
        result.ShouldContain("Odea Avenue");          // minor road names are visible
        result.ShouldContain("Lister Avenue");
    }

    [Fact]
    public void Summarize_MultipleCorridors_WithMinorTrafficFillOrder_AllTiersVisible()
    {
        // With MinorTraffic requested, Major + Moderate + Minor corridors all appear under their
        // respective tier headers — no tier is suppressed.
        var major = CreateCorridor("M1", primaryType: "accident", driveTime: 45, normalDriveTime: 10);
        var moderate = CreateCorridor("Pacific Highway", primaryType: "congestion",
            driveTime: 22, normalDriveTime: 12, totalLengthMeters: 1200);
        var minor = CreateCorridor("King Street", primaryType: "roadHazard", totalLengthMeters: 300);
        var context = CreateContext(
            [major, moderate, minor],
            city: "Sydney",
            fillOrder: [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Major:");
        result.ShouldContain("Moderate:");
        result.ShouldContain("Minor");
        result.ShouldContain("M1");
        result.ShouldContain("Pacific Highway");
        result.ShouldContain("King Street");
    }

    [Fact]
    public void Summarize_SevereHeadline_WithMinorTrafficFillOrder_ShowsMinorTier()
    {
        // Severe headline with MinorTraffic escalation: Minor tier is present with road names.
        var severe = CreateCorridor("Southern Expressway", primaryType: "congestion",
            driveTime: Option<int>.Some(60), normalDriveTime: Option<int>.Some(10));
        var minorA = CreateCorridor("Odea Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var minorB = CreateCorridor("Lister Avenue", primaryType: "congestion", totalLengthMeters: 800);
        var context = CreateContext(
            [severe, minorA, minorB],
            fillOrder: [FillSource.Weather, FillSource.Fuel, FillSource.MinorTraffic, FillSource.Filler]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Minor");               // minor tier is present
        result.ShouldContain("Odea Avenue");
        result.ShouldContain("Lister Avenue");
    }



    // ── Bidirectional closure merge (Mildura, 2026-07-06) ──

    [Fact]
    public void Summarize_BidirectionalClosureSameRoad_MergesIntoOneBothDirectionsBullet()
    {
        // Observed live (Mildura, 2026-07-06): Walker Road closed citybound AND outbound arrived
        // as two separate corridors; the writer voiced only the surviving one ("Walker Road
        // citybound is shut"), inviting listeners onto the still-closed outbound side. Same-road
        // closures with differing directions must merge into ONE corridor stating "both directions".
        var citybound = CreateCorridor("Walker Road", primaryType: "roadClosure", direction: "citybound",
            description: "Road closed",
            incidents: [new TrafficIncident("walker-cb", "Closed", 3, "Walker Road", "roadClosure")]);
        var outbound = CreateCorridor("Walker Road", primaryType: "roadClosure", direction: "outbound",
            description: "Road closed",
            incidents: [new TrafficIncident("walker-ob", "Closed", 3, "Walker Road", "roadClosure")]);
        var context = CreateContext([citybound, outbound]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("(1 corridor)");
        result.ShouldContain("Walker Road both directions");
        result.ShouldNotContain("citybound");
        result.ShouldNotContain("outbound");
    }

    [Fact]
    public void Summarize_TwoClosuresSameRoadSameDirection_AreNotMerged()
    {
        // Same road, same direction — these are two genuinely separate closure reports, not a
        // bidirectional pair; the merge must only fire on DIFFERING directions.
        var first = CreateCorridor("Ocean Drive", primaryType: "roadClosure", direction: "citybound",
            description: "Road closed",
            incidents: [new TrafficIncident("od1", "Closed", 3, "Ocean Drive", "roadClosure")]);
        var second = CreateCorridor("Ocean Drive", primaryType: "roadClosure", direction: "citybound",
            description: "Road closed",
            incidents: [new TrafficIncident("od2", "Closed", 3, "Ocean Drive", "roadClosure")]);
        var context = CreateContext([first, second]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("(2 corridors)");
        result.ShouldNotContain("both directions");
    }

    [Fact]
    public void Summarize_ClosurePlusNonClosureSameRoad_AreNotMerged()
    {
        // A closure and a non-closure (congestion) corridor on the same road are different facts
        // — merging them would hide that one direction is fully shut, not just slow.
        var closure = CreateCorridor("Beach Road", primaryType: "roadClosure", direction: "citybound",
            description: "Road closed",
            incidents: [new TrafficIncident("br1", "Closed", 3, "Beach Road", "roadClosure")]);
        var congestion = CreateCorridor("Beach Road", primaryType: "congestion", direction: "outbound",
            description: "Heavy traffic building",
            incidents: [new TrafficIncident("br2", "Heavy traffic building slowly", 2, "Beach Road", "congestion")]);
        var context = CreateContext([closure, congestion]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("(2 corridors)");
        result.ShouldNotContain("both directions");
    }

    // ── Hot-board on-time suppression (Sydney, 2026-07-06) ──

    [Fact]
    public void Summarize_HotBoardViaStandstillDescription_SuppressesOnTimeAside()
    {
        // Observed live (Sydney, 2026-07-06): a flow corridor sat "at a standstill" while the
        // routing API's on-time aside for the SAME road (matched only by colloquial vs gazetted
        // name, so road-name matching can't catch it) aired as "only a touch slow". A standstill
        // description alone — no closure required — must suppress ALL on-time asides class-wide.
        var standstill = CreateCorridor("Regional Road", primaryType: "congestion",
            description: "Traffic at a standstill", totalLengthMeters: 4000);
        var onTimeRoute = new MonitoredRouteStatus(
            "Local Bypass run",
            new CoveragePoint(-33.0, 151.0),
            new CoveragePoint(-33.1, 151.1),
            20,
            20,
            15.0);
        var context = CreateContext([standstill], monitoredRoutes: [onTimeRoute]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("running close to normal");
        result.ShouldNotContain("running to time");
    }

    [Fact]
    public void Summarize_QuietBoardWithNonStandstillCorridor_OnTimeAsideStillRenders()
    {
        // A corridor that is neither a closure nor described with a strong-pace phrase is a
        // QUIET board — the on-time aside must still render, proving suppression is targeted at
        // the closure/standstill condition and not any corridor's mere presence.
        var lightCongestion = CreateCorridor("Regional Road", primaryType: "congestion",
            description: "Traffic building gradually", totalLengthMeters: 500);
        var onTimeRoute = new MonitoredRouteStatus(
            "Local Bypass run",
            new CoveragePoint(-33.0, 151.0),
            new CoveragePoint(-33.1, 151.1),
            21,
            20,
            15.0);
        var context = CreateContext([lightCongestion], monitoredRoutes: [onTimeRoute]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("running close to normal");
    }

    [Fact]
    public void Summarize_HotBoardWithClosure_DelayedMonitoredRouteStillRenders()
    {
        // A DELAYED monitored route (the headline channel, not the on-time aside) is never
        // suppressed by the hot-board rule — only on-time asides are. It must still render
        // even while a closure elsewhere makes the board "hot".
        var closure = CreateCorridor("Pacific Motorway", primaryType: "roadClosure", direction: "northbound",
            description: "Road closed",
            incidents: [new TrafficIncident("pm1", "Closed", 3, "Pacific Motorway", "roadClosure")]);
        var delayedRoute = new MonitoredRouteStatus(
            "Gateway Arterial run",
            new CoveragePoint(-27.4, 153.0),
            new CoveragePoint(-27.5, 153.1),
            30,
            20,
            18.0);
        var context = CreateContext([closure], monitoredRoutes: [delayedRoute]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Monitored drive-time routes");
        result.ShouldContain("Gateway Arterial run");
        result.ShouldContain("10 min slower than usual");
    }

    // ── Separate-spots anchor label (Brisbane, 2026-07-06) ──

    [Fact]
    public void Summarize_TwoDistinctAnchoredIncidents_UsesSeparateSpotsLabel()
    {
        // Observed live (Brisbane, 2026-07-06): construction "between Bruce Hwy and Sandgate Rd"
        // plus a breakdown "at Steve Irwin Way" (~45km apart) aired as one welded place, "a
        // breakdown near Sandgate Road at Steve Irwin Way". Two anchors on the same corridor must
        // be labelled as SEPARATE spots, never rendered as twin bare "Location:" lines.
        var incidents = new[]
        {
            new TrafficIncident("i1", "Between Bruce Highway and Sandgate Road - Road construction", 2,
                "Beerburrum Range Road", "construction"),
            new TrafficIncident("i2", "At Steve Irwin Way - Broken-down vehicle", 2,
                "Beerburrum Range Road", "disabledVehicle")
        };
        var corridor = CreateCorridor("Beerburrum Range Road", incidents: incidents, totalLengthMeters: 5000);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain(
            "Locations (SEPARATE spots along this road — voice separately or pick the worst; never combine into one place):");
        result.ShouldContain("Between Bruce Highway and Sandgate Road");
        result.ShouldContain("At Steve Irwin Way");
    }

    [Fact]
    public void Summarize_SingleAnchoredIncident_StillUsesSingularLocationLabel()
    {
        // Guard: a corridor with only ONE anchored incident keeps the plain "Location:" form —
        // the SEPARATE-spots label is only for genuinely plural anchors.
        var incident = new TrafficIncident("i1", "At Steve Irwin Way - Broken-down vehicle", 2,
            "Beerburrum Range Road", "disabledVehicle");
        var corridor = CreateCorridor("Beerburrum Range Road", incidents: [incident], totalLengthMeters: 5000);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Location: At Steve Irwin Way");
        result.ShouldNotContain("Locations (SEPARATE spots");
    }

    // ── Existing behavior guard: unaffected single-direction closure ──

    [Fact]
    public void Summarize_SingleDirectionClosure_RendersDirectionAndClosureLabelUnchanged()
    {
        // A closure with no bidirectional partner must render exactly as before the merge fix —
        // its own direction, unmerged.
        var corridor = CreateCorridor("Bruce Highway", primaryType: "roadClosure", direction: "southbound",
            description: "Road closed southbound");
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Bruce Highway southbound");
        result.ShouldContain("road closure");
        result.ShouldNotContain("both directions");
    }

    [Fact]
    public void Summarize_StatusPhraseAnchor_IsSuppressed()
    {
        // Observed live (Brisbane, 2026-07-13): HERE put a bare status in the incident description
        // ("Closed due to road construction") for a construction corridor with no cross-street.
        // Rendered as a Location anchor it leaked "Closed" into the bullet and the writer voiced the
        // roadworks item as closed. A status is not a location — it must never become an anchor.
        var incident = new TrafficIncident(
            "i1", "Closed due to road construction", 2, "Gilchrist Avenue", "construction");
        var corridor = CreateCorridor(
            "Gilchrist Avenue", primaryType: "construction", description: "roadworks outbound",
            incidents: [incident], totalLengthMeters: 700);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldNotContain("Closed due to road construction");
        result.ShouldNotContain("Location: Closed");
    }

    [Fact]
    public void Summarize_GenuineCrossStreetAnchor_SurvivesStatusPhraseGuard()
    {
        // The status-phrase guard must not over-suppress a real cross-street anchor.
        var incident = new TrafficIncident("i1", "At Milton Road", 2, "Frederick Street", "construction");
        var corridor = CreateCorridor(
            "Frederick Street", primaryType: "construction", description: "roadworks southbound",
            incidents: [incident], totalLengthMeters: 200);
        var context = CreateContext([corridor]);

        string result = TrafficNarrativeSummarizer.Summarize(context);

        result.ShouldContain("Milton Road");
    }

    // ── Authority detail (2026-08-18): the road authority's own operational detail reaches the
    // writer as a vetted, provenance-tagged enrichment — never as raw feed text, and never as a
    // second claim. Source-generic via OfficialEvidences: every feed flows through the one line.
    // Scope: FULL bullets only — a condensed minor is a passing mention and carries no sub-lines,
    // so a quiet worksite's ops detail deliberately never reaches the writer.

    private static TrafficCorridor CrashWithEvidence(string? officialDescription)
    {
        return CreateCorridor("Pease Street", severity: 1, primaryType: "accident",
            description: "Multi-vehicle crash", driveTime: Option<int>.Some(20),
            normalDriveTime: Option<int>.Some(8)) with
        {
            Sources = new[] { "here", "qldtraffic" },
            OfficialEvidences =
            [
                new OfficialEvidence(
                    "qldtraffic", "Crash: Pease Street", IsIncidentClass: true,
                    Description: officialDescription, MatchBasis: "road-name")
            ]
        };
    }

    [Fact]
    public void AuthorityDetail_ReachesTheWriter_TaggedWithItsSource()
    {
        string result = TrafficNarrativeSummarizer.Summarize(
            CreateContext([CrashWithEvidence("Traffic control on scene")]));

        result.ShouldContain("Authority detail (qldtraffic): Traffic control on scene");
    }

    [Fact]
    public void AuthorityDetail_AdvisoryText_IsStrippedBeforeTheWriterSeesIt()
    {
        string result = TrafficNarrativeSummarizer.Summarize(CreateContext([
            CrashWithEvidence("Traffic control on scene. Allow extra time and drive to conditions.")
        ]));

        result.ShouldContain("Traffic control on scene");
        result.ShouldNotContain("Allow extra time");
        result.ShouldNotContain("drive to conditions");
    }

    [Fact]
    public void AuthorityDetail_AddingNothingBeyondTheBullet_IsDropped()
    {
        string result = TrafficNarrativeSummarizer.Summarize(
            CreateContext([CrashWithEvidence("Multi-vehicle crash")]));

        result.ShouldNotContain("Authority detail");
    }

    [Fact]
    public void AuthorityDetail_AbsentWhenNoAuthorityContributed()
    {
        var corridor = CreateCorridor("Pease Street", severity: 1, primaryType: "accident",
            description: "Multi-vehicle crash", driveTime: Option<int>.Some(20),
            normalDriveTime: Option<int>.Some(8));

        string result = TrafficNarrativeSummarizer.Summarize(CreateContext([corridor]));

        result.ShouldNotContain("Authority detail");
    }

    [Fact]
    public void AuthorityDetail_StockSignageAdvice_IsStrippedEntirely()
    {
        // QLDTraffic's stock roadwork advice, observed live 2026-08-18: "Observe signage" is
        // driver advice, not operational detail — the no-advice boundary eats it whole.
        string result = TrafficNarrativeSummarizer.Summarize(
            CreateContext([CrashWithEvidence("Observe signage")]));

        result.ShouldNotContain("Authority detail");
        result.ShouldNotContain("Observe signage");
    }

    [Fact]
    public void AuthorityDetail_NeverOnACondensedMinorBullet()
    {
        // A quiet standing worksite renders as a one-line passing mention; the authority's ops
        // detail is Major/Moderate colour and must not grow the minor tier.
        var corridor = CreateCorridor("Pease Street", primaryType: "construction",
            description: "roadworks") with
        {
            Sources = new[] { "here", "qldtraffic" },
            OfficialEvidences =
            [
                new OfficialEvidence(
                    "qldtraffic", "Roadworks", IsIncidentClass: false,
                    Description: "Traffic control on scene", MatchBasis: "road-name")
            ]
        };

        string result = TrafficNarrativeSummarizer.Summarize(CreateContext([corridor]));

        result.ShouldNotContain("Authority detail");
    }
}

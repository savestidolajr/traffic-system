using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.AzureOpenAI;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.AzureOpenAI;

public sealed class EvidenceUnitBuilderTests
{
    // ── Fixture ──────────────────────────────────────────────────────────
    //
    // Two corridors, both promoted above Minor tier (one by incident-type floor, one by measured
    // delay) so BOTH render as FULL bullets — BuildEvidenceUnits must mirror AppendFullCorridorEvidence
    // for each. "Beerburrum Range Road" reproduces the live "combined_locations" defect (Brisbane,
    // 2026-07-06): construction "between Bruce Hwy and Sandgate Rd" plus an unrelated breakdown "at
    // Steve Irwin Way", ~45km apart on the same road, with NO Extent — so CorridorAnchors surfaces
    // BOTH cross-street anchors (locationCap=2) as two SEPARATE location evidence units; the
    // structured draft's evidence-id contract must preserve both when a sentence cites them.

    private static TrafficCorridor CreateCombinedLocationsCorridor()
    {
        var incidents = new[]
        {
            new TrafficIncident("i1", "Between Bruce Highway and Sandgate Road - Road construction", 2,
                "Beerburrum Range Road", "construction"),
            new TrafficIncident("i2", "At Steve Irwin Way - Broken-down vehicle", 2,
                "Beerburrum Range Road", "disabledVehicle")
        };
        // FunctionalClass=1 + PrimaryType="accident" floors this corridor at Major regardless of
        // delay/length, so it always gets the full bullet treatment (never condensed/suppressed).
        return new TrafficCorridor(
            "Beerburrum Range Road", 1, 1, 5000, 80, "accident",
            "Two separate incidents affecting this stretch", incidents);
    }

    private static TrafficCorridor CreateSecondCorridor()
    {
        var incident = new TrafficIncident(
            "i3", "At Chapel Street - Lane closure due to works", 2, "Pacific Motorway", "laneRestriction",
            Summary: Option<string>.Some("Left lane blocked"),
            Comment: Option<string>.Some("Vms Trailer on scene"));
        // 15 min measured delay promotes this "congestion" corridor to Moderate tier — still a full
        // bullet, but via the delay path rather than the type floor, exercising a different corridor.
        return new TrafficCorridor(
            "Pacific Motorway", 2, 3, 2000, 60, "congestion",
            "Slow moving traffic through the interchange", [incident],
            Extent: Option<string>.Some("City Road to Toorak Road"),
            DriveTimeMinutes: Option<int>.Some(25),
            NormalDriveTimeMinutes: Option<int>.Some(10));
    }

    private static WeatherReport CreateWeather() => new("Overcast", 9.4, default, 20, []);

    private static FuelPriceReport CreateFuel() =>
        new([new FuelPriceSummary("Unleaded 91", 1.45, 1.60, 12, CheapestStationSuburb: "Southport")]);

    private static readonly IReadOnlyList<string> GoodRunRoadNames =
        ["Anzac Parade", "Southport Nerang Road"];

    private static BroadcastWithTraffic CreateContext()
    {
        var combined = CreateCombinedLocationsCorridor();
        var second = CreateSecondCorridor();
        return new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "evidence-unit-test-job",
            Location = "Gold Coast",
            Traffic = "Corridors reported for Gold Coast",
            Incidents = combined.Incidents.Concat(second.Incidents).ToList(),
            Corridors = [combined, second],
            FillOrder = [FillSource.Weather, FillSource.Fuel],
            Weather = Option<WeatherReport>.Some(CreateWeather()),
            Fuel = Option<FuelPriceReport>.Some(CreateFuel()),
            GoodRunRoads = GoodRunRoadNames
        };
    }

    // ── Structural-rule target: two separate location units, one road ─────

    [Fact]
    public void BuildEvidenceUnits_CombinedLocationsCorridor_ProducesTwoDistinctLocationUnitsSameSourceKey()
    {
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(CreateContext());

        var locationUnits = units
            .Where(u => u.SourceKey == "corridor:Beerburrum Range Road" && u.Field == "location")
            .OrderBy(u => u.Id)
            .ToList();

        // Exactly two — one per anchor. No Extent on this corridor means locationCap=2, so both
        // survive (a corridor WITH an Extent caps at 1 and could never trigger the structural rule).
        locationUnits.Count.ShouldBe(2);
        locationUnits[0].SourceKey.ShouldBe(locationUnits[1].SourceKey);
        // Ids are short and road-free since 2026-08-20 ("r1:location:0"), so assert the SHAPE and
        // the per-anchor suffix rather than a literal that hardcodes the id scheme.
        locationUnits[0].Id.ShouldEndWith(":location:0");
        locationUnits[1].Id.ShouldEndWith(":location:1");
        locationUnits[0].Id.ShouldNotContain("corridor");
        locationUnits[0].Value.ShouldBe("Between Bruce Highway and Sandgate Road");
        locationUnits[1].Value.ShouldBe("At Steve Irwin Way");
    }

    [Fact]
    public void BuildEvidenceUnits_SameRoadCorridorsHaveUniqueDiscriminatedIdsAndSuburbs()
    {
        var corridors = new[] { "Aeroglen", "Cairns North", "Palm Cove" }
            .Select((suburb, index) =>
            {
                var incident = new TrafficIncident(
                    $"same-road-{index}", $"At {suburb} Road - Congestion", 1,
                    "Captain Cook Highway", "congestion");
                return new TrafficCorridor(
                    "Captain Cook Highway", 2, 1, 1000, 20, "congestion",
                    $"Congestion near {suburb}", [incident],
                    Suburb: Option<string>.Some(suburb));
            })
            .ToArray();
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "same-road-corridors",
            Location = "Cairns",
            Traffic = "ignored fallback",
            Corridors = corridors,
            Incidents = corridors.SelectMany(c => c.Incidents).ToList(),
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };

        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);
        units.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count().ShouldBe(units.Count);

        // Ids are short and road-free ("r1", "r2", ...) since 2026-08-20, so the corridor part is
        // everything before the first ':'. Three same-road corridors must still get three distinct
        // handles, and the same corridor must keep its handle when the input order changes — that
        // stability is the point of the test, not the literal text of the id.
        static string Handle(EvidenceUnit unit) => unit.Id[..unit.Id.IndexOf(':')];

        var corridorIds = units
            .Where(u => u.SourceKey == "corridor:Captain Cook Highway")
            .Select(Handle)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        corridorIds.Count.ShouldBe(3);
        corridorIds.ShouldAllBe(id => !id.Contains("Captain Cook Highway"));

        units.Where(u => u.Field == "suburb").Select(u => u.Value)
            .ShouldBe(["Aeroglen", "Cairns North", "Palm Cove"]);
        var cairnsNorth = units.Single(u => u.Field == "suburb" && u.Value == "Cairns North");
        cairnsNorth.SourceKey.ShouldBe("corridor:Captain Cook Highway");

        var originalIds = units
            .Where(u => u.Field == "suburb")
            .ToDictionary(u => u.Value, Handle);
        var reordered = context with { Corridors = corridors.Reverse().ToArray() };
        var reorderedIds = TrafficNarrativeSummarizer.BuildEvidenceUnits(reordered)
            .Where(u => u.Field == "suburb")
            .ToDictionary(u => u.Value, Handle);
        foreach (var (suburb, id) in originalIds)
            reorderedIds[suburb].ShouldBe(id);
    }

    [Fact]
    public void BuildEvidenceUnits_CorridorWithoutSuburbEmitsNoCorridorSuburbUnit()
    {
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(CreateContext());
        units.ShouldNotContain(u => u.SourceKey.StartsWith("corridor:") && u.Field == "suburb");
    }

    [Fact]
    public void BuildEvidenceUnits_CombinedLocationsCorridor_MapsRemainingFieldsPresentAndAbsent()
    {
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(CreateContext());
        var corridorUnits = units.Where(u => u.SourceKey == "corridor:Beerburrum Range Road").ToList();

        // Two distinct incident types, excluding the header's own "accident" label.
        corridorUnits.ShouldContain(u => u.Field == "incident" && u.Value == "roadworks");
        corridorUnits.ShouldContain(u => u.Field == "incident" && u.Value == "breakdown");

        // No measured delay and a >3000 m single observation on a non-standing-footprint type ->
        // the "long queue" band, which since 2026-08-25 is SPOKEN before the writer sees it: the
        // band itself aired verbatim on 6 of 13 end-to-end reads, so the id keeps ":impact" for
        // citations while the field and value carry sayable road-feel. The km figure this used to
        // carry ("queue ~5km") was ours, not HERE's: derived by summing every incident on the road
        // (Cairns 2026-08-13 aired a 12.2 km sum of overlapping works records as "queues back about
        // ten kays").
        corridorUnits.ShouldContain(u =>
            u.Field == "road feel"
            && u.Value == RoadFeelTranslator.Speak("long queue", "Beerburrum Range Road"));
        corridorUnits.ShouldNotContain(u => u.Value.Contains("long queue"));
        corridorUnits.ShouldNotContain(u => u.Field == "road feel" && u.Value.Contains("km"));

        corridorUnits.ShouldContain(u => u.Field == "detail" && u.Value == "Two separate incidents affecting this stretch");

        // Fields with no backing data on this corridor must NOT appear — no Extent set, neither
        // incident carries a Summary or a Comment.
        corridorUnits.Count(u => u.Field == "extent").ShouldBe(0);
        corridorUnits.Count(u => u.Field == "lanes").ShouldBe(0);
        corridorUnits.Count(u => u.Field == "sourceDetail").ShouldBe(0);
    }

    [Fact]
    public void BuildEvidenceUnits_ClearedFactsAreDeduplicatedByRoadAndCondition()
    {
        // Types chosen to PASS the cleared-worthiness gate (2026-08-23): this test is about
        // deduplication by road+condition, not about which clears deserve airtime, and it previously
        // used two congestion clears — a category the gate now drops entirely, which would have made
        // it pass or fail for the wrong reason.
        var cleared = new[]
        {
            new IncidentDeltaItem("c1", "roadClosure", 1, "Harbour Bridge", "Cairns North", default, 0, default),
            new IncidentDeltaItem("c2", "roadClosure", 1, "Harbour Bridge", "Cairns North", default, 0, default),
            new IncidentDeltaItem("c3", "accident", 1, "Harbour Bridge", "Cairns North", default, 0, default)
        };
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "cleared-dedup",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Incidents = [],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false),
            IncidentChanges = Option<IncidentDeltaContext>.Some(
                new IncidentDeltaContext([], [], cleared, []))
        };

        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context)
            .Where(u => u.Field == "cleared")
            .ToList();

        units.Count.ShouldBe(2);
        units.Count(u => u.Value.Contains("road closure on Harbour Bridge", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        units.Count(u => u.Value.Contains("crash on Harbour Bridge", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
    }

    [Fact]
    public void BuildEvidenceUnits_SecondCorridor_MapsFullBulletFieldsIncludingExtentAndLanes()
    {
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(CreateContext());
        var corridorUnits = units.Where(u => u.SourceKey == "corridor:Pacific Motorway").ToList();

        corridorUnits.ShouldContain(u => u.Field == "incident" && u.Value == "lane closure");
        // A MEASUREMENT OUTRANKS OUR DERIVATION OF IT. This corridor carries delayMinutes, so it no
        // longer also gets road-feel: FormatImpact bands anything up to five minutes as "minor
        // delays" and speaks it as "a touch slower", while the station palette reserves that for one
        // or two and wants "a slower run" from three. Shipping both handed the writer two severity
        // scales that disagree on 7 of 9 delay values, always understating.
        corridorUnits.ShouldContain(u => u.Field == "delayMinutes");
        corridorUnits.ShouldNotContain(u => u.Field == "road feel");
        corridorUnits.ShouldContain(u => u.Field == "extent" && u.Value == "City Road to Toorak Road");
        corridorUnits.ShouldContain(u => u.Field == "lanes" && u.Value == "Left lane blocked");
        corridorUnits.ShouldContain(u => u.Field == "sourceDetail" && u.Value == "Vms Trailer on scene");
        corridorUnits.ShouldContain(u => u.Field == "detail" && u.Value == "Slow moving traffic through the interchange");

        // Extent.HasValue caps location at 1, even though a second anchor candidate could exist —
        // this corridor has only one incident description anyway, so the cap is not independently
        // exercised here beyond confirming exactly one location unit is emitted.
        corridorUnits.Count(u => u.Field == "location").ShouldBe(1);
        corridorUnits.Single(u => u.Field == "location").Value.ShouldBe("At Chapel Street");
    }
    [Fact]
    public void BuildEvidenceUnits_ClassifierOutputs_AreNeverCitable()
    {
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(CreateContext());
        var corridorUnits = units.Where(u => u.SourceKey == "corridor:Pacific Motorway").ToList();

        // The writer must never be handed our own classifier labels: "Leadable" and "Moderate" are
        // not words a presenter can say, and magnitudeTier restated a magnitude already carried as
        // delayMinutes. Measured 2026-08-20 over four live draft captures, voiceability earned 0
        // citations from 783 prompt chars and magnitudeTier 2 from 776.
        corridorUnits.ShouldNotContain(u => u.Field == "magnitudeTier");
        corridorUnits.ShouldNotContain(u => u.Field == "voiceability");

        // The concrete magnitude survives, and is still mirrored by the prose so the two agree.
        corridorUnits.ShouldContain(u => u.Field == "delayMinutes" && u.Value == "15");
        TrafficNarrativeSummarizer.Summarize(CreateContext()).ShouldContain("delayMinutes: 15");
    }

    [Fact]
    public void BuildEvidenceUnits_IntermediateClosedEdgeOpen_EmitsNoAccessEvidenceOrProse()
    {
        var incident = new TrafficIncident(
            "closed-edge", "Road closure", 1, "Craig Street", "roadClosure",
            JunctionTraversability: Option<string>.Some("intermediateClosedEdgeOpen"));
        var corridor = new TrafficCorridor(
            "Craig Street", 2, 1, 800, 40, "roadClosure", "Road closed", [incident]);
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "closed-edge-no-access",
            Location = "Perth",
            Traffic = "ignored fallback",
            Corridors = [corridor],
            Incidents = [incident],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };

        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);

        units.ShouldNotContain(unit => unit.Field == "access");
        TrafficNarrativeSummarizer.Summarize(context).ShouldNotContain("Access:");
    }

    [Fact]
    public void BuildEvidenceUnits_MinorTierCorridor_StillGetsTheFullFieldSet()
    {
        // Live 2026-08-20: minor-tier condensation starved 5 of 7 cairns corridors and both Sydney
        // minor items, so an item like Mitchell Road reached the writer as a bare location with no
        // incident and nothing to say. The tier decides ORDER, never how much the writer is told.
        var incident = new TrafficIncident(
            "i-minor", "Left lane blocked", 1, "Mitchell Road", "construction");
        var corridor = new TrafficCorridor(
            "Mitchell Road", 1, 5, 320, 5, "construction",
            "roadworks between Sydney Park Road and Maddox Street", [incident],
            Suburb: Option<string>.Some("Alexandria"),
            Direction: Option<string>.Some("northbound"),
            OfficialEvidences:
            [
                new OfficialEvidence(
                    "livetraffic-nsw", "SCHEDULED ROADWORK", IsIncidentClass: false,
                    AdviceTags: ["Use an alternative route"], SpeedLimitKph: 40,
                    LanesClosed: 2, LanesTotal: 3)
            ]);
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 22,
            JobId = "minor-tier",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Corridors = [corridor],
            Incidents = [incident],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };

        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);

        units.ShouldContain(u => u.Field == "detail");
        units.ShouldContain(u => u.Field == "direction" && u.Value == "northbound");
        units.ShouldContain(u => u.Field == "lanesClosed" && u.Value == "two of three lanes closed");
        // A fact with its unit, not a phrasing: "40 through the works" shipped once and the writer
        // read it back as "It's 40 through the works, so check the signs", inventing the advice.
        units.ShouldContain(u => u.Field == "speedLimit" && u.Value == "40 kilometres an hour");
        units.ShouldContain(u => u.Field == "advice" && u.Value == "use an alternative route");
    }

    [Fact]
    public void BuildEvidenceUnits_MultipleAdviceTags_VoicesOnlyOne()
    {
        // Every item ending in "find another way around" is a machine tell. The authority already
        // orders these by usefulness, so the first is the one worth airing.
        var incident = new TrafficIncident("i-advice", "roadworks", 1, "Church Street", "construction");
        var corridor = new TrafficCorridor(
            "Church Street", 2, 4, 400, 8, "construction", "Roadworks northbound", [incident],
            OfficialEvidences:
            [
                new OfficialEvidence(
                    "livetraffic-nsw", "ROADWORK", IsIncidentClass: false,
                    AdviceTags: ["Allow extra travel time", "Plan your journey", "Use an alternative route"])
            ]);
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 22,
            JobId = "advice-cap",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Corridors = [corridor],
            Incidents = [incident],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };

        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);

        units.Count(u => u.Field == "advice").ShouldBe(1);
        units.Single(u => u.Field == "advice").Value.ShouldBe("allow extra travel time");
    }

    [Fact]
    public void BuildEvidenceUnits_NonPositiveDelay_OmitsDelayEvidence()
    {
        var incident = new TrafficIncident("i-zero", "traffic", 1, "M4", "congestion");
        var corridor = new TrafficCorridor(
            "M4", 1, 1, 0, 20, "congestion", "Traffic reported", [incident],
            FlowDelayMinutes: Option<int>.Some(0));
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "zero-delay",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Corridors = [corridor],
            Incidents = [incident],
            Content = new BulletinContentSelection(Traffic: true, Weather: false, Fuel: false)
        };

        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);
        string summary = TrafficNarrativeSummarizer.Summarize(context);

        // A zero delay must not become a silent "nothing here": the item still reaches the writer,
        // it just carries no delay fact. Mitchell Road (live, 2026-08-20) is the case that matters —
        // a road closure with 0 min delay, which is newsworthy precisely because delay is not the
        // measure of it.
        units.ShouldNotBeEmpty();
        units.ShouldNotContain(u => u.Field == "delayMinutes");
        summary.ShouldNotContain("delayMinutes:");
    }

    // ── Fuel + good-run mapping ─────────────────────────────────────────

    [Fact]
    public void BuildEvidenceUnits_FuelAndGoodRunRoads_MapsPriceAndStatusFacts()
    {
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(CreateContext());

        // Spoken, not numeric (2026-08-25). A bare "$1.45" handed to the writer aired verbatim on
        // the port lincoln panel read, and ElevenLabs does not reliably speak a currency token.
        var fuelPriceUnit = units.Single(u => u.Id == "fuel:unleaded:price");
        fuelPriceUnit.Value.ShouldBe("a dollar forty-five");
        fuelPriceUnit.Value.ShouldNotContain("$");

        var fuelSuburbUnit = units.Single(u => u.Id == "fuel:unleaded:suburb");
        fuelSuburbUnit.Value.ShouldBe("Southport");
        // Good runs are ONE source (2026-08-25). A source per road is a subject per road, and the
        // writer duly produced a sentence per road: four "X is clear" lines for one fact, the last
        // re-listing roads already named. Grouped, the roads carry a shared state and sweep.
        var goodRunUnits = units.Where(u => u.Field == "clear road").ToList();
        goodRunUnits.Count.ShouldBe(2);
        goodRunUnits.ShouldAllBe(u => u.SourceKey == "goodrun");
        goodRunUnits.Select(u => u.Value).ShouldBe(["Anzac Parade", "Southport Nerang Road"], ignoreOrder: true);
    }

    // ── Weather mapping ───────────────────────────────────────────────────

    [Fact]
    public void BuildEvidenceUnits_Weather_MapsConditionAndTemperatureFacts()
    {
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(CreateContext());

        var conditionUnit = units.Single(u => u.Id == "weather:condition");
        conditionUnit.Field.ShouldBe("condition");
        conditionUnit.SourceKey.ShouldBe("weather");
        // "Overcast" is naturalized to "cloudy" by ContentFormatters' phrasing table — the evidence
        // unit must carry the SAME naturalized word, not the raw HERE vocabulary.
        conditionUnit.Value.ShouldBe("cloudy");

        var temperatureUnit = units.Single(u => u.Id == "weather:temperature");
        temperatureUnit.Field.ShouldBe("temperature");
        temperatureUnit.Value.ShouldBe("9 degrees");
    }

    // ── Invariant: evidence never diverges from the prose the writer sees ─

    [Fact]
    public void BuildEvidenceUnits_Values_AppearVerbatimInProseRenderings()
    {
        var context = CreateContext();
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);

        string traffic = TrafficNarrativeSummarizer.Summarize(context);
        string weatherProse = ContentFormatters.FormatWeather(CreateWeather());
        string fuelProse = ContentFormatters.FormatFuel(CreateFuel());

        // A citation is only trustworthy if the evidence text is a literal substring of the SAME
        // prose block the model is shown — divergence here would mean the model could voice a fact
        // it never actually saw, or an evidence id could point at something the prose omits.
        var beerburrumAnchors = units
            .Where(u => u.SourceKey == "corridor:Beerburrum Range Road" && u.Field == "location")
            .Select(u => u.Value);
        foreach (string anchor in beerburrumAnchors)
            traffic.ShouldContain(anchor);

        traffic.ShouldContain(units.Single(u =>
            u.SourceKey == "corridor:Beerburrum Range Road" && u.Field == "detail").Value);
        traffic.ShouldContain(units.Single(u => u.Field == "extent").Value);

        weatherProse.ShouldContain(units.Single(u => u.Id == "weather:condition").Value);
        weatherProse.ShouldContain(units.Single(u => u.Id == "weather:temperature").Value);

        fuelProse.ShouldContain(units.Single(u => u.Id == "fuel:unleaded:price").Value);
        fuelProse.ShouldContain(units.Single(u => u.Id == "fuel:unleaded:suburb").Value);
    }

    [Fact]
    public void BuildEvidenceUnits_AbsentOptionalFacts_EmitNothing()
    {
        // An incident carrying none of the enrichment facts must leave the writer's data block
        // exactly as it was before this wave: absent means omitted, never a placeholder or a zero.
        // Asserted as absence of the new labels rather than a byte-comparison against hardcoded
        // prose, so the test cannot break on unrelated impact-wording changes.
        var incident = new TrafficIncident("i-absent", "crash", 1, "M4", "accident");
        var corridor = new TrafficCorridor(
            "M4", 1, 1, 2000, 80, "accident", "Multi-vehicle crash", [incident]);
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "absent-facts",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Corridors = [corridor],
            Incidents = [incident]
        };

        string summary = TrafficNarrativeSummarizer.Summarize(context);
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context);

        summary.ShouldNotContain("Lanes closed:");
        summary.ShouldNotContain("Total lanes:");
        summary.ShouldNotContain("Other affected asset");
        summary.ShouldNotContain("Reduced speed limit:");
        units.ShouldNotContain(u => u.Field == "lanesClosed" || u.Field == "lanesTotal"
            || u.Field == "otherAsset" || u.Field == "speedLimitKph");
    }

    [Fact]
    public void BuildEvidenceUnits_OtherAsset_IsSeparateAndCitable()
    {
        const string prose =
            "The southbound on-ramps to the Warringah Freeway will be closed. " +
            "The Sydney Harbour Tunnel will also be closed southbound during this time.";
        var official = new OfficialTrafficEvent(
            "livetraffic-nsw", prose, false, [],
            RoadName: "Warringah Freeway",
            Description: prose,
            Category: "SCHEDULED ROADWORK",
            SubCategory: "Warringah Freeway Upgrade",
            OtherAssets: ["Sydney Harbour Tunnel"]);
        var incident = OfficialIncidentNormalizer.ToIncident(official, DateTimeOffset.UtcNow);
        var corridor = new TrafficCorridor(
            "Warringah Freeway", 2, 2, 0, 80, "construction", prose, [incident],
            Sources: ["livetraffic-nsw"]);
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "other-asset",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Corridors = [corridor],
            Incidents = [incident]
        };

        string summary = TrafficNarrativeSummarizer.Summarize(context);
        var unit = TrafficNarrativeSummarizer.BuildEvidenceUnits(context)
            .Single(u => u.Field == "otherAsset");

        summary.ShouldContain("Other affected asset (separate from Warringah Freeway): Sydney Harbour Tunnel");
        unit.Value.ShouldBe("Sydney Harbour Tunnel");
        summary.ShouldContain(unit.Value);
    }

    [Fact]
    public void BuildEvidenceUnits_NswRoadworksLaneFacts_ReachWriterWithDistinctCounts()
    {
        var official = new OfficialTrafficEvent(
            "livetraffic-nsw",
            "The road has been reduced to a single lane in each direction 24/7 until approximately September 2026",
            false, [],
            RoadName: "Warringah Freeway",
            Category: "SCHEDULED ROADWORK",
            SubCategory: "Warringah Freeway Upgrade",
            LanesClosed: 1,
            LanesTotal: 2);
        var incident = OfficialIncidentNormalizer.ToIncident(official, DateTimeOffset.UtcNow);
        var corridor = new TrafficCorridor(
            "Warringah Freeway", 2, 2, 0, 80, "construction", official.Headline, [incident]);
        var context = new BroadcastWithTraffic
        {
            TargetDurationSeconds = 30,
            JobId = "lane-facts",
            Location = "Sydney",
            Traffic = "ignored fallback",
            Corridors = [corridor],
            Incidents = [incident]
        };

        string summary = TrafficNarrativeSummarizer.Summarize(context);
        var units = TrafficNarrativeSummarizer.BuildEvidenceUnits(context)
            .Where(u => u.Field is "lanesClosed" or "lanesTotal")
            .ToList();

        summary.ShouldContain("Lanes closed: 1");
        summary.ShouldContain("Total lanes: 2");
        units.ShouldContain(u => u.Field == "lanesClosed" && u.Value == "1");
        units.ShouldContain(u => u.Field == "lanesTotal" && u.Value == "2");
        units.ShouldAllBe(u => summary.Contains(u.Value, StringComparison.Ordinal));
    }
}

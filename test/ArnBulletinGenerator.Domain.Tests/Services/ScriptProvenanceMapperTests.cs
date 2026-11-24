using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Services;

public class ScriptProvenanceMapperTests
{
    private static TrafficCorridor Corridor(string roadName) =>
        new(roadName, Severity: 2, FunctionalClass: 1, TotalLengthMeters: 500, ImpactScore: 100,
            PrimaryType: "congestion", Description: "heavy", Incidents: []);

    private static FlowRoad Flow(string roadName) =>
        new(roadName, JamFactor: 1, LengthMeters: 1_000);

    [Fact]
    public void Map_AttributesSentenceToTheCorridorItNames()
    {
        var corridors = new[] { Corridor("M4 Western Motorway"), Corridor("Anzac Bridge") };
        var result = ScriptProvenanceMapper.Map(
            "The M4 Western Motorway is heavy citybound. Anzac Bridge is moving nicely.", corridors);

        result.Count.ShouldBe(2);
        result[0].CorridorKeys.ShouldBe(["M4 Western Motorway"]);
        result[1].CorridorKeys.ShouldBe(["Anzac Bridge"]);
    }

    [Fact]
    public void Map_AttributesMultipleCorridorsNamedInOneSentence()
    {
        var corridors = new[] { Corridor("M4 Western Motorway"), Corridor("Anzac Bridge") };
        var result = ScriptProvenanceMapper.Map(
            "The M4 Western Motorway and Anzac Bridge are both moving nicely.", corridors);

        result.Count.ShouldBe(1);
        result[0].CorridorKeys.ShouldBe(["M4 Western Motorway", "Anzac Bridge"]);
    }

    [Fact]
    public void Map_AttributesSentenceViaRoadNickname()
    {
        // Real regression: HERE geocodes both directional Tullamarine Fwy incidents under the
        // corridor's canonical RoadName "Tullamarine", but the script correctly used the
        // everyday "Tulla" instead — a plain substring match against "Tullamarine" missed it,
        // leaving the sentence's real corridor citation empty.
        var corridors = new[] { Corridor("Tullamarine") };
        var result = ScriptProvenanceMapper.Map(
            "The Tulla citybound between the airport and Mickleham Road is under construction.",
            corridors);

        result.Count.ShouldBe(1);
        result[0].CorridorKeys.ShouldBe(["Tullamarine"]);
    }

    [Fact]
    public void Map_CompoundSentence_LinksNicknamedCorridorAlongsideOthers()
    {
        // The exact shape of a reported false positive: a three-road sentence where only the
        // middle road is named by nickname. Before the fix, "Tullamarine" dropped out of
        // CorridorKeys entirely, so the operator console's claim checker compared the
        // sentence's "citybound" against West Gate/Western Freeway's directions instead of
        // Tullamarine's — and flagged a contradiction that didn't exist.
        var corridors = new[]
        {
            Corridor("West Gate"), Corridor("Tullamarine"), Corridor("Western Freeway"),
        };
        var result = ScriptProvenanceMapper.Map(
            "There's a slow patch on the West Gate, the Tulla citybound between the airport " +
            "and Mickleham Road, and the Western Freeway outbound at Hopkins Road.",
            corridors);

        result.Count.ShouldBe(1);
        result[0].CorridorKeys.ShouldBe(["West Gate", "Tullamarine", "Western Freeway"]);
    }

    [Fact]
    public void Map_AttributesSentenceWhenTheReadSplitsTheRoadNameWithAPreposition()
    {
        // Live regression, dev job 1e1db059: HERE named the corridor
        // "M1 GENERAL HOLMES DRIVE (WESTBOUND)" and the writer was handed its speakable form
        // "M1 General Holmes Drive". A global guidance rule requires the definite article on route
        // codes, so the read voiced "the M1 at General Holmes Drive" — legitimate, but the
        // contiguous substring match saw no citation, and the operator console showed the sentence
        // with no backing corridor.
        var corridors = new[] { Corridor("Banks Drive"), Corridor("M1 GENERAL HOLMES DRIVE (WESTBOUND)") };
        var result = ScriptProvenanceMapper.Map(
            "Banks Drive and the M1 at General Holmes Drive have minor delays too.", corridors);

        result.Count.ShouldBe(1);
        result[0].CorridorKeys.ShouldBe(["Banks Drive", "M1 GENERAL HOLMES DRIVE (WESTBOUND)"]);
    }

    [Fact]
    public void Map_DoesNotAttributeWhenTheNameWordsAreTooFarApart()
    {
        // The insertion allowance is for articles and prepositions, not for spelling a road name
        // out of common words scattered across a sentence. "Drive" and "Street" appear in most
        // bulletins; a corridor must not collect a citation it never earned.
        var corridors = new[] { Corridor("Holmes Drive") };
        var result = ScriptProvenanceMapper.Map(
            "Holmes is clear, but the usual queue is back on Anzac Parade and Wyndham Drive.",
            corridors);

        result[0].CorridorKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Map_MatchesWholeWordsOnly()
    {
        // A substring scan attributed "M4" to any sentence containing "M40"; the route codes are
        // different roads.
        var corridors = new[] { Corridor("M4") };
        var result = ScriptProvenanceMapper.Map("The M40 is heavy through the interchange.", corridors);

        result[0].CorridorKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Map_AttributesSentenceWhenTheReadContractsTheRoadName()
    {
        // The naturalness polish rewrites "The M4 is slow" as "The M4's slow", and the polish gate
        // reverts the whole rephrase when corridor lineage changes — so a possessive/contraction
        // clitic must not cost the road its citation, or every contraction would be reverted.
        var corridors = new[] { Corridor("M4"), Corridor("Victoria Road") };
        var result = ScriptProvenanceMapper.Map(
            "The M4's a touch slow citybound. Victoria Road's moving slowly.", corridors);

        result.Count.ShouldBe(2);
        result[0].CorridorKeys.ShouldBe(["M4"]);
        result[1].CorridorKeys.ShouldBe(["Victoria Road"]);
    }

    [Theory]
    // A read collapses a shared road type across an enumeration; the substring scan matched these
    // by accident, so a whole-word matcher has to allow the plural explicitly (job 50fd80e3).
    [InlineData("Wallgrove Road",
        "Camden Valley Way's got works at Beech Road, while Narellan, Showground and Wallgrove Roads are a bit slow.")]
    // HERE abbreviates the road type or a direction suffix; ToSpeakable expands it, and the
    // expanded form is what the read voices (jobs 17c0b28e, 482f38dd, c62b8610).
    [InlineData("Garfield Road E", "Garfield Road East is stopped solid too.")]
    [InlineData("Railway Ter", "Railway Terrace is blocked citybound.")]
    [InlineData("Sydney Harbour Brg",
        "Sydney Harbour Bridge is BLOCKED southbound between the tolls and Observatory Tunnel.")]
    // Split-name reads found across the live dev corpus: the road type or a cross-street is
    // separated from the rest of the name by a preposition (jobs 9e0c09e8, c3e1297f, 269da768).
    [InlineData("Botany Road", "Southern Cross Drive's a bit slow citybound at Botany Road.")]
    [InlineData("Deer Park Bypass",
        "Crawling on the Western Ring Road citybound at the Deer Park Bypass with heavy congestion.")]
    [InlineData("WEST GATE FREEWAY (WESTBOUND)", "Point Cook Road's closed westbound at the West Gate Freeway ramp.")]
    public void Map_AttributesRoadsNamedAcrossTheLiveDevCorpus(string roadName, string sentence)
    {
        // Measured on 97 live dev jobs: 24 citations across 19 jobs that the contiguous substring
        // scan dropped, each a road the read plainly names.
        var result = ScriptProvenanceMapper.Map(sentence, [Corridor(roadName)]);

        result[0].CorridorKeys.ShouldBe([roadName]);
    }

    [Fact]
    public void MentionedRoadNames_DoesNotAssembleANameAcrossSentences()
    {
        // The protected-roads list drives the polish prompt AND the polish gate. A name stitched
        // together from two sentences is not a road the read named, and telling the polisher to
        // preserve it verbatim would protect text that does not exist.
        var corridors = new[] { Corridor("Banks Drive") };

        ScriptProvenanceMapper.MentionedRoadNames("Banks is clear. Drive safely.", corridors)
            .ShouldBeEmpty();
        ScriptProvenanceMapper.MentionedRoadNames("Banks Drive is slow. Drive safely.", corridors)
            .ShouldBe(["Banks Drive"]);
    }

    [Fact]
    public void Map_SentenceNamingNoCorridor_HasNoKeys()
    {
        var corridors = new[] { Corridor("M4 Western Motorway") };
        var result = ScriptProvenanceMapper.Map(
            "The M4 Western Motorway is heavy. Clear skies and mild, sixteen degrees.", corridors);

        result.Count.ShouldBe(2);
        result[0].CorridorKeys.ShouldBe(["M4 Western Motorway"]);
        result[1].CorridorKeys.ShouldBeEmpty(); // weather line — no corridor lineage
    }

    [Fact]
    public void Map_NoCorridors_AllSentencesHaveNoKeys()
    {
        var result = ScriptProvenanceMapper.Map("Everything is clear. Drive safely.", []);
        result.Count.ShouldBe(2);
        result.ShouldAllBe(s => s.CorridorKeys.Count == 0);
    }

    private static MonitoredRouteStatus Route(string name, int driveMinutes = 33, int typicalMinutes = 26) =>
        new(name, new CoveragePoint(-33.8148, 151.0017), new CoveragePoint(-33.8688, 151.2093),
            driveMinutes, typicalMinutes, DistanceKm: 25.4);

    [Fact]
    public void Map_AttributesSentenceToTheMonitoredRouteItNames()
    {
        // Job cb5eadd4 (test, 2026-08-23): a correct, HERE-grounded drive-time claim reached the
        // operator console with no lineage because a monitored route is a ROUTE and can never
        // appear among the corridors. The M4-M5 Link corridor beside it is a different fact.
        var result = ScriptProvenanceMapper.Map(
            "The M4 from Parramatta to the city is a slower run, taking 33 minutes.",
            [Corridor("M4-M5 Link")],
            [Route("the M4 from Parramatta to the city")]);

        result.Count.ShouldBe(1);
        result[0].RouteKeys.ShouldBe(["the M4 from Parramatta to the city"]);
    }

    [Fact]
    public void Map_MonitoredRouteAndCorridorInOneScript_AreAttributedSeparately()
    {
        var result = ScriptProvenanceMapper.Map(
            "The M5 is heavily congested westbound through Padstow. "
            + "The M4 from Parramatta to the city is a slower run, taking 33 minutes.",
            [Corridor("M5")],
            [Route("the M4 from Parramatta to the city")]);

        result.Count.ShouldBe(2);
        result[0].CorridorKeys.ShouldBe(["M5"]);
        result[0].RouteKeys.ShouldBeEmpty();
        result[1].CorridorKeys.ShouldBeEmpty();
        result[1].RouteKeys.ShouldBe(["the M4 from Parramatta to the city"]);
    }

    [Fact]
    public void Map_ReadParaphrasesTheRouteAway_ClaimsNoRouteLineage()
    {
        // A lineage the operator cannot check is worse than none: only the configured name, voiced
        // as a whole phrase, counts.
        var result = ScriptProvenanceMapper.Map(
            "The motorway run into town is slower than usual.",
            [],
            [Route("the M4 from Parramatta to the city")]);

        result.Count.ShouldBe(1);
        result[0].RouteKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Map_NoCorridorsButAMonitoredRoute_StillAttributesTheRoute()
    {
        var result = ScriptProvenanceMapper.Map(
            "The Warringah Freeway into the city is running to time.",
            [],
            [Route("the Warringah Freeway into the city", driveMinutes: 12, typicalMinutes: 12)]);

        result.Count.ShouldBe(1);
        result[0].RouteKeys.ShouldBe(["the Warringah Freeway into the city"]);
    }

    [Fact]
    public void Map_AttributesSentenceToFlowOnlyRoads()
    {
        var result = ScriptProvenanceMapper.Map(
            "The M5 and M7 are running freely through the west.", [],
            flowRoads: [Flow("M5"), Flow("M7")]);

        result.Count.ShouldBe(1);
        result[0].CorridorKeys.ShouldBe(["M5", "M7"]);
        result[0].RouteKeys.ShouldBeEmpty();
    }

    [Fact]
    public void Map_ClassifiesSelectedGoodRunsPerSentence_AndLeavesSupplementsToTheWorkflow()
    {
        // Weather and fuel lineage is authored by the workflow that wrote the sentence
        // (DraftedSentence.Section) and merged at persistence; the mapper claims only roads.
        var result = ScriptProvenanceMapper.Map(
            "The M5 and M7 are running freely through the west. "
            + "Unleaded is a dollar ninety-three at Silverwater. "
            + "Clear and mild, twenty-four degrees.",
            [],
            flowRoads: [Flow("M5"), Flow("M7")],
            goodRunRoads: ["M5", "M7"]);

        result.Count.ShouldBe(3);
        result[0].CorridorKeys.ShouldBe(["M5", "M7"]);
        result[0].EvidenceIds.ShouldBe(["goodrun:M5", "goodrun:M7"]);
        result[1].EvidenceIds.ShouldBeEmpty();
        result[2].EvidenceIds.ShouldBeEmpty();
    }

    [Fact]
    public void Map_AttributesAFlowRoadAndMonitoredRouteInTheSameSentence()
    {
        const string routeName = "the Warringah Freeway into the city";
        var result = ScriptProvenanceMapper.Map(
            "The Warringah Freeway into the city is running to time.", [],
            [Route(routeName, driveMinutes: 12, typicalMinutes: 12)],
            [Flow("Warringah Freeway")]);

        result.Count.ShouldBe(1);
        result[0].CorridorKeys.ShouldBe(["Warringah Freeway"]);
        result[0].RouteKeys.ShouldBe([routeName]);
    }

    [Fact]
    public void MapRoadPhraseSpans_ReturnsTheCompleteCanonicalRoadPhrase()
    {
        const string script = "Traffic is slow on Gillies Highway this afternoon.";

        var spans = ScriptProvenanceMapper.MapRoadPhraseSpans(
            script,
            ["Gillies Highway"]);

        spans.ShouldHaveSingleItem();
        spans[0].RoadName.ShouldBe("Gillies Highway");
        spans[0].Text.ShouldBe("Gillies Highway");
        script[spans[0].Start..spans[0].End].ShouldBe("Gillies Highway");
    }

    [Fact]
    public void MapRoadPhraseSpans_UsesTheRenderedSpeakableRoadPhrase()
    {
        const string script = "Traffic is slow on Pacific Motorway this afternoon.";

        var spans = ScriptProvenanceMapper.MapRoadPhraseSpans(
            script,
            ["Pacific Mwy"]);

        spans.ShouldHaveSingleItem();
        spans[0].RoadName.ShouldBe("Pacific Mwy");
        spans[0].Text.ShouldBe("Pacific Motorway");
        script[spans[0].Start..spans[0].End].ShouldBe("Pacific Motorway");
    }

    [Fact]
    public void MapRoadPhraseSpans_RecognizesAConfiguredRoadNickname()
    {
        const string script = "The Tulla is slow this afternoon.";

        var spans = ScriptProvenanceMapper.MapRoadPhraseSpans(
            script,
            ["Tullamarine"]);

        spans.ShouldHaveSingleItem();
        spans[0].RoadName.ShouldBe("Tullamarine");
        spans[0].Text.ShouldBe("Tulla");
        script[spans[0].Start..spans[0].End].ShouldBe("Tulla");
    }

    [Fact]
    public void MapRoadPhraseSpans_StripsTrailingPossessiveOrContractionFromTargetText()
    {
        const string script = "Pittwater Road's closed northbound near North Manly.";

        var spans = ScriptProvenanceMapper.MapRoadPhraseSpans(
            script,
            ["Pittwater Road"]);

        spans.ShouldHaveSingleItem();
        spans[0].RoadName.ShouldBe("Pittwater Road");
        spans[0].Text.ShouldBe("Pittwater Road");
        script[spans[0].Start..spans[0].End].ShouldBe("Pittwater Road's");
    }

    [Fact]
    public void Map_EmptyScript_ReturnsEmpty()
    {
        ScriptProvenanceMapper.Map("", [Corridor("M4")]).ShouldBeEmpty();
    }
}

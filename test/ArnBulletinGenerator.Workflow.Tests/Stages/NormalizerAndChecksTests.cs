using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Workflow.Contracts;
using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

public sealed class NormalizerAndChecksTests
{
    [Fact]
    public void Evidence_units_form_events_in_first_source_key_appearance_order()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("a-1", "corridor:Alpha Road", "delayMinutes", "5"),
                new EvidenceUnit("b-1", "corridor:Beta Road", "delayMinutes", "7"),
                new EvidenceUnit("a-2", "corridor:Alpha Road", "impact", "slow")
            ],
            []);

        var normalized = Normalizer.Normalize(request, NewTrace());

        normalized.Events.Select(@event => @event.EventId)
            .ShouldBe(["corridor:Alpha Road", "corridor:Beta Road"]);
        normalized.Events.Count.ShouldBe(2);
    }

    [Fact]
    public void Facts_in_each_event_preserve_evidence_arrival_order()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("first", "corridor:Alpha Road", "impact", "slow"),
                new EvidenceUnit("second", "corridor:Alpha Road", "delayMinutes", "5"),
                new EvidenceUnit("third", "corridor:Alpha Road", "location", "junction")
            ],
            []);

        var normalized = Normalizer.Normalize(request, NewTrace());

        normalized.Events[0].Facts.Select(fact => fact.EvidenceId)
            .ShouldBe(["first", "second", "third"]);
    }

    [Fact]
    public void Corridor_ordinals_remain_separate_events_even_when_the_road_name_matches()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("south-1", "corridor:South Road#1", "delayMinutes", "8"),
                new EvidenceUnit("south-2", "corridor:South Road#2", "delayMinutes", "12")
            ],
            []);

        var normalized = Normalizer.Normalize(request, NewTrace());

        // Conflating these source identities is precisely how a claim gets bound to the wrong road.
        normalized.Events.Select(@event => @event.EventId)
            .ShouldBe(["corridor:South Road#1", "corridor:South Road#2"]);
        normalized.Events.Select(@event => @event.Facts.Single().EvidenceId)
            .ShouldBe(["south-1", "south-2"]);
    }

    [Fact]
    public void Source_keys_map_to_weather_fuel_corridor_and_unknown_event_kinds()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("w", "weather", "condition", "clear"),
                new EvidenceUnit("f", "fuel", "regular", "190"),
                new EvidenceUnit("c", "corridor:Alpha Road", "impact", "slow"),
                new EvidenceUnit("u", "news", "headline", "none")
            ],
            []);

        var normalized = Normalizer.Normalize(request, NewTrace());

        normalized.Events.Select(@event => @event.Kind)
            .ShouldBe([
                BulletinEventKind.Weather,
                BulletinEventKind.Fuel,
                BulletinEventKind.Corridor,
                BulletinEventKind.Unknown
            ]);
    }

    [Fact]
    public void Corridor_labels_include_the_road_name_and_present_suburb()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("road", "corridor:South Road#1", "delayMinutes", "8"),
                new EvidenceUnit("suburb", "corridor:South Road#1", "suburb", "Edwardstown")
            ],
            []);

        var normalized = Normalizer.Normalize(request, NewTrace());

        normalized.Events[0].Label.ShouldBe("South Road, Edwardstown");
    }

    [Fact]
    public void Required_sections_resolve_to_existing_event_ids_and_skip_missing_sections()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("road", "corridor:South Road", "impact", "slow"),
                new EvidenceUnit("weather", "weather", "condition", "clear")
            ],
            ["CORRIDOR:South ROAD", "missing", "weather"]);

        var normalized = Normalizer.Normalize(request, NewTrace());

        normalized.RequiredEventIds.ShouldBe(["corridor:South Road", "weather"]);
    }

    [Fact]
    public void Empty_evidence_normalizes_to_empty_input_without_throwing()
    {
        var request = new WorkflowRequest("job", "market", "station", "location", 30, [], []);

        var normalized = Normalizer.Normalize(request, NewTrace());

        normalized.Events.ShouldBeEmpty();
        normalized.RequiredEventIds.ShouldBeEmpty();
    }

    [Fact]
    public void Events_expose_all_evidence_ids_and_case_insensitive_fact_lookup()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("impact-1", "corridor:South Road", "Impact", "slow"),
                new EvidenceUnit("impact-2", "corridor:South Road", "IMPACT", "heavy")
            ],
            []);

        var eventItem = Normalizer.Normalize(request, NewTrace()).Events[0];

        eventItem.EvidenceIds.ShouldBe(["impact-1", "impact-2"]);
        eventItem.Fact("impact")!.EvidenceId.ShouldBe("impact-1");
        eventItem.Has("ImPaCt").ShouldBeTrue();
        eventItem.AllFacts("impact").Count.ShouldBe(2);
    }

    [Fact]
    public void A_clean_read_returns_every_check_result_and_all_checks_pass()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("road-fact", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan(
            [new FittedStory("traffic-story", ["corridor:South Road"], StoryRole.Traffic, ["road-fact"], ["road-fact"], 5)],
            5,
            []);
        var read = new ComposedRead([
            new ComposedSentence(1, "South Road traffic remains slow", ["road-fact"], "traffic-story")
        ]);

        var results = HardChecks.Run(request, input, plan, read, NewTrace());

        results.Count.ShouldBe(15);
        results.Select(result => result.Name).ShouldBe([
            "CitationExists",
            "CitationAuthorised",
            "MandatoryContentPresent",
            "NoStoryDropped",
            "RunningOrderFollowsPlan",
            // Replaced NoDuplicateRoad, which flagged a road named once as the affected road and
            // again as another event's cross-street. A road name is not a fact.
            "NoDuplicateClaim",
            "NoUnsupportedScopeClaim",
            "NoRepeatedDirection",
            // Closes the shrinking treadmill: a fact the fitter spent budget on must reach the read.
            "SelectedFactCoverage",
            "SentenceLength",
            "UnspokenPunctuation",
            "NoAdvice",
            "NoStationBookend",
            "DelayFigureOnce",
            "WordTargetBand"
        ]);
        results.All(result => result.Passed).ShouldBeTrue();
    }

    [Fact]
    public void Quiet_board_scope_must_stay_on_named_roads()
    {
        var request = new WorkflowRequest(
            "job",
            "sydney",
            "station",
            "Sydney",
            30,
            [
                new EvidenceUnit("m5", "goodrun:M5", "road", "M5"),
                new EvidenceUnit("m7", "goodrun:M7", "road", "M7")
            ],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = FittedPlan.Empty;
        var broadRead = new ComposedRead([
            new ComposedSentence(
                1,
                "A clean run on the M5 through Moorebank and the M7 at Len Waters Estate, with only minor movement elsewhere",
                ["m5", "m7"])
        ]);
        var namedRoadRead = new ComposedRead([
            new ComposedSentence(1, "The M5 and M7 are running clean", ["m5", "m7"])
        ]);

        var broadResult = HardChecks.Run(request, input, plan, broadRead, NewTrace())
            .Single(check => check.Name == "NoUnsupportedScopeClaim");
        var namedRoadResult = HardChecks.Run(request, input, plan, namedRoadRead, NewTrace())
            .Single(check => check.Name == "NoUnsupportedScopeClaim");

        broadResult.Passed.ShouldBeFalse();
        broadResult.Detail.ShouldContain("elsewhere");
        namedRoadResult.Passed.ShouldBeTrue();
    }

    [Fact]
    public void Repeated_direction_token_fails_but_distinct_directions_pass()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("church", "corridor:Church Street", "road", "Church Street")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = FittedPlan.Empty;
        var repeatedRead = new ComposedRead([
            new ComposedSentence(1, "Northbound Church Street has roadworks northbound", ["church"])
        ]);
        var distinctRead = new ComposedRead([
            new ComposedSentence(1, "Northbound Church Street has roadworks, while southbound traffic is clear", ["church"])
        ]);

        var repeatedResult = HardChecks.Run(request, input, plan, repeatedRead, NewTrace())
            .Single(check => check.Name == "NoRepeatedDirection");
        var distinctResult = HardChecks.Run(request, input, plan, distinctRead, NewTrace())
            .Single(check => check.Name == "NoRepeatedDirection");

        repeatedResult.Passed.ShouldBeFalse();
        repeatedResult.Detail.ShouldContain("direction token");
        distinctResult.Passed.ShouldBeTrue();
    }

    [Fact]
    public void Citation_exists_fails_and_names_an_unknown_evidence_id()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("known", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan([], 0, []);
        var read = new ComposedRead([
            new ComposedSentence(1, "South Road traffic is slow", ["missing-evidence"])
        ]);

        var result = HardChecks.Run(request, input, plan, read, NewTrace())
            .Single(check => check.Name == "CitationExists");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("missing-evidence");
    }

    [Fact]
    public void Citation_authorised_fails_when_a_story_cites_evidence_it_was_not_granted()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("granted", "corridor:South Road", "impact", "slow"),
                new EvidenceUnit("not-granted", "corridor:South Road", "delayMinutes", "5")
            ],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan(
            [new FittedStory("traffic-story", ["corridor:South Road"], StoryRole.Traffic, ["granted"], ["granted"], 4)],
            0,
            []);
        var read = new ComposedRead([
            new ComposedSentence(1, "South Road traffic is slow", ["not-granted"], "traffic-story")
        ]);

        var result = HardChecks.Run(request, input, plan, read, NewTrace())
            .Single(check => check.Name == "CitationAuthorised");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("not-granted");
    }

    [Fact]
    public void Mandatory_content_fails_when_a_fitted_weather_story_is_not_cited()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [
                new EvidenceUnit("road", "corridor:South Road", "impact", "slow"),
                new EvidenceUnit("weather", "weather", "condition", "clear")
            ],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan(
            [
                new FittedStory("traffic-story", ["corridor:South Road"], StoryRole.Traffic, ["road"], ["road"], 4),
                new FittedStory("weather-story", ["weather"], StoryRole.Weather, ["weather"], ["weather"], 4)
            ],
            0,
            []);
        var read = new ComposedRead([
            new ComposedSentence(1, "South Road traffic is slow", ["road"], "traffic-story")
        ]);

        var result = HardChecks.Run(request, input, plan, read, NewTrace())
            .Single(check => check.Name == "MandatoryContentPresent");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("weather-story");
    }

    [Fact]
    public void No_story_dropped_fails_when_a_fitted_story_has_no_realising_sentence()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("road", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan(
            [new FittedStory("bought-story", ["corridor:South Road"], StoryRole.Traffic, ["road"], ["road"], 4)],
            0,
            []);
        var read = new ComposedRead([
            new ComposedSentence(1, "South Road traffic is slow", ["road"])
        ]);

        var result = HardChecks.Run(request, input, plan, read, NewTrace())
            .Single(check => check.Name == "NoStoryDropped");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("bought-story");
    }

    [Fact]
    public void Sentence_length_fails_and_reports_the_count_when_a_sentence_exceeds_the_limit()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("road", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan([], 0, []);
        var read = new ComposedRead([
            new ComposedSentence(
                1,
                "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty twentyone twentytwo twentythree twentyfour twentyfive",
                ["road"])
        ]);

        var result = HardChecks.Run(request, input, plan, read, NewTrace())
            .Single(check => check.Name == "SentenceLength");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("25 words");
    }

    [Fact]
    public void Unspoken_punctuation_fails_when_a_sentence_contains_a_semicolon_or_colon()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("road", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan([], 0, []);
        var read = new ComposedRead([
            new ComposedSentence(1, "South Road is slow; traffic is building", ["road"])
        ]);

        var result = HardChecks.Run(request, input, plan, read, NewTrace())
            .Single(check => check.Name == "UnspokenPunctuation");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain(";");
    }

    [Fact]
    public void No_advice_fails_for_advice_but_passes_when_the_read_only_states_conditions()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("road", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan([], 0, []);
        var adviceRead = new ComposedRead([
            new ComposedSentence(1, "South Road is slow, so allow extra time", ["road"])
        ]);
        var conditionsRead = new ComposedRead([
            new ComposedSentence(1, "South Road traffic is slow", ["road"])
        ]);

        var adviceResult = HardChecks.Run(request, input, plan, adviceRead, NewTrace())
            .Single(check => check.Name == "NoAdvice");
        var conditionsResult = HardChecks.Run(request, input, plan, conditionsRead, NewTrace())
            .Single(check => check.Name == "NoAdvice");

        adviceResult.Passed.ShouldBeFalse();
        conditionsResult.Passed.ShouldBeTrue();
    }

    [Fact]
    public void No_station_bookend_fails_for_deterministic_opening_or_sign_off_wording()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("road", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan([], 0, []);
        var poweredByRead = new ComposedRead([
            new ComposedSentence(1, "Traffic update powered by station", ["road"])
        ]);
        var openingRead = new ComposedRead([
            new ComposedSentence(1, "Now looking over South Road traffic", ["road"])
        ]);

        // These are added deterministically around the read, so a composer writing one duplicates the sign-off.
        var poweredByResult = HardChecks.Run(request, input, plan, poweredByRead, NewTrace())
            .Single(check => check.Name == "NoStationBookend");
        var openingResult = HardChecks.Run(request, input, plan, openingRead, NewTrace())
            .Single(check => check.Name == "NoStationBookend");

        poweredByResult.Passed.ShouldBeFalse();
        openingResult.Passed.ShouldBeFalse();
    }

    [Fact]
    public void Delay_figure_once_fails_for_two_figures_but_passes_for_one()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("road", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan([], 0, []);
        var repeatedRead = new ComposedRead([
            new ComposedSentence(1, "South Road has 5 minutes delay, then another 10 minutes delay", ["road"])
        ]);
        var singleRead = new ComposedRead([
            new ComposedSentence(1, "South Road has 5 minutes delay", ["road"])
        ]);

        var repeatedResult = HardChecks.Run(request, input, plan, repeatedRead, NewTrace())
            .Single(check => check.Name == "DelayFigureOnce");
        var singleResult = HardChecks.Run(request, input, plan, singleRead, NewTrace())
            .Single(check => check.Name == "DelayFigureOnce");

        repeatedResult.Passed.ShouldBeFalse();
        singleResult.Passed.ShouldBeTrue();
    }

    [Fact]
    public void Empty_composed_read_returns_one_focused_failing_result()
    {
        var request = new WorkflowRequest("job", "market", "station", "location", 30, [], []);
        var input = Normalizer.Normalize(request, NewTrace());
        var plan = new FittedPlan([], 0, []);

        var results = HardChecks.Run(request, input, plan, ComposedRead.Empty, NewTrace());

        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("ComposedRead");
        results[0].Passed.ShouldBeFalse();
    }

    [Fact]
    public void Word_target_band_reports_measured_and_target_counts_for_pass_and_fail_and_skips_zero_target()
    {
        var request = new WorkflowRequest(
            "job",
            "market",
            "station",
            "location",
            30,
            [new EvidenceUnit("road", "corridor:South Road", "impact", "slow")],
            []);
        var input = Normalizer.Normalize(request, NewTrace());
        var passingPlan = new FittedPlan([], 4, []);
        var passingRead = new ComposedRead([
            new ComposedSentence(1, "South Road is slow", ["road"])
        ]);
        var failingPlan = new FittedPlan([], 4, []);
        var failingRead = new ComposedRead([
            new ComposedSentence(1, "South Road traffic", ["road"])
        ]);
        var zeroTargetPlan = new FittedPlan([], 0, []);

        var passingResult = HardChecks.Run(request, input, passingPlan, passingRead, NewTrace())
            .Single(check => check.Name == "WordTargetBand");
        var failingResult = HardChecks.Run(request, input, failingPlan, failingRead, NewTrace())
            .Single(check => check.Name == "WordTargetBand");
        var zeroTargetResults = HardChecks.Run(request, input, zeroTargetPlan, passingRead, NewTrace());

        passingResult.Passed.ShouldBeTrue();
        passingResult.Detail.ShouldContain("Measured 4 words");
        passingResult.Detail.ShouldContain("target of 4 words");
        failingResult.Passed.ShouldBeFalse();
        failingResult.Detail.ShouldContain("Measured 3 words");
        failingResult.Detail.ShouldContain("target of 4 words");
        zeroTargetResults.ShouldNotContain(result => result.Name == "WordTargetBand");
    }


    [Fact]
    public void A_station_required_fixture_the_planner_never_planned_still_fails()
    {
        // Live on 2026-08-26 the planner produced four traffic stories and no weather or fuel story
        // for melbourne. The check then validated the plan against ITSELF, found no fixture story to
        // look for, and reported PASS while the read went to air with neither fixture. A missing
        // fixture is a breach whether or not anything planned it, so the check answers to the
        // request contract instead.
        var request = new WorkflowRequest(
            "job", "melbourne", "KIIS 101.1", "Melbourne", 30,
            [
                new EvidenceUnit("road", "corridor:Plenty Road", "impact", "slow"),
                new EvidenceUnit("weather:condition", "weather", "condition", "Sunny"),
                new EvidenceUnit("weather:temperature", "weather", "temperature", "15 degrees")
            ],
            ["weather"]);
        var input = Normalizer.Normalize(request, NewTrace());

        // A plan with traffic only, exactly as the live planner emitted it.
        var plan = new FittedPlan(
            [new FittedStory("traffic", ["corridor:Plenty Road"], StoryRole.Traffic, ["road"], ["road"], 6)],
            6,
            []);
        var read = new ComposedRead([
            new ComposedSentence(1, "Plenty Road is slow", ["road"], "traffic")
        ]);

        var result = HardChecks.Run(request, input, plan, read, NewTrace())
            .Single(check => check.Name == "MandatoryContentPresent");

        result.Passed.ShouldBeFalse();
        result.Detail.ShouldContain("weather");
    }

    private static TraceRecorder NewTrace() =>
        new("run", "wf", "desc", "job", "market", "station", 30.0);
}

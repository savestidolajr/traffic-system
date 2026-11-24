using ArnBulletinGenerator.Workflow.Model;
using ArnBulletinGenerator.Workflow.Stages;
using ArnBulletinGenerator.Workflow.Trace;
using Shouldly;
using Xunit;

namespace ArnBulletinGenerator.Workflow.Tests.Stages;

public sealed class FitterTests
{
    [Fact]
    public void Fitter_preserves_the_plan_order_when_priorities_would_prefer_a_different_order()
    {
        static BulletinEvent Event(string id) => new(
            id,
            BulletinEventKind.Corridor,
            $"{id} Road",
            [new EventFact($"{id}:detail", "detail", "slow")]);

        static PlannedStory Story(string id, int priority) => new(
            id,
            [id],
            StoryRole.Traffic,
            priority,
            [$"{id}:detail"],
            []);

        var first = Event("first");
        var second = Event("second");
        var third = Event("third");
        var plan = new BulletinPlan([
            Story("first", 1),
            Story("second", 100),
            Story("third", 50)]);
        var input = new NormalizedInput([first, second, third], []);
        var storyWords = WordEstimator.EstimateStoryWords([second], ["second:detail"]);

        // The plan is the composer's running order; fitting may subtract stories but must never reorder them.
        var fitted = Fitter.Fit(
            plan,
            input,
            storyWords * 2,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Stories.Select(story => story.StoryId).ShouldBe(["second", "third"]);
    }

    [Fact]
    public void Fitter_keeps_every_story_and_marks_every_story_kept_when_everything_fits()
    {
        static BulletinEvent Event(string id) => new(
            id,
            BulletinEventKind.Corridor,
            $"{id} Road",
            [new EventFact($"{id}:detail", "detail", "slow")]);

        static PlannedStory Story(string id) => new(
            id,
            [id],
            StoryRole.Traffic,
            10,
            [$"{id}:detail"],
            []);

        var events = new[] { Event("alpha"), Event("beta") };
        var plan = new BulletinPlan([Story("alpha"), Story("beta")]);
        var input = new NormalizedInput(events, []);
        var target = plan.Stories.Sum(story =>
            WordEstimator.EstimateStoryWords([events.Single(e => e.EventId == story.EventIds[0])], story.RequiredFacts));

        var fitted = Fitter.Fit(
            plan,
            input,
            target,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Stories.Count.ShouldBe(2);
        fitted.Decisions.Count.ShouldBe(2);
        fitted.Decisions.ShouldAllBe(decision => decision.Kept);
    }

    [Fact]
    public void Fitter_drops_a_low_priority_story_whole_and_keeps_all_required_facts_of_the_survivor()
    {
        static BulletinEvent Event(string id) => new(
            id,
            BulletinEventKind.Corridor,
            $"{id} Road",
            [
                new EventFact($"{id}:location", "location", "at the intersection"),
                new EventFact($"{id}:detail", "detail", "moving slowly"),
            ]);

        static PlannedStory Story(string id, int priority) => new(
            id,
            [id],
            StoryRole.Traffic,
            priority,
            [$"{id}:location", $"{id}:detail"],
            []);

        var low = Event("low");
        var high = Event("high");
        var plan = new BulletinPlan([Story("low", 1), Story("high", 99)]);
        var input = new NormalizedInput([low, high], []);
        var highWords = WordEstimator.EstimateStoryWords([high], plan.Stories[1].RequiredFacts);

        var fitted = Fitter.Fit(
            plan,
            input,
            highWords,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Stories.Select(story => story.StoryId).ShouldBe(["high"]);
        fitted.Stories[0].EvidenceIds.ShouldContain("high:location");
        fitted.Stories[0].EvidenceIds.ShouldContain("high:detail");
        fitted.Decisions.Single(decision => decision.Subject == "low").Kept.ShouldBeFalse();
    }

    [Theory]
    [InlineData("weather", StoryRole.Weather, BulletinEventKind.Weather)]
    [InlineData("fuel", StoryRole.Fuel, BulletinEventKind.Fuel)]
    public void Fitter_drops_optional_weather_and_fuel_when_the_budget_is_tight(
        string supplementId,
        StoryRole supplementRole,
        BulletinEventKind supplementKind)
    {
        var supplement = new BulletinEvent(
            supplementId,
            supplementKind,
            supplementId,
            [new EventFact($"{supplementId}:detail", "detail", "present")]);
        var traffic = new BulletinEvent(
            "traffic",
            BulletinEventKind.Corridor,
            "Traffic Road",
            [new EventFact("traffic:detail", "detail", "present")]);
        var supplementStory = new PlannedStory(
            supplementId,
            [supplementId],
            supplementRole,
            1,
            [$"{supplementId}:detail"],
            []);
        var trafficStory = new PlannedStory(
            "traffic",
            ["traffic"],
            StoryRole.Traffic,
            100,
            ["traffic:detail"],
            []);
        var trafficWords = WordEstimator.EstimateStoryWords([traffic], trafficStory.RequiredFacts);

        var fitted = Fitter.Fit(
            new BulletinPlan([supplementStory, trafficStory]),
            new NormalizedInput([supplement, traffic], ["traffic"]),
            trafficWords,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Stories.Select(story => story.StoryId).ShouldBe(["traffic"]);
        fitted.Decisions.Single(decision => decision.Subject == supplementId).Kept.ShouldBeFalse();
    }

    [Theory]
    [InlineData("weather", StoryRole.Weather, BulletinEventKind.Weather)]
    [InlineData("fuel", StoryRole.Fuel, BulletinEventKind.Fuel)]
    public void Fitter_keeps_required_weather_and_fuel_when_the_target_is_too_small(
        string supplementId,
        StoryRole supplementRole,
        BulletinEventKind supplementKind)
    {
        var supplement = new BulletinEvent(
            supplementId,
            supplementKind,
            supplementId,
            [new EventFact($"{supplementId}:detail", "detail", "present")]);
        var story = new PlannedStory(
            supplementId,
            [supplementId],
            supplementRole,
            1,
            [$"{supplementId}:detail"],
            []);

        var fitted = Fitter.Fit(
            new BulletinPlan([story]),
            new NormalizedInput([supplement], [supplementId]),
            0,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Stories.Select(candidate => candidate.StoryId).ShouldBe([supplementId]);
        var decision = fitted.Decisions.Single(candidate => candidate.Subject == supplementId);
        decision.Kept.ShouldBeTrue();
        decision.Reason.ShouldContain("mandatory");
        decision.Reason.ShouldContain("overshoot");
    }

    [Fact]
    public void Fitter_buys_optional_facts_in_priority_order_and_only_cites_purchased_details()
    {
        static BulletinEvent Event() => new(
            "road",
            BulletinEventKind.Corridor,
            "Main Road",
            [
                new EventFact("road:required", "detail", "slow"),
                new EventFact("road:high", "detail", "queues"),
                new EventFact("road:low", "detail", "delays"),
            ]);

        var road = Event();
        var story = new PlannedStory(
            "road-story",
            ["road"],
            StoryRole.Traffic,
            10,
            ["road:required"],
            [
                new OptionalFact(["road:high"], 2, 100),
                new OptionalFact(["road:low"], 2, 1),
            ]);
        var plan = new BulletinPlan([story]);
        var baseWords = WordEstimator.EstimateStoryWords([road], story.RequiredFacts);

        var fitted = Fitter.Fit(
            plan,
            new NormalizedInput([road], []),
            baseWords + 2,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        var fittedStory = fitted.Stories.Single();

        // Purchase is recorded in MustExpress: those are the facts the budget was spent on and the
        // composer is obliged to voice. Authorized is deliberately wider - it also carries facts the
        // fitter did NOT buy, so the composer may cite a suburb it naturally mentions without the
        // fitter having priced it. Asserting purchase against the wider set would pass vacuously.
        fittedStory.MustExpressEvidenceIds.ShouldContain("road:required");
        fittedStory.MustExpressEvidenceIds.ShouldContain("road:high");
        fittedStory.MustExpressEvidenceIds.ShouldNotContain("road:low");
        fittedStory.AuthorizedEvidenceIds.ShouldContain("road:low");
        fitted.Decisions.Single(decision => decision.Subject.Contains("optional 1")).Kept.ShouldBeTrue();
        fitted.Decisions.Single(decision => decision.Subject.Contains("optional 2")).Kept.ShouldBeFalse();
    }

    [Fact]
    public void Fitter_gives_every_considered_story_and_optional_fact_a_non_empty_reason()
    {
        static BulletinEvent Event(string id) => new(
            id,
            BulletinEventKind.Corridor,
            $"{id} Road",
            [new EventFact($"{id}:detail", "detail", "slow")]);

        static PlannedStory Story(string id) => new(
            id,
            [id],
            StoryRole.Traffic,
            10,
            [$"{id}:detail"],
            [new OptionalFact([$"{id}:optional"], 1, 1)]);

        var alpha = Event("alpha");
        var beta = Event("beta");
        var plan = new BulletinPlan([Story("alpha"), Story("beta")]);
        var fitted = Fitter.Fit(
            plan,
            new NormalizedInput([alpha, beta], []),
            100,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Decisions.Count.ShouldBe(4);
        fitted.Decisions.Select(decision => decision.Subject).ShouldContain("alpha");
        fitted.Decisions.Select(decision => decision.Subject).ShouldContain("beta");
        foreach (var decision in fitted.Decisions)
        {
            string.IsNullOrWhiteSpace(decision.Reason).ShouldBeFalse();
        }
    }

    [Fact]
    public void Fitter_empty_plan_preserves_the_requested_target_and_has_no_decisions()
    {
        var fitted = Fitter.Fit(
            BulletinPlan.Empty,
            new NormalizedInput([], []),
            123,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Stories.ShouldBeEmpty();
        fitted.TargetWords.ShouldBe(123);
        fitted.Decisions.ShouldBeEmpty();
    }

    [Fact]
    public void Fitter_drops_a_story_with_a_missing_event_and_names_that_event_in_the_decision()
    {
        var story = new PlannedStory(
            "orphan-story",
            ["missing-event"],
            StoryRole.Traffic,
            100,
            ["missing-event:detail"],
            []);
        var fitted = Fitter.Fit(
            new BulletinPlan([story]),
            new NormalizedInput([], []),
            100,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Stories.ShouldBeEmpty();
        var decision = fitted.Decisions.Single();
        decision.Subject.ShouldBe("orphan-story");
        decision.Kept.ShouldBeFalse();
        decision.Reason.ShouldContain("missing-event");
    }

    [Fact]
    public void Fitter_with_a_zero_target_keeps_only_mandatory_content()
    {
        static BulletinEvent Event(string id, BulletinEventKind kind = BulletinEventKind.Corridor) => new(
            id,
            kind,
            id,
            [new EventFact($"{id}:detail", "detail", "present")]);

        static PlannedStory Story(string id, StoryRole role) => new(
            id,
            [id],
            role,
            10,
            [$"{id}:detail"],
            []);

        var weather = Event("weather", BulletinEventKind.Weather);
        var required = Event("required");
        var optional = Event("optional");
        var plan = new BulletinPlan([
            Story("weather", StoryRole.Weather),
            Story("required", StoryRole.Traffic),
            Story("optional", StoryRole.Traffic)]);

        var fitted = Fitter.Fit(
            plan,
            new NormalizedInput([weather, required, optional], ["required"]),
            0,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        fitted.Stories.Select(story => story.StoryId).ShouldBe(["required"]);
        fitted.Decisions.Single(decision => decision.Subject == "optional").Kept.ShouldBeFalse();
    }

    [Fact]
    public void Refit_at_the_same_target_preserves_the_original_surviving_story_ids()
    {
        static BulletinEvent Event(string id) => new(
            id,
            BulletinEventKind.Corridor,
            $"{id} Road",
            [new EventFact($"{id}:detail", "detail", "slow")]);

        static PlannedStory Story(string id, int priority) => new(
            id,
            [id],
            StoryRole.Traffic,
            priority,
            [$"{id}:detail"],
            []);

        var low = Event("low");
        var high = Event("high");
        var plan = new BulletinPlan([Story("low", 1), Story("high", 100)]);
        var input = new NormalizedInput([low, high], []);
        var target = WordEstimator.EstimateStoryWords([high], ["high:detail"]);
        var first = Fitter.Fit(
            plan,
            input,
            target,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));
        var refit = Fitter.Refit(
            first,
            plan,
            input,
            target,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        refit.Stories.Select(story => story.StoryId).ShouldBe(first.Stories.Select(story => story.StoryId));
    }

    [Fact]
    public void Refit_at_a_smaller_target_drops_content_but_keeps_a_required_fixture_and_explains_the_change()
    {
        var weather = new BulletinEvent(
            "weather",
            BulletinEventKind.Weather,
            "Weather",
            [new EventFact("weather:condition", "condition", "sunny")]);
        var traffic = new BulletinEvent(
            "traffic",
            BulletinEventKind.Corridor,
            "Traffic Road",
            [new EventFact("traffic:detail", "detail", "slow")]);
        var plan = new BulletinPlan([
            new PlannedStory("weather", ["weather"], StoryRole.Weather, 1, ["weather:condition"], []),
            new PlannedStory("traffic", ["traffic"], StoryRole.Traffic, 100, ["traffic:detail"], [])]);
        var input = new NormalizedInput([weather, traffic], ["weather"]);
        var fullTarget = WordEstimator.EstimateStoryWords([weather], ["weather:condition"])
            + WordEstimator.EstimateStoryWords([traffic], ["traffic:detail"]);
        var first = Fitter.Fit(
            plan,
            input,
            fullTarget,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        var refit = Fitter.Refit(
            first,
            plan,
            input,
            0,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        refit.Stories.Select(story => story.StoryId).ShouldBe(["weather"]);
        var trafficDecision = refit.Decisions.Single(decision => decision.Subject == "traffic");
        trafficDecision.Kept.ShouldBeFalse();
        trafficDecision.Reason.ShouldContain("dropped on refit");
        trafficDecision.Reason.ShouldContain("target fell");
    }

    [Fact]
    public void Refit_at_a_larger_target_keeps_every_story_previously_kept()
    {
        static BulletinEvent Event(string id) => new(
            id,
            BulletinEventKind.Corridor,
            $"{id} Road",
            [new EventFact($"{id}:detail", "detail", "slow")]);

        static PlannedStory Story(string id, int priority) => new(
            id,
            [id],
            StoryRole.Traffic,
            priority,
            [$"{id}:detail"],
            []);

        var firstEvent = Event("first");
        var secondEvent = Event("second");
        var plan = new BulletinPlan([Story("first", 100), Story("second", 1)]);
        var input = new NormalizedInput([firstEvent, secondEvent], []);
        var oneStoryWords = WordEstimator.EstimateStoryWords([firstEvent], ["first:detail"]);
        var twoStoryWords = oneStoryWords + WordEstimator.EstimateStoryWords([secondEvent], ["second:detail"]);
        var first = Fitter.Fit(
            plan,
            input,
            oneStoryWords,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        var refit = Fitter.Refit(
            first,
            plan,
            input,
            twoStoryWords,
            new TraceRecorder("run", "wf", "desc", "job", "market", "station", 30.0));

        foreach (var story in first.Stories)
        {
            refit.Stories.Select(candidate => candidate.StoryId).ShouldContain(story.StoryId);
        }
        refit.Stories.Select(story => story.StoryId).ShouldBe(["first", "second"]);
    }

    [Fact]
    public void EstimateWords_counts_whitespace_delimited_words_and_keeps_a_hyphenated_compound_together()
    {
        WordEstimator.EstimateWords("South-Road remains slow")
            .ShouldBe(3);
        WordEstimator.EstimateWords("South Road remains slow")
            .ShouldBe(4);
    }

    [Fact]
    public void EstimateWords_returns_zero_for_empty_and_whitespace_only_input()
    {
        WordEstimator.EstimateWords("").ShouldBe(0);
        WordEstimator.EstimateWords(" \t\n ").ShouldBe(0);
    }

    [Fact]
    public void EstimateFactWords_is_positive_for_each_non_empty_fact()
    {
        var facts = new[]
        {
            new EventFact("known", "detail", "slow"),
            new EventFact("unknown", "unpriced-field", "available"),
            new EventFact("long", "location", "a very long location description"),
        };

        foreach (var fact in facts)
        {
            WordEstimator.EstimateFactWords(fact).ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void EstimateFactWords_prices_a_long_value_above_a_short_value_and_never_below_its_words()
    {
        var shortFact = new EventFact("short", "detail", "slow road");
        var longFact = new EventFact("long", "detail", "slow traffic queues are building along the road");

        var shortCost = WordEstimator.EstimateFactWords(shortFact);
        var longCost = WordEstimator.EstimateFactWords(longFact);

        longCost.ShouldBeGreaterThan(shortCost);
        longCost.ShouldBeGreaterThanOrEqualTo(WordEstimator.EstimateWords(longFact.Value));
    }

    [Fact]
    public void EstimateStoryWords_grows_when_evidence_ids_are_added_and_is_positive_without_optional_facts()
    {
        var bulletinEvent = new BulletinEvent(
            "road",
            BulletinEventKind.Corridor,
            "Main Road",
            [
                new EventFact("road:detail", "detail", "slow"),
                new EventFact("road:location", "location", "near the station"),
            ]);

        var noFacts = WordEstimator.EstimateStoryWords([bulletinEvent], []);
        var oneFact = WordEstimator.EstimateStoryWords([bulletinEvent], ["road:detail"]);
        var bothFacts = WordEstimator.EstimateStoryWords(
            [bulletinEvent],
            ["road:detail", "road:location"]);

        noFacts.ShouldBeGreaterThan(0);
        oneFact.ShouldBeGreaterThan(noFacts);
        bothFacts.ShouldBeGreaterThan(oneFact);
    }

    [Fact]
    public void FieldCosts_is_non_empty_and_every_calibrated_cost_is_positive()
    {
        WordEstimator.FieldCosts.Count.ShouldBeGreaterThan(0);
        WordEstimator.FieldCosts.Values.All(cost => cost > 0).ShouldBeTrue();
    }
}

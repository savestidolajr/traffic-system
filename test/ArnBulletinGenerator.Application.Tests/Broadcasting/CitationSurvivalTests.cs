using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

public sealed class CitationSurvivalTests
{
    [Fact]
    public void MechanicalSuburbRetentionRepair_preserves_every_sentence_citation()
    {
        var context = Context(new TrafficCorridor(
            "M4", 2, 1, 1000, 40, "congestion", "Slow traffic", [], Option<string>.Some("Parramatta")));
        const string before = "The M4 is slow at Church Street.";
        string after = SuburbRetentionDetector.Repair(before, context);
        var drafts = new[]
        {
            new DraftedSentence(1, before, ["corridor:m4"])
        };
        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(before, after, drafts)!;

        preserved.Count.ShouldBe(1);
        preserved[0].Text.ShouldBe("The M4 near Parramatta is slow at Church Street.");
        preserved[0].EvidenceIds.ShouldBe(["corridor:m4"]);
    }

    [Fact]
    public void WordCeilingTrim_drops_last_sentence_and_keeps_survivor_citations()
    {
        const string before = "The M4 is slow near Parramatta. The M5 is clear near Liverpool. Cloudy, twelve degrees today.";
        var drafts = new[]
        {
            new DraftedSentence(1, "The M4 is slow near Parramatta.", ["corridor:m4"]),
            new DraftedSentence(2, "The M5 is clear near Liverpool.", ["corridor:m5"]),
            new DraftedSentence(3, "Cloudy, twelve degrees today.", ["weather:condition"]) { Section = BulletinSegment.Weather }
        };
        string after = ScriptWordCeiling.TrimToCeiling(before, 5, FixtureLines.From(drafts, required: false));

        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(before, after, drafts)!;

        // Order is a 1-based index INTO THE SCRIPT — RemoveSentence/SpliceSentence both do
        // `Split(script)` and bounds-check against its count. This previously asserted [1, 3],
        // which cannot address a two-sentence script: a reviewer citing order 3 was silently
        // discarded, and one citing order 2 patched the weather line while meaning the third
        // sentence. Dev job 63684f38 (2026-08-24) is the live case — the reviewer was handed
        // orders 2..5 for a four-sentence read and every drop hit the wrong sentence.
        preserved.Select(s => s.Order).ShouldBe([1, 2]);
        preserved.Select(s => s.Text).ShouldBe([
            "The M4 is slow near Parramatta.", "Cloudy, twelve degrees today."]);
        preserved.SelectMany(s => s.EvidenceIds).ShouldBe(["corridor:m4", "weather:condition"]);
    }

    // (The AlignRefinedCitations tests were deleted 2026-08-21 with the whole-script refine path:
    //  re-aligning citations after an LLM rewrite was only ever that path's concern. Deterministic
    //  transforms are covered by PreserveMechanicalCitations above; the sentence patch validates
    //  its own citations.)

    [Fact]
    public async Task SecondReviewAttempt_receivesStructuredDraftAfterSentencePatch()
    {
        var reviewer = Substitute.For<IScriptReviewer>();
        var refiner = Substitute.For<IScriptRefiner>();
        var metrics = Substitute.For<IBroadcastMetrics>();
        var safetyFilter = Substitute.For<IContentSafetyFilter>();
        var safetyReviewer = Substitute.For<IScriptSafetyReviewer>();
        var jobs = Substitute.For<IJobRepository>();
        jobs.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var updater = new JobStatusUpdater(
            jobs,
            Substitute.For<INotificationService>(),
            TimeProvider.System,
            Substitute.For<ILogger<BroadcastOrchestrator>>());
        safetyFilter.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        safetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));

        var structuredArgs = new List<IReadOnlyList<DraftedSentence>?>();
        reviewer.ReviewScriptAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(),
                Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(),
                Arg.Do<IReadOnlyList<DraftedSentence>?>(structuredArgs.Add),
                Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(1, "STYLE", "Change the wording.")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));
        refiner.PatchSentenceAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<DraftedSentence>.Success(
                call.Arg<DraftedSentence>() with
                {
                    Text = "Cairns traffic is crawling along the Captain Cook Highway near Smithfield."
                }));

        var sut = new ScriptReviewLoop(
            reviewer,
            refiner,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 2 }),
            metrics,
            safetyFilter,
            safetyReviewer,
            Substitute.For<ILogger<BroadcastOrchestrator>>(),
            updater, TimeProvider.System);
        var context = Context() with { JobId = "citation-survival", Location = "cairns" };
        var drafts = new[]
        {
            new DraftedSentence(1, "Traffic is slow on the M4.", ["corridor:Captain Cook Highway#2:detail"])
        };

        var result = await sut.ReviewAndRefineScriptAsync(
            context, drafts[0].Text, 0.4, CancellationToken.None, drafts);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DraftedSentences!.Single().EvidenceIds.ShouldBe(["corridor:Captain Cook Highway#2:detail"]);
        structuredArgs.Count.ShouldBe(2);
        structuredArgs[1]!.Single().EvidenceIds.ShouldBe(["corridor:Captain Cook Highway#2:detail"]);
        structuredArgs[1]!.Single().Text.ShouldBe("Cairns traffic is crawling along the Captain Cook Highway near Smithfield.");

        var beforePersisted = ScriptGenerationEngine.AlignDraftedSentenceEvidence(
            [new JobScriptSentence(result.Value.Script, [], [])],
            drafts);
        var beforeCited = beforePersisted.Sentences.Count(sentence => sentence.EvidenceIds.Count > 0);
        var persisted = ScriptGenerationEngine.ApplyLoopSentenceEvidence(
            [new JobScriptSentence(result.Value.Script, [], [])],
            result.Value.DraftedSentences!);
        var afterCited = persisted.Sentences.Count(sentence => sentence.EvidenceIds.Count > 0);
        beforeCited.ShouldBe(0);
        afterCited.ShouldBe(1);
        persisted.Sentences.Single().EvidenceIds.ShouldBe(["corridor:Captain Cook Highway#2:detail"]);
        persisted.Sentences.Single().EvidenceIds.ShouldNotContain("draft:stale");
    }
    [Fact]
    public void StructuredEvidenceOwner_survives_persistence_when_sentence_names_cross_street()
    {
        const string text =
            "At Shorter Avenue in Beverly Hills, a breakdown has one of three lanes closed, adding three minutes.";
        var persisted = ScriptGenerationEngine.ApplyLoopSentenceEvidence(
            [new JobScriptSentence(text, [])],
            [
                new DraftedSentence(
                    1,
                    text,
                    ["r1:location:0", "r1:lanesClosed"],
                    corridorKeys: ["King Georges Road"])
            ]);

        persisted.Sentences.Single().CorridorKeys.ShouldBe(["King Georges Road"]);
        persisted.Sentences.Single().EvidenceIds.ShouldBe(
            ["r1:location:0", "r1:lanesClosed"]);
    }


    private static BroadcastWithTraffic Context(params TrafficCorridor[] corridors) => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = 30,
        JobId = "citation-survival",
        Location = "sydney",
        VoiceId = Option<string>.Some("voice"),
        Incidents = [],
        Corridors = corridors
    };
}

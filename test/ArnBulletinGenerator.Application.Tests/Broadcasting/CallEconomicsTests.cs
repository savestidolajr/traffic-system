using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services.Detectors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

public sealed class CallEconomicsTests
{
    private readonly IScriptReviewer _reviewer = Substitute.For<IScriptReviewer>();
    private readonly IScriptRefiner _refiner = Substitute.For<IScriptRefiner>();
    private readonly IBroadcastMetrics _metrics = Substitute.For<IBroadcastMetrics>();
    private readonly IContentSafetyFilter _safetyFilter = Substitute.For<IContentSafetyFilter>();
    private readonly IScriptSafetyReviewer _safetyReviewer = Substitute.For<IScriptSafetyReviewer>();
    private readonly ScriptReviewLoop _sut;

    public CallEconomicsTests()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var jobStatusUpdater = new JobStatusUpdater(
            jobs,
            Substitute.For<INotificationService>(),
            TimeProvider.System,
            Substitute.For<ILogger<BroadcastOrchestrator>>());

        _safetyFilter.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        _safetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));

        _sut = new ScriptReviewLoop(
            _reviewer,
            _refiner,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 2 }),
            _metrics,
            _safetyFilter,
            _safetyReviewer,
            Substitute.For<ILogger<BroadcastOrchestrator>>(),
            jobStatusUpdater, TimeProvider.System);
    }

    [Fact]
    public async Task RepairedAndCleanScript_doesNotSpendARefineCall()
    {
        var context = Context(new TrafficCorridor(
            "M4", 2, 1, 1000, 40, "congestion", "Slow traffic", [], Option<string>.Some("Parramatta")));
        _reviewer.ReviewScriptAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(),
                Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "Approved.")));

        var result = await _sut.ReviewAndRefineScriptAsync(
            context, "The M4 is slow at Church Street.", 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Script.ShouldContain("Parramatta");
    }

    [Fact]
    public async Task GenuineReviewerFabrication_UnresolvableFeedbackDefersWithoutPatch()
    {
        var context = Context();
        _reviewer.ReviewScriptAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(),
                Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    "The M4 is running 38 minutes, but the source says about 9 minutes slower than usual.")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "Approved.")));

        var result = await _sut.ReviewAndRefineScriptAsync(
            context, "Traffic is moving slowly on the M4.", 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _refiner.DidNotReceiveWithAnyArgs()
            .PatchSentenceAsync(default!, default!, default!, default!, default, default);
    }


    [Fact]
    public void SectionOrderRepair_preserves_each_sentence_citation_when_sentences_move()
    {
        const string before = "Traffic is slow on the M4. Fuel is $1.80 a litre. It's 22 degrees and sunny.";
        var drafts = new List<DraftedSentence>
        {
            new(1, "Traffic is slow on the M4.", ["corridor:m4"]),
            new(2, "Fuel is $1.80 a litre.", ["fuel:unleaded"]),
            new(3, "It's 22 degrees and sunny.", ["weather:condition"])
        };
        string after = SectionOrderDetector.Repair(before, Context());

        var preserved = ScriptReviewLoop.PreserveMechanicalCitations(before, after, drafts)!;

        preserved.Count.ShouldBe(3);
        preserved.Select(sentence => sentence.Order).ShouldBe([1, 2, 3]);
        preserved.Select(sentence => sentence.Text).ShouldBe([
            "Traffic is slow on the M4.",
            "It's 22 degrees and sunny.",
            "Fuel is $1.80 a litre."
        ]);
        preserved.SelectMany(sentence => sentence.EvidenceIds).ShouldBe([
            "corridor:m4",
            "weather:condition",
            "fuel:unleaded"
        ]);
    }
    private static BroadcastWithTraffic Context(params TrafficCorridor[] corridors) => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = 30,
        JobId = "call-economics-test",
        Location = "sydney",
        VoiceId = Option<string>.Some("voice"),
        Incidents = [],
        Corridors = corridors
    };
}

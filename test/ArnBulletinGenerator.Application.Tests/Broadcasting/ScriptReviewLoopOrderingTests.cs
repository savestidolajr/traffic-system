using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests.Broadcasting;

public sealed class ScriptReviewLoopOrderingTests
{
    private readonly IScriptReviewer _reviewer = Substitute.For<IScriptReviewer>();
    private readonly IScriptRefiner _refiner = Substitute.For<IScriptRefiner>();
    private readonly IBroadcastMetrics _metrics = Substitute.For<IBroadcastMetrics>();
    private readonly IContentSafetyFilter _safetyFilter = Substitute.For<IContentSafetyFilter>();
    private readonly IScriptSafetyReviewer _safetyReviewer = Substitute.For<IScriptSafetyReviewer>();
    private readonly JobStatusUpdater _jobStatusUpdater;
    private readonly ScriptReviewLoop _sut;

    public ScriptReviewLoopOrderingTests()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobStatusUpdater = new JobStatusUpdater(
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
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 1 }),
            _metrics,
            _safetyFilter,
            _safetyReviewer,
            Substitute.For<ILogger<BroadcastOrchestrator>>(),
            _jobStatusUpdater, TimeProvider.System);
    }

    [Fact]
    public async Task SuburbRetentionRepair_happens_before_refine()
    {
        var context = Context(new TrafficCorridor(
            "M4", 2, 1, 1000, 40, "congestion", "Slow traffic", [], Option<string>.Some("Parramatta")));
        const string script = "The M4 is slow at Church Street.";
        ApproveReview();

        var result = await _sut.ReviewAndRefineScriptAsync(context, script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Script.ShouldContain("Parramatta");
    }

    [Fact]
    public async Task UnresolvableLegacyFeedback_DefersToNextReviewWithoutPatch()
    {
        var context = Context();
        const string script = "Traffic is very very slow on the M4 today.";
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(false, "Vary the repeated wording")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));

        var result = await _sut.ReviewAndRefineScriptAsync(context, script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _refiner.DidNotReceiveWithAnyArgs()
            .PatchSentenceAsync(default!, default!, default!, default!, default, default);
    }

    [Fact]
    public async Task WordCeiling_runs_after_repairs()
    {
        var context = Context(new TrafficCorridor(
            "M4", 2, 1, 1000, 40, "congestion", "Slow traffic", [], Option<string>.Some("Parramatta"))) with
        {
            TargetWordCountOverride = Option<int>.Some(12)
        };
        const string script = "The M4 is slow at Church Street. A second sentence is also slow today.";
        ApproveReview();

        var result = await _sut.ReviewAndRefineScriptAsync(context, script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        WordCounter.Count(result.Value.Script).ShouldBeLessThanOrEqualTo(ScriptWordCeiling.For(12));
        result.Value.Script.ShouldContain("Parramatta");
    }
    private void ApproveReview() =>
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));

    private static BroadcastWithTraffic Context(params TrafficCorridor[] corridors) => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = 30,
        JobId = "ordering-test",
        Location = "sydney",
        VoiceId = Option<string>.Some("voice"),
        Model = Option<string>.Some("eleven_v3"),
        VoiceSettings = Option<VoiceSettings>.Some(VoiceSettings.Default),
        Incidents = [],
        Corridors = corridors,
        Content = new BulletinContentSelection()
    };
}

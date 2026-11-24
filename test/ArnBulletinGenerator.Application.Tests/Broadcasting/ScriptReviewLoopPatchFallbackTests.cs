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

/// <summary>
///     The 2026-08-21 test-env failure: the reviewer proved a sentence unsupported, the sentence
///     patch failed on word tolerance, and the loop KEPT the flagged sentence — so the next review
///     found the same defect and the job burned its budget to a final-grounding failure. These
///     tests pin the repair ladder: unrepairable reviewer-flagged sentence => drop it; required
///     fixture carrier => deterministically re-splice the fetched fixture line.
/// </summary>
public sealed class ScriptReviewLoopPatchFallbackTests
{
    private readonly IScriptReviewer _reviewer = Substitute.For<IScriptReviewer>();
    private readonly IScriptRefiner _refiner = Substitute.For<IScriptRefiner>();
    private readonly List<string> _jobLogs = [];
    private readonly ScriptReviewLoop _sut;

    public ScriptReviewLoopPatchFallbackTests()
    {
        var jobs = Substitute.For<IJobRepository>();
        jobs.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success())
            .AndDoes(call => _jobLogs.Add(call.ArgAt<string>(1)));
        var jobStatusUpdater = new JobStatusUpdater(
            jobs,
            Substitute.For<INotificationService>(),
            TimeProvider.System,
            Substitute.For<ILogger<BroadcastOrchestrator>>());

        var safetyFilter = Substitute.For<IContentSafetyFilter>();
        safetyFilter.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        var safetyReviewer = Substitute.For<IScriptSafetyReviewer>();
        safetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));

        _sut = new ScriptReviewLoop(
            _reviewer,
            _refiner,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 1 }),
            Substitute.For<IBroadcastMetrics>(),
            safetyFilter,
            safetyReviewer,
            Substitute.For<ILogger<BroadcastOrchestrator>>(),
            jobStatusUpdater, TimeProvider.System);
    }

    [Fact]
    public async Task ReviewerFlaggedSentence_WhenPatchFails_IsDroppedNotRetained()
    {
        // Attempt-1 review rejects: sentence 2 is unsupported. The patch fails (word tolerance).
        // The loop must DROP sentence 2, and the final grounding review then approves the
        // remaining read — the defective sentence never ships.
        const string script =
            "The M4 is slow at Church Street. " +
            "Cairns is clear overnight, with temperatures at 18 degrees. " +
            "Traffic is running smoothly on the M2.";
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(2, "NOT_FROM_DATA", "\"Cairns is clear overnight\" is a roads claim with no named clear roads supplied.")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));
        _refiner.PatchSentenceAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedSentence>.Failure("Refined sentence inflated beyond word tolerance."));

        var result = await _sut.ReviewAndRefineScriptAsync(Context(), script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue($"loop failed\n{string.Join("\n", _jobLogs)}");
        result.Value.Script.ShouldContain("The M4 is slow at Church Street");
        result.Value.Script.ShouldContain("running smoothly on the M2");
        result.Value.Script.ShouldNotContain("Cairns is clear overnight");
    }

    [Fact]
    public async Task UnorderedIssue_WithQuotedSpan_ResolvesToTheQuotedSentence_NeverGuessed()
    {
        // The reviewer omits the order but quotes sentence 3 verbatim. The patch must target
        // sentence 3 — the pre-fix keyword mapper defaulted to the LEAD sentence on no match.
        const string script =
            "The M4 is slow at Church Street. " +
            "Traffic is running smoothly on the M2. " +
            "Liberty Grove has a slight queue.";
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForUnresolvedSentence("NOT_FROM_DATA", "\"Liberty Grove has a slight queue\" cites no supplied queue evidence for that road.")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));
        _refiner.PatchSentenceAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<DraftedSentence>.Success(
                call.Arg<DraftedSentence>() with { Text = "Liberty Grove is moving nicely." }));

        var result = await _sut.ReviewAndRefineScriptAsync(Context(), script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Script.ShouldContain("Liberty Grove is moving nicely");
        await _refiner.Received(1).PatchSentenceAsync(
            Arg.Any<BroadcastWithTraffic>(),
            Arg.Is<DraftedSentence>(s => s.Order == 3),
            Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FinalGrounding_TwoFlaggedSentences_DropsBothWithoutOrderDrift()
    {
        // The reviewer's orders anchor to the REVIEWED script: dropping sentence 2 first shifts
        // every later order, so a naive in-order drop would remove innocent sentence 5 instead of
        // flagged sentence 4. The gate resolves all issues against the pre-drop split and drops
        // highest-order first. Loop patches are no-ops, so both defects reach the gate intact.
        const string script =
            "The M4 is slow at Church Street. " +
            "The M1 is bumper to bumper through the city. " +
            "Traffic is running smoothly on the M2. " +
            "Victoria Road is completely closed at West Street. " +
            "Liberty Grove is moving nicely.";
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                // Loop attempt: reject; patches land as no-ops (uncommitted improvement).
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                [
                    ReviewIssue.ForSentence(2, "NOT_FROM_DATA", "\"bumper to bumper\" has no supplied severity evidence."),
                    ReviewIssue.ForSentence(4, "NOT_FROM_DATA", "\"completely closed\" contradicts the supplied works detail.")
                ])),
                // Final grounding attempt 1: same two defects flagged.
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                [
                    ReviewIssue.ForSentence(2, "NOT_FROM_DATA", "\"bumper to bumper\" has no supplied severity evidence."),
                    ReviewIssue.ForSentence(4, "NOT_FROM_DATA", "\"completely closed\" contradicts the supplied works detail.")
                ])),
                // Final grounding attempt 2 (after the drops): approve.
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));
        _refiner.PatchSentenceAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<DraftedSentence>.Success(call.Arg<DraftedSentence>()));

        var result = await _sut.ReviewAndRefineScriptAsync(Context(), script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue($"loop failed\n{string.Join("\n", _jobLogs)}");
        result.Value.Script.ShouldContain("The M4 is slow at Church Street");
        result.Value.Script.ShouldContain("running smoothly on the M2");
        result.Value.Script.ShouldContain("Liberty Grove is moving nicely");
        result.Value.Script.ShouldNotContain("bumper to bumper");
        result.Value.Script.ShouldNotContain("completely closed");
    }

    [Fact]
    public async Task DroppedSentence_IsReportedToTheNextReview_SoTheReviewerNeverDemandsItsReturn()
    {
        // The 76f4f8d0 whipsaw: review flags sentence 2, the patch fails, the ladder drops it —
        // then the next review complained the item was MISSING and the job burned out at the gate.
        // Every review after a drop now receives the removed sentence list.
        const string script =
            "The M4 is slow at Church Street. " +
            "The M1 is bumper to bumper through the city. " +
            "Traffic is running smoothly on the M2.";
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(2, "NOT_FROM_DATA", "\"bumper to bumper\" has no supplied severity evidence.")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));
        _refiner.PatchSentenceAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedSentence>.Failure("Refined sentence inflated beyond word tolerance."));

        var result = await _sut.ReviewAndRefineScriptAsync(Context(), script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue($"loop failed\n{string.Join("\n", _jobLogs)}");
        result.Value.Script.ShouldNotContain("bumper to bumper");
        // The second (approving) review must have been told the sentence was removed deliberately.
        await _reviewer.Received(2).ReviewScriptAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(),
            Arg.Is<IReadOnlyList<string>?>(r => r != null && r.Any(s => s.Contains("bumper to bumper"))));
    }

    [Fact]
    public async Task FinalGrounding_NovelIssueOnUntouchedSentence_IsNotedNotActedOn()
    {
        // The gate exists to catch a bad last mutation, not to let the reviewer move the goalposts.
        // Loop flags order 2 (patch no-ops, budget exhausts); the gate then raises a NEW defect
        // against untouched sentence 1 — persistent-adjacent nothing. Ship with a log line.
        const string script =
            "The M4 is slow at Church Street. " +
            "The M1 is bumper to bumper through the city. " +
            "Traffic is running smoothly on the M2.";
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                // Loop attempt 1: sentence 2 flagged.
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(2, "NOT_FROM_DATA", "\"bumper to bumper\" has no supplied severity evidence.")])),
                // Gate attempt 1: a NOVEL claim against untouched sentence 1.
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(1, "CONTINUITY", "\"The M4 is slow at Church Street\" is presented as new on a re-read.")])));
        _refiner.PatchSentenceAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<DraftedSentence>.Success(call.Arg<DraftedSentence>()));

        var result = await _sut.ReviewAndRefineScriptAsync(Context(), script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue($"loop failed\n{string.Join("\n", _jobLogs)}");
        result.Value.Script.ShouldContain("The M4 is slow at Church Street");
        result.Value.Script.ShouldContain("bumper to bumper");
        _jobLogs.ShouldContain(log => log.Contains("noted, not acted on"));
    }

    [Fact]
    public async Task FinalGrounding_LastAttemptRejectsOnNovelContinuityOnly_ShipsInsteadOfFailingTheJob()
    {
        // Live dev job b56a431c (2026-08-21): the gate's LAST attempt rejected with a first-time
        // cleared-item continuity complaint and failed the whole job — the novelty screen has to
        // govern the failure decision, not just the drop pass.
        const string script =
            "The M4 is slow at Church Street. " +
            "The M1 is bumper to bumper through the city. " +
            "Traffic is running smoothly on the M2.";
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                // Loop attempt: a factual defect on sentence 2 (patch no-ops, budget exhausts).
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(2, "NOT_FROM_DATA", "\"bumper to bumper\" has no supplied severity evidence.")])),
                // Gate attempt 1: same defect persists -> actionable, sentence 2 is dropped.
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(2, "NOT_FROM_DATA", "\"bumper to bumper\" has no supplied severity evidence.")])),
                // Gate attempt 2 (last): only a novel cleared-item continuity complaint remains.
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForScript("CONTINUITY", "The re-read omits acknowledgement of the cleared works on Church Street.")])));
        _refiner.PatchSentenceAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<DraftedSentence>.Success(call.Arg<DraftedSentence>()));

        var result = await _sut.ReviewAndRefineScriptAsync(Context(), script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue($"loop failed\n{string.Join("\n", _jobLogs)}");
        result.Value.Script.ShouldContain("The M4 is slow at Church Street");
        result.Value.Script.ShouldNotContain("bumper to bumper");
        _jobLogs.ShouldContain(log => log.Contains("nothing actionable remained"));
    }

    [Fact]
    public async Task FlaggedRequiredWeatherSentence_WhenPatchFails_IsDroppedAndFixtureRespliced()
    {
        // The flagged sentence is the sole carrier of a REQUIRED fixture. The patch fails, so
        // the loop drops the defective sentence and deterministically re-splices weather from the
        // fetched report rather than retaining the unsupported claim.
        const string script =
            "The M4 is slow at Church Street. " +
            "Cairns is clear overnight, with temperatures at 18 degrees.";
        _reviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(2, "NOT_FROM_DATA", "\"Cairns is clear overnight\" is a roads claim with no named clear roads supplied.")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));
        _refiner.PatchSentenceAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedSentence>.Failure("Refined sentence inflated beyond word tolerance."));

        var context = Context() with
        {
            Content = new BulletinContentSelection(Traffic: true, Weather: true, WeatherRequired: true),
            Weather = Option<WeatherReport>.Some(new WeatherReport("Clear", 18.0, Option<string>.None, 5, []))
        };

        var result = await _sut.ReviewAndRefineScriptAsync(context, script, 0.4, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue($"loop failed\n{string.Join("\n", _jobLogs)}");
        result.Value.Script.ShouldContain("The M4 is slow at Church Street");
        result.Value.Script.ShouldContain("Clear, 18 degrees.");
        result.Value.Script.ShouldNotContain("Cairns is clear overnight");
        await _refiner.Received(1).PatchSentenceAsync(
            Arg.Any<BroadcastWithTraffic>(),
            Arg.Is<DraftedSentence>(s => s.Order == 2),
            Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    private static BroadcastWithTraffic Context() => new()
    {
        Traffic = "Traffic",
        TargetDurationSeconds = 22,
        JobId = "patch-fallback-test",
        Location = "cairns",
        VoiceId = Option<string>.Some("voice"),
        Model = Option<string>.Some("eleven_v3"),
        VoiceSettings = Option<VoiceSettings>.Some(VoiceSettings.Default),
        Incidents = [],
        Corridors = [],
        Content = new BulletinContentSelection()
    };
}

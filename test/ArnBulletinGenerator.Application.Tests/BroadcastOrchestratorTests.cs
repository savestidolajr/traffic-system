using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public partial class BroadcastOrchestratorTests
{
    private const string TestScript = "Traffic is flowing smoothly across all major roads in Sydney today";
    private readonly IAudioPipelineService _audioPipeline = Substitute.For<IAudioPipelineService>();
    private readonly IBroadcastOutput _broadcastOutput = Substitute.For<IBroadcastOutput>();
    private readonly IDurationEstimator _durationEstimator = Substitute.For<IDurationEstimator>();
    private readonly IFillerAudioCacheService _fillerAudioCacheService = Substitute.For<IFillerAudioCacheService>();
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly IScriptGenerator _scriptGenerator = Substitute.For<IScriptGenerator>();
    private readonly IScriptRefiner _scriptRefiner = Substitute.For<IScriptRefiner>();
    private readonly IScriptDirector _scriptDirector = Substitute.For<IScriptDirector>();
    private readonly IScriptReviewer _scriptReviewer = Substitute.For<IScriptReviewer>();
    private readonly BroadcastOrchestrator _sut;
    private readonly ITrafficProvider _trafficProvider = Substitute.For<ITrafficProvider>();
    private readonly IVoiceCalibrationService _voiceCalibrationService = Substitute.For<IVoiceCalibrationService>();
    private readonly IContentSafetyFilter _contentSafetyFilter = Substitute.For<IContentSafetyFilter>();
    private readonly IScriptSafetyReviewer _scriptSafetyReviewer = Substitute.For<IScriptSafetyReviewer>();
    private readonly IPriorityRoadsRepository _priorityRoadsRepository = Substitute.For<IPriorityRoadsRepository>();
    private readonly IFillerStatementsRepository _fillerStatementsRepository = Substitute.For<IFillerStatementsRepository>();
    private readonly IFutureLessonRepository _marketFeedbackRepository = Substitute.For<IFutureLessonRepository>();
    private readonly IGuidanceRepository _guidanceRepository = Substitute.For<IGuidanceRepository>();
    private readonly IEditorialSettingsRepository _editorialSettingsRepository =
        Substitute.For<IEditorialSettingsRepository>();
    private readonly IWeatherProvider _weatherProvider = Substitute.For<IWeatherProvider>();
    private readonly IFuelPriceProvider _fuelPriceProvider = Substitute.For<IFuelPriceProvider>();
    private readonly IRoutingProvider _routingProvider = Substitute.For<IRoutingProvider>();
    private readonly IBulletinFallbackService _bulletinFallbackService = Substitute.For<IBulletinFallbackService>();
    // Covering a slot must finish at the playout drop; the orchestrator delegates
    // that sequence to IJobService.DeliverCoveredSlotAsync.
    private readonly IJobService _jobService = Substitute.For<IJobService>();
    private readonly IIncidentStateRepository _incidentStateRepository = Substitute.For<IIncidentStateRepository>();

    public BroadcastOrchestratorTests()
    {
        var incidentStateService = new IncidentStateService(
            _incidentStateRepository,
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success([]));

        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(20, 5.8, 1.45));
        _contentSafetyFilter.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Allowed());
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));
        _priorityRoadsRepository.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([]));
        _fillerStatementsRepository.GetStatementsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FillerStatement>>.Success([]));
        _marketFeedbackRepository.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success([]));
        _guidanceRepository.GetForScopesAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<GuidanceEntry>>.Success([]));
        _editorialSettingsRepository.GetApplicableAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<EditorialSettings>>.Success([]));
        _weatherProvider.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Failure("not configured"));
        _fuelPriceProvider.GetFuelPricesAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<FuelPriceReport>.Failure("not configured"));
        _fillerAudioCacheService.ResolveAsync(Arg.Any<FillerStatement>(), Arg.Any<string>(),
                Arg.Any<VoiceSettings>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([], 0, [])));

        // Covering an unairable slot finishes at the playout drop via
        // IJobService.DeliverCoveredSlotAsync; the sequence itself is pinned in JobServiceTests.
        _jobService.DeliverCoveredSlotAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
            new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService, _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
            incidentStateService, _jobRepository,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 0 }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            new NoOpBroadcastMetrics(),
            _priorityRoadsRepository,
            _fillerStatementsRepository,
            _marketFeedbackRepository,
            _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService,
            _jobService,
            Substitute.For<ILogger<BroadcastOrchestrator>>());

        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepository.GetLatestBulletinForMarketTodayAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        // Default so EVERY test has a usable audio sink. PersistFailedCandidateAsync saves the
        // best failed candidate on the content-limited/exhausted paths, which previously never
        // touched the output; an unconfigured NSubstitute member returns a null Task and the
        // await NREs before the method's own null guard can run. Tests needing a save FAILURE
        // override this locally.
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private static BroadcastWithTraffic Ctx()
    {
        return new BroadcastWithTraffic
        {
            Traffic = "Traffic",
            TargetDurationSeconds = 30.0,
            JobId = "test-job-id",
            Location = "sydney",
            VoiceId = Option<string>.Some("test-voice-id"),
            Incidents = []
        };
    }

    private void SetupSuccessfulPipeline(string? script = null, byte[]? audio = null)
    {
        script ??= TestScript;
        audio ??= [1, 2, 3];
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(script, [])));
        _durationEstimator.EstimateDuration(script).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(script, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, script, 30.0) { SpokenDurationSeconds = 30.0, SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    // Builds a FRESH Job pinned at the given operator-facing status on every call, used to
    // mock GetJobAsync for the finalize-guard tests below. The pipeline's own in-flight status
    // transitions (HereFetched, IncidentsSelected, ...) run via UpdateJobAsync, which re-reads
    // the job from the repository and mutates whatever instance that read returns — a single
    // shared mock instance would get carried past WaitingApproval/Rejected before
    // SaveFinalBroadcastResultAsync's own re-read ever saw it. Returning a brand-new clone per
    // call means every read, including the finalize-time one, observes the pinned status.
    private static Job PinnedJob(JobStatus status, string scriptText, string audioUrl)
    {
        var job = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        if (status == JobStatus.Rejected)
            job.TransitionTo(JobStatus.Rejected, utcNow: DateTime.UtcNow);
        job.ScriptText = Option<string>.Some(scriptText);
        job.AudioUrl = Option<string>.Some(audioUrl);
        return job;
    }

    [Fact]
    public async Task AllStepsSucceed_ReturnsSuccess()
    {
        SetupSuccessfulPipeline();
        (await _sut.OrchestrateAsync(Ctx())).IsSuccess.ShouldBeTrue();
    }

    private void SetupContentLimitedPipeline()
    {
        // Generation succeeds but synthesis underruns the slot on every attempt (SlotFilled=false):
        // the orchestrator's content-limited path should fire.
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(26.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], TestScript, 26.0)
                { SpokenDurationSeconds = 26.0, SlotFilled = false }));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(26.0);
    }

    [Fact]
    public async Task ContentLimited_RetainsFailedCandidate_MarksFailed_AndDoesNotPublishEarly()
    {
        SetupContentLimitedPipeline();
        _bulletinFallbackService.TryApplyFallbackAsync(Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FallbackOutcome(FallbackKind.FillerCart));

        var sut = CreateSutWithRetries(0);
        var result = await sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        // Playout delivery is not executed early — left for the deadline sweep or manual operator action.
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task ContentLimited_OperatorAttendedRegeneration_MarksFailed_AndNeverCoversEarly()
    {
        // Prod 2026-08-18 05:44: an operator's curated regeneration came back content-limited and
        // the cart was covered AND published ten minutes before the deadline, while the operator
        // was still working the slot. Attended generations must fail visibly instead; the deadline
        // sweep remains the slot's no-dead-air net.
        SetupContentLimitedPipeline();
        _bulletinFallbackService.TryApplyFallbackAsync(Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FallbackOutcome(FallbackKind.FillerCart));

        var sut = CreateSutWithRetries(0);
        var result = await sut.OrchestrateAsync(Ctx() with { OperatorAttended = true });

        result.IsFailure.ShouldBeTrue();
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Marked Failed so the console shows an actionable card, not a cart publish.
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentLimited_NoFallbackAvailable_ReturnsContentLimitedFailure()
    {
        SetupContentLimitedPipeline();
        // Default _bulletinFallbackService mock returns FallbackKind.None (no fallback available).

        var metrics = Substitute.For<IBroadcastMetrics>();
        var incidentStateService = new IncidentStateService(
            Substitute.For<IIncidentStateRepository>(),
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);
        var sut = new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
                new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService, _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
            incidentStateService, _jobRepository,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 0, MaxGenerationRetries = 0 }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            metrics,
            _priorityRoadsRepository,
            _fillerStatementsRepository,
            _marketFeedbackRepository,
            _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService,
            _jobService,
            Substitute.For<ILogger<BroadcastOrchestrator>>());
        Job? failedJob = null;
        _jobRepository.SaveJobAsync(
                Arg.Do<Job>(job =>
                {
                    if (job.Status == JobStatus.Failed)
                        failedJob = job;
                }),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldStartWith("Content-limited:");
        failedJob.ShouldNotBeNull();
        failedJob!.ScriptText.HasValue.ShouldBeTrue();
        failedJob.AudioUrl.HasValue.ShouldBeTrue();
        failedJob.MeasuredDurationSec.HasValue.ShouldBeTrue();
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        metrics.Received(1).TrackPipelineStage("content_limited", 0, Arg.Any<string>(), false);
    }

    [Fact]
    public async Task CandidateRetentionFailure_DoesNotReplaceContentLimitedFailure()
    {
        SetupContentLimitedPipeline();
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result>(new InvalidOperationException("blob unavailable")));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldStartWith("Content-limited:");
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(job => job.Status == JobStatus.Failed), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     A never-approving reviewer exhausts the final grounding hard gate, which is the one
    ///     scenario the 2026-07-26 TEST load burst caught stranding a slot: the gate correctly
    ///     refuses the script, but the job used to terminate Failed with no standby applied, so the
    ///     slot stayed uncovered until the JobWatchdog deadline sweep.
    /// </summary>
    private void SetupGroundingGateExhaustedPipeline()
    {
        SetupScriptOnlyPipeline();
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(
                new ScriptReviewResult(false, "restates the same incident already voiced earlier")));
    }

    [Fact]
    public async Task GroundingGateExhausted_MarksFailed_AndDoesNotPublishEarly()
    {
        SetupGroundingGateExhaustedPipeline();
        _bulletinFallbackService.TryApplyFallbackAsync(Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FallbackOutcome(FallbackKind.FillerCart));

        var sut = CreateSutWithReview();
        var result = await sut.OrchestrateAsync(
            Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(30) });

        result.IsFailure.ShouldBeTrue();
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), FallbackReasons.QualityGateFailed, Arg.Any<CancellationToken>());
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task GroundingGateExhausted_NoFallbackAvailable_CarriesPermanentDisposition()
    {
        SetupGroundingGateExhaustedPipeline();
        // Default _bulletinFallbackService mock returns FallbackKind.None (no standby available).

        var sut = CreateSutWithReview();
        var result = await sut.OrchestrateAsync(
            Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(30) });

        result.IsFailure.ShouldBeTrue();
        // Permanent, not Retryable: QueueFailurePolicy must acknowledge rather than redeliver a
        // pipeline that already spent its whole refine budget on this content.
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Permanent);
        result.FailureMetadata.Value.Code.ShouldBe("broadcast.final_grounding_failed");
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), FallbackReasons.QualityGateFailed, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliveryStyle_AppliesTags_PersistsPlainAndTaggedSeparately()
    {
        const string plain = TestScript;
        const string tagged = "[urgent] " + TestScript;

        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(plain, [])));
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(ci => Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], ci.ArgAt<string>(0), 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _scriptDirector.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(tagged));
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));

        Job? saved = null;
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(ci => { saved = ci.Arg<Job>(); return Result.Success(); });

        var ctx = Ctx() with
        {
            Model = Option<string>.Some("eleven_v3"),
            DeliveryStyle = Option<DeliveryStylePolicy>.Some(new DeliveryStylePolicy("Urgent", ["[urgent]"], 2)),
            DeliveryStyleId = Option<string>.Some("ds-test"),
            DeliveryStyleVersion = Option<int>.Some(3)
        };

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        saved.ShouldNotBeNull();
        saved!.ScriptText.HasValue.ShouldBeTrue();
        saved.ScriptText.GetValueOr("").ShouldBe(plain);
        saved.TaggedScriptText.HasValue.ShouldBeTrue();
        saved.TaggedScriptText.GetValueOr("").ShouldBe(tagged);
        saved.DeliveryStyleId.GetValueOr("").ShouldBe("ds-test");
        saved.DeliveryStyleVersion.GetValueOr(0).ShouldBe(3);
        await _scriptDirector.Received(1).DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(),
            Arg.Any<string?>(), "eleven_v3", Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeliveryStyle_RestoresApprovedCasing_WhenDirectorAddsAllCaps()
    {
        // Capitalisation is an allowed director instrument, but the approved script's casing is the
        // canonical text sent to TTS: ALL-CAPS reaching ElevenLabs risks a shouted or letter-by-letter
        // read (observed live 2026-08-20, cairns job 6ea0155c aired "moving NICELY"). This must be a
        // PURE casing change — the same word in caps. Using a DIFFERENT word here would exercise the
        // word-preservation guard instead, which is the next test's job, and the tagged read would be
        // correctly discarded rather than case-corrected.
        const string directed =
            "[urgent] Traffic is flowing SMOOTHLY across all major roads in Sydney today!";

        var saved = await RunWithDirectedScript(directed);

        saved.ShouldNotBeNull();
        // The tag survives; only the shouted word is restored to the approved casing.
        saved!.TaggedScriptText.GetValueOr("").ShouldBe(
            "[urgent] Traffic is flowing smoothly across all major roads in Sydney today!");
        string stripped = DeliveryTags.Strip(saved.TaggedScriptText.GetValueOr(""));
        DeliveryTags.WordsPreserved(TestScript, stripped).ShouldBeTrue();
        // No separate "not SMOOTHLY" assertion: Shouldly's ShouldNotContain is case-INSENSITIVE, so
        // it would match the restored "smoothly" and fail. The exact ShouldBe above is the stronger
        // check anyway — it pins the whole tagged string, casing included.
        saved.DeliveryStyleId.GetValueOr("").ShouldBe("ds-test");
    }

    [Fact]
    public async Task DeliveryStyle_RevertsToPlain_WhenDirectorChangesAWord()
    {
        // "smoothly" -> "freely" is a real word change, so the tagged read must be discarded and no
        // delivery provenance recorded.
        const string tagged = "[urgent] Traffic is flowing freely across all major roads in Sydney today";

        var saved = await RunWithDirectedScript(tagged);

        saved.ShouldNotBeNull();
        saved!.ScriptText.GetValueOr("").ShouldBe(TestScript);
        saved.TaggedScriptText.HasValue.ShouldBeFalse();
        saved.DeliveryStyleId.HasValue.ShouldBeFalse();
    }

    /// <summary>
    ///     Runs one full orchestration whose only variable is what the delivery director returns,
    ///     and hands back the persisted job for the tagged-script assertions above.
    /// </summary>
    private async Task<Job?> RunWithDirectedScript(string directed)
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(ci => Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], ci.ArgAt<string>(0), 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _scriptDirector.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(directed));
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(ScriptSafetyResult.Safe()));

        Job? saved = null;
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(ci => { saved = ci.Arg<Job>(); return Result.Success(); });

        var ctx = Ctx() with
        {
            Model = Option<string>.Some("eleven_v3"),
            DeliveryStyle = Option<DeliveryStylePolicy>.Some(new DeliveryStylePolicy("Urgent", ["[urgent]"], 2)),
            DeliveryStyleId = Option<string>.Some("ds-test"),
            DeliveryStyleVersion = Option<int>.Some(3)
        };

        (await _sut.OrchestrateAsync(ctx)).IsSuccess.ShouldBeTrue();
        return saved;
    }

    [Fact]
    public async Task DeliveryStyle_Neutral_DoesNotInvokeDirector()
    {
        SetupSuccessfulPipeline();
        (await _sut.OrchestrateAsync(Ctx())).IsSuccess.ShouldBeTrue();
        await _scriptDirector.DidNotReceive().DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FinalBroadcast_PersistsWaitingApproval_DespiteConcurrentLogWrite()
    {
        // Regression for the optimistic-concurrency save-race: AddLogAsync runs its own
        // read-modify-write that bumps the job entity's ETag. The WaitingApproval transition
        // MUST be saved BEFORE the "broadcast ready" log is appended — otherwise that save runs
        // on a stale ETag, fails with a conflict, and strands the job at SCRIPT_READY even
        // though the audio synthesized and uploaded fine.
        SetupSuccessfulPipeline();

        var job = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        var storedETag = 1; // monotonically bumped by every persisted write, like Azure Table.
        var waitingApprovalSaved = false;

        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                job.ETag = storedETag.ToString(); // a fresh read always carries the current ETag
                return Result<Job>.Success(job);
            });
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                storedETag++; // appending a log mutates the entity → new ETag
                return Result.Success();
            });
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var saved = ci.Arg<Job>();
                if (!string.IsNullOrEmpty(saved.ETag) && saved.ETag != storedETag.ToString())
                    return Result.Failure("ETag conflict (simulated)");
                storedETag++;
                if (saved.Status == JobStatus.WaitingApproval)
                    waitingApprovalSaved = true;
                return Result.Success();
            });

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        waitingApprovalSaved.ShouldBeTrue();
    }

    [Fact]
    public async Task ScriptGenerationFails_ReturnsFailureAndSkipsAudio()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Failure("Script error"));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Script error");
        await _audioPipeline.DidNotReceiveWithAnyArgs()
            .SynthesizeWithDurationTargetAsync(default!, default);
    }

    [Fact]
    public async Task AudioSynthesisFails_ReturnsFailure()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Failure("Audio error"));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Audio error");
    }

    [Fact]
    public async Task RevoiceOnly_SynthesisFails_StillPersistsEditedScriptToJob()
    {
        // RISK-31 regression: a failed re-voice attempt must durably save the operator's edited
        // script to job.ScriptText BEFORE synthesis is attempted, so any retry that falls back to
        // job.ScriptText (no editedScript resubmitted) reads the edit rather than stale pre-edit
        // text.
        const string editedScript = "Traffic is heavy on the M4 near Parramatta after a crash.";
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(editedScript)
        };
        _audioPipeline.SynthesizeWithDurationTargetAsync(editedScript, Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(),
                Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(),
                Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Failure("Re-voice synthesis failed: TTS error"));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsFailure.ShouldBeTrue();
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.ScriptText.HasValue && j.ScriptText.Value == editedScript),
            Arg.Any<CancellationToken>());
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevoiceOnly_OperatorTonedScript_IsSynthesisedVerbatim()
    {
        // The operator placed the v3 audio tags themselves in the review UI. The words are
        // untouched, so the tagged read is what must reach the voice — not the plain script,
        // and not the station's delivery style on top of it (decision 2026-08-02).
        const string plain = "The Monash is closed citybound. Take Burke Road instead.";
        const string toned = "[solemn] The Monash is closed citybound. [professional] Take Burke Road instead.";
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(plain),
            // Operator tags can only air on a v3 voice; every other model speaks them.
            Model = Option<string>.Some("eleven_v3"),
            TonedScript = Option<string>.Some(toned)
        };
        StubSuccessfulSynthesis();

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _audioPipeline.Received().SynthesizeWithDurationTargetAsync(
            toned, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(),
            Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(),
            Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
    }

    [Theory]
    [InlineData("eleven_multilingual_v2")]
    [InlineData("eleven_turbo_v2_5")]
    public async Task RevoiceOnly_OperatorTonedScript_IsRefused_WhenTheVoiceWouldSpeakTheTags(string model)
    {
        // The operator placed real tags, but this voice does not interpret them - it reads them out,
        // so "[solemn]" would air as the word "solemn". The plain words air instead, and the job log
        // says so: an operator who deliberately set a tone is entitled to know it was not used.
        const string plain = "The Monash is closed citybound. Take Burke Road instead.";
        const string toned = "[solemn] The Monash is closed citybound. [professional] Take Burke Road instead.";
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(plain),
            Model = Option<string>.Some(model),
            TonedScript = Option<string>.Some(toned)
        };
        StubSuccessfulSynthesis();

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _audioPipeline.DidNotReceive().SynthesizeWithDurationTargetAsync(
            toned, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(),
            Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(),
            Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
        await _audioPipeline.Received().SynthesizeWithDurationTargetAsync(
            plain, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(),
            Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(),
            Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
    }

    [Fact]
    public async Task RevoiceOnly_TonedReadThatOnlyFitsByTrimming_DropsTheToneAndAirsEveryWord()
    {
        // A tag slows the read, so a script that fitted plainly can overrun once tagged - and
        // the pipeline's last resort is to DROP trailing sentences and re-synthesise
        // (AudioPipelineService CASE 3). Losing a road closure to a delivery choice is exactly
        // the harm the word guard exists to prevent, so the tone goes, not the content.
        const string plain = "The Monash is closed citybound. Take Burke Road instead. Expect delays.";
        const string toned = "[solemn] The Monash is closed citybound. Take Burke Road instead. Expect delays.";
        const string trimmed = "[solemn] The Monash is closed citybound. Take Burke Road instead.";
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(plain),
            // Operator tags can only air on a v3 voice; every other model speaks them.
            Model = Option<string>.Some("eleven_v3"),
            TonedScript = Option<string>.Some(toned)
        };
        StubSuccessfulSynthesis();
        // The tagged take comes back a sentence short: the fitter cut it to make the slot.
        _audioPipeline.SynthesizeWithDurationTargetAsync(toned, Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], trimmed, 30.0) { SlotFilled = true }));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        // The approved words are re-voiced plainly instead of airing a cut bulletin.
        await _audioPipeline.Received().SynthesizeWithDurationTargetAsync(
            plain, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(),
            Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(),
            Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.ScriptText.HasValue && j.ScriptText.Value == plain),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevoiceOnly_PassesTheStationOutroAsTheTrimLock()
    {
        // Parity with ScriptGenerationEngine: an overrun is fitted by dropping trailing
        // sentences, and the station's closer sits at the very end. A re-voice onto a slower
        // voice is the easiest way to overrun a read that used to fit, so the re-voice path
        // must pass the same lockedSuffix the generation path does — it didn't, and the voice
        // change deleted the bookend first (dev, 2026-08-07).
        const string outro = "Download the iHeartRadio app — search 4CA Cairns.";
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(TestScript),
            Bookends = Option<ResolvedBookends>.Some(new ResolvedBookends("", outro, "bk-1", 3))
        };
        StubSuccessfulSynthesis();

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _audioPipeline.Received().SynthesizeWithDurationTargetAsync(
            Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
            Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(),
            Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(),
            Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: outro);
    }

    [Fact]
    public async Task RevoiceOnly_ReadTrimmedByTheSlotFit_TellsTheOperatorInTheJobLog()
    {
        // A re-voice promises the operator's exact words, but the pipeline's overrun fit may
        // drop trailing sentences (CASE 3) as its last resort — and on an UNTAGGED read the
        // toned-fallback word guard never runs, so the trim used to be completely silent: the
        // only trace was a shorter script, and the audio pre-review can't catch it because it
        // compares the transcript against the already-trimmed script (dev job 0c1c11e8,
        // 2026-08-07: a voice change silently deleted the station's drafted sign-off). The
        // words may go, but never quietly.
        const string full = "The Monash is closed citybound. Take Burke Road instead. Download the app for more.";
        const string trimmed = "The Monash is closed citybound. Take Burke Road instead.";
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(full)
        };
        StubSuccessfulSynthesis();
        // The fitter cut the read to make the slot: the take comes back a sentence short.
        _audioPipeline.SynthesizeWithDurationTargetAsync(full, Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], trimmed, 30.0) { SlotFilled = true }));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _jobRepository.Received().AddLogAsync(Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("Slot fit dropped the end of the read")),
            Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevoiceOnly_TonedScriptThatChangedTheWords_IsRefusedAndThePlainReadAirs()
    {
        // Tags are stripped before the word comparison, so anything inside brackets is never
        // safety-reviewed. A payload that also altered the SPOKEN words must therefore lose the
        // tone rather than reach air — the words that aired must be the words that were approved.
        const string plain = "The Monash is closed citybound. Take Burke Road instead.";
        const string tampered = "[solemn] The Monash is OPEN citybound. Take Burke Road instead.";
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(plain),
            // Operator tags can only air on a v3 voice; every other model speaks them.
            Model = Option<string>.Some("eleven_v3"),
            TonedScript = Option<string>.Some(tampered)
        };
        StubSuccessfulSynthesis();

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _audioPipeline.Received().SynthesizeWithDurationTargetAsync(
            plain, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(),
            Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(),
            Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
        await _audioPipeline.DidNotReceive().SynthesizeWithDurationTargetAsync(
            tampered, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(),
            Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(),
            Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
    }

    private void StubSuccessfulSynthesis()
    {
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(ci => Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], ci.ArgAt<string>(0), 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    [Fact]
    public async Task RevoiceOnly_TonedScriptWithNoTags_StillLetsTheStationDeliveryStyleRun()
    {
        // Found on dev 2026-08-02: a toned script that carries no tags directs nothing, so
        // treating it as "the operator took over" cost the station its configured delivery
        // style and gave nothing back. No tags means no takeover.
        const string plain = "The Monash is closed citybound. Take Burke Road instead.";
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(plain),
            // Operator tags can only air on a v3 voice; every other model speaks them.
            Model = Option<string>.Some("eleven_v3"),
            TonedScript = Option<string>.Some(plain),
            DeliveryStyle = Option<DeliveryStylePolicy>.Some(
                new DeliveryStylePolicy("Warm breakfast read.", ["[warmly]"], 2))
        };
        _scriptDirector.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success($"[warmly] {plain}"));
        StubSuccessfulSynthesis();

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _scriptDirector.Received().DirectAsync(plain, Arg.Any<DeliveryStylePolicy>(),
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevoiceOnly_RecordsThePronunciationVersionItActuallySynthesisedWith()
    {
        // Only ScriptGenerationEngine writes the pronunciation pin, and a re-voice never
        // runs it. So a re-voice that refreshed the dictionary synthesised with a NEWER
        // version than the job pinned, and left the record claiming the old one — the
        // popover cites the wrong version and a republish cannot reproduce the audio.
        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(TestScript),
            PronunciationStack = PronunciationStack.Of(
                new PronunciationLocator("dict-1", "version-REFRESHED"), null)
        };

        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(ci => Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], ci.ArgAt<string>(0), 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j =>
                j.PronunciationVersionId.HasValue &&
                j.PronunciationVersionId.Value == "version-REFRESHED" &&
                j.PronunciationDictionaryId.HasValue &&
                j.PronunciationDictionaryId.Value == "dict-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevoiceOnly_KeepsTheGeneratorInputSnapshotItCannotRebuild()
    {
        // A re-voice never re-fetches traffic, so its context carries no corridors and no good-run
        // roads. The evidence columns are already guarded against that, but the replay snapshot was
        // written through unguarded — so every operator re-voice blanked GeneratorInputJson and the
        // job could no longer be replayed. Found on job fa754637 (Gold Coast): GoodRunRoadsJson still
        // held its six roads while the snapshot recorded zero, and a regression fixture built from it
        // generated an empty script.
        var stored = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        stored.GeneratorInput = GeneratorInputSnapshot.FromContext(Ctx() with
        {
            GoodRunRoads = ["Gooding Drive", "Markeri Street", "Burleigh Road", "Benowa Road"]
        });
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(stored));

        var ctx = Ctx() with
        {
            RevoiceOnly = true,
            PreviousScript = Option<string>.Some(TestScript),
            GoodRunRoads = []
        };
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(ci => Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], ci.ArgAt<string>(0), 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.GeneratorInput != null && j.GeneratorInput.GoodRunRoads.Count == 4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScriptOnlyMode_ReturnsSuccessWithoutAudioPipeline()
    {
        var ctx = Ctx() with { ScriptOnly = true };
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript, Arg.Any<double>()).Returns(27.0);

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldBeEmpty();
        result.Value.Message.ShouldContain("ScriptOnly");
        await _audioPipeline.DidNotReceiveWithAnyArgs()
            .SynthesizeWithDurationTargetAsync(default!, default);
    }

    [Fact]
    public async Task EmptyScriptGenerated_ReturnsFailure()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript("", [])));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("empty");
    }

    [Fact]
    public async Task WhitespaceOnlyScript_ReturnsFailure()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript("   ", [])));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("empty");
    }

    [Fact]
    public async Task SaveFails_ReturnsFailure()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        byte[] audio = new byte[] { 1, 2, 3 };
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Blob storage unavailable"));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Blob storage unavailable");
    }

    [Fact]
    public async Task ZeroDurationAudio_ReturnsFailure()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        byte[] audio = new byte[] { 1, 2, 3 };
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(0.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("0s duration");
    }

    [Fact]
    public async Task SuccessfulBroadcast_RecordsActualDurationOnCalibration()
    {
        SetupSuccessfulPipeline();
        await _sut.OrchestrateAsync(Ctx());

        await _voiceCalibrationService.Received().RecordActualDurationAsync(
            Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(), Arg.Any<double>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulBroadcast_SavesJobWithWaitingApprovalStatus()
    {
        SetupSuccessfulPipeline();
        await _sut.OrchestrateAsync(Ctx());

        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.WaitingApproval), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentLimitedUnderrun_AfterRetries_FailsToFallback()
    {
        // ARN requires an exactly-slot-length file. When content + slow-to-fill genuinely cannot
        // reach the slot (SlotFilled=false on every attempt), the orchestrator fails the job rather
        // than ship a short bulletin — the existing fallback (last good same-day bulletin / filler
        // cart) then covers the air slot.
        byte[] audio = [1, 2, 3];
        SetupSuccessfulPipeline(audio: audio);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult(audio, TestScript, 30.0) { SpokenDurationSeconds = 21.0, SlotFilled = false }));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("could not fill");
        await _jobRepository.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.WaitingApproval), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnderrunCalibration_RecordsSpokenSpan_NotPaddedFileLength()
    {
        // The calibration EMA must learn from the SPOKEN span (21s → ~3.97 syll/s), not the padded
        // file length (30s → 2.77 syll/s) — otherwise it converges toward an artificially slow rate
        // that shrinks every future word budget and worsens the dead air (the self-poisoning loop).
        byte[] audio = [1, 2, 3];
        SetupSuccessfulPipeline(audio: audio);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult(audio, TestScript, 30.0) { SpokenDurationSeconds = 21.0 }));

        await _sut.OrchestrateAsync(Ctx());

        await _voiceCalibrationService.Received().RecordActualDurationAsync(
            Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(), 21.0, Arg.Any<CancellationToken>());
        await _voiceCalibrationService.DidNotReceive().RecordActualDurationAsync(
            Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(), 30.0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SmallTrailingSilence_WithinTolerance_IsAccepted()
    {
        // A near-target read (28.5s for a 30s slot) is slot-filled by the pipeline (slow-to-fill +
        // ≤0.3s pad), so SlotFilled=true and it is accepted.
        byte[] audio = [1, 2, 3];
        SetupSuccessfulPipeline(audio: audio);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult(audio, TestScript, 30.0) { SpokenDurationSeconds = 28.5, SlotFilled = true }));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.WaitingApproval), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulBroadcast_SendsNotification()
    {
        var notificationService = Substitute.For<INotificationService>();
        notificationService.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var incidentStateService = new IncidentStateService(
            Substitute.For<IIncidentStateRepository>(),
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        var voiceCal = Substitute.For<IVoiceCalibrationService>();
        voiceCal.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(20, 5.8, 1.45));

        var jobRepo = Substitute.For<IJobRepository>();
        jobRepo.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value));
        jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        jobRepo.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var sut = new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
                new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService, voiceCal, _broadcastOutput),
            new ContentProviders(_trafficProvider, Substitute.For<IWeatherProvider>(), Substitute.For<IFuelPriceProvider>(), Substitute.For<IRoutingProvider>()),
            incidentStateService, jobRepo,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 0 }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            notificationService, TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            new NoOpBroadcastMetrics(),
            Substitute.For<IPriorityRoadsRepository>(),
            Substitute.For<IFillerStatementsRepository>(),
            Substitute.For<IFutureLessonRepository>(),
            Substitute.For<IGuidanceRepository>(),
            Substitute.For<IEditorialSettingsRepository>(),
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService,
            _jobService,
            Substitute.For<ILogger<BroadcastOrchestrator>>());

        SetupSuccessfulPipeline();
        await sut.OrchestrateAsync(Ctx());

        await notificationService.Received().SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Overrun_PipelineCannotFit_ReturnsFailure()
    {
        // An overrun the pipeline cannot compress within the speed cap returns a Failure
        // (DurationActualTooFar); the orchestrator propagates it and the job fails to the fallback.
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Failure("Synthesized duration 35.0s is over target 30.0s"));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("over target");
    }

    [Fact]
    public async Task PermanentTtsError_AbortsRetries()
    {
        var sut = CreateSutWithRetries(2);

        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Failure(OperationalFailure.Permanent("tts.permanent", "BadRequest: invalid voice")));

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        // Should only have called GenerateScript once (no retries for permanent errors)
        await _scriptGenerator.Received(1).GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulBroadcast_ReturnsWavFileName()
    {
        SetupSuccessfulPipeline();

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        result.Value.FileName.ShouldEndWith(".wav");
    }

    [Fact]
    public async Task SuccessfulBroadcast_SetsScriptTextOnJob()
    {
        SetupSuccessfulPipeline();
        await _sut.OrchestrateAsync(Ctx());

        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.ScriptText.HasValue), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulBroadcast_SetsMeasuredDurationOnJob()
    {
        SetupSuccessfulPipeline();
        await _sut.OrchestrateAsync(Ctx());

        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.MeasuredDurationSec.HasValue), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulBroadcast_SetsAudioUrlOnJob()
    {
        SetupSuccessfulPipeline();
        await _sut.OrchestrateAsync(Ctx());

        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.AudioUrl.HasValue), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuccessfulBroadcast_AudioFileNameContainsJobId()
    {
        // Collision-proofing: same-second generations across markets (and future
        // multi-candidate buckets, SDD decision appendix A) must never share a blob name.
        SetupSuccessfulPipeline();
        await _sut.OrchestrateAsync(Ctx());

        await _broadcastOutput.Received(1).SaveAsync(
            Arg.Any<byte[]>(),
            Arg.Is<string>(name => name.Contains("test-job-id")),
            Arg.Any<CancellationToken>());
        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.AudioUrl.HasValue && j.AudioUrl.Value.Contains("test-job-id")
                && j.AudioUrl.Value.EndsWith(".wav")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScriptGenerationFails_UpdatesJobToFailedStatus()
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Failure("LLM unavailable"));

        await _sut.OrchestrateAsync(Ctx());

        await _jobRepository.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FallbackUsedJob_SkipsStatusUpdates()
    {
        var fallbackJob = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        fallbackJob.TransitionTo(JobStatus.FallbackUsed, utcNow: DateTime.UtcNow);
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(fallbackJob));

        SetupSuccessfulPipeline();
        var result = await _sut.OrchestrateAsync(Ctx());

        // SaveFinalBroadcastResultAsync returns failure for terminal-state jobs
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("terminal state");
    }

    [Fact]
    public async Task SaveFinalBroadcastResult_SaveJobFails_ReturnsFailure()
    {
        SetupSuccessfulPipeline();
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Table storage unavailable"));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Table storage unavailable");
    }

    [Fact]
    public async Task SaveFinalBroadcastResult_AlreadyApproved_ReturnsFailure()
    {
        var approvedJob = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        approvedJob.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        approvedJob.TransitionTo(JobStatus.Approved, utcNow: DateTime.UtcNow);
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(approvedJob));

        SetupSuccessfulPipeline();
        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("terminal state");
        await _jobRepository.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.WaitingApproval), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveFinalBroadcastResult_AlreadyPublished_ReturnsFailure()
    {
        var pubJob = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        pubJob.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        pubJob.TransitionTo(JobStatus.Approved, utcNow: DateTime.UtcNow);
        pubJob.TransitionTo(JobStatus.Published, utcNow: DateTime.UtcNow);
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(pubJob));

        SetupSuccessfulPipeline();
        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("terminal state");
        await _jobRepository.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.WaitingApproval), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveFinalBroadcastResult_AlreadyWaitingApproval_ReturnsFailure()
    {
        // Duplicate queue worker (redelivery racing the lease check): a second worker
        // reaching finalize after the first already parked the job at WaitingApproval
        // must not clobber the exact script/audio an operator is reviewing.
        const string reviewedScript = "Original reviewed script";
        const string reviewedAudioUrl = "original-audio.wav";
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<Job>.Success(PinnedJob(JobStatus.WaitingApproval, reviewedScript, reviewedAudioUrl)));

        SetupSuccessfulPipeline();
        var ctx = Ctx();
        var result = await _sut.OrchestrateAsync(ctx);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("operator-facing result");
        // Mid-pipeline draft persistence (PersistScriptOnJob against the worker's own
        // ephemeral read) is expected; the invariant is that no save ever parks the job
        // at WaitingApproval or attaches the duplicate worker's newly synthesized audio.
        await _jobRepository.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.WaitingApproval), Arg.Any<CancellationToken>());
        await _jobRepository.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.AudioUrl.GetValueOr(string.Empty).Contains(ctx.JobId)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveFinalBroadcastResult_AlreadyRejected_ReturnsFailure()
    {
        // Same race, but the operator already rejected. The duplicate worker must not
        // resurrect the job into WaitingApproval behind the operator's decision.
        const string reviewedScript = "Original reviewed script";
        const string reviewedAudioUrl = "original-audio.wav";
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<Job>.Success(PinnedJob(JobStatus.Rejected, reviewedScript, reviewedAudioUrl)));

        SetupSuccessfulPipeline();
        var ctx = Ctx();
        var result = await _sut.OrchestrateAsync(ctx);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("operator-facing result");
        // Same reasoning as the WaitingApproval variant: mid-pipeline draft persistence
        // is expected; no save may resurrect WaitingApproval or attach new audio.
        await _jobRepository.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.WaitingApproval), Arg.Any<CancellationToken>());
        await _jobRepository.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.AudioUrl.GetValueOr(string.Empty).Contains(ctx.JobId)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveFinalBroadcastResult_AlreadyFailed_IsReprocessed()
    {
        // A Failed job is reprocessable (transient-failure requeue / operator regenerate):
        // orchestration must advance it through to WaitingApproval, not bail like the
        // operator-final states above.
        var failedJob = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        failedJob.MarkFailed("Previous failure", DateTime.UtcNow);
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(failedJob));

        SetupSuccessfulPipeline();
        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task RetryOnNonPermanentError_RetriesGeneration()
    {
        var sut = CreateSutWithRetries(1);
        byte[] audio = new byte[] { 1, 2, 3 };

        // First attempt fails with non-permanent error, second succeeds
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(
                Result<AudioSynthesisResult>.Failure("Transient API error"),
                Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        await _scriptGenerator.Received(2).GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JobLoadFails_ReturnsResultAnyway()
    {
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("Job not found"));

        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        byte[] audio = new byte[] { 1, 2, 3 };
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // When job can't be loaded, SaveFinalBroadcastResultAsync returns failure
        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Failed to retrieve job");
    }

    [Fact]
    public async Task SuccessfulBroadcast_IncludesDebugInfo()
    {
        SetupSuccessfulPipeline();

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        result.Value.DebugInfo.ShouldNotBeNull();
        result.Value.DebugInfo.FinalDuration.ShouldBeGreaterThan(0);
        result.Value.DebugInfo.ScriptGenerator.ShouldNotBeNullOrEmpty();
    }

    private BroadcastOrchestrator CreateSutWithRetries(int maxRetries)
    {
        var incidentStateService = new IncidentStateService(
            Substitute.For<IIncidentStateRepository>(),
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        return new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
            new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService, _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
            incidentStateService, _jobRepository,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 0, MaxGenerationRetries = maxRetries }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            new NoOpBroadcastMetrics(),
            _priorityRoadsRepository,
            _fillerStatementsRepository,
            _marketFeedbackRepository,
            _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService,
            _jobService,
            Substitute.For<ILogger<BroadcastOrchestrator>>());
    }

    private BroadcastOrchestrator CreateSutWithReview(int maxReviewRetries = 1, int maxGenerationRetries = 0)
    {
        return new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
            new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService, _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
            new IncidentStateService(
                Substitute.For<IIncidentStateRepository>(),
                Substitute.For<ILogger<IncidentStateService>>(),
                Options.Create(new IncidentStateOptions()),
                TimeProvider.System),
            _jobRepository,
            Options.Create(new BroadcastOptions
            { MaxReviewRefineRetries = maxReviewRetries, MaxGenerationRetries = maxGenerationRetries }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            new NoOpBroadcastMetrics(),
            _priorityRoadsRepository,
            _fillerStatementsRepository,
            _marketFeedbackRepository,
            _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService,
            _jobService,
            Substitute.For<ILogger<BroadcastOrchestrator>>());
    }

    private BroadcastOrchestrator CreateSutWithMonitoredRoutes(MonitoredRoutesOptions monitoredRoutesOptions)
    {
        return new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
            new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService, _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
            new IncidentStateService(
                _incidentStateRepository,
                Substitute.For<ILogger<IncidentStateService>>(),
                Options.Create(new IncidentStateOptions()),
                TimeProvider.System),
            _jobRepository,
            Options.Create(new BroadcastOptions { MaxReviewRefineRetries = 0 }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            new NoOpBroadcastMetrics(),
            _priorityRoadsRepository,
            _fillerStatementsRepository,
            _marketFeedbackRepository,
            _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(monitoredRoutesOptions),
            _bulletinFallbackService,
            _jobService,
            Substitute.For<ILogger<BroadcastOrchestrator>>());
    }

    private void SetupScriptOnlyPipeline(string script = TestScript)
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(script, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
    }

    // ── ReviewAndRefineScriptAsync ────────────────────────────────────

    [Fact]
    public async Task ReviewApproved_NoRefineNeeded()
    {
        var sut = CreateSutWithReview();
        SetupScriptOnlyPipeline();
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        var result = await sut.OrchestrateAsync(Ctx() with { ScriptOnly = true });

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ReviewFeedback_PatchSucceeds_UsesPatchedSentence()
    {
        const string refined = "Refined traffic script with updated wording for Sydney listeners";
        var sut = CreateSutWithReview();
        SetupScriptOnlyPipeline();
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(1, "STYLE", "Too informal")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));
        _scriptRefiner.PatchSentenceAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<DraftedSentence>.Success(
                call.Arg<DraftedSentence>() with { Text = refined }));

        var result = await sut.OrchestrateAsync(
            Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(30) });

        result.IsSuccess.ShouldBeTrue();
        await _scriptRefiner.Received().PatchSentenceAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Is<DraftedSentence>(s => s.Order == 1),
            Arg.Is<string>(f => f.Contains("Too informal")), Arg.Any<IReadOnlyList<EvidenceUnit>>(),
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReviewFails_UsesCurrentScript()
    {
        var sut = CreateSutWithReview();
        SetupScriptOnlyPipeline();
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Failure("LLM timeout"),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));

        var result = await sut.OrchestrateAsync(Ctx() with { ScriptOnly = true });

        // The main-loop review call errors (transient); the final grounding gate's own review then
        // succeeds and approves — a transient review failure is recovered, not fatal.
        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ReviewFeedback_PatchFails_UsesCurrentScript()
    {
        var sut = CreateSutWithReview();
        SetupScriptOnlyPipeline();
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(1, "STYLE", "Needs work")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));
        _scriptRefiner.PatchSentenceAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedSentence>.Failure("Patch error"));

        var result = await sut.OrchestrateAsync(Ctx() with { ScriptOnly = true });

        result.IsSuccess.ShouldBeTrue();
    }

    // ── ApplyStyleGateAsync ──────────────────────────────────────────

    [Fact]
    public async Task StyleGate_NoIssues_PassesThrough()
    {
        // Default TestScript has no style issues — should pass through
        SetupSuccessfulPipeline();
        var result = await _sut.OrchestrateAsync(Ctx());
        result.IsSuccess.ShouldBeTrue();

    }
    [Fact]
    public async Task StyleGate_IssuesDetected_PatchSucceeds()
    {
        const string badScript = "Alex: There is heavy traffic on the M4 motorway heading westbound today";
        const string fixedScript = "There is heavy traffic on the M4 motorway heading westbound today folks";
        var sut = CreateSutWithReview(0);
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(badScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _scriptRefiner.PatchSentenceAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Result<DraftedSentence>.Success(
                call.Arg<DraftedSentence>() with { Text = fixedScript }));

        var result = await sut.OrchestrateAsync(Ctx() with { ScriptOnly = true });

        result.IsSuccess.ShouldBeTrue();
        await _scriptRefiner.Received().PatchSentenceAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Is<DraftedSentence>(s => s.Order == 1),
            Arg.Any<string>(), Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StyleGate_IssuesDetected_PatchFails_ReportsFailure()
    {
        const string badScript = "Alex: There is heavy traffic on the M4 motorway heading westbound today";
        var sut = CreateSutWithReview(0);
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(badScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _scriptRefiner.PatchSentenceAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedSentence>.Failure("Patch unavailable"));

        var result = await sut.OrchestrateAsync(Ctx() with { ScriptOnly = true });

        result.IsFailure.ShouldBeTrue();
        await _scriptRefiner.Received().PatchSentenceAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── EnsureWordTargetComplianceAsync ──────────────────────────────

    [Fact]
    public async Task WordTarget_WithinTolerance_PassesThrough()
    {
        // Script word count close to target → no correction needed
        SetupScriptOnlyPipeline();
        // TargetWordCountOverride is set by the generation loop from calibration, simulate it
        var ctx = Ctx() with
        {
            ScriptOnly = true,
            TargetWordCountOverride = Option<int>.Some(WordCounter.Count(TestScript))
        };

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        // Refiner not called for word target correction (only possibly for style gate)
    }

    [Fact]
    public async Task WordTarget_OvershootIsAccepted_ForPipelineToTrim()
    {
        // Overshoot is no longer a word-count failure: the gate accepts a slightly-long script
        // and the audio pipeline trims it to the slot (measure-then-fit). Any refining here is
        // for style only — the word count itself does not block.
        const string longScript =
            "There is very heavy traffic on all major roads across greater Sydney across the network with delays expected on the M4 M5 and Pacific Highway heading into the city";
        var sut = CreateSutWithReview(0);
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(longScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        // Word target set lower than script count to trigger correction
        int wordCount = WordCounter.Count(longScript);
        var ctx = Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(wordCount - 15) };

        var result = await sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
    }

    // Regression, deployed dev jobs fd6283f003 (melbourne) and 8d8e5ec168 (darwin), both 22s slots:
    // the review-refine ratchet scored (fidelity, style, word-DEFICIT), and deficit clamps at zero
    // above target, so an inflated candidate was previously committed on the deficit axis. The
    // shipped script must now come back inside the ceiling.
    [Fact]
    public async Task InflatingDraft_IsCappedBackToTheWordCeiling()
    {
        const string drafted =
            "The M4 is running slowly at Church Street. Botany Road has minor delays at Hale Street.";
        // A refine that adds five more traffic sentences and clears nothing: pure inflation.
        int targetWords = WordCounter.Count(drafted);

        var sut = CreateSutWithReview(1);
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(drafted, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, string.Empty)));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var ctx = Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(targetWords) };
        var result = await sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.HasValue.ShouldBeTrue();
        ScriptWordCeiling.Excess(savedJob.ScriptText.Value, targetWords).ShouldBe(0);
        // The lead item survives: the cap drops the least important trailing items, never the top.
        savedJob.ScriptText.Value.ShouldStartWith("The M4 is running slowly at Church Street.");
    }

    // Regression, deployed dev jobs 027729aac1 and d94648edf6 (both sydney, 22s): the persisted plain
    // script read "... Passing clouds, 14 degrees. . That's KIIS traffic and weather together." A
    // delivery tag sat between the last body sentence and the station outro, and stripping it
    // collapsed the punctuation to ". ". The generator's normaliser chain runs BEFORE the strip, so
    // only a normalise on the stripped text catches it. The operator sees this script in the console
    // and it lands in the word timings, so the artifact must never persist.
    [Fact]
    public async Task DeliveryTagStrip_DoesNotLeaveADoubledTerminatorInThePersistedScript()
    {
        const string plain = "Botany Road's under the pump at Hale Street. Passing clouds, 14 degrees.";
        // The director returns the same words with a tag where the artifact forms.
        const string tagged =
            "Botany Road's under the pump at Hale Street. Passing clouds, 14 degrees. [gently]";

        SetupSuccessfulPipeline(plain);
        _scriptDirector.DirectAsync(Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success(tagged));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.HasValue.ShouldBeTrue();
        savedJob.ScriptText.Value.ShouldNotContain(". .");
        savedJob.ScriptText.Value.ShouldNotContain(" .");
    }

    // ── Final grounding check after review-refine exhaustion ──

    [Fact]
    public async Task FinalGrounding_MainLoopExhaustsWithoutApproval_DropsFlaggedSentence()
    {
        const string script = "An unsupported claim leads this bulletin. Traffic is moving on the M4 today.";
        var sut = CreateSutWithReview(2);
        SetupScriptOnlyPipeline(script);

        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false, "Needs a clearer opening")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false, "Still too vague for broadcast")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(1, "NOT_FROM_DATA", "Claims a location not supported by the source data")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(
            Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(30) });

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.Value.ShouldBe("Traffic is moving on the M4 today.");
        await _scriptReviewer.Received(4).ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
        Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
    }

    [Fact]
    public async Task FinalGrounding_MainLoopExhaustsButGroundingApprovesUnchanged_ShipsAsIs()
    {
        var sut = CreateSutWithReview(2);
        SetupScriptOnlyPipeline();

        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false, "Needs a clearer opening")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false, "Still too vague for broadcast")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(
            Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(30) });

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.ShouldBe(Option<string>.Some(TestScript));
        await _scriptReviewer.Received(3).ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
        Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
    }

    [Fact]
    public async Task FinalGrounding_MainLoopExhaustsAndGroundingNeverApproves_FailsGenerationAttempt()
    {
        var sut = CreateSutWithReview(2);
        SetupScriptOnlyPipeline();

        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false, "Needs a clearer opening")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false, "Still too vague for broadcast")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(1, "NOT_FROM_DATA", "Claims a location not supported by the source data")])),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false,
                    [ReviewIssue.ForSentence(1, "NOT_FROM_DATA", "Still not grounded in the source data")])));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(
            Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(30) });

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("could not isolate the defect");
        savedJob.ShouldNotBeNull();
        savedJob!.Status.ShouldBe(JobStatus.Failed);
        savedJob.ScriptText.HasValue.ShouldBeTrue();
        savedJob.ScriptText.GetValueOr(string.Empty).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Ratchet_RefineThatShortensWithoutClearingDefect_IsRejectedAndFullerScriptKept()
    {
        // Review/refine ratchet (2026-07-13): a refine that only SHORTENS an already full-length
        // script to satisfy a soft/LLM-only flag — clearing no deterministic defect — is a
        // regression on the duration axis and must be REJECTED, keeping the longer script. This is
        // the word-shave that starved the 30s slot (77 -> 74 -> content-limited fallback). Without
        // the ratchet the shave would commit and ship; with it, the fuller draft survives.
        const string draftScript =
            "Traffic across Sydney is easing back to a normal weekday pattern across the network, with the usual steady flow returning to the main routes and no major hold-ups reported anywhere across the network right now";
        var sut = CreateSutWithReview(1);
        SetupScriptOnlyPipeline(draftScript);

        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(false, "Reads a little flat — tighten it")),
                Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Pin the word target to the draft's length so the draft sits at-target (deficit 0) and the
        // shave lands far under — the exact condition the ratchet's duration axis must reject.
        var ctx = Ctx() with
        {
            ScriptOnly = true,
            TargetWordCountOverride = Option<int>.Some(WordCounter.Count(draftScript))
        };
        var result = await sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        // The shortening refine was rejected — the longer, slot-filling draft ships, not the shave.
        savedJob!.ScriptText.ShouldBe(Option<string>.Some(draftScript));
    }

    [Fact]
    public async Task FinalGrounding_MainLoopApprovesCleanly_GroundingCheckNeverRuns()
    {
        // THE regression guard: when the main loop approves cleanly on its very first pass (the
        // already-working happy path), the new final grounding check must add ZERO extra reviewer
        // calls. Uses the SAME nonzero-MaxReviewRefineRetries SUT as the other 3 tests so this is
        // a meaningful test of the new gate, not a vacuous pass from a disabled main loop.
        var sut = CreateSutWithReview(2);
        SetupScriptOnlyPipeline();

        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        var result = await sut.OrchestrateAsync(
            Ctx() with { ScriptOnly = true, TargetWordCountOverride = Option<int>.Some(30) });

        result.IsSuccess.ShouldBeTrue();
        // Exactly one call, ever — proving the new grounding check adds zero extra calls on the
        // already-working happy path.
        await _scriptReviewer.Received(1).ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
        Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
    }

    // ── ExtractDurationFromError (tested via retry behavior) ─────────

    [Fact]
    public async Task ExtractDuration_ValidPattern_UsedInRetryAdjustment()
    {
        // When audio fails with "Actual duration 35.2s", the retry loop extracts 35.2
        // and uses it to adjust word targets → observable as different target in retry
        var sut = CreateSutWithRetries(1);
        byte[] audio = new byte[] { 1, 2, 3 };
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        // First attempt fails with extractable duration, second succeeds
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(
                Result<AudioSynthesisResult>.Failure("Actual duration 35.2s exceeded target"),
                Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(Ctx());

        // Retried and succeeded → proves duration was extracted and used
        result.IsSuccess.ShouldBeTrue();
        await _scriptGenerator.Received(2).GenerateScriptAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractDuration_NoMatch_RetriesWithZeroDuration()
    {
        // Error without "Actual duration" pattern → extracted duration is 0
        var sut = CreateSutWithRetries(1);
        byte[] audio = new byte[] { 1, 2, 3 };
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(
                Result<AudioSynthesisResult>.Failure("Something went wrong"),
                Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(Ctx());

        // Still retries (non-permanent error) — proves no-match returns None(0)
        result.IsSuccess.ShouldBeTrue();
    }

    // ── IsPermanentTtsError ──────────────────────────────────────────

    [Theory]
    [InlineData("BadRequest: invalid input")]
    [InlineData("Unauthorized: bad API key")]
    [InlineData("Forbidden: access denied")]
    [InlineData("PaymentRequired: subscription expired")]
    public async Task PermanentTtsError_AbortsSingleRetry(string errorMessage)
    {
        var sut = CreateSutWithRetries(2);
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Failure(OperationalFailure.Permanent("tts.permanent", errorMessage)));

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(errorMessage);
        // Only 1 attempt — no retries for permanent errors
        await _scriptGenerator.Received(1).GenerateScriptAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(), Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    // ── CalculateMaxCorridors (tested via corridor selection behavior) ──

    [Theory]
    [InlineData(30.0, 6)] // 30/5 = 6, clamped to min 6
    [InlineData(15.0, 6)] // 15/5 = 3, clamped to min 6
    [InlineData(60.0, 12)] // 60/5 = 12
    [InlineData(100.0, 15)] // 100/5 = 20, clamped to max 15
    public async Task CalculateMaxCorridors_BoundaryValues(double targetDuration, int expectedMax)
    {
        // Create more corridors than any max so selection is visible
        var corridors = Enumerable.Range(0, 20)
            .Select(i => new TrafficCorridor($"Road{i}", 3, 2, 1000, 50, "congestion", $"Desc{i}", []))
            .ToList();
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));

        var request = new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = targetDuration,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value
        };

        // Script generator captures the context — we can inspect corridor count
        BroadcastWithTraffic? capturedContext = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capturedContext = call.Arg<BroadcastWithTraffic>();
                return Result<DraftedScript>.Failure("stop here");
            });

        await _sut.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);

        capturedContext.ShouldNotBeNull();
        capturedContext!.Corridors.Count.ShouldBe(expectedMax);
    }

    [Fact]
    public async Task Orchestrate_StoresFullIncidentPool_NotJustSelectedCorridors()
    {
        // 8 corridors, one incident each. At 30s only 6 corridors are selected, so the
        // full pool (8) must exceed the bulletin subset (6) — that gap is exactly what
        // the review UI shows under "Not in this bulletin".
        var corridors = Enumerable.Range(0, 8)
            .Select(i => new TrafficCorridor(
                $"Road{i}", 3, 2, 1000, 100 - i, "congestion", $"Desc{i}",
                new[] { new TrafficIncident($"inc-{i}", $"Delay on Road{i}", 1, $"Road{i}", "congestion") }))
            .ToList();
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // Incidents are persisted before generation; failing the stub stops the rest cheaply.
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Failure("stop here"));

        var request = new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value
        };

        await _sut.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);

        savedJob.ShouldNotBeNull();
        savedJob!.AllIncidents.Count.ShouldBe(8);  // every fetched corridor's incident
        savedJob.Incidents.Count.ShouldBe(6);      // only the selected corridors back the bulletin
        savedJob.Incidents.Select(i => i.Key)
            .ShouldBeSubsetOf(savedJob.AllIncidents.Select(i => i.Key));
    }

    [Fact]
    public async Task Orchestrate_NarrativeIncidentChangeSummary_ExcludesItemSuppressedByReconciliation()
    {
        // A DIFFERENT HERE incident is newly active on M1 while a previous M1 incident has
        // cleared. The raw audit trail (IncidentChangeSummary) keeps both facts as ground truth,
        // but the narrative-facing summary (NarrativeIncidentChangeSummary — what the script
        // generator actually saw after IncidentDeltaReconciler.SuppressClearedOnActiveRoads)
        // must drop the "cleared" claim so a correct on-air statement is never audited as
        // fabricated (2026-07-07, job 4417add2).
        var corridors = new List<TrafficCorridor>
        {
            new("M1", 3, 2, 1000, 100, "roadworks", "Roadworks on M1",
                new[] { new TrafficIncident("active-m1", "Roadworks on M1", 3, "M1", "roadworks") })
        };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success(new[]
            {
                new IncidentState("cleared-m1", "sydney", DateTime.UtcNow, DateTime.UtcNow,
                    IncidentStateStatus.Active, "roadworks", 2, 500, "hash", "HERE", DateTime.UtcNow)
                {
                    LastReportedAt = Option<DateTime>.Some(DateTime.UtcNow),
                    RoadName = Option<string>.Some("M1")
                }
            }));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // Incidents are persisted before generation; failing the stub stops the rest cheaply.
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Failure("stop here"));

        var request = new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value
        };

        await _sut.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);

        savedJob.ShouldNotBeNull();
        savedJob!.IncidentChangeSummary.Value.ClearedCount.ShouldBe(1);
        savedJob.NarrativeIncidentChangeSummary.Value.ClearedCount.ShouldBe(0);
        savedJob.NarrativeIncidentChangeSummary.Value.ClearedCount
            .ShouldNotBe(savedJob.IncidentChangeSummary.Value.ClearedCount);
    }

    [Fact]
    public async Task Orchestrate_NoPublishedPredecessorToday_SuppressesIncidentChangesFromPrompt()
    {
        // No same-day Published bulletin exists (default _jobRepository setup returns None from
        // GetLatestBulletinForMarketTodayAsync) — "changes since your last bulletin"/"cleared"
        // framing must not reach the writer even though the traffic feed genuinely changed.
        var corridors = new List<TrafficCorridor>
        {
            new("M4", 3, 2, 1000, 50, "congestion", "Crash on M4",
                new[] { new TrafficIncident("inc-m4", "Crash on M4", 3, "M4", "accident") })
        };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success(new[]
            {
                new IncidentState("cleared-1", "sydney", DateTime.UtcNow, DateTime.UtcNow,
                    IncidentStateStatus.Active, "roadworks", 2, 500, "hash", "HERE", DateTime.UtcNow)
                {
                    LastReportedAt = Option<DateTime>.Some(DateTime.UtcNow),
                    RoadName = Option<string>.Some("Pacific Highway")
                }
            }));

        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var request = new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value
        };

        await _sut.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.IncidentChanges.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task Orchestrate_PublishedPredecessorToday_IncludesIncidentChangesInPrompt()
    {
        var corridors = new List<TrafficCorridor>
        {
            new("M4", 3, 2, 1000, 50, "congestion", "Crash on M4",
                new[] { new TrafficIncident("inc-m4", "Crash on M4", 3, "M4", "accident") })
        };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));

        _incidentStateRepository.GetCurrentByMarketAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success(new[]
            {
                new IncidentState("cleared-1", "sydney", DateTime.UtcNow, DateTime.UtcNow,
                    IncidentStateStatus.Active, "roadworks", 2, 500, "hash", "HERE", DateTime.UtcNow)
                {
                    LastReportedAt = Option<DateTime>.Some(DateTime.UtcNow),
                    RoadName = Option<string>.Some("Pacific Highway")
                }
            }));

        var prior = Job.Create("prior-published", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        prior.ScriptText = Option<string>.Some("earlier published bulletin");
        _jobRepository.GetLatestBulletinForMarketTodayAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(prior)));

        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var request = new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value
        };

        await _sut.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.IncidentChanges.HasValue.ShouldBeTrue();
        captured.IncidentChanges.Value.ClearedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("cleared-1");
    }

    [Fact]
    public async Task Orchestrate_PopulatesJobMonitoredRoutes_FromConfiguredMarketRoutes()
    {
        var corridors = new List<TrafficCorridor>
        {
            new("M4", 3, 2, 1000, 50, "congestion", "Crash on M4",
                new[] { new TrafficIncident("inc-m4", "Crash on M4", 3, "M4", "accident") })
        };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));

        _routingProvider.GetDriveTimeAsync(
                Arg.Any<(double, double)>(), Arg.Any<(double, double)>(), Arg.Any<CancellationToken>())
            .Returns(Result<DriveTimeResult>.Success(new DriveTimeResult(42, 20, 15.5)));

        var monitoredRoutesOptions = new MonitoredRoutesOptions
        {
            Markets = new Dictionary<string, List<MonitoredRouteDefinition>>(StringComparer.OrdinalIgnoreCase)
            {
                ["sydney"] =
                [
                    new MonitoredRouteDefinition
                    {
                        Name = "Airport Run",
                        Origin = new CoveragePoint(-33.8, 151.2),
                        Destination = new CoveragePoint(-33.9, 151.1)
                    }
                ]
            }
        };
        var sut = CreateSutWithMonitoredRoutes(monitoredRoutesOptions);

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Failure("stop here"));

        var request = new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value
        };

        await sut.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);

        savedJob.ShouldNotBeNull();
        savedJob!.MonitoredRoutes.Count.ShouldBe(1);
        var route = savedJob.MonitoredRoutes.Single();
        route.Name.ShouldBe("Airport Run");
        route.DriveTimeMinutes.ShouldBe(42);
        route.NormalDriveTimeMinutes.ShouldBe(20);
    }

    // ── TryRefreshTrafficAsync ───────────────────────────────────────

    [Fact]
    public async Task EmptyCorridors_TriggersReFetch()
    {
        var jobWithGeo = Job.Create("test-job", "Station", "Market", 30, DateTime.UtcNow,
            Option<double>.Some(-33.8), Option<double>.Some(151.2), Option<double>.Some(25)).Value;
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(jobWithGeo));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var freshCorridors = new List<TrafficCorridor>
        {
            new("M4", 3, 2, 5000, 80, "congestion", "Delays on M4", [])
        };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(freshCorridors));

        SetupScriptOnlyPipeline();

        var ctx = Ctx() with { ScriptOnly = true, Corridors = [] };
        _ = await _sut.OrchestrateAsync(ctx);

        // Traffic provider was called to re-fetch
        await _trafficProvider.Received().GetCorridorsAsync(
            Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }


    // ── AI Script Safety Review ───────────────────────────────────────

    [Fact]
    public async Task OrchestrateAsync_SafetyReviewBlocks_GenerationFails()
    {
        SetupSuccessfulPipeline();
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(new ScriptSafetyResult(false,
                ["self-harm: implies a person jumped (\"man jumped from the bridge\")"])));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Content safety violation");
        result.Error.ShouldContain("self-harm");
        await _jobRepository.Received().AddLogAsync(Arg.Any<string>(),
            Arg.Is<string>(m => m.StartsWith("AI safety review blocked")), Arg.Any<JobLogLevel>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrchestrateAsync_SafetyReviewerUnavailable_FailsOpen()
    {
        SetupSuccessfulPipeline();
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Failure("Safety failed (503 ServiceUnavailable)"));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        await _jobRepository.Received().AddLogAsync(Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("AI safety review unavailable")), Arg.Any<JobLogLevel>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrchestrateAsync_SafetyReviewPasses_LoggedForOperator()
    {
        SetupSuccessfulPipeline();

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        await _scriptSafetyReviewer.Received().ReviewScriptSafetyAsync(TestScript, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _jobRepository.Received().AddLogAsync(Arg.Any<string>(),
            Arg.Is<string>(m => m == "AI safety review passed"), Arg.Any<JobLogLevel>(),
            Arg.Any<CancellationToken>());
    }

    // ── Content sources: weather / fuel / priority roads / curation (WS1, WS2) ──

    private Job SetupJobWithGeo()
    {
        var jobWithGeo = Job.Create("test-job", "Station", "Market", 30, DateTime.UtcNow,
            Option<double>.Some(-33.8), Option<double>.Some(151.2), Option<double>.Some(25)).Value;
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(jobWithGeo));
        return jobWithGeo;
    }

    private BroadcastRequest Request(BulletinContentSelection? content = null)
    {
        return new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value,
            VoiceId = Option<string>.Some("test-voice-id"),
            Content = content ?? BulletinContentSelection.Default
        };
    }

    [Fact]
    public async Task CuratedRegeneration_FiltersAndOrdersFreshCorridors()
    {
        SetupJobWithGeo();
        var fresh = new List<TrafficCorridor>
        {
            new("M4", 1, 2, 5000, 90, "accident", "Crash on M4", []),
            new("M5", 2, 2, 4000, 70, "congestion", "Delays on M5", []),
            new("Punt Road", 3, 3, 2000, 50, "congestion", "Slow on Punt Rd", [])
        };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(fresh));
        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var ctx = Ctx() with
        {
            Corridors = [],
            CuratedCorridorRoads = Option<IReadOnlyList<string>>.Some(["Punt Road", "M4"])
        };

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Corridors.Select(c => c.RoadName).ShouldBe(["Punt Road", "M4"]);
        captured.CuratedCorridorRoads.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task CuratedRegeneration_AllSelectionsCleared_FallsBackToStandardRanking()
    {
        SetupJobWithGeo();
        var fresh = new List<TrafficCorridor>
        {
            new("M4", 1, 2, 5000, 90, "accident", "Crash on M4", [])
        };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(fresh));
        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var ctx = Ctx() with
        {
            Corridors = [],
            CuratedCorridorRoads = Option<IReadOnlyList<string>>.Some(["Old Cleared Road"])
        };

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Corridors.Select(c => c.RoadName).ShouldBe(["M4"]);
        captured.CuratedCorridorRoads.HasValue.ShouldBeFalse(); // curation dropped, standard ranking
        await _jobRepository.Received().AddLogAsync(Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("curated corridors have cleared")), Arg.Any<JobLogLevel>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WeatherSelected_FetchedAndAttachedAndPersisted()
    {
        var corridors = new List<TrafficCorridor> { new("M4", 1, 2, 5000, 90, "accident", "Crash", []) };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));
        _weatherProvider.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Success(new WeatherReport(
                "Sunny", 24.5, Option<string>.None, 5, [])));
        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(Request(new BulletinContentSelection(true, Weather: true)), Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Weather.HasValue.ShouldBeTrue();
        captured.Weather.Value.Conditions.ShouldBe("Sunny");
        savedJob.ShouldNotBeNull();
        savedJob!.Weather.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task WeatherProviderFails_BulletinContinuesWithoutWeather()
    {
        var corridors = new List<TrafficCorridor> { new("M4", 1, 2, 5000, 90, "accident", "Crash", []) };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));
        _weatherProvider.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Failure("HERE weather down"));
        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var result = await _sut.OrchestrateAsync(Request(new BulletinContentSelection(true, Weather: true)), Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Weather.HasValue.ShouldBeFalse();
        await _jobRepository.Received().AddLogAsync(Arg.Any<string>(),
            Arg.Is<string>(m => m.Contains("Weather data unavailable")), Arg.Any<JobLogLevel>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrafficDisabled_SkipsTrafficFetch_GeneratesFromWeather()
    {
        _weatherProvider.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Success(new WeatherReport(
                "Storms", 18.0, Option<string>.None, 90, ["Severe thunderstorm warning"])));
        SetupSuccessfulPipeline();

        var result = await _sut.OrchestrateAsync(
            Request(new BulletinContentSelection(Traffic: false, Weather: true)),
            Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _trafficProvider.DidNotReceiveWithAnyArgs()
            .GetCorridorsAsync(default!, default, default);
    }

    [Fact]
    public async Task TrafficDisabled_NoContentAvailable_FailsJob()
    {
        _weatherProvider.GetWeatherAsync(Arg.Any<CoverageArea>(), Arg.Any<CancellationToken>())
            .Returns(Result<WeatherReport>.Failure("down"));

        var result = await _sut.OrchestrateAsync(
            Request(new BulletinContentSelection(Traffic: false, Weather: true)),
            Phase.FetchAndGenerate, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("No content available");
    }

    [Fact]
    public async Task PriorityRoads_BoostIncidents_AndFlowTier_ReportGoodRuns()
    {
        // "Back Street" outranks "M1" on raw score; M1 is a priority road and must win after boost.
        // "Pacific Motorway" is a watched priority road that the FLOW FEED reports as flowing -> good run.
        var corridors = new List<TrafficCorridor>
        {
            new("Back Street", 3, 4, 1000, 100, "congestion", "Slow", []),
            new("M1", 2, 1, 5000, 90, "congestion", "Delays on M1", [])
        };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));
        _trafficProvider.GetFreeFlowingRoadsAsync(
                Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<IReadOnlyList<FlowRoad>>.Success([new FlowRoad("Pacific Motorway", 1.5, 9000)]));
        _priorityRoadsRepository.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([new PriorityRoad("M1"), new PriorityRoad("Pacific Motorway")]));
        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var result = await _sut.OrchestrateAsync(Request(), Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.Corridors[0].RoadName.ShouldBe("M1"); // 90 * 1.3 = 117 > 100
        captured.GoodRunRoads.ShouldBe(["Pacific Motorway"]);
    }

    [Fact]
    public async Task FlowPool_AppliesIncidentExclusionsBeforeFinalTwentyRoadCap()
    {
        var corridors = Enumerable.Range(0, 5)
            .Select(index => new TrafficCorridor(
                $"Road {index}", 2, 2, 1_000, 90 - index, "accident", "Crash", []))
            .ToList();
        var flowRoads = Enumerable.Range(0, 25)
            .Select(index => new FlowRoad($"Road {index}", 1.0, 5_000 - index))
            .ToList();
        _trafficProvider.GetCorridorsAsync(
                Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));
        _trafficProvider.GetFreeFlowingRoadsAsync(
                Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<string>?>())
            .Returns(call =>
            {
                var excluded = call.ArgAt<IReadOnlyList<string>?>(3);
                IReadOnlyList<FlowRoad> available = excluded is null
                    ? flowRoads.Take(24).ToList()
                    : flowRoads.Skip(19).ToList();
                return Result<IReadOnlyList<FlowRoad>>.Success(available);
            });
        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(
                Arg.Do<BroadcastWithTraffic>(context => captured = context),
                Arg.Any<double>(),
                Arg.Any<VoiceSettings>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var result = await _sut.OrchestrateAsync(
            Request(), Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.FlowPool.Count.ShouldBe(20);
        captured.FlowPool.Select(road => road.RoadName)
            .ShouldBe(Enumerable.Range(5, 20).Select(index => $"Road {index}"));
        await _trafficProvider.Received(2).GetFreeFlowingRoadsAsync(
            Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>(),
            Arg.Any<IReadOnlyList<string>?>());
    }

    /// <summary>
    ///     The name a good run AIRS under must come from the provider, never from the operator's
    ///     configured string. A watched road absent from the flow feed used to be synthesised into
    ///     the pool under whatever was typed, so a misspelling — which can never match a corridor,
    ///     and therefore always looked "flowing" — was read out verbatim (dev, 2026-08-12).
    /// </summary>
    [Fact]
    public async Task WatchedRoadAbsentFromFlowFeed_IsNeverReportedAsAGoodRun()
    {
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(
                [new TrafficCorridor("M1", 2, 1, 5000, 90, "congestion", "Delays on M1", [])]));
        // The feed reports one flowing road, and it is NOT the misspelling below.
        _trafficProvider.GetFreeFlowingRoadsAsync(
                Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<IReadOnlyList<FlowRoad>>.Success([new FlowRoad("Pacific Motorway", 1.5, 9000)]));
        _priorityRoadsRepository.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([new PriorityRoad("Pacfic Motorwy")]));
        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var result = await _sut.OrchestrateAsync(Request(), Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.GoodRunRoads.ShouldNotContain("Pacfic Motorwy");
        captured.FlowPool.Select(f => f.RoadName).ShouldNotContain("Pacfic Motorwy");
    }

    /// <summary>
    ///     The boost fields must SURVIVE onto the persisted job. `AudioFinalizer` built its
    ///     `JobFlowRoad` rows positionally and passed `f.FunctionalClass` into the 8th slot, which is
    ///     `BaseScore` (double?) — the int converted implicitly, so it compiled silently, wrote the
    ///     functional class into BaseScore, never persisted BoostAmount at all, and left
    ///     FunctionalClass on its default of 3. The review rundown therefore rendered a bare "(+)"
    ///     with no amount (dev job e4dae3b1, every flow road: BaseScore 3, BoostAmount null, FC 3).
    ///     The persistence round-trip tests never caught it because they construct JobFlowRoad with
    ///     NAMED arguments — the defect was at this call site, not in the mapper.
    /// </summary>
    [Fact]
    public async Task PriorityBoostedFlowRoad_PersistsItsBoostAmountBaseScoreAndFunctionalClass()
    {
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(
                [new TrafficCorridor("M4", 1, 2, 5000, 90, "accident", "Crash", [])]));
        // A motorway (functional class 1) so a leaked default of 3 is unmistakable.
        _trafficProvider.GetFreeFlowingRoadsAsync(
                Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<IReadOnlyList<FlowRoad>>.Success(
                [new FlowRoad("Pacific Motorway", 1.5, 9000, FunctionalClass: 1)]));
        _priorityRoadsRepository.GetRoadsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PriorityRoad>>.Success([new PriorityRoad("Pacific Motorway")]));
        SetupSuccessfulPipeline();

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(Request(), Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        var flow = savedJob!.FlowRoads.Single(f => f.RoadName == "Pacific Motorway");

        flow.PriorityBoosted.ShouldBeTrue();
        // The direct cause of the bare "(+)": the amount has to reach the job at all.
        flow.BoostAmount.ShouldNotBeNull();
        flow.BoostAmount!.Value.ShouldBeGreaterThan(0);
        // BaseScore must be the pre-boost score, never the functional class.
        flow.BaseScore.ShouldNotBeNull();
        flow.Score.ShouldBe(flow.BaseScore!.Value + flow.BoostAmount!.Value, 0.001);
        // And the real class must survive rather than falling back to the arterial default.
        flow.FunctionalClass.ShouldBe(1);
    }

    [Fact]
    public async Task FutureLessons_FlowIntoGenerationContext()
    {
        var corridors = new List<TrafficCorridor> { new("M4", 1, 2, 5000, 90, "accident", "Crash", []) };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));
        var now = DateTime.UtcNow;
        _marketFeedbackRepository.GetRecentAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<FutureLessonEntry>>.Success(
                [new FutureLessonEntry(
                    "lesson-1",
                    "Test Station",
                    "old-job",
                    FutureLessonCategory.WordingStyle,
                    "Use the locally preferred road name in every bulletin.",
                    now,
                    now,
                    1,
                    now.AddDays(90))]));
        SetupSuccessfulPipeline();

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        var result = await _sut.OrchestrateAsync(Request() with
        {
            StationId = Option<string>.Some("Test Station")
        }, Phase.FetchAndGenerate, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        captured.ShouldNotBeNull();
        captured!.FutureLessons.Count.ShouldBe(1);
        captured.FutureLessons[0].Text.ShouldContain("locally preferred road name");
        await _marketFeedbackRepository.Received(1).GetRecentAsync(
            "Test Station", Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdHocJob_NoStampedContext_CarriesForwardPriorSameDayBulletin()
    {
        var corridors = new List<TrafficCorridor> { new("M4", 1, 2, 5000, 90, "accident", "Crash", []) };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));

        var prior = Job.Create("prior-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        prior.ScriptText = Option<string>.Some("earlier bulletin");
        _jobRepository.GetLatestBulletinForMarketTodayAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(prior)));

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Failure("stop here"));

        var request = new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value
        };

        await _sut.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.PreviousScript.HasValue.ShouldBeTrue();
        captured.PreviousScript.Value.ShouldBe("earlier bulletin");
    }

    [Fact]
    public async Task AdHocJob_PriorBulletinIsSelf_DoesNotCarryForward()
    {
        var corridors = new List<TrafficCorridor> { new("M4", 1, 2, 5000, 90, "accident", "Crash", []) };
        _trafficProvider.GetCorridorsAsync(Arg.Any<CoverageArea>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<TrafficCorridor>>.Success(corridors));

        // GetJobAsync returns a job with JobId "test-job"; the only same-day prior IS that job.
        var prior = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        prior.ScriptText = Option<string>.Some("earlier bulletin");
        _jobRepository.GetLatestBulletinForMarketTodayAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(prior)));

        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Failure("stop here"));

        var request = new BroadcastRequest
        {
            JobId = "test-job-id",
            Location = "sydney",
            TargetDurationSeconds = 30,
            Coverage = CoverageArea.Create(-33.8, 151.2, 25).Value
        };

        await _sut.OrchestrateAsync(request, Phase.FetchAndGenerate, CancellationToken.None);

        captured.ShouldNotBeNull();
        captured!.PreviousScript.IsNone.ShouldBeTrue();
    }
    // ── Pace Boost (DeliveryPaceControl) ─────────────────────────────────

    [Fact]
    public async Task PaceBoostWpm_Positive_PassesNonDefaultMaxSpeedUpFactorOverride()
    {
        // Voice calibration returns WPM=20 (default stub). With PaceBoostWpm=20,
        // ComputeEffectiveMaxSpeedUpFactor(20, 20, 1.15, 1.35) = 1.35 — wider than
        // the default 1.15 cap. The orchestrator must pass this to the audio pipeline.
        // Use a script long enough to satisfy the boosted word target (20 words).
        const string longScript = "Traffic is flowing smoothly across all major roads in Sydney today. The M4 motorway is clear from the city to Parramatta.";
        SetupSuccessfulPipeline(longScript);
        // SetupSuccessfulPipeline's stub only specifies 8 args, so NSubstitute matches the
        // trailing pronunciationLocator/maxSpeedUpFactorOverride/allowDynamicSlot/
        // maxSlowDownFactorOverride by their inferred defaults (None/null/false/null) — re-stub with
        // explicit Arg.Any<> on all of them so a non-null override matches. Every optional arg added
        // to SynthesizeWithDurationTargetAsync must be listed here or a boosted call silently stops
        // matching and the orchestration fails on a null Result.
        _audioPipeline.SynthesizeWithDurationTargetAsync(Arg.Any<string>(), Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), Arg.Any<double?>(),
                lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], longScript, 30.0) { SpokenDurationSeconds = 30.0, SlotFilled = true }));
        var ctx = Ctx() with { PaceBoostWpm = 20 };

        await _sut.OrchestrateAsync(ctx);

        // ComputeEffectiveMaxSlowDownFactor(20, 20, 1.0, 1.35) earns half the boost's widening,
        // capped at 1 + (1.35-1)/2 = 1.175 — so a boosted call must widen BOTH ends, not just the
        // speed-up side. A boosted read that lands short is otherwise rejected against an unwidened
        // floor, which costs a whole extra generation pass.
        await _audioPipeline.Received(1).SynthesizeWithDurationTargetAsync(
            Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
            Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
            Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
            Arg.Any<PronunciationStack?>(),
            Arg.Is<double?>(f => f.HasValue && f.Value > 1.0), Arg.Any<bool>(),
            Arg.Is<double?>(f => f.HasValue && f.Value > 1.0), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
    }

    [Fact]
    public async Task PaceBoostWpm_Zero_PassesNullMaxSpeedUpFactorOverride()
    {
        // Regression guard: default context (PaceBoostWpm=0) must pass null override,
        // leaving the audio pipeline at its built-in MaxSpeedUpFactor — byte-identical
        // to pre-feature behaviour.
        SetupSuccessfulPipeline();

        await _sut.OrchestrateAsync(Ctx());

        await _audioPipeline.Received(1).SynthesizeWithDurationTargetAsync(
            Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
            Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
            Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
            Arg.Any<PronunciationStack?>(),
            Arg.Is<double?>(f => !f.HasValue), Arg.Any<bool>(),
            Arg.Is<double?>(f => !f.HasValue), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
    }

    [Fact]
    public async Task PaceBoostWpm_Positive_ContextCarriesPaceBoostToGeneration()
    {
        // Verify the BroadcastWithTraffic passed to script generation carries the
        // PaceBoostWpm — the orchestrator uses this internally to size the word target.
        const string longScript = "Traffic is flowing smoothly across all major roads in Sydney today. The M4 motorway is clear from the city to Parramatta.";
        BroadcastWithTraffic? captured = null;
        _scriptGenerator.GenerateScriptAsync(
                Arg.Do<BroadcastWithTraffic>(c => captured = c), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(longScript, [])));
        _durationEstimator.EstimateDuration(longScript).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), Arg.Any<double?>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], longScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var ctx = Ctx() with { PaceBoostWpm = 25 };
        await _sut.OrchestrateAsync(ctx);

        captured.ShouldNotBeNull();
        captured!.PaceBoostWpm.ShouldBe(25.0);
    }
    // ── Retry math: measured-duration correction ──────────────────────

    /// <summary>
    /// Creates a SUT with the given BroadcastOptions, overrides the calibration WPM
    /// (so baseWordTarget is wide enough for the retry math to exercise),
    /// runs an underrun-on-attempt-1 scenario (SlotFilled=false, then true),
    /// and returns the TargetWordCountOverride captured from the second attempt.
    /// </summary>
    private async Task<int> BuildSutAndCaptureSecondAttemptTarget(BroadcastOptions options, double wpm = 120)
    {
        // Override the shared calibration mock for this run — the constructor's
        // default (WPM=20) yields a baseWordTarget so low that the ±30% adjustment
        // range clamps both variants to the same value.
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(wpm, 5.8, 1.45));

        var sut = CreateSutWithRetries(1, options);
        var contexts = new List<BroadcastWithTraffic>();
        _scriptGenerator.GenerateScriptAsync(
                Arg.Do<BroadcastWithTraffic>(c => contexts.Add(c)), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(
                Result<AudioSynthesisResult>.Success(
                    new AudioSynthesisResult([1, 2, 3], TestScript, 20.0)
                    { SpokenDurationSeconds = 20.0, SlotFilled = false }),
                Result<AudioSynthesisResult>.Success(
                    new AudioSynthesisResult([1, 2, 3], TestScript, 30.0)
                    { SpokenDurationSeconds = 30.0, SlotFilled = true }));
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // When the word-target compliance path triggers (script too short for target),
        // the refiner must return a result — default mock returns null → NRE.

        var ctx = new BroadcastWithTraffic
        {
            Traffic = "Traffic",
            TargetDurationSeconds = 30.0,
            JobId = "test-job-id",
            Location = "sydney",
            VoiceId = Option<string>.Some("test-voice-id"),
            Incidents = []
        };
        await sut.OrchestrateAsync(ctx);

        contexts.Count.ShouldBeGreaterThanOrEqualTo(2,
            "Expected at least 2 generation attempts (underrun then retry)");
        return contexts[1].TargetWordCountOverride.GetValueOr(0);
    }

    /// <summary>
    /// Creates a SUT with custom BroadcastOptions and the given MaxGenerationRetries.
    /// Mirrors CreateSutWithRetries but takes full options.
    /// </summary>
    private BroadcastOrchestrator CreateSutWithRetries(int maxRetries, BroadcastOptions options)
    {
        var incidentStateService = new IncidentStateService(
            Substitute.For<IIncidentStateRepository>(),
            Substitute.For<ILogger<IncidentStateService>>(),
            Options.Create(new IncidentStateOptions()),
            TimeProvider.System);

        return new BroadcastOrchestrator(
            new ScriptPipelineRoles(_scriptGenerator, _scriptReviewer, _scriptRefiner,
                _scriptDirector, _scriptSafetyReviewer, _contentSafetyFilter),
            new AudioPipelineServices(_audioPipeline, _durationEstimator, _fillerAudioCacheService, _voiceCalibrationService, _broadcastOutput),
            new ContentProviders(_trafficProvider, _weatherProvider, _fuelPriceProvider, _routingProvider),
            incidentStateService, _jobRepository,
            Options.Create(new BroadcastOptions
            {
                MaxGenerationRetries = maxRetries,
                MaxReviewRefineRetries = 0,
                MaxPerRetryWordShift = options.MaxPerRetryWordShift,
                DampingFactor = options.DampingFactor,
                MaxWordCountAdjustmentPercent = options.MaxWordCountAdjustmentPercent,
                WordsPerMinute = options.WordsPerMinute,
            }),
            Options.Create(new DeliveryOptions()),
            Options.Create(new SpeechDurationOptions()),
            Substitute.For<INotificationService>(), TimeProvider.System,
            Substitute.For<IMemoryCache>(),
            new NoOpBroadcastMetrics(),
            _priorityRoadsRepository,
            _fillerStatementsRepository,
            _marketFeedbackRepository,
            _guidanceRepository,
            _editorialSettingsRepository,
            Options.Create(new MonitoredRoutesOptions()),
            _bulletinFallbackService,
            _jobService,
            Substitute.For<ILogger<BroadcastOrchestrator>>(),
            workBudgetOptions: Options.Create(new BroadcastWorkBudgetOptions
            {
                // This helper's maxRetries is the established test contract. Supply the
                // corresponding bounded total to exercise retry arithmetic without changing
                // production's 2-generation/2-synthesis defaults.
                MaxGenerationAttempts = Math.Max(1, maxRetries + 1),
                MaxSynthesisAttempts = Math.Max(1, maxRetries + 1)
            }));
    }


    [Fact]
    public async Task Undershoot_RetryTargetScalesWordsByMeasuredDuration()
    {
        // Pinned regression guard: an undershooting first attempt (20.0s of a 30.0s target,
        // TestScript = 11 words) must land on the EXACT scaled target, with no overshoot bias.
        //
        // WPM=120, target=30s, speed=1.0 -> baseWordTarget = round(30*120/60) = 60.
        // The surviving decoupled loop sends a content-limited underrun straight to fresh regen
        // because MaxContentExtendRetries=0; duration-positive retry arithmetic is:
        //   prior = 11 words, raw = round(11 * 30/20) = round(16.5) = 16
        //   stepClamped = clamp(16, 60-200, 60+200) = 16 (unclamped)
        //   nextTargetWords = clamp(16, max(10, 60-60), 60+60) = 16
        // DampingFactor is only used for durationless failures, so it does not affect this path.
        var target = await BuildSutAndCaptureSecondAttemptTarget(
            new BroadcastOptions
            {
                MaxPerRetryWordShift = 200,
                DampingFactor = 0.5,
                MaxWordCountAdjustmentPercent = 100
            });

        target.ShouldBe(16);
    }

    // ── Retry math: DampingFactor / MaxPerRetryWordShift ─────────────

    [Fact]
    public async Task MaxPerRetryWordShift_ClampsRetryTargetTighter()
    {
        // MaxPerRetryWordShift bounds how far the retry target can move from the previous
        // attempt's target in a single step. A small value (4) clamps more tightly than a
        // large value (50). Both are now read live from _options (previously hardcoded).
        //
        // With WPM=120 → baseWordTarget≈60, MaxWordCountAdjustmentPercent=100 → range [0..120]:
        //   shift=4 forces the target within 4 of the initial60, shift=50 allows more movement.
        var targetSmallShift = await BuildSutAndCaptureSecondAttemptTarget(
            new BroadcastOptions
            {
                MaxPerRetryWordShift = 4,
                DampingFactor = 0.5,
                MaxWordCountAdjustmentPercent = 100
            });
        var targetLargeShift = await BuildSutAndCaptureSecondAttemptTarget(
            new BroadcastOptions
            {
                MaxPerRetryWordShift = 50,
                DampingFactor = 0.5,
                MaxWordCountAdjustmentPercent = 100
            });

        targetSmallShift.ShouldNotBe(targetLargeShift,
            $"MaxPerRetryWordShift=4 (target={targetSmallShift}) and 50 (target={targetLargeShift}) " +
            "should produce DIFFERENT retry targets — proving the value is read from options, not hardcoded");
    }

    [Fact]
    public async Task MaxPerRetryWordShift_SmallValue_ClampsCloserToPreviousTarget()
    {
        // Stronger assertion: when the raw retry target pulls BELOW the previous target,
        // a small MaxPerRetryWordShift should produce a target CLOSER to the previous
        // attempt's target (higher value) than a large shift.
        var targetSmallShift = await BuildSutAndCaptureSecondAttemptTarget(
            new BroadcastOptions
            {
                MaxPerRetryWordShift = 4,
                DampingFactor = 0.5,
                MaxWordCountAdjustmentPercent = 100
            });
        var targetLargeShift = await BuildSutAndCaptureSecondAttemptTarget(
            new BroadcastOptions
            {
                MaxPerRetryWordShift = 50,
                DampingFactor = 0.5,
                MaxWordCountAdjustmentPercent = 100
            });

        // Under our scenario (overrun on attempt 1 → retry target wants to decrease),
        // the small shift clamp prevents as large a decrease, leaving the target higher.
        targetSmallShift.ShouldBeGreaterThan(targetLargeShift,
            $"With MaxPerRetryWordShift=4 (target={targetSmallShift}), the clamp should keep the " +
            $"retry target CLOSER to the previous attempt's target than shift=50 (target={targetLargeShift})");
    }

    // ── Filler top-up: appended AFTER generation/review, never sent to LLM ────

    [Fact]
    public async Task FillerAppended_WhenDurationShortOfTarget()
    {
        // The LLM-approved traffic-only script estimates 20s vs a 30s target → 10s gap.
        // Filler statements configured for this market should be appended verbatim.
        string fillerText = "SENTINEL FILLER: Always drive to the conditions.";
        var fillerStmt = new FillerStatement("f1", fillerText);
        var ctx = Ctx() with
        {
            ScriptOnly = true,
            FillerStatements = [fillerStmt]
        };

        SetupScriptOnlyPipeline();
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(20.0);
        // Realistic voice pace (150 WPM = 2.5 wps) — the default class-level calibration (20 WPM)
        // is an unrelated placeholder that would make even this short filler look too "slow" to
        // fit the 10s gap once filler cost is computed at the SAME calibrated rate as the gap.
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(150, 5.8, 1.45));
        // Realistic calibration can push word-target compliance into an actual refine pass;
        // stub it as a harmless pass-through so that path doesn't null-ref on an unconfigured mock.

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.HasValue.ShouldBeTrue();
        savedJob.ScriptText.Value.ShouldContain(fillerText);
    }

    [Fact]
    public async Task FillerNotAppended_WhenDurationMeetsTarget()
    {
        // Traffic-only script already fills the 30s slot → no gap, no filler.
        string fillerText = "SENTINEL FILLER: Always drive to the conditions.";
        var fillerStmt = new FillerStatement("f1", fillerText);
        var ctx = Ctx() with
        {
            ScriptOnly = true,
            FillerStatements = [fillerStmt]
        };

        SetupScriptOnlyPipeline();
        // Duration meets/exceeds target → fillerGapSeconds = max(0, 30 - 30) = 0
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.HasValue.ShouldBeTrue();
        savedJob.ScriptText.Value.ShouldNotContain(fillerText);
    }


    [Fact]
    public async Task LlmCallsNeverReceiveFillerContent()
    {
        // Filler is applied deterministically AFTER generation/review — the mocked LLM
        // collaborators must never see filler text in any of their arguments.
        string fillerText = "SENTINEL FILLER: Keep your headlights on in poor visibility.";
        var fillerStmt = new FillerStatement("f1", fillerText);
        var ctx = Ctx() with
        {
            ScriptOnly = true,
            FillerStatements = [fillerStmt],
            TargetWordCountOverride = Option<int>.Some(30)
        };

        // Use a SUT with review enabled so we can verify the reviewer's arguments too.
        var sut = CreateSutWithReview(maxReviewRetries: 1);
        SetupScriptOnlyPipeline();
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(20.0);
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        var result = await sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        // ScriptGenerator: verify it was called (filler is on the context but not in the prompt)
        await _scriptGenerator.Received().GenerateScriptAsync(
            Arg.Any<BroadcastWithTraffic>(),
            Arg.Any<double>(),
            Arg.Any<VoiceSettings>(),
            Arg.Any<CancellationToken>());
        // ScriptReviewer: the script argument must not contain filler text
        await _scriptReviewer.Received().ReviewScriptAsync(Arg.Is<string>(s => !s.Contains(fillerText)),
        Arg.Any<int>(),
        Arg.Any<int?>(),
        Arg.Any<string?>(),
        Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
    }

    // ── Filler audio cache resolve + stitch (ScriptOnly=false path) ──────────

    [Fact]
    public async Task FillerResolvedSuccessfully_StitchesAudioOntoTrafficSynthesis()
    {
        // Traffic-only script estimates 20s vs a 30s target → 10s gap → filler selected.
        // Filler audio is resolved successfully and stitched onto the traffic synthesis.
        string fillerText = "SENTINEL FILLER: Always drive to the conditions.";
        var fillerStmt = new FillerStatement("f1", fillerText);
        var ctx = Ctx() with { FillerStatements = [fillerStmt] };

        byte[] trafficAudio = [1, 2, 3];
        byte[] fillerAudioBytes = [4, 5, 6];
        byte[] stitchedAudio = [1, 2, 3, 0, 0, 0, 4, 5, 6];
        byte[] finalAudioBytes = [1, 2, 3, 0, 0, 0, 4, 5, 6, 7, 8, 9];

        // Script generation
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        // Duration estimation: traffic-only estimate is 20s (below 30s target → gap)
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(20.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(20.0);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);

        // Voice calibration: override to 150 WPM for realistic filler selection
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(150, 5.8, 1.45));

        // Refiner stub (pass-through; style gate may or may not trigger)

        // Filler audio cache: resolve returns a 2.0s clip
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio(fillerAudioBytes, 2.0, [])));

        // Synthesis: succeeds with traffic-only audio
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult(trafficAudio, TestScript, 20.0) { SpokenDurationSeconds = 20.0, SlotFilled = true }));

        // Stitching, fine-tuning, and slot-fill check
        _audioPipeline.StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<double>())
            .Returns(stitchedAudio);
        _audioPipeline.FineTuneToTargetDuration(Arg.Any<byte[]>(), Arg.Any<double>(), Arg.Any<double?>())
            .Returns(finalAudioBytes);
        _audioPipeline.IsSlotFilled(Arg.Any<byte[]>(), Arg.Any<double>())
            .Returns(true);

        // Broadcast output
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Capture saved job
        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.HasValue.ShouldBeTrue();
        savedJob.ScriptText.Value.ShouldContain(fillerText);

        // Synthesis was called with a target duration LESS than context.TargetDurationSeconds (30.0)
        // because filler duration (2.0s) and pause (0.35s) were subtracted: 30 - 2.0 - 0.35 = 27.65
        await _audioPipeline.Received(1).SynthesizeWithDurationTargetAsync(
            Arg.Any<string>(),
            Arg.Is<double>(d => d < ctx.TargetDurationSeconds),
            Arg.Any<Option<string>>(),
            Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<double>(),
            Arg.Any<Option<string>>(),
            Arg.Any<PronunciationStack?>(),
            Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());

        // Stitching, fine-tuning, and slot-fill check were invoked
        _audioPipeline.Received(1).StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), 0.35);
        _audioPipeline.Received(1).FineTuneToTargetDuration(Arg.Any<byte[]>(), ctx.TargetDurationSeconds, Arg.Any<double?>());
        _audioPipeline.Received(1).IsSlotFilled(Arg.Any<byte[]>(), ctx.TargetDurationSeconds);
    }

    [Fact]
    public async Task FillerResolveFails_FallsBackToTrafficOnlyAudio()
    {
        // Filler statements are selected (gap exists), but ResolveAsync fails for the only
        // statement → resolvedFiller is empty, synthesis runs with the full target duration,
        // and no stitching/fine-tuning occurs.
        string fillerText = "SENTINEL FILLER: Always drive to the conditions.";
        var fillerStmt = new FillerStatement("f1", fillerText);
        var ctx = Ctx() with { FillerStatements = [fillerStmt] };

        byte[] trafficAudio = [1, 2, 3];

        // Script generation
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        // Duration estimation: traffic-only estimate is 20s (below 30s target → gap)
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(20.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(20.0);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);

        // Voice calibration
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(150, 5.8, 1.45));

        // Refiner stub (pass-through)

        // Filler audio cache: resolve FAILS
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Failure("synthesis error"));

        // Synthesis: succeeds with traffic-only audio
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult(trafficAudio, TestScript, 30.0) { SpokenDurationSeconds = 30.0, SlotFilled = true }));

        // Broadcast output
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Capture saved job
        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(ctx);

        // No exception, job completes successfully with traffic-only audio
        result.IsSuccess.ShouldBeTrue();
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.HasValue.ShouldBeTrue();
        // Script text should NOT contain filler (resolvedFiller is empty)
        savedJob.ScriptText.Value.ShouldNotContain(fillerText);

        // Stitching and fine-tuning were NEVER called
        _audioPipeline.DidNotReceive().StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<double>());
        _audioPipeline.DidNotReceive().FineTuneToTargetDuration(Arg.Any<byte[]>(), Arg.Any<double>(), Arg.Any<double?>());

        // Synthesis was called with the FULL target duration (30.0), no filler duration subtracted
        await _audioPipeline.Received(1).SynthesizeWithDurationTargetAsync(
            Arg.Any<string>(),
            Arg.Is<double>(d => d == ctx.TargetDurationSeconds),
            Arg.Any<Option<string>>(),
            Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<double>(),
            Arg.Any<Option<string>>(),
            Arg.Any<PronunciationStack?>(),
            Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());
    }

    [Fact]
    public async Task NoFillerSelected_SynthesisTargetIsUnchangedFromContext()
    {
        // No filler statements configured → synthesis receives context.TargetDurationSeconds unchanged.
        var ctx = Ctx(); // default: FillerStatements = []

        byte[] trafficAudio = [1, 2, 3];

        // Script generation
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));

        // Duration estimation
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(27.0);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);

        // Refiner stub (pass-through)

        // Synthesis: succeeds
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult(trafficAudio, TestScript, 30.0) { SpokenDurationSeconds = 30.0, SlotFilled = true }));

        // Broadcast output
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();

        // Synthesis was called with context.TargetDurationSeconds exactly
        await _audioPipeline.Received(1).SynthesizeWithDurationTargetAsync(
            Arg.Any<string>(),
            Arg.Is<double>(d => d == ctx.TargetDurationSeconds),
            Arg.Any<Option<string>>(),
            Arg.Any<Option<VoiceSettings>>(),
            Arg.Any<Option<string>>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<double>(),
            Arg.Any<Option<string>>(),
            Arg.Any<PronunciationStack?>(),
            Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>());

        // No stitching or fine-tuning
        _audioPipeline.DidNotReceive().StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<double>());
        _audioPipeline.DidNotReceive().FineTuneToTargetDuration(Arg.Any<byte[]>(), Arg.Any<double>(), Arg.Any<double?>());
    }

    [Fact]
    public async Task ReviewAndRefineRunsOnEveryGenerationAttempt()
    {
        var sut = CreateSutWithReview(maxReviewRetries: 1, maxGenerationRetries: 1);
        byte[] audio = new byte[] { 1, 2, 3 };

        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(27.0);

        // Review always approves — each generation attempt's review loop resolves in one round.
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        // First attempt synthesis fails (non-permanent), second succeeds — forces a 2nd generation attempt.
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(
                Result<AudioSynthesisResult>.Failure("Transient API error"),
                Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(
            Ctx() with { TargetWordCountOverride = Option<int>.Some(30) });

        result.IsSuccess.ShouldBeTrue();

        // Proves review-refine ran once per generation attempt (2 attempts = 2 review calls).
        // Before the fix, the gate `runProducerReview = generationAttempt == 1` caused only 1 review call.
        await _scriptReviewer.Received(2).ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
        Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>());
    }

    // ── Review/refine sentence-patch gate: severity tiers + convergence guard ──
    // Motivated by job fb44a570 (Melbourne 3AW, 2026-07-08): a STYLE detector false-positived on
    // every round and outvoted an APPROVED verdict, burning 4 repairs for an over-length script.

    [Fact]
    public async Task Style_only_defect_with_approved_review_does_not_trigger_a_patch()
    {
        // "closed at Parramatta Road, at Silverwater Road" repeats the identical preposition to
        // introduce a second anchor in one sentence — RepeatedPrepositionDetector (a STYLE-tier
        // gate with a safe mechanical Repair, no context/corridors required) genuinely fires.
        // Nothing here trips any BLOCKING-tier detector (meta-leak/type-stack/fake-location/
        // unselected-source/suburb-retention/sentence-clone).
        const string script = "The M4 is closed at Parramatta Road, at Silverwater Road across the network.";
        var sut = CreateSutWithReview();
        SetupScriptOnlyPipeline(script);
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        var result = await sut.OrchestrateAsync(Ctx() with { ScriptOnly = true });

        result.IsSuccess.ShouldBeTrue();
        // The style nit is left to post-loop mechanical repair, never an expensive sentence patch.
    }

    [Fact]
    public async Task Blocking_defect_persisting_across_a_patch_stops_the_loop()
    {
        // "data" is a META-LEAK blocking defect with no safe mechanical repair. Keep the narrow
        // sentence patch as a no-op so the SAME blocking signature survives and the convergence
        // guard stops the loop on the very next attempt. This intentionally replaced the old
        // FakeLocation fixture when FakeLocation gained repair-first handling in the 2026-08-19
        // ordering change: a mechanically repairable defect must now consume zero patches.
        const string script = "The M2 is closed near the data across the network while crews respond.";
        // MaxReviewRefineRetriesCeiling (BroadcastOrchestrator's private clamp) is 3 — configuring
        // exactly at the ceiling proves the guard stops the loop well short of it, not merely
        // short of an arbitrarily low retry count.
        var sut = CreateSutWithReview(maxReviewRetries: 3);
        SetupScriptOnlyPipeline(script);
        _scriptReviewer.ReviewScriptAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int?>(),
                Arg.Any<string?>(), Arg.Any<BroadcastWithTraffic?>(), Arg.Any<CancellationToken>(), Arg.Any<IReadOnlyList<DraftedSentence>?>(), Arg.Any<IReadOnlyList<string>?>())
            .Returns(Result<ScriptReviewResult>.Success(new ScriptReviewResult(true, "")));

        int narrowInvocationCount = 0;
        _scriptRefiner.PatchSentenceAsync(
                Arg.Any<BroadcastWithTraffic>(), Arg.Any<DraftedSentence>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyList<EvidenceUnit>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                narrowInvocationCount++;
                return Result<DraftedSentence>.Success(callInfo.ArgAt<DraftedSentence>(1));
            });

        var result = await sut.OrchestrateAsync(Ctx() with { ScriptOnly = true });

        result.IsSuccess.ShouldBeTrue();
        // Convergence guard breaks on attempt 2 (same blocking signature repeats) — the narrow
        // refiner is invoked exactly once, nowhere near the MaxReviewRefineRetriesCeiling (3).
        narrowInvocationCount.ShouldBe(1);
    }

    // ── Typed Failure Disposition Assertions ────────────────────────────────
    //
    // Proves that the four OperationalFailure dispositions survive to the
    // top-level Result<BroadcastResult> unchanged, and that content-limited
    // still routes to fallback before a terminal failure.

    [Fact]
    public async Task TerminalState_ConflictDisposition_ReachesTopLevel()
    {
        // Job is already Approved — the orchestrator must return Conflict, not Retryable.
        var job = Job.Create("test-job", "Test Station", "Test Market", 30, DateTime.UtcNow).Value;
        job.TransitionTo(JobStatus.Approved, utcNow: DateTime.UtcNow);
        _jobRepository.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Conflict);
        result.FailureMetadata.Value.Code.ShouldBe("broadcast.job_already_terminal");
        result.Error.ShouldStartWith("Job already in terminal state:");
    }

    [Fact]
    public async Task ContentLimited_NoFallback_ContentLimitedDisposition_ReachesTopLevel()
    {
        // Content-limited exhaustion with no fallback must carry ContentLimited disposition.
        SetupContentLimitedPipeline();
        // Default mock: no fallback available.

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.ContentLimited);
        result.FailureMetadata.Value.Code.ShouldBe("broadcast.content_limited");
        result.Error.ShouldStartWith("Content-limited:");
    }

    [Fact]
    public async Task ContentLimited_WithFallback_RoutesToFallbackBeforeTerminalFailure()
    {
        // Content-limited with a fallback available must succeed (not fail with ContentLimited).
        SetupContentLimitedPipeline();
        _bulletinFallbackService.TryApplyFallbackAsync(Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new FallbackOutcome(FallbackKind.FillerCart));

        var sut = CreateSutWithRetries(0);
        var result = await sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        await _bulletinFallbackService.DidNotReceive().TryApplyFallbackAsync(
            Arg.Any<Job>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task SafetyRejected_WordlistBlock_SafetyRejectedDisposition_ReachesTopLevel()
    {
        // Deterministic wordlist block must carry SafetyRejected disposition.
        SetupSuccessfulPipeline();
        _contentSafetyFilter.CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ContentSafetyResult.Blocked(["banned term: foo"]));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.SafetyRejected);
        result.FailureMetadata.Value.Code.ShouldBe("broadcast.content_safety_rejected");
        result.Error.ShouldContain("Content safety violation");
    }

    [Fact]
    public async Task SafetyRejected_AiBlock_SafetyRejectedDisposition_ReachesTopLevel()
    {
        // AI safety review block must carry SafetyRejected disposition.
        SetupSuccessfulPipeline();
        _scriptSafetyReviewer.ReviewScriptSafetyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScriptSafetyResult>.Success(new ScriptSafetyResult(false,
                ["self-harm reference"])));

        var result = await _sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.SafetyRejected);
        result.FailureMetadata.Value.Code.ShouldBe("broadcast.content_safety_rejected");
        result.Error.ShouldContain("Content safety violation");
    }

    [Fact]
    public async Task PermanentTtsError_AbortsRetries_PermanentDisposition()
    {
        // Permanent TTS error must carry Permanent disposition and abort retries.
        var sut = CreateSutWithRetries(2);
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Failure(OperationalFailure.Permanent("tts.permanent", "BadRequest: invalid voice")));

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsFailure.ShouldBeTrue();
        result.FailureMetadata.HasValue.ShouldBeTrue();
        result.FailureMetadata.Value.Disposition.ShouldBe(FailureDisposition.Permanent);
        result.FailureMetadata.Value.Code.ShouldBe("tts.permanent");
        // Only one generation attempt — permanent errors abort retries.
        await _scriptGenerator.Received(1).GenerateScriptAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransientTtsError_RetriesGeneration_RetryableDisposition()
    {
        // Transient TTS error must carry Retryable disposition and allow retries.
        var sut = CreateSutWithRetries(1);
        byte[] audio = [1, 2, 3];
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(TestScript).Returns(27.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(TestScript, Arg.Any<double>(),
                Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(
                Result<AudioSynthesisResult>.Failure("Transient API error"),
                Result<AudioSynthesisResult>.Success(new AudioSynthesisResult(audio, TestScript, 30.0) { SlotFilled = true }));
        _durationEstimator.GetDuration(audio).Returns(30.0);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await sut.OrchestrateAsync(Ctx());

        result.IsSuccess.ShouldBeTrue();
        // Two generation attempts — transient errors allow retries.
        await _scriptGenerator.Received(2).GenerateScriptAsync(
            Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
            Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>());
    }
}

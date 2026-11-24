using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Domain.Contracts;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     Post-synthesis filler rescue wiring (BroadcastOrchestrator.GenerateAndSynthesizeAsync).
///     When a take comes back content-limited (SlotFilled=false), the orchestrator re-derives the
///     TRUE gap and the voice's MEASURED words-per-second from the spoken take, re-runs the
///     selector over the not-yet-used filler pool with the 1.5s overshoot tolerance, and feeds any
///     rescue audio into the existing stitch → fine-tune → re-gate path.
/// </summary>
public class BroadcastOrchestratorFillerRescueTests
{
    // 11 words. A 26.0s measured read gives 11/26 ≈ 0.42 wps, clamped up to the 1.0 wps floor —
    // so each rescue candidate's cost in seconds equals its word count (deterministic arithmetic).
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

    public BroadcastOrchestratorFillerRescueTests()
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

    /// <summary>
    ///     Generation succeeds and the PRE-synthesis filler gap estimates to zero (traffic-only
    ///     estimate == 30s target), so no filler is selected before synthesis — the only route to
    ///     filler is the post-synthesis rescue. Synthesis then underruns with the given measured
    ///     spoken span (SlotFilled=false).
    /// </summary>
    private void SetupContentLimitedTake(double spokenSeconds)
    {
        _scriptGenerator.GenerateScriptAsync(Arg.Any<BroadcastWithTraffic>(), Arg.Any<double>(),
                Arg.Any<VoiceSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result<DraftedScript>.Success(new DraftedScript(TestScript, [])));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(30.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(30.0);
        _durationEstimator.GetDuration(Arg.Any<byte[]>()).Returns(30.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], TestScript, spokenSeconds)
                { SpokenDurationSeconds = spokenSeconds, SlotFilled = false }));
        _audioPipeline.StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), Arg.Any<double>())
            .Returns([1, 2, 3, 4, 5, 6]);
        _audioPipeline.FineTuneToTargetDuration(Arg.Any<byte[]>(), Arg.Any<double>(), Arg.Any<double?>())
            .Returns([1, 2, 3, 4, 5, 6, 7]);
        _audioPipeline.IsSlotFilled(Arg.Any<byte[]>(), Arg.Any<double>())
            .Returns(true);
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    [Fact]
    public async Task Rescue_ClosesMeasuredGapViaOvershootTolerance_ContentLimitedTakeBecomesSuccess()
    {
        // Measured read 26.0s vs 30s slot → TRUE gap 4.0s; measured wps clamps to 1.0, so the
        // 5-word statement costs 5.0s — it does NOT fit within 4.0s and is only selectable
        // through the wired 1.5s overshoot tolerance (window (4.0, 5.5]). The rescued audio joins
        // the stitch → fine-tune → re-gate path, which fills the slot: the attempt that would have
        // failed to the fallback path succeeds instead.
        string rescueText = "SENTINEL RESCUE drive safely today"; // 5 words
        var ctx = Ctx() with { FillerStatements = [new FillerStatement("rescue-1", rescueText)] };
        SetupContentLimitedTake(spokenSeconds: 26.0);
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([4, 5, 6], 4.0, [])));

        Job? savedJob = null;
        _jobRepository.SaveJobAsync(Arg.Do<Job>(j => savedJob = j), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        // The rescue resolved audio for the overshoot statement (pre-synthesis selected nothing)…
        await _fillerAudioCacheService.Received(1).ResolveAsync(
            Arg.Is<FillerStatement>(s => s.Id == "rescue-1"), Arg.Any<string>(), Arg.Any<VoiceSettings>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        // …and it flowed through the existing stitch + fine-tune + slot re-gate path.
        _audioPipeline.Received(1).StitchAudioClips(Arg.Any<IReadOnlyList<byte[]>>(), 0.35);
        _audioPipeline.Received(1).FineTuneToTargetDuration(Arg.Any<byte[]>(), ctx.TargetDurationSeconds, Arg.Any<double?>());
        _audioPipeline.Received(1).IsSlotFilled(Arg.Any<byte[]>(), ctx.TargetDurationSeconds);
        // The persisted script carries the appended filler text verbatim.
        savedJob.ShouldNotBeNull();
        savedJob!.ScriptText.HasValue.ShouldBeTrue();
        savedJob.ScriptText.Value.ShouldContain(rescueText);
    }

    [Fact]
    public async Task Rescue_StitchesTrimmedTrafficAudio_NotRawSynthesisAudio()
    {
        // The traffic-only take may already be padded toward its (unreached) target with raw
        // trailing silence (SynthesizeWithDurationTargetAsync's underrun path). Stitching filler
        // directly onto that padded clip buries it behind dead air, so the orchestrator must trim
        // to the spoken content FIRST and stitch the TRIMMED clip, not synthesis.Audio verbatim
        // (2026-07-07 job 4417add2 dead-air fix).
        string rescueText = "SENTINEL RESCUE drive safely today"; // 5 words
        var ctx = Ctx() with { FillerStatements = [new FillerStatement("rescue-1", rescueText)] };
        SetupContentLimitedTake(spokenSeconds: 26.0);
        byte[] trimmedTrafficAudio = [9, 9, 9];
        _audioPipeline.TrimToSpokenDuration(Arg.Any<byte[]>()).Returns(trimmedTrafficAudio);
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([4, 5, 6], 4.0, [])));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        _audioPipeline.Received(1).TrimToSpokenDuration(Arg.Any<byte[]>());
        _audioPipeline.Received(1).StitchAudioClips(
            Arg.Is<IReadOnlyList<byte[]>>(clips => clips.Count > 0 && clips[0].SequenceEqual(trimmedTrafficAudio)),
            Arg.Any<double>());
    }

    // ── Pace matching the stitched clip to the read ──
    // A cached clip keeps whatever rate a standalone one-sentence render gave it, while the read
    // around it has been compressed into the slot. Measured across six aired dev bulletins the clip
    // articulates a median 8% faster, worst case 35% (job 53c160ad).

    /// <summary>
    ///     A read measured at ~5.54 syllables/second: 7 syllables (Greensborough 3, Bypass 2,
    ///     closed 2) over 1.264s of spoken time, pauses excluded.
    /// </summary>
    private static IReadOnlyList<WordTiming> ReadTimings()
    {
        return
        [
            new WordTiming("Greensborough", 0.0, 0.632),
            new WordTiming("Bypass", 0.7, 1.121),
            new WordTiming("closed", 1.2, 1.411)
        ];
    }

    [Fact]
    public async Task Stitch_ClipFasterThanTheRead_IsRetimedTowardTheReadsRate()
    {
        // Same 5-word statement the overshoot-tolerance test uses — the only one the measured gap
        // selects (selection costs it by WORD count, so the clip's timings below are free to model
        // the mismatch). The clip articulates 10 syllables in 1.5s = 6.67 syl/s against the read's
        // 5.54: a 20% jump at the seam, so it must be STRETCHED before stitching and the stretched
        // bytes are what get stitched. Both rates are articulation — pairing the clip's articulation
        // against a gross figure inverts the correction (see FillerPaceMatcherTests).
        const string rescueText = "SENTINEL RESCUE drive safely today"; // 5 words, 10 syllables
        const double cachedSeconds = 1.5;
        IReadOnlyList<WordTiming> clipTimings =
        [
            new WordTiming("SENTINEL", 0.00, 0.45),
            new WordTiming("RESCUE", 0.45, 0.75),
            new WordTiming("drive", 0.75, 0.90),
            new WordTiming("safely", 0.90, 1.20),
            new WordTiming("today", 1.20, 1.50)
        ];
        var ctx = Ctx() with { FillerStatements = [new FillerStatement("rescue-1", rescueText)] };
        SetupContentLimitedTake(spokenSeconds: 26.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], TestScript, 26.0)
                { SpokenDurationSeconds = 26.0, SlotFilled = false, WordTimings = ReadTimings() }));
        byte[] cachedClip = [4, 5, 6];
        byte[] retimedClip = [7, 7, 7, 7];
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(
                new ResolvedFillerAudio(cachedClip, cachedSeconds, clipTimings)));
        _audioPipeline.RetimeClip(Arg.Any<byte[]>(), Arg.Any<double>()).Returns(retimedClip);

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        // Stretched, not compressed, and never past the clamp.
        _audioPipeline.Received(1).RetimeClip(
            Arg.Is<byte[]>(b => b.SequenceEqual(cachedClip)),
            Arg.Is<double>(t => t > cachedSeconds
                                && t <= cachedSeconds * FillerPaceMatcher.MaxRetimeFactor + 0.0001));
        // The retimed bytes are what reach the stitch.
        _audioPipeline.Received(1).StitchAudioClips(
            Arg.Is<IReadOnlyList<byte[]>>(clips => clips.Any(c => c.SequenceEqual(retimedClip))
                                                   && !clips.Any(c => c.SequenceEqual(cachedClip))),
            Arg.Any<double>());
    }

    [Fact]
    public async Task Stitch_ReadRateUnknown_LeavesTheCachedClipUntouched()
    {
        // No word timings (a stub voice, an unparsed timing payload) means no measurable read rate.
        // The clip must stitch exactly as it did before pace matching existed — degrade, never guess.
        const string rescueText = "SENTINEL RESCUE drive safely today";
        var ctx = Ctx() with { FillerStatements = [new FillerStatement("rescue-1", rescueText)] };
        SetupContentLimitedTake(spokenSeconds: 26.0);
        byte[] cachedClip = [4, 5, 6];
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio(cachedClip, 4.0, [])));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        _audioPipeline.DidNotReceiveWithAnyArgs().RetimeClip(default!, default);
        _audioPipeline.Received(1).StitchAudioClips(
            Arg.Is<IReadOnlyList<byte[]>>(clips => clips.Any(c => c.SequenceEqual(cachedClip))),
            Arg.Any<double>());
    }

    [Fact]
    public async Task Rescue_DoesNotRun_WhenSlotIsFilled()
    {
        // Same market configuration, but the take fills the slot (SlotFilled=true): the rescue
        // gate is the content-limited underrun, so no filler audio is ever resolved or stitched —
        // and the new pre-stitch TrimToSpokenDuration call (2026-07-07 dead-air fix) never fires
        // either, since there is no padded clip to trim and no filler to land after it.
        string rescueText = "SENTINEL RESCUE drive safely today";
        var ctx = Ctx() with { FillerStatements = [new FillerStatement("rescue-1", rescueText)] };
        SetupContentLimitedTake(spokenSeconds: 30.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], TestScript, 30.0)
                { SpokenDurationSeconds = 30.0, SlotFilled = true }));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _fillerAudioCacheService.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default!, default!, default!, default!, default);
        _audioPipeline.DidNotReceiveWithAnyArgs().StitchAudioClips(default!, default);
        _audioPipeline.DidNotReceive().TrimToSpokenDuration(Arg.Any<byte[]>());
    }

    [Fact]
    public async Task Rescue_FindsNothingBeyondTolerance_AttemptStaysContentLimited()
    {
        // TRUE gap 4.0s, measured wps 1.0 → the only statement costs 10.0s, far beyond the
        // 4.0 + 1.5s overshoot window. The rescue selects nothing, no audio is resolved, and the
        // attempt keeps its content-limited outcome — the orchestrator fails to the fallback path
        // exactly as it did before the rescue existed.
        string tooLong = "SENTINEL RESCUE one two three four five six seven eight"; // 10 words
        var ctx = Ctx() with { FillerStatements = [new FillerStatement("too-long", tooLong)] };
        SetupContentLimitedTake(spokenSeconds: 26.0);

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("could not fill");
        await _fillerAudioCacheService.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default!, default!, default!, default!, default);
        _audioPipeline.DidNotReceiveWithAnyArgs().StitchAudioClips(default!, default);
    }

    [Fact]
    public async Task Rescue_ExcludesStatementsAlreadySelectedPreSynthesis()
    {
        // Pre-synthesis: 150 WPM calibration (2.5 wps) and a 28s traffic-only estimate leave a
        // 2.0s gap — statement "a" (5 words = 2.0s) is selected and resolved BEFORE synthesis
        // ("b" ties on cost but loses the Id tiebreak, and the covered gap stops selection).
        // Post-synthesis: spoken 24.0s → TRUE gap 30 − 24 − (2.0 + 0.35 pause) = 3.65s at measured
        // 1.0 wps. Both statements cost 5.0s; without the used-Id exclusion the Id tiebreak would
        // pick "a" AGAIN (double-resolve, duplicate on air). The rescue must resolve "b" instead.
        var ctx = Ctx() with
        {
            FillerStatements =
            [
                new FillerStatement("a", "SENTINEL ALPHA drive safely today"),  // 5 words
                new FillerStatement("b", "SENTINEL BRAVO drive safely today"),  // 5 words
            ]
        };
        SetupContentLimitedTake(spokenSeconds: 24.0);
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(150, 5.8, 1.45));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(28.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(28.0);
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([7, 8], 2.0, [])));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        // "a" resolved exactly once (pre-synthesis) — the rescue never re-resolves a used statement.
        await _fillerAudioCacheService.Received(1).ResolveAsync(
            Arg.Is<FillerStatement>(s => s.Id == "a"), Arg.Any<string>(), Arg.Any<VoiceSettings>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        // "b" resolved exactly once (the rescue's overshoot pick from the remaining pool).
        await _fillerAudioCacheService.Received(1).ResolveAsync(
            Arg.Is<FillerStatement>(s => s.Id == "b"), Arg.Any<string>(), Arg.Any<VoiceSettings>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreSynthesisSelection_Skipped_WhenFillOrderOmitsFiller()
    {
        // Identical market shape to the pre-synthesis leg of
        // Rescue_ExcludesStatementsAlreadySelectedPreSynthesis: 150 WPM calibration (2.5 wps) and
        // a 28s traffic-only estimate leave a 2.0s gap that the 5-word statement (2.0s) would
        // close — but this station's FillOrder omits Filler, so the deterministic selection never
        // runs. The take itself fills the slot: the job succeeds with no filler audio resolved
        // and nothing stitched.
        var ctx = Ctx() with
        {
            FillOrder = [FillSource.Weather, FillSource.Fuel],
            FillerStatements = [new FillerStatement("a", "SENTINEL ALPHA drive safely today")]
        };
        SetupContentLimitedTake(spokenSeconds: 30.0);
        _voiceCalibrationService.GetCalibrationMetricsAsync(Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<bool>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(new CalibrationMetrics(150, 5.8, 1.45));
        _durationEstimator.EstimateDuration(Arg.Any<string>()).Returns(28.0);
        _durationEstimator.EstimateDuration(Arg.Any<string>(), Arg.Any<double>()).Returns(28.0);
        _audioPipeline.SynthesizeWithDurationTargetAsync(
                Arg.Any<string>(), Arg.Any<double>(), Arg.Any<Option<string>>(), Arg.Any<Option<VoiceSettings>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<double>(), Arg.Any<Option<string>>(),
                Arg.Any<PronunciationStack?>(), Arg.Any<double?>(), Arg.Any<bool>(), lockedSuffix: Arg.Any<string?>(), lockedPrefix: Arg.Any<string?>())
            .Returns(Result<AudioSynthesisResult>.Success(
                new AudioSynthesisResult([1, 2, 3], TestScript, 30.0)
                { SpokenDurationSeconds = 30.0, SlotFilled = true }));
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([7, 8], 2.0, [])));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsSuccess.ShouldBeTrue();
        await _fillerAudioCacheService.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default!, default!, default!, default!, default);
        _audioPipeline.DidNotReceiveWithAnyArgs().StitchAudioClips(default!, default);
    }

    [Fact]
    public async Task Rescue_Skipped_WhenFillOrderOmitsFiller()
    {
        // Identical underrun to Rescue_ClosesMeasuredGapViaOvershootTolerance (26.0s spoken →
        // 4.0s TRUE gap; the 5-word statement is selectable through the 1.5s overshoot window,
        // and resolution is stubbed to succeed) — but this station's FillOrder omits Filler. The
        // rescue must not run: no filler audio resolved, nothing stitched, and the attempt keeps
        // its content-limited failure exactly as if the cart were empty.
        var ctx = Ctx() with
        {
            FillOrder = [FillSource.Weather, FillSource.Fuel],
            FillerStatements = [new FillerStatement("rescue-1", "SENTINEL RESCUE drive safely today")]
        };
        SetupContentLimitedTake(spokenSeconds: 26.0);
        _fillerAudioCacheService.ResolveAsync(
                Arg.Any<FillerStatement>(), Arg.Any<string>(), Arg.Any<VoiceSettings>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedFillerAudio>.Success(new ResolvedFillerAudio([4, 5, 6], 4.0, [])));

        var result = await _sut.OrchestrateAsync(ctx);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("could not fill");
        await _fillerAudioCacheService.DidNotReceiveWithAnyArgs()
            .ResolveAsync(default!, default!, default!, default!, default);
        _audioPipeline.DidNotReceiveWithAnyArgs().StitchAudioClips(default!, default);
    }
}

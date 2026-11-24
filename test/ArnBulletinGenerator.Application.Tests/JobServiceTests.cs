using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class JobServiceTests
{
    private readonly IFileDeliveryService _fileDeliveryService = Substitute.For<IFileDeliveryService>();
    private readonly IBroadcastOutput _broadcastOutput = Substitute.For<IBroadcastOutput>();
    private readonly IJobRepository _jobRepo = Substitute.For<IJobRepository>();
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly IModelSelector _modelSelector = Substitute.For<IModelSelector>();
    private readonly IVoiceProfileService _voiceProfiles = Substitute.For<IVoiceProfileService>();
    private readonly IVoiceProvider _voiceProvider = Substitute.For<IVoiceProvider>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly IQueuePublisher _queuePublisher = Substitute.For<IQueuePublisher>();
    private readonly IPronunciationDictionaryService _pronunciation =
        Substitute.For<IPronunciationDictionaryService>();
    private readonly IDeliveryStyleService _delivery = Substitute.For<IDeliveryStyleService>();
    private readonly IBookendService _bookends = Substitute.For<IBookendService>();
    private readonly JobService _sut;

    public JobServiceTests()
    {
        _modelSelector.SelectModelAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<ModelSelectionUseCase>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult("eleven_turbo_v2_5"));
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success([]));
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepo.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _notificationService.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _fileDeliveryService.DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Result<FileDeliveryStatus>.Success(FileDeliveryStatus.Delivered));

        _broadcastOutput.MarkPublishedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // No configured schedules by default — CreateJobAsync's MarketId resolution falls
        // back to a slugified Location; tests that need a canonical-slug match stub this
        // explicitly with CreateSchedule(...).
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));

        _sut = new JobService(_jobRepo, _scheduleRepository, _queuePublisher, _modelSelector,
            _voiceProfiles,
            _voiceProvider,
            _notificationService, _fileDeliveryService,
            _broadcastOutput,
            _pronunciation,
            _delivery,
            _bookends,
            Substitute.For<IBroadcastMetrics>(),
            Options.Create(new BroadcastOptions
            { DefaultVoiceId = "default-voice", DefaultLanguage = "en" }),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()), Substitute.For<ILogger<PerVoiceFillerCartResolver>>()), Substitute.For<IBulletinFallbackService>(), TimeProvider.System, Substitute.For<ILogger<JobService>>());
    }

    private static MarketSchedule CreateSchedule(string marketId, string name, string station = "2GB")
    {
        return MarketSchedule.Create(marketId, name, station, "Australia/Sydney", "0 6 * * *", true,
            CoverageArea.Create(0, 0, 1).Value, "voice-1", "output/path", 30, 15, 5,
            DateTime.UtcNow, DateTime.UtcNow).Value;
    }

    private Job CreateTestJob(JobStatus status = JobStatus.Requested)
    {
        var job = Job.Create("job-1", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        // Walk through states to reach the target status
        if (status >= JobStatus.WaitingApproval && status != JobStatus.Failed)
            job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        if (status == JobStatus.Approved)
        {
            job.Approve(DateTime.UtcNow, "test");
        }
        else if (status == JobStatus.Rejected)
        {
            job.Reject(new RejectionEntry("reason", "notes", DateTime.UtcNow, "test"), DateTime.UtcNow, "test");
        }
        else if (status == JobStatus.Published)
        {
            job.Approve(DateTime.UtcNow, "test");
            job.Publish(DateTime.UtcNow, "test");
        }
        else if (status == JobStatus.Failed)
        {
            job.MarkFailed("error", DateTime.UtcNow, "test");
        }
        // Every persisted row carries a revision, and the lifecycle calls are all
        // revision-guarded, so a job with a null ETag is not a state storage can produce.
        // Leaving it null made system-initiated delivery fail with "a revision token is
        // required" in tests only.
        job.ETag = "etag-test";
        return job;
    }

    private void SetupJobFound(Job job)
    {
        _jobRepo.GetJobAsync(job.JobId, Arg.Any<CancellationToken>()).Returns(Result<Job>.Success(job));
    }

    private void SetupJobNotFound(string jobId = "missing")
    {
        _jobRepo.GetJobAsync(jobId, Arg.Any<CancellationToken>()).Returns(Result<Job>.Failure("Not found"));
    }

    private DeliveryQueueCommand PreparePendingDelivery(Job job)
    {
        job.AudioUrl = Option<string>.Some("https://audio.example.com/bulletin.wav");
        job.TransitionTo(JobStatus.DeliveryPending, "operator", utcNow: DateTime.UtcNow);
        job.DeliveryIdempotencyKey = "delivery-key";
        job.DeliveryReviewedEtag = "etag-test";
        job.DeliveryKind = Option<DeliveryKind>.Some(DeliveryKind.Publish);
        job.DeliveryRequestedBy = "operator";
        _scheduleRepository.GetScheduleAsync(job.Market, Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(
                CreateSchedule(job.Market, "Sydney", job.Station)));
        return new DeliveryQueueCommand(job.JobId, "etag-test", "delivery-key", "operator");
    }

    [Fact]
    public async Task ProceedPastCurationAsync_StaleExpectedEtag_ReturnsRevisionMismatch()
    {
        var job = CreateTestJob();
        job.TransitionTo(JobStatus.HereFetched, utcNow: DateTime.UtcNow);
        job.TransitionTo(JobStatus.IncidentsSelected, utcNow: DateTime.UtcNow);
        job.TransitionTo(JobStatus.AwaitingCuration, utcNow: DateTime.UtcNow);
        job.VoiceConfig = Option<JobVoiceConfig>.Some(
            new JobVoiceConfig("elevenlabs", "voice-1", "Voice", "eleven_v3"));
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.ProceedPastCurationAsync(job.JobId, expectedEtag: "stale-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.RevisionMismatch);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // ApproveJobAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ApproveJobAsync_WaitingApproval_Succeeds_SavesWithoutCard()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Approved);
        job.ApprovedBy.ShouldBe("unknown");
        job.ApprovedAt.ShouldNotBeNull();
        await _jobRepo.Received(1).SaveJobAsync(job, Arg.Any<CancellationToken>());
        await _jobRepo.Received(1)
            .AddLogAsync(job.JobId, Arg.Any<string>(), JobLogLevel.Info, Arg.Any<CancellationToken>());
        // Card design 2026-07-05: operator-initiated transitions are audit noise - no card.
        await _notificationService.DidNotReceive().SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveJobAsync_JobNotFound_ReturnsFailure()
    {
        SetupJobNotFound();

        var result = await _sut.ApproveJobAsync("missing");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    [Fact]
    public async Task ApproveJobAsync_TerminalState_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.Published);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsFailure.ShouldBeTrue();
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveJobAsync_FromRequested_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.Requested);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Requested");
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveJobAsync_SaveFails_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Job was modified by another writer; please retry."));

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("Job was modified by another writer; please retry.");
    }

    [Fact]
    public async Task ApproveJobAsync_NotificationFailure_IsSwallowed()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);
        _notificationService.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Teams down"));

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Approved);
    }

    [Fact]
    public async Task ApproveJobAsync_FromFallbackUsed_Succeeds()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.TransitionTo(JobStatus.FallbackUsed, utcNow: DateTime.UtcNow);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Approved);
    }

    [Fact]
    public async Task ApproveJobAsync_MismatchedExpectedEtag_ReturnsFailure_NeverSavesOrApproves()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "stale-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.RevisionMismatch);
        job.Status.ShouldBe(JobStatus.WaitingApproval);
        job.ApprovedBy.ShouldBeNull();
        job.ApprovedAt.ShouldBeNull();
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveJobAsync_MatchingExpectedEtag_Succeeds()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "current-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Approved);
    }

    [Fact]
    public async Task ApproveJobAsync_NullEtag_ReturnsFailure_NeverSaves()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.EtagRequired);
        job.Status.ShouldBe(JobStatus.WaitingApproval);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveJobAsync_EmptyEtag_ReturnsFailure_NeverSaves()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, expectedEtag: "");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.EtagRequired);
        job.Status.ShouldBe(JobStatus.WaitingApproval);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveJobAsync_ByObjectIdProvided_StampsApprovedByObjectId()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.ApproveJobAsync(job.JobId, "operator@arn.com.au", expectedEtag: "test-etag", byObjectId: "obj-id-123");

        result.IsSuccess.ShouldBeTrue();
        job.ApprovedByObjectId.ShouldBe("obj-id-123");
    }

    // ──────────────────────────────────────────────
    // EscalateJobAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task EscalateJobAsync_WaitingApproval_Succeeds()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);

        var result = await _sut.EscalateJobAsync(job.JobId, "Past deadline");

        result.IsSuccess.ShouldBeTrue();
        await _jobRepo.Received(1).AddLogAsync(job.JobId, Arg.Is<string>(s => s.Contains("escalated")),
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendJobNotificationAsync(job, NotificationKind.UrgentApproval,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateJobAsync_JobNotFound_ReturnsFailure()
    {
        SetupJobNotFound();

        var result = await _sut.EscalateJobAsync("missing", "reason");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task EscalateJobAsync_NotWaitingApproval_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.Approved);
        SetupJobFound(job);

        var result = await _sut.EscalateJobAsync(job.JobId, "reason");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.OnlyWaitingCanEscalate);
    }

    [Fact]
    public async Task EscalateJobAsync_SameReasonAlreadyEscalated_SucceedsIdempotent_SkipsDuplicate()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.AddLog(new JobLog(DateTime.UtcNow, JobLogLevel.Warn, "Job escalated: Past deadline"));
        SetupJobFound(job);

        var result = await _sut.EscalateJobAsync(job.JobId, "Past deadline");

        result.IsSuccess.ShouldBeTrue();
        await _jobRepo.DidNotReceive().AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
            Arg.Any<CancellationToken>());
        await _notificationService.DidNotReceive()
            .SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateJobAsync_DifferentReason_SendsSecondTier()
    {
        // Two-tier alerts (QT-004): the reminder (notify window) and the urgent
        // (escalation window) escalations carry DIFFERENT reason tokens and must
        // both fire — dedupe is per (job, reason), not per job.
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.AddLog(new JobLog(DateTime.UtcNow, JobLogLevel.Warn, "Job escalated: approval reminder"));
        SetupJobFound(job);

        var result = await _sut.EscalateJobAsync(job.JobId, "playout imminent");

        result.IsSuccess.ShouldBeTrue();
        await _jobRepo.Received(1).AddLogAsync(job.JobId, "Job escalated: playout imminent",
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
        await _notificationService.Received(1)
            .SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // CreateJobAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateJobAsync_ValidCommand_ReturnsJobWithCorrectProperties()
    {
        var command = new CreateJobCommand("SYD", 30.0,
            33.8, 151.2, 25.0, "Australia/Sydney",
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        var job = result.Value;
        job.Station.ShouldBe("SYD");
        // No schedule backs "SYD" (default empty schedule list) — Market stays the raw
        // Location text, NOT a slugified variant: a slug of an unmatched location can coincide
        // with a real market id and make the record claim a market the bulletin was generated
        // without (bug-bash 2026-08-05 #7).
        job.Market.ShouldBe("SYD");
        job.TargetDurationSeconds.ShouldBe(30.0);
        job.Status.ShouldBe(JobStatus.Requested);
        job.VoiceConfig.HasValue.ShouldBeTrue();
        job.Latitude.HasValue.ShouldBeTrue();
        job.Longitude.HasValue.ShouldBeTrue();
        await _jobRepo.Received(1).SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task CreateJobAsync_UnresolvableVoiceProfile_ReturnsFailure()
    {
        _voiceProfiles.GetAsync("vp-missing", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("profile not found"));

        var command = new CreateJobCommand("SYD", 30.0,
            VoiceProfileId: Option<string>.Some("vp-missing"));

        var result = await _sut.CreateJobAsync(command);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("could not be resolved");
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateJobAsync_ResolvedVoiceProfile_StoresCurrentTuningOnJob()
    {
        var settings = VoiceSettings.Create(
            stability: 0.72,
            similarityBoost: 0.81,
            style: 0.13,
            useSpeakerBoost: false,
            speed: 1.1).Value;
        var profile = VoiceProfile.Create(
            "Studio Voice",
            "Profile description",
            "profile-base-voice",
            "Base Voice",
            "eleven_multilingual_v2",
            settings,
            "Initial tuning",
            "test",
            DateTime.UtcNow,
            paceBoostWpm: 18,
            id: "vp-resolved").Value;
        _voiceProfiles.GetAsync("vp-resolved", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(profile));

        var command = new CreateJobCommand("SYD", 30.0,
            VoiceProfileId: Option<string>.Some("vp-resolved"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        var voiceConfig = result.Value.VoiceConfig.Value;
        voiceConfig.VoiceId.ShouldBe("profile-base-voice");
        voiceConfig.VoiceName.ShouldBe("Studio Voice");
        voiceConfig.Model.ShouldBe("eleven_multilingual_v2");
        voiceConfig.Settings.HasValue.ShouldBeTrue();
        voiceConfig.Settings.Value.ShouldBe(settings);
        voiceConfig.PaceBoostWpm.ShouldBe(18);
    }

    [Fact]
    public async Task CreateJobAsync_BareVoice_UsesProviderVoiceName()
    {
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success(
                [new Voice("provider-voice", "Provider Voice", Option<string>.None, Option<string>.None, new Dictionary<string, string>())]));

        var command = new CreateJobCommand("SYD", 30.0,
            VoiceId: Option<string>.Some("provider-voice"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceConfig.Value.VoiceName.ShouldBe("Provider Voice");
    }


    [Fact]
    public async Task CreateJobAsync_LocationMatchesScheduleName_ResolvesToCanonicalMarketId()
    {
        // Manual/ad-hoc broadcasts carry only a free-text Location — it must resolve to the
        // schedule's canonical MarketId slug, not be duplicated verbatim into job.Market
        // (C1/H2: raw display text breaks exact-match market-scoped operator access). The
        // job's Station must likewise be the SCHEDULE's station name — the same value the
        // poller writes — so station-keyed corpora (future lessons) taught on ad-hoc bulletins
        // stay visible to schedule-station lookups (guidance/promote 404 otherwise).
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                [CreateSchedule("sydney", "Sydney, NSW, Australia")]));
        var command = new CreateJobCommand("Sydney, NSW, Australia", 30.0,
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        var job = result.Value;
        job.Station.ShouldBe("2GB");
        job.Market.ShouldBe("sydney");
    }

    [Fact]
    public async Task CreateJobAsync_LocationMatchesScheduleMarketId_ResolvesToCanonicalMarketId()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                [CreateSchedule("melbourne", "Melbourne Drive")]));
        var command = new CreateJobCommand("melbourne", 30.0,
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Market.ShouldBe("melbourne");
    }

    [Fact]
    public async Task CreateJobAsync_LocationNoScheduleMatch_KeepsRawLocation_NotSlugOrRealMarket()
    {
        // An unmatched location must not silently become a slug: "Gold Coast" slugs to
        // "goldcoast", which IS a real market id, so the record would claim a configured
        // market the bulletin was generated without (observed live 2026-08-05: the goldcoast
        // job's record claimed the real market while its log said nothing was configured).
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                [CreateSchedule("mildura", "Mildura")]));
        var command = new CreateJobCommand("Mildura Drive Traffic", 30.0,
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Market.ShouldBe("Mildura Drive Traffic");
    }

    [Fact]
    public async Task CreateJobAsync_ScheduleLookupFails_KeepsRawLocation()
    {
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Failure("Table Storage unavailable"));
        var command = new CreateJobCommand("Sydney, NSW, Australia", 30.0,
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Market.ShouldBe("Sydney, NSW, Australia");
    }

    [Fact]
    public async Task CreateJobAsync_LocationMatchesScheduleStation_ResolvesToCanonicalMarketId()
    {
        // The schedule's Station display name ("DCOAST 101.7") is a legitimate operator-visible
        // value copied straight from the console; it must resolve to the real market, not mint
        // an orphan one (observed live 2026-08-02: Market=dcoast1017 matching no schedule).
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                [CreateSchedule("demo-coast", "Demo Coast FM", "DCOAST 101.7")]));
        var command = new CreateJobCommand("DCOAST 101.7", 30.0,
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Market.ShouldBe("demo-coast");
    }

    [Fact]
    public async Task CreateJobAsync_StationMatchIsCaseAndWhitespaceInsensitive()
    {
        // "  dcoast  101.7 " (extra whitespace, wrong case) must still resolve — the console
        // display value is copied, not typed, so exact-match would be a footgun.
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(
                [CreateSchedule("demo-coast", "Demo Coast FM", "DCOAST 101.7")]));
        var command = new CreateJobCommand("  dcoast  101.7 ", 30.0,
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Market.ShouldBe("demo-coast");
    }

    [Fact]
    public async Task CreateJobAsync_NoVoice_ReturnsFailure()
    {
        var command = new CreateJobCommand("MEL", 25.0);

        var result = await _sut.CreateJobAsync(command);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(
            "A voice is required: supply VoiceProfileId (preferred, resolves the profile's full tuning) or VoiceId.");
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateJobAsync_SaveFails_ReturnsFailure()
    {
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Table Storage unavailable"));
        var command = new CreateJobCommand("SYD", 30.0,
            VoiceId: Option<string>.Some("oKDz3nEzNfwKIVvUwPs0"));

        var result = await _sut.CreateJobAsync(command);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Table Storage unavailable");
    }

    // ──────────────────────────────────────────────
    // RejectJobAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RejectJobAsync_ValidRejection_Succeeds()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);
        var command = new RejectJobCommand(job.JobId, RejectionReasonCode.RevisionRequested, "Script pacing is too rushed", "producer.jane@arn.com.au");

        var result = await _sut.RejectJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Rejected);
        job.RejectionHistory.Single().By.ShouldBe("producer.jane@arn.com.au");
        await _jobRepo.Received(1).SaveJobAsync(job, Arg.Any<CancellationToken>());
        await _jobRepo.Received(1).AddLogAsync(job.JobId, Arg.Is<string>(s => s.Contains("rejected")), JobLogLevel.Warn,
            Arg.Any<CancellationToken>());
        // Card design 2026-07-05: operator-initiated transitions are audit noise - no card.
        await _notificationService.DidNotReceive().SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectJobAsync_EmptyNotes_Succeeds()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);
        var command = new RejectJobCommand(job.JobId, RejectionReasonCode.RevisionRequested, "", "operator-test");

        var result = await _sut.RejectJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Rejected);
        await _jobRepo.Received(1).AddLogAsync(job.JobId,
            "Job rejected: RevisionRequested.", JobLogLevel.Warn, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectJobAsync_JobNotFound_ReturnsFailure()
    {
        SetupJobNotFound();
        var command = new RejectJobCommand("missing", RejectionReasonCode.RevisionRequested, "notes", "operator-test");

        var result = await _sut.RejectJobAsync(command);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task RejectJobAsync_JobNotWaitingApproval_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.Approved);
        SetupJobFound(job);
        var command = new RejectJobCommand(job.JobId, RejectionReasonCode.RevisionRequested, "notes", "operator-test");

        var result = await _sut.RejectJobAsync(command);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task RejectJobAsync_StampsTheRealActorInStatusHistory()
    {
        // The reject actor must flow into the StatusHistory audit stamp too — before the fix it
        // defaulted to the literal "operator" while the real identity lived only in
        // RejectionHistory, so the two audit surfaces disagreed (observed live 2026-08-05).
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);
        var command = new RejectJobCommand(job.JobId, RejectionReasonCode.RevisionRequested, "Script pacing is too rushed", "producer.jane@arn.com.au");

        var result = await _sut.RejectJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        var rejectionStamp = job.StatusHistory.Last(s => s.Status == JobStatus.Rejected);
        rejectionStamp.By.ShouldBe("producer.jane@arn.com.au");
        rejectionStamp.By.ShouldNotBe("operator");
    }

    [Fact]
    public async Task RejectJobAsync_SaveFails_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Job was modified by another writer; please retry."));
        var command = new RejectJobCommand(job.JobId, RejectionReasonCode.RevisionRequested, "notes", "operator-test");

        var result = await _sut.RejectJobAsync(command);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("Job was modified by another writer; please retry.");
    }

    // ──────────────────────────────────────────────
    // PublishJobAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PublishJobAsync_FromApproved_Succeeds()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        job.PublishedBy.ShouldBe("unknown");
        job.PublishedAt.ShouldNotBeNull();
        await _jobRepo.Received(1).SaveJobAsync(job, Arg.Any<CancellationToken>());
        await _jobRepo.Received(1)
            .AddLogAsync(job.JobId, Arg.Any<string>(), JobLogLevel.Info, Arg.Any<CancellationToken>());
        // Card design 2026-07-05: operator-initiated transitions are audit noise - no card.
        await _notificationService.DidNotReceive().SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_FromNonApproved_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.MustBeApprovedToPublish);
    }

    [Fact]
    public async Task PublishJobAsync_FromNonApproved_WithAudio_NeverDeliversToPlayout()
    {
        // C1 (critical): publish must validate approval BEFORE any side effect. Before the
        // fix, a WaitingApproval job with audio present would still have its audio delivered
        // to playout even though the Publish transition itself would later fail.
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.MustBeApprovedToPublish);
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_JobNotFound_ReturnsFailure()
    {
        SetupJobNotFound();

        var result = await _sut.PublishJobAsync("missing");

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishJobAsync_WithAudio_MarksAudioPublished()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _broadcastOutput.Received(1)
            .MarkPublishedAsync("broadcast_20260612_080000.wav", Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task PublishJobAsync_WithAudio_SavesPublishedTransitionBeforeDeliveryAuditLog()
    {
        // C1 regression guard: AddLogAsync bumps the stored row's ETag directly without
        // updating this in-memory Job's ETag. Before the fix, the "delivered to playout"
        // audit log was written BEFORE the Published-transition SaveJobAsync, so that save
        // presented a now-stale ETag and permanently 409'd. Assert no AddLogAsync call for
        // this job happens before the transition is durably saved.
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        SetupJobFound(job);

        bool saved = false;
        bool logWrittenBeforeSave = false;
        _jobRepo.AddLogAsync(job.JobId, Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success())
            .AndDoes(_ =>
            {
                if (!saved) logWrittenBeforeSave = true;
            });
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success())
            .AndDoes(_ => saved = true);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        saved.ShouldBeTrue();
        logWrittenBeforeSave.ShouldBeFalse();
        await _jobRepo.Received(1).AddLogAsync(job.JobId, "Audio delivered to playout system", JobLogLevel.Info,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_WithoutAudio_DoesNotMarkPublished()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _broadcastOutput.DidNotReceive()
            .MarkPublishedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     A normal operator bulletin must reach the drop by FallbackDeadlineUtc (scheduled air
    ///     minus the ingest buffer). Pinned so the covered-slot exception below can never leak
    ///     into the operator path.
    /// </summary>
    [Fact]
    public async Task PublishJobAsync_NormalJob_HoldsDeliveryToTheFallbackDeadline()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        job.ScheduledForUtc = Option<DateTime>.Some(new DateTime(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc));
        job.FallbackDeadlineUtc = Option<DateTime>.Some(new DateTime(2026, 6, 12, 7, 57, 0, DateTimeKind.Utc));
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _fileDeliveryService.Received(1).DeliverAsync(
            job.AudioUrl.Value, job.Market, Arg.Any<string?>(),
            new DateTimeOffset(new DateTime(2026, 6, 12, 7, 57, 0, DateTimeKind.Utc)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     A COVERED slot (standby cart / same-day repeat, FallbackAppliedAt set) only ever exists
    ///     at or after FallbackDeadlineUtc — the sweep may not supersede a live slot. Holding its
    ///     delivery to that same clock made coverage unsatisfiable: prod 2026-08-16, three
    ///     unattended slots covered ~0.4s past the deadline, every delivery refused, MISSED.
    ///     The cover is airable until the slot's air time, so that is its delivery cutoff.
    /// </summary>
    [Fact]
    public async Task PublishJobAsync_CoveredSlot_HoldsDeliveryToTheAirTimeInstead()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("filler-cart.wav");
        job.ScheduledForUtc = Option<DateTime>.Some(new DateTime(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc));
        job.FallbackDeadlineUtc = Option<DateTime>.Some(new DateTime(2026, 6, 12, 7, 57, 0, DateTimeKind.Utc));
        job.FallbackAppliedAt = Option<DateTime>.Some(new DateTime(2026, 6, 12, 7, 57, 0, 400, DateTimeKind.Utc));
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _fileDeliveryService.Received(1).DeliverAsync(
            job.AudioUrl.Value, job.Market, Arg.Any<string?>(),
            new DateTimeOffset(new DateTime(2026, 6, 12, 8, 0, 0, DateTimeKind.Utc)),
            Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task PublishJobAsync_TaggingFails_PublishStillSucceeds()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        SetupJobFound(job);
        _broadcastOutput.MarkPublishedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("storage unavailable"));

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        await _fileDeliveryService.Received(1)
            .DeliverAsync(job.AudioUrl.Value, job.Market, Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_TaggingThrows_PublishStillSucceeds()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        SetupJobFound(job);
        _broadcastOutput.MarkPublishedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new InvalidOperationException("boom"));

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
    }

    [Fact]
    public async Task PublishJobAsync_SaveFails_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        SetupJobFound(job);
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Job was modified by another writer; please retry."));

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("Job was modified by another writer; please retry.");
    }

    [Fact]
    public async Task PublishJobAsync_MismatchedExpectedEtag_ReturnsFailure_NeverDeliversOrSaves()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "current-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "stale-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.RevisionMismatch);
        job.Status.ShouldBe(JobStatus.Approved);
        job.PublishedBy.ShouldBeNull();
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_MatchingExpectedEtag_Succeeds()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "current-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
    }

    [Fact]
    public async Task PublishJobAsync_NullEtag_ReturnsFailure_NeverDelivers()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: null);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.EtagRequired);
        job.Status.ShouldBe(JobStatus.Approved);
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_EmptyEtag_ReturnsFailure_NeverDelivers()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.EtagRequired);
        job.Status.ShouldBe(JobStatus.Approved);
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_ByObjectIdProvided_StampsPublishedByObjectId()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, "operator@arn.com.au", expectedEtag: "test-etag", byObjectId: "obj-id-456");

        result.IsSuccess.ShouldBeTrue();
        job.PublishedByObjectId.ShouldBe("obj-id-456");
    }
    [Fact]
    public async Task PublishJobAsync_FromFallbackUsed_ReturnsFailure_NeverDelivers()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.TransitionTo(JobStatus.FallbackUsed, utcNow: DateTime.UtcNow);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, "operator@arn.com.au", expectedEtag: "test-etag", byObjectId: "obj-id-789");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.MustBeApprovedToPublish);
        // Publishing a covered slot DIRECTLY is still refused: the audio has to be signed
        // off first, which is exactly what DeliverCoveredSlotAsync does before it publishes.
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_FromFallbackUsed_AfterApproval_SucceedsAndDeliversAudioSha256()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.TransitionTo(JobStatus.FallbackUsed, utcNow: DateTime.UtcNow);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        job.AudioSha256 = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        SetupJobFound(job);

        // To air a fallback job: Approve first, then Publish.
        job.Approve(DateTime.UtcNow, "operator@arn.com.au");
        job.ETag = "test-etag-2";
        // Re-stub the job lookup so the next PublishJobAsync fetch returns the updated job.
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, "operator@arn.com.au", expectedEtag: "test-etag-2", byObjectId: "obj-id-789");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        await _fileDeliveryService.Received(1)
            .DeliverAsync(job.AudioUrl.Value, job.Market, "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // RejectToFallbackAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RejectToFallbackAsync_NoSameDayBulletin_AppliesFillerCart()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);
        _jobRepo.GetLastSuccessfulJobForMarketAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));

        var metrics = Substitute.For<IBroadcastMetrics>();
        var sut = new JobService(_jobRepo, _scheduleRepository, _queuePublisher, _modelSelector, Substitute.For<IVoiceProfileService>(),
            Substitute.For<IVoiceProvider>(),
            _notificationService,
            _fileDeliveryService,
            _broadcastOutput,
            Substitute.For<IPronunciationDictionaryService>(),
            Substitute.For<IDeliveryStyleService>(),
            Substitute.For<IBookendService>(),
            metrics,
            Options.Create(new BroadcastOptions
            {
                DefaultVoiceId = "default-voice",
                DefaultLanguage = "en",
                FillerCartAudioUrl = "https://example.com/filler.mp3"
            }),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()), Substitute.For<ILogger<PerVoiceFillerCartResolver>>()), Substitute.For<IBulletinFallbackService>(), TimeProvider.System, Substitute.For<ILogger<JobService>>());

        var result = await sut.RejectToFallbackAsync(
            new RejectToFallbackCommand(job.JobId, RejectionReasonCode.RevisionRequested, "reports a crash that isn't there", "operator-test"));

        result.IsSuccess.ShouldBeTrue();
        // Reject-to-fallback delivers what it covers, so the job ends Published: the
        // operator said "air the fallback", and a status change that left the drop
        // folder empty was the bug. FallbackUsed is now a transient step, and the
        // provenance stays readable via the Fallback block on the published DTO.
        job.Status.ShouldBe(JobStatus.Published);
        metrics.Received(1).TrackFallbackActivated("SYD", "operator_rejected_filler");
    }

    [Fact]
    public async Task RejectToFallbackAsync_NoSameDay_PerVoiceCartResolves_AppliesPerVoiceCart()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);
        _jobRepo.GetLastSuccessfulJobForMarketAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));

        var perVoiceCache = Substitute.For<IFillerAudioCacheService>();
        var perVoiceFillerCart = new PerVoiceFillerCartResolver(
            perVoiceCache, Options.Create(new BroadcastOptions { FillerCartScript = "Test cart script." }),
            Substitute.For<ILogger<PerVoiceFillerCartResolver>>());
        perVoiceCache.ResolveCartAsync(
                "Test cart script.", "voice-1", VoiceSettings.Default, "eleven_v3", Arg.Any<CancellationToken>())
            .Returns(Result<ResolvedCartClip>.Success(new ResolvedCartClip(
                "https://example.com/filler-cache/__cart__/voice-key.wav", [1, 2, 3], 15.98, [])));
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "elevenlabs", "voice-1", "Voice One", "eleven_v3",
            Option<VoiceSettings>.Some(VoiceSettings.Default)));

        var metrics = Substitute.For<IBroadcastMetrics>();
        var sut = new JobService(_jobRepo, _scheduleRepository, _queuePublisher, _modelSelector, Substitute.For<IVoiceProfileService>(),
            Substitute.For<IVoiceProvider>(),
            _notificationService,
            _fileDeliveryService,
            _broadcastOutput,
            Substitute.For<IPronunciationDictionaryService>(),
            Substitute.For<IDeliveryStyleService>(),
            Substitute.For<IBookendService>(),
            metrics,
            Options.Create(new BroadcastOptions
            {
                DefaultVoiceId = "default-voice",
                DefaultLanguage = "en",
                FillerCartAudioUrl = "https://example.com/evergreen.mp3"
            }),
            perVoiceFillerCart, Substitute.For<IBulletinFallbackService>(), TimeProvider.System, Substitute.For<ILogger<JobService>>());

        var result = await sut.RejectToFallbackAsync(
            new RejectToFallbackCommand(job.JobId, RejectionReasonCode.RevisionRequested, "reports a crash that isn't there", "operator-test"));

        result.IsSuccess.ShouldBeTrue();
        // Reject-to-fallback delivers what it covers, so the job ends Published: the
        // operator said "air the fallback", and a status change that left the drop
        // folder empty was the bug. FallbackUsed is now a transient step, and the
        // provenance stays readable via the Fallback block on the published DTO.
        job.Status.ShouldBe(JobStatus.Published);
        // Per-voice cart URL wins over the configured evergreen cart.
        job.AudioUrl.Value.ShouldBe("https://example.com/filler-cache/__cart__/voice-key.wav");
        // Finding 32 visibility on the operator path: the measured under-fill is surfaced on the
        // job and warned about, so approving does not ship a silent 14s hole.
        job.MeasuredDurationSec.Value.ShouldBe(15.98);
        await _jobRepo.Received(1).AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.Contains("dead air", StringComparison.Ordinal)),
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
        metrics.Received(1).TrackFallbackActivated("SYD", "operator_rejected_filler");
    }

    [Fact]
    public async Task RejectToFallbackAsync_FromNonWaiting_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.Approved);
        SetupJobFound(job);

        var result = await _sut.RejectToFallbackAsync(
            new RejectToFallbackCommand(job.JobId, RejectionReasonCode.RevisionRequested, "notes", "operator-test"));

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task RejectToFallbackAsync_MissingNotes_StillProceedsToJobLookup()
    {
        // Notes are optional; with no stubbed job the next failure is the lookup, not a
        // notes guard.
        SetupJobNotFound("job-1");

        var result = await _sut.RejectToFallbackAsync(
            new RejectToFallbackCommand("job-1", RejectionReasonCode.RevisionRequested, "", "operator-test"));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("not found");
    }

    [Fact]
    public async Task RejectToFallbackAsync_SameDayBulletinExists_ReusesItNotFiller()
    {
        // Regression: an operator reject must re-air the most recent approved same-day
        // bulletin when one is available, not fall straight through to the generic filler cart.
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);

        var source = Job.Create("prior-approved", "SYD", "SYD", 30.0, DateTime.UtcNow).Value;
        source.AudioUrl = Option<string>.Some("https://blob/prior.mp3");
        source.ScriptText = Option<string>.Some("prior script");
        _jobRepo.GetLastSuccessfulJobForMarketAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(source)));

        var metrics = Substitute.For<IBroadcastMetrics>();
        var sut = new JobService(_jobRepo, _scheduleRepository, _queuePublisher, _modelSelector, Substitute.For<IVoiceProfileService>(),
            Substitute.For<IVoiceProvider>(),
            _notificationService,
            _fileDeliveryService,
            _broadcastOutput,
            Substitute.For<IPronunciationDictionaryService>(),
            Substitute.For<IDeliveryStyleService>(),
            Substitute.For<IBookendService>(),
            metrics,
            Options.Create(new BroadcastOptions
            {
                DefaultVoiceId = "default-voice",
                DefaultLanguage = "en"
            }),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()), Substitute.For<ILogger<PerVoiceFillerCartResolver>>()), Substitute.For<IBulletinFallbackService>(), TimeProvider.System, Substitute.For<ILogger<JobService>>());

        var result = await sut.RejectToFallbackAsync(
            new RejectToFallbackCommand(job.JobId, RejectionReasonCode.RevisionRequested, "reports a crash that isn't there", "operator-test"));

        result.IsSuccess.ShouldBeTrue();
        // Reject-to-fallback delivers what it covers, so the job ends Published: the
        // operator said "air the fallback", and a status change that left the drop
        // folder empty was the bug. FallbackUsed is now a transient step, and the
        // provenance stays readable via the Fallback block on the published DTO.
        job.Status.ShouldBe(JobStatus.Published);
        job.AudioUrl.Value.ShouldBe("https://blob/prior.mp3"); // reused the prior bulletin, not the filler cart
        metrics.Received(1).TrackFallbackActivated("SYD", "operator_rejected_same_day");
    }

    [Fact]
    public async Task RejectToFallbackAsync_SaveFails_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        SetupJobFound(job);
        _jobRepo.GetLastSuccessfulJobForMarketAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Job was modified by another writer; please retry."));

        var metrics = Substitute.For<IBroadcastMetrics>();
        var sut = new JobService(_jobRepo, _scheduleRepository, _queuePublisher, _modelSelector, Substitute.For<IVoiceProfileService>(),
            Substitute.For<IVoiceProvider>(),
            _notificationService,
            _fileDeliveryService,
            _broadcastOutput,
            Substitute.For<IPronunciationDictionaryService>(),
            Substitute.For<IDeliveryStyleService>(),
            Substitute.For<IBookendService>(),
            metrics,
            Options.Create(new BroadcastOptions
            {
                DefaultVoiceId = "default-voice",
                DefaultLanguage = "en",
                FillerCartAudioUrl = "https://example.com/filler.mp3"
            }),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()), Substitute.For<ILogger<PerVoiceFillerCartResolver>>()), Substitute.For<IBulletinFallbackService>(), TimeProvider.System, Substitute.For<ILogger<JobService>>());

        var result = await sut.RejectToFallbackAsync(
            new RejectToFallbackCommand(job.JobId, RejectionReasonCode.RevisionRequested, "notes", "operator-test"));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("Job was modified by another writer; please retry.");
        metrics.DidNotReceive().TrackFallbackActivated(Arg.Any<string>(), Arg.Any<string>());
    }

    // ──────────────────────────────────────────────
    // SetAirSourceAsync — the operator override of what airs in a slot: attach a chosen
    // source (an earlier same-day bulletin or the filler cart) to the slot's job AND deliver
    // it to playout before the call returns.
    // ──────────────────────────────────────────────

    private Job CreateApprovedSourceJob(string id, string station = "SYD", string market = "SYD",
        DateTime? createdAt = null)
    {
        var source = Job.Create(id, station, market, 30.0, createdAt ?? DateTime.UtcNow).Value;
        source.AudioUrl = Option<string>.Some("https://blob/source.wav");
        source.ScriptText = Option<string>.Some("source script");
        var created = createdAt ?? DateTime.UtcNow;
        source.TransitionTo(JobStatus.WaitingApproval, utcNow: created);
        source.Approve(created, "prior-operator");
        return source;
    }

    [Fact]
    public async Task SetAirSourceAsync_Repeat_AppliesSourceAudioAndPublishes()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);
        var source = CreateApprovedSourceJob("source-1");
        SetupJobFound(source);
        // Timezone only feeds the day-window (same convention as BulletinFallbackServiceTests):
        // an unresolvable schedule degrades to the UTC day window, which is fine here.
        _scheduleRepository.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("none"));

        var result = await _sut.SetAirSourceAsync(
            new SetAirSourceCommand(job.JobId, AirSourceKind.Repeat, source.JobId, "op-test", "test-etag"));

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        // The chosen source's audio was attached to the slot job, not the slot's own (absent) audio.
        job.AudioUrl.Value.ShouldBe("https://blob/source.wav");
        job.FallbackSourceJobId.Value.ShouldBe(source.JobId);
        // The reason names the operator and records that this was a manual choice.
        job.FallbackReason.Value.ShouldContain("op-test");
        // The approve→publish delivery is attributed to the requesting operator, not to "system".
        job.ApprovedBy.ShouldBe("op-test");
        job.PublishedBy.ShouldBe("op-test");
        await _fileDeliveryService.Received(1)
            .DeliverAsync("https://blob/source.wav", "SYD", Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        // The override itself is audited on the job (who, what they chose).
        await _jobRepo.Received(1).AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.Contains("op-test set the air source", StringComparison.Ordinal)),
            JobLogLevel.Info, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAirSourceAsync_Cart_CallsFillerCartPathAndPublishes()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);

        var fallbackService = Substitute.For<IBulletinFallbackService>();
        fallbackService.ApplyFillerCartAsync(job, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var applied = job.ApplyFillerFallback("https://example.com/filler.mp3", DateTime.UtcNow,
                    (string)callInfo[2], (string)callInfo[1]);
                return applied.IsSuccess ? Result.Success() : Result.Failure(applied.Error);
            });

        var sut = new JobService(_jobRepo, _scheduleRepository, _queuePublisher, _modelSelector, _voiceProfiles,
            _voiceProvider,
            _notificationService, _fileDeliveryService,
            _broadcastOutput,
            _pronunciation,
            _delivery,
            _bookends,
            Substitute.For<IBroadcastMetrics>(),
            Options.Create(new BroadcastOptions
            { DefaultVoiceId = "default-voice", DefaultLanguage = "en" }),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()),
                Substitute.For<ILogger<PerVoiceFillerCartResolver>>()),
            fallbackService, TimeProvider.System, Substitute.For<ILogger<JobService>>());

        var result = await sut.SetAirSourceAsync(
            new SetAirSourceCommand(job.JobId, AirSourceKind.Cart, null, "op-test", "test-etag"));

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        job.AudioUrl.Value.ShouldBe("https://example.com/filler.mp3");
        // The filler-cart path was used and the operator's identity flowed into it.
        await fallbackService.Received(1)
            .ApplyFillerCartAsync(job, Arg.Any<string>(), "op-test", Arg.Any<CancellationToken>());
        job.FallbackReason.Value.ShouldContain("op-test");
        await _fileDeliveryService.Received(1)
            .DeliverAsync("https://example.com/filler.mp3", "SYD", Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAirSourceAsync_Repeat_SourceFromAnotherStation_IsRejected()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);
        var source = CreateApprovedSourceJob("source-1", station: "3AW", market: "MEL");
        SetupJobFound(source);

        var result = await _sut.SetAirSourceAsync(
            new SetAirSourceCommand(job.JobId, AirSourceKind.Repeat, source.JobId, "op-test", "test-etag"));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("different market or station");
        // Nothing was applied, saved, or delivered.
        job.Status.ShouldBe(JobStatus.WaitingApproval);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAirSourceAsync_Repeat_SourceFromPreviousDay_IsRejected()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);
        var yesterday = DateTime.UtcNow.AddDays(-1);
        var source = CreateApprovedSourceJob("source-1", createdAt: yesterday);
        SetupJobFound(source);
        _scheduleRepository.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("none"));

        var result = await _sut.SetAirSourceAsync(
            new SetAirSourceCommand(job.JobId, AirSourceKind.Repeat, source.JobId, "op-test", "test-etag"));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("previous broadcast day");
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAirSourceAsync_EtagMismatch_FailsWithoutTouchingDelivery()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "current-etag";
        SetupJobFound(job);

        var result = await _sut.SetAirSourceAsync(
            new SetAirSourceCommand(job.JobId, AirSourceKind.Repeat, "source-1", "op-test", "stale-etag"));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.RevisionMismatch);
        // The mismatch is checked before ANY side effect: no source load, no apply, no save,
        // no delivery.
        job.Status.ShouldBe(JobStatus.WaitingApproval);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAirSourceAsync_DeliveryFailure_SurfacesFailureAndLeavesAuditEntry()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "test-etag";
        SetupJobFound(job);
        var source = CreateApprovedSourceJob("source-1");
        SetupJobFound(source);
        _scheduleRepository.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("none"));

        _fileDeliveryService.DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Result<FileDeliveryStatus>.Failure("Zetta drop folder unreachable"));

        var result = await _sut.SetAirSourceAsync(
            new SetAirSourceCommand(job.JobId, AirSourceKind.Repeat, source.JobId, "op-test", "test-etag"));

        result.IsFailure.ShouldBeTrue();
        // The operator is told the slot WAS changed but the file did not reach playout — never
        // a bare success, and never silence.
        result.Error.ShouldContain("did NOT reach playout");
        job.Status.ShouldBe(JobStatus.Approved); // applied + approved, but not delivered
        // The override itself is audited regardless of the delivery outcome...
        await _jobRepo.Received(1).AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.Contains("op-test set the air source", StringComparison.Ordinal)),
            JobLogLevel.Info, Arg.Any<CancellationToken>());
        // ...and the delivery failure is written to the job's audit log too.
        await _jobRepo.Received(1).AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.Contains("did NOT reach playout", StringComparison.Ordinal)),
            JobLogLevel.Error, Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The revision guard bit us on dev: every path that covers a slot saves the job first,
    ///     and <c>SaveJobAsync</c> does not write the new ETag back onto the caller's instance,
    ///     so a held reference is stale the moment it is saved. Passing it to the
    ///     revision-guarded approve made the sign-off fail and the covered slot never reached
    ///     the drop — visible only in telemetry. Delivery therefore re-reads before BOTH
    ///     lifecycle calls, and this pins that: storage is authoritative about the revision.
    /// </summary>
    [Fact]
    public async Task DeliverCoveredSlotAsync_ReReadsTheRevisionBeforeEachGuardedCall()
    {
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.ETag = "etag-stale";
        SetupJobFound(job);

        // Every write advances the stored revision, exactly as Table Storage does — and the
        // approve write also advances the stored STATUS, which publish's own guard reads.
        string stored = "etag-1";
        _jobRepo.When(r => r.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()))
            .Do(_ => stored = stored == "etag-1" ? "etag-2" : "etag-3");
        _jobRepo.GetJobAsync(job.JobId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                // A distinct instance, as the entity mapper hands back — mutating the caller's
                // own object here would hide the very staleness this test exists to catch.
                var fresh = CreateTestJob(
                    stored == "etag-1" ? JobStatus.WaitingApproval : JobStatus.Approved);
                fresh.ETag = stored;
                // A covered slot always carries the audio it is covered WITH; publish only
                // delivers when it does, and DeliverCoveredSlotAsync refuses without it.
                fresh.AudioUrl = Option<string>.Some("https://audio.example.com/cart.wav");
                return Result<Job>.Success(fresh);
            });

        var result = await _sut.DeliverCoveredSlotAsync(
            job.JobId, "the standby cart", "system (deadline)");

        result.IsSuccess.ShouldBeTrue();

        await _fileDeliveryService.Received(1).DeliverAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        // The coverage line states coverage only. It deliberately does NOT claim the audio
        // reached playout: publish returns Success for a skipped delivery too (playout off for
        // the environment or for the station), so claiming delivery here contradicted the
        // delivery line on the same job. Whether it went is asserted from the delivery call
        // above and logged by PublishJobAsync itself.
        await _jobRepo.Received().AddLogAsync(job.JobId,
            "Slot covered by the standby cart.", JobLogLevel.Warn, Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().AddLogAsync(job.JobId,
            Arg.Is<string>(m => m.Contains("covered") && m.Contains("delivered to playout")),
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // ProcessDeliveryAsync — retry and idempotency
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ProcessDeliveryAsync_TwoTransientFailures_RetainsPendingThenPublishes()
    {
        var job = CreateTestJob(JobStatus.Approved);
        var command = PreparePendingDelivery(job);
        SetupJobFound(job);
        _fileDeliveryService.DeliverAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<FileDeliveryStatus>.Failure("temporary failure one"),
                Result<FileDeliveryStatus>.Failure("temporary failure two"),
                Result<FileDeliveryStatus>.Success(FileDeliveryStatus.Delivered));

        var first = await _sut.ProcessDeliveryAsync(command);

        first.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.DeliveryPending);
        job.DeliveryAttemptCount.ShouldBe(1);
        job.DeliveryLastError.ShouldBe("temporary failure one");

        var second = await _sut.ProcessDeliveryAsync(command);

        second.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.DeliveryPending);
        job.DeliveryAttemptCount.ShouldBe(2);
        job.DeliveryLastError.ShouldBe("temporary failure two");

        var third = await _sut.ProcessDeliveryAsync(command);

        third.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        job.DeliveryAttemptCount.ShouldBe(3);
        job.DeliveryLastError.ShouldBeNull();
        await _fileDeliveryService.Received(3).DeliverAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());

        // A host redelivery after completion is an acknowledgement no-op: it must never write
        // the same drop file a second time.
        var replay = await _sut.ProcessDeliveryAsync(command);

        replay.IsSuccess.ShouldBeTrue();
        await _fileDeliveryService.Received(3).DeliverAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDeliveryAsync_ThirdFailure_MarksFailedAndRedeliveryDoesNotDeliver()
    {
        var job = CreateTestJob(JobStatus.Approved);
        var command = PreparePendingDelivery(job);
        SetupJobFound(job);
        _fileDeliveryService.DeliverAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
                Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(
                Result<FileDeliveryStatus>.Failure("temporary failure one"),
                Result<FileDeliveryStatus>.Failure("temporary failure two"),
                Result<FileDeliveryStatus>.Failure("terminal failure"));

        var first = await _sut.ProcessDeliveryAsync(command);
        var second = await _sut.ProcessDeliveryAsync(command);
        var third = await _sut.ProcessDeliveryAsync(command);

        first.IsFailure.ShouldBeTrue();
        second.IsFailure.ShouldBeTrue();
        third.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Failed);
        job.FailedAtStatus.Value.ShouldBe(JobStatus.DeliveryPending);
        job.DeliveryAttemptCount.ShouldBe(3);
        job.DeliveryLastError.ShouldBe("terminal failure");
        await _fileDeliveryService.Received(3).DeliverAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());

        var replay = await _sut.ProcessDeliveryAsync(command);

        replay.IsFailure.ShouldBeTrue();
        await _fileDeliveryService.Received(3).DeliverAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // RegenerateJobAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RegenerateJobAsync_FromRejected_Succeeds_ReturnsMessage()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId);

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobId.ShouldBe(job.JobId);
        job.Status.ShouldBe(JobStatus.Requested);
        await _jobRepo.Received(1).SaveJobAsync(job, Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // Voice override on regeneration — the operator picked a DIFFERENT voice during review.
    // Resolved server-side to the profile's CURRENT version (same trust rule as
    // CreateJobAsync: never a client-supplied snapshot), applied to BOTH the re-voice path
    // and a full regenerate, and pinned on the job for provenance.
    // ──────────────────────────────────────────────

    private static VoiceProfile ProfileWithCurrentTuning(
        string id, string model, double paceBoostWpm, VoiceSettings? settings = null) =>
        VoiceProfile.Create("Studio Voice", null, "profile-base-voice", "Base Voice",
            model, settings ?? VoiceSettings.Default, "Initial tuning", "test", DateTime.UtcNow,
            paceBoostWpm: paceBoostWpm, id: id).Value;

    [Fact]
    public async Task RegenerateJobAsync_WithVoiceOverride_ResolvesCurrentTuningAndPinsIt()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "ElevenLabs", "old-voice", "Old Voice", "eleven_multilingual_v2",
            Option<VoiceSettings>.Some(VoiceSettings.Default), PaceBoostWpm: 0));
        SetupJobFound(job);
        var settings = VoiceSettings.Create(stability: 0.72, similarityBoost: 0.81, style: 0.13,
            useSpeakerBoost: false, speed: 1.1).Value;
        _voiceProfiles.GetAsync("vp-resolved", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(
                ProfileWithCurrentTuning("vp-resolved", "eleven_multilingual_v2", 18, settings)));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            VoiceProfileId: Option<string>.Some("vp-resolved")));

        result.IsSuccess.ShouldBeTrue();
        // Persisted BEFORE the queue message is built: a later failed retry replays
        // job.VoiceConfig, so the row must already carry the operator's chosen voice.
        job.VoiceConfig.HasValue.ShouldBeTrue();
        job.VoiceConfig.Value.VoiceId.ShouldBe("profile-base-voice");
        job.VoiceConfig.Value.VoiceName.ShouldBe("Studio Voice");
        job.VoiceConfig.Value.Model.ShouldBe("eleven_multilingual_v2");
        job.VoiceConfig.Value.Settings.Value.ShouldBe(settings);
        job.VoiceConfig.Value.PaceBoostWpm.ShouldBe(18);
        job.VoiceProfileId.Value.ShouldBe("vp-resolved");
        job.VoiceProfileVersion.Value.ShouldBe(1);
        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.VoiceConfig.Match(vc => vc.VoiceId, () => "none") == "profile-base-voice"),
            Arg.Any<CancellationToken>());
        // The queue message carries the NEW voice and the boost that regen previously dropped.
        result.Value.VoiceId.ShouldBe("profile-base-voice");
        result.Value.Model.ShouldBe("eleven_multilingual_v2");
        result.Value.VoiceSettings.ShouldBe(settings);
        result.Value.PaceBoostWpm.ShouldBe(18);
    }

    [Fact]
    public async Task RegenerateJobAsync_UnresolvableVoiceOverride_FailsAndDoesNotResetTheJob()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        _voiceProfiles.GetAsync("vp-missing", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("profile not found"));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            VoiceProfileId: Option<string>.Some("vp-missing")));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("vp-missing");
        result.Error.ShouldContain("could not be resolved");
        // The hard failure happens BEFORE the regeneration reset: the job is still Rejected,
        // not half-way through a reset the operator would have to reject again to undo.
        job.Status.ShouldBe(JobStatus.Rejected);
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegenerateJobAsync_VersionlessVoiceOverride_FailsNamingTheProfile()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        var versionless = VoiceProfile.Reconstitute("vp-empty", "Empty Voice", null, "base-voice",
            "Base Voice", "ElevenLabs", "active", DateTime.UtcNow, DateTime.UtcNow, "test", [], []);
        _voiceProfiles.GetAsync("vp-empty", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(versionless));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            VoiceProfileId: Option<string>.Some("vp-empty")));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("vp-empty");
        result.Error.ShouldContain("no versions");
        job.Status.ShouldBe(JobStatus.Rejected);
    }

    [Fact]
    public async Task RegenerateJobAsync_VoiceOverrideSameModel_KeepsTheOperatorsTonedScript()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "ElevenLabs", "old-voice", "Old Voice", "eleven_multilingual_v2",
            Option<VoiceSettings>.Some(VoiceSettings.Default)));
        SetupJobFound(job);
        _voiceProfiles.GetAsync("vp-same-model", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(
                ProfileWithCurrentTuning("vp-same-model", "eleven_multilingual_v2", 0)));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            RevoiceOnly: true,
            TonedScript: Option<string>.Some("[pace slow]The West Gate is jammed.[/pace]"),
            VoiceProfileId: Option<string>.Some("vp-same-model")));

        result.IsSuccess.ShouldBeTrue();
        result.Value.TonedScript.ShouldBe("[pace slow]The West Gate is jammed.[/pace]");
    }

    [Fact]
    public async Task RegenerateJobAsync_VoiceOverrideModelCross_DropsTheTonedScript()
    {
        // Tags written for the OLD model would steer differently (or fall silent) on the new
        // one; the re-voice re-runs the delivery director, which re-derives tags for the new
        // model from the same approved words.
        var job = CreateTestJob(JobStatus.Rejected);
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "ElevenLabs", "old-voice", "Old Voice", "eleven_turbo_v2_5",
            Option<VoiceSettings>.Some(VoiceSettings.Default)));
        SetupJobFound(job);
        _voiceProfiles.GetAsync("vp-cross-model", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(
                ProfileWithCurrentTuning("vp-cross-model", "eleven_multilingual_v2", 0)));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            RevoiceOnly: true,
            TonedScript: Option<string>.Some("[pace slow]The West Gate is jammed.[/pace]"),
            VoiceProfileId: Option<string>.Some("vp-cross-model")));

        result.IsSuccess.ShouldBeTrue();
        result.Value.TonedScript.ShouldBeNull();
    }

    [Fact]
    public async Task RegenerateJobAsync_WithoutVoiceOverride_CopiesTheJobsOwnVoice()
    {
        // The regression guard for the whole feature: absent VoiceProfileId the behaviour is
        // byte-identical to before — the voice is copied from the job, the operator's toned
        // script rides through untouched, and no profile resolution (or fabricated pins) occurs.
        var job = CreateTestJob(JobStatus.Rejected);
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            "ElevenLabs", "aired-voice", "Aired Voice", "eleven_multilingual_v2",
            Option<VoiceSettings>.Some(VoiceSettings.Default), PaceBoostWpm: 12));
        SetupJobFound(job);

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            RevoiceOnly: true,
            TonedScript: Option<string>.Some("[pace slow]The West Gate is jammed.[/pace]")));

        result.IsSuccess.ShouldBeTrue();
        result.Value.VoiceId.ShouldBe("aired-voice");
        result.Value.Model.ShouldBe("eleven_multilingual_v2");
        result.Value.VoiceSettings.ShouldBe(VoiceSettings.Default);
        result.Value.TonedScript.ShouldBe("[pace slow]The West Gate is jammed.[/pace]");
        result.Value.PaceBoostWpm.ShouldBe(12);
        job.VoiceProfileId.HasValue.ShouldBeFalse();
        job.VoiceProfileVersion.HasValue.ShouldBeFalse();
        await _voiceProfiles.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // RefreshPronunciation — the operator changed a rule during review.
    // Without it the queue message carries the version the job AIRED with and
    // synthesis replays the old rules, so the fix is a silent no-op.
    // ──────────────────────────────────────────────

    private PronunciationDictionary DictionaryWithCurrentVersion(
        string providerId, string currentVersionId)
    {
        return PronunciationDictionary.Reconstitute(
            "pd-1", providerId, "Melbourne Roads", null, "active",
            DateTime.UtcNow, DateTime.UtcNow, "alice", [],
            [new PronunciationDictionaryVersion(
                4, currentVersionId, [], null, DateTime.UtcNow, "alice")]);
    }
    private static DeliveryStyle StyleWithVersions(string id, params (int Version, string Tone)[] versions) =>
        DeliveryStyle.Reconstitute(id, "Drive Energy", null, "active",
            DateTime.UtcNow, DateTime.UtcNow, "alice", [],
            versions.Select(v => new DeliveryStyleVersion(
                v.Version, new DeliveryStylePolicy(v.Tone, ["excited"], 2),
                null, DateTime.UtcNow, "alice")).ToList());

    private void SetupStyleFound(DeliveryStyle style) =>
        _delivery.GetAsync(style.Id, Arg.Any<CancellationToken>())
            .Returns(Result<DeliveryStyle>.Success(style));

    // ----------------------------------------------
    // Delivery style on regeneration. The job pins an id + version but NOT the policy, and
    // the queue message carried neither — so every regenerate fell back to the neutral
    // default and silently lost the station's configured delivery (dev, 2026-07-28).

    [Fact]
    public async Task RegenerateJobAsync_ReplaysTheDeliveryVersionTheJobAiredWith()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.DeliveryStyleId = Option<string>.Some("ds-1");
        job.DeliveryStyleVersion = Option<int>.Some(2);
        SetupJobFound(job);
        SetupStyleFound(StyleWithVersions("ds-1", (2, "AIRED tone"), (3, "NEWER tone")));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.Value.DeliveryStyleVersion.ShouldBe(2);
        result.Value.DeliveryStyle!.ToneDescription.ShouldBe("AIRED tone");
    }

    [Fact]
    public async Task RegenerateJobAsync_RefreshDelivery_UsesTheStylesCurrentVersion()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.DeliveryStyleId = Option<string>.Some("ds-1");
        job.DeliveryStyleVersion = Option<int>.Some(2);
        SetupJobFound(job);
        SetupStyleFound(StyleWithVersions("ds-1", (2, "AIRED tone"), (3, "NEWER tone")));

        var result = await _sut.RegenerateJobAsync(
            new RegenerateJobCommand(job.JobId, RefreshDelivery: true));

        result.Value.DeliveryStyleVersion.ShouldBe(3);
        result.Value.DeliveryStyle!.ToneDescription.ShouldBe("NEWER tone");
    }

    // ----------------------------------------------
    // Bookends on regeneration. Same trap as the delivery style above, and it bit for the same
    // reason: RequestRegeneration CLEARS BookendId/Version, so resolving the pair after the reset
    // reads an already-empty job and the regenerated bulletin silently loses the station's opener
    // and closer (caught on dev, 2026-08-04, regenerating adelaide job 28e0d261fe71).

    private static Bookend PairWithVersions(
        string id, params (int Version, string Intro, string Outro)[] versions) =>
        Bookend.Reconstitute(id, "QA pair", null, "active",
            DateTime.UtcNow, DateTime.UtcNow, "alice", [],
            versions.Select(v => new BookendVersion(
                v.Version, new BookendTemplate(v.Intro, v.Outro), null, DateTime.UtcNow, "alice")).ToList());

    private void SetupBookendFound(Bookend bookend) =>
        _bookends.GetAsync(bookend.Id, Arg.Any<CancellationToken>())
            .Returns(Result<Bookend>.Success(bookend));

    [Fact]
    public async Task RegenerateJobAsync_CarriesTheBookendPairIntoTheQueueMessage()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.BookendId = Option<string>.Some("bk-1");
        job.BookendVersion = Option<int>.Some(1);
        SetupJobFound(job);
        SetupBookendFound(PairWithVersions("bk-1", (1, "Opening line.", "Closing line.")));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Bookends.ShouldNotBeNull();
        result.Value.Bookends!.IntroText.ShouldBe("Opening line.");
        result.Value.Bookends.OutroText.ShouldBe("Closing line.");
        result.Value.Bookends.Version.ShouldBe(1);
    }

    [Fact]
    public async Task RegenerateJobAsync_ReplaysTheBookendVersionTheJobAiredWith()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.BookendId = Option<string>.Some("bk-1");
        job.BookendVersion = Option<int>.Some(2);
        SetupJobFound(job);
        SetupBookendFound(PairWithVersions("bk-1",
            (2, "AIRED open.", "AIRED close."), (3, "NEWER open.", "NEWER close.")));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.Value.Bookends!.IntroText.ShouldBe("AIRED open.");
        result.Value.Bookends.Version.ShouldBe(2);
    }

    [Fact]
    public async Task RegenerateJobAsync_OutroOnlyPair_StillCarriesTheCloser()
    {
        // The one-sided case is what actually failed on dev: an empty intro must not be mistaken
        // for "no bookend".
        var job = CreateTestJob(JobStatus.Rejected);
        job.BookendId = Option<string>.Some("bk-1");
        job.BookendVersion = Option<int>.Some(1);
        SetupJobFound(job);
        SetupBookendFound(PairWithVersions("bk-1", (1, "", "Closer only.")));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.Value.Bookends!.IntroText.ShouldBe("");
        result.Value.Bookends.OutroText.ShouldBe("Closer only.");
    }

    [Fact]
    public async Task RegenerateJobAsync_NoBookendOnTheJob_CarriesNone()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.Value.Bookends.ShouldBeNull();
    }

    [Fact]
    public async Task RegenerateJobAsync_PinnedVersionGone_FallsBackToCurrentNotNeutral()
    {
        // Losing the exact version is a reason to use the nearest real policy, not to
        // silently drop the station's delivery altogether.
        var job = CreateTestJob(JobStatus.Rejected);
        job.DeliveryStyleId = Option<string>.Some("ds-1");
        job.DeliveryStyleVersion = Option<int>.Some(99);
        SetupJobFound(job);
        SetupStyleFound(StyleWithVersions("ds-1", (3, "NEWER tone")));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.Value.DeliveryStyle!.ToneDescription.ShouldBe("NEWER tone");
    }

    [Fact]
    public async Task RegenerateJobAsync_NoDeliveryStyle_SendsNone()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.Value.DeliveryStyle.ShouldBeNull();
        result.Value.DeliveryStyleId.ShouldBeNull();
    }

    [Fact]
    public async Task RegenerateJobAsync_StyleLookupFails_FallsBackToNeutralWithoutFailing()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.DeliveryStyleId = Option<string>.Some("ds-gone");
        SetupJobFound(job);
        _delivery.GetAsync("ds-gone", Arg.Any<CancellationToken>())
            .Returns(Result<DeliveryStyle>.Failure("not found"));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.IsSuccess.ShouldBeTrue();
        result.Value.DeliveryStyle.ShouldBeNull();
    }

    [Fact]
    public async Task RegenerateJobAsync_RefreshPronunciation_UsesTheDictionarysCurrentVersion()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.PronunciationDictionaryId = Option<string>.Some("elevenlabs-dict-1");
        job.PronunciationVersionId = Option<string>.Some("version-AIRED");
        SetupJobFound(job);
        _pronunciation.ListAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<PronunciationDictionary>>.Success(
                [DictionaryWithCurrentVersion("elevenlabs-dict-1", "version-CURRENT")]));

        var result = await _sut.RegenerateJobAsync(
            new RegenerateJobCommand(job.JobId, RefreshPronunciation: true));

        result.IsSuccess.ShouldBeTrue();
        result.Value.PronunciationLocator.ShouldNotBeNull();
        result.Value.PronunciationLocator!.VersionId.ShouldBe("version-CURRENT");
    }
    // ----------------------------------------------
    // A re-voice re-renders words that are already fixed. It never re-fetches traffic, so
    // the job's existing script AND evidence snapshot still describe exactly what is about
    // to be synthesised. The regeneration reset used to wipe both: the script (so the
    // re-voice had nothing to synthesise and died) and the corridors/incidents (so the
    // cockpit showed "0 incidents cited" against a bulletin making road claims).
    // Both observed on dev, 2026-07-28.

    [Fact]
    public async Task RegenerateJobAsync_RevoiceOnly_KeepsTheScriptThroughTheReset()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The West Gate's under the pump.");
        SetupJobFound(job);

        var result = await _sut.RegenerateJobAsync(
            new RegenerateJobCommand(job.JobId, RevoiceOnly: true));

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreviousScript.ShouldBe("The West Gate's under the pump.");
        job.ScriptText.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task RegenerateJobAsync_RevoiceOnly_KeepsTheEvidenceSnapshot()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The West Gate's under the pump.");
        job.SetIncidents([new JobIncident("here-1", 1, "West Gate delays", "West Gate Freeway")]);
        SetupJobFound(job);

        await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId, RevoiceOnly: true));

        // The operator is about to approve these exact words; the evidence that justifies
        // them must still be on the record to check against.
        job.Incidents.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RegenerateJobAsync_FullRegenerate_ClearsScriptButRetainsTrafficEvidence()
    {
        // A full regenerate re-writes from a FRESH fetch; keeping the traffic snapshot
        // until fresh traffic arrives keeps the Adjust tab accessible while generating.
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The West Gate's under the pump.");
        job.SetIncidents([new JobIncident("here-1", 1, "West Gate delays", "West Gate Freeway")]);
        SetupJobFound(job);

        await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        job.Incidents.Count.ShouldBe(1);
        job.ScriptText.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task RegenerateJobAsync_RevoiceOnly_PrefersTheOperatorsEditedScript()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The stale wording.");
        SetupJobFound(job);

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(
            job.JobId, EditedScript: Option<string>.Some("The operator's wording."),
            RevoiceOnly: true));

        result.Value.PreviousScript.ShouldBe("The operator's wording.");
    }

    [Fact]
    public async Task RegenerateJobAsync_FullRegenerate_DoesNotResurrectTheOldScript()
    {
        // A full regenerate wants fresh words. PreviousScript there means "the
        // operator's edit, preserve it", and same-day continuity travels separately
        // on PreviousScriptContext — so feeding the old script back would change
        // what the LLM is told.
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The old wording.");
        SetupJobFound(job);

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.Value.PreviousScript.ShouldBeNull();
    }

    // ----------------------------------------------
    // Re-voice observation capture: the operator's script edit is held on the job until a
    // successful publish turns it into lesson-learning evidence.

    [Fact]
    public async Task RegenerateJobAsync_RevoiceWithChangedScript_CapturesPendingObservation()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The M4 is experiencing delays");
        SetupJobFound(job);

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(
            job.JobId, EditedScript: Option<string>.Some("The M4 is crawling"),
            RevoiceOnly: true), by: "producer.jane@arn.com.au");

        result.IsSuccess.ShouldBeTrue();
        var observation = job.PendingRevoiceObservation.Value;
        observation.OriginalScript.ShouldBe("The M4 is experiencing delays");
        observation.EditedScript.ShouldBe("The M4 is crawling");
        observation.By.ShouldBe("producer.jane@arn.com.au");
    }

    [Fact]
    public async Task RegenerateJobAsync_RevoiceWithWhitespaceOnlyEdit_CapturesNothing()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The M4 is experiencing delays");
        SetupJobFound(job);

        await _sut.RegenerateJobAsync(new RegenerateJobCommand(
            job.JobId, EditedScript: Option<string>.Some("  The M4 is  experiencing delays "),
            RevoiceOnly: true));

        job.PendingRevoiceObservation.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task RegenerateJobAsync_SecondRevoiceBeforePublish_KeepsTheFirstOriginal()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The generated wording");
        job.PendingRevoiceObservation = Option<RevoiceObservation>.Some(new RevoiceObservation(
            DateTime.UtcNow, "operator-1", "The FIRST generated wording", "The generated wording"));
        SetupJobFound(job);

        await _sut.RegenerateJobAsync(new RegenerateJobCommand(
            job.JobId, EditedScript: Option<string>.Some("The final operator wording"),
            RevoiceOnly: true));

        var observation = job.PendingRevoiceObservation.Value;
        observation.OriginalScript.ShouldBe("The FIRST generated wording");
        observation.EditedScript.ShouldBe("The final operator wording");
    }

    [Fact]
    public async Task RegenerateJobAsync_FullRegenerate_DiscardsPendingObservation()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.ScriptText = Option<string>.Some("The generated wording");
        job.PendingRevoiceObservation = Option<RevoiceObservation>.Some(new RevoiceObservation(
            DateTime.UtcNow, "operator-1", "The generated wording", "The edited wording"));
        SetupJobFound(job);

        await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        // The words are being rewritten — the pending edit no longer describes what airs.
        job.PendingRevoiceObservation.HasValue.ShouldBeFalse();
    }

    // ----------------------------------------------
    // Delivery-gated lesson-learning enqueue on publish.

    private Job CreateApprovedJobWithObservation()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("https://blob/bulletin.wav");
        job.ScriptText = Option<string>.Some("The M4 is crawling");
        job.PendingRevoiceObservation = Option<RevoiceObservation>.Some(new RevoiceObservation(
            DateTime.UtcNow, "producer.jane@arn.com.au",
            "The M4 is experiencing delays", "The M4 is crawling"));
        SetupJobFound(job);
        return job;
    }

    [Fact]
    public async Task PublishJobAsync_DeliveredWithPendingObservation_EnqueuesLessonLearning()
    {
        var job = CreateApprovedJobWithObservation();
        _queuePublisher.PublishLessonLearningMessageAsync(
                Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _queuePublisher.Received(1).PublishLessonLearningMessageAsync(
            Arg.Is<LessonLearningQueueMessage>(m =>
                m.JobId == job.JobId &&
                m.Station == job.Station &&
                m.OriginalScript == "The M4 is experiencing delays" &&
                m.PublishedScript == "The M4 is crawling" &&
                m.By == "producer.jane@arn.com.au"),
            Arg.Any<CancellationToken>());
        // The clear is committed by the Published save.
        job.PendingRevoiceObservation.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task PublishJobAsync_StationPlayoutDisabled_DoesNotLearnAndRetainsObservation()
    {
        var job = CreateApprovedJobWithObservation();
        var schedule = MarketSchedule.Create(job.Market, "Sydney", "2GB", "Australia/Sydney",
            "0 6 * * *", true, CoverageArea.Create(0, 0, 1).Value, "voice-1", "output/path",
            30, 15, 5, DateTime.UtcNow, DateTime.UtcNow, playoutEnabled: false).Value;
        _scheduleRepository.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(schedule));

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _queuePublisher.DidNotReceive().PublishLessonLearningMessageAsync(
            Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>());
        // No playout evidence yet: the observation stays for a future delivered publish.
        job.PendingRevoiceObservation.HasValue.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishJobAsync_NoPendingObservation_NeverEnqueues()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("https://blob/bulletin.wav");
        job.ScriptText = Option<string>.Some("The M4 is crawling");
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _queuePublisher.DidNotReceive().PublishLessonLearningMessageAsync(
            Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_EnqueueFailure_PublishStillSucceeds()
    {
        var job = CreateApprovedJobWithObservation();
        _queuePublisher.PublishLessonLearningMessageAsync(
                Arg.Any<LessonLearningQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("queue down"));

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
    }

    [Fact]
    public async Task RegenerateJobAsync_WithoutRefresh_ReplaysTheAiredVersion()
    {
        // The default MUST stay faithful: a stale-recovery republish has to
        // reproduce what aired, not silently pick up later edits.
        var job = CreateTestJob(JobStatus.Rejected);
        job.PronunciationDictionaryId = Option<string>.Some("elevenlabs-dict-1");
        job.PronunciationVersionId = Option<string>.Some("version-AIRED");
        SetupJobFound(job);

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId));

        result.Value.PronunciationLocator!.VersionId.ShouldBe("version-AIRED");
        await _pronunciation.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegenerateJobAsync_RefreshPronunciation_DictionaryGone_FallsBackToTheAiredVersion()
    {
        // Dropping the dictionary entirely would be far worse than replaying an
        // old version — every rule would stop applying mid-review.
        var job = CreateTestJob(JobStatus.Rejected);
        job.PronunciationDictionaryId = Option<string>.Some("elevenlabs-dict-1");
        job.PronunciationVersionId = Option<string>.Some("version-AIRED");
        SetupJobFound(job);
        _pronunciation.ListAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<PronunciationDictionary>>.Success([]));

        var result = await _sut.RegenerateJobAsync(
            new RegenerateJobCommand(job.JobId, RefreshPronunciation: true));

        result.Value.PronunciationLocator!.VersionId.ShouldBe("version-AIRED");
    }

    [Fact]
    public async Task RegenerateJobAsync_RefreshPronunciation_LookupFails_FallsBackToTheAiredVersion()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.PronunciationDictionaryId = Option<string>.Some("elevenlabs-dict-1");
        job.PronunciationVersionId = Option<string>.Some("version-AIRED");
        SetupJobFound(job);
        _pronunciation.ListAsync(Arg.Any<CancellationToken>()).Returns(
            Result<IReadOnlyList<PronunciationDictionary>>.Failure("storage down"));

        var result = await _sut.RegenerateJobAsync(
            new RegenerateJobCommand(job.JobId, RefreshPronunciation: true));

        result.IsSuccess.ShouldBeTrue();
        result.Value.PronunciationLocator!.VersionId.ShouldBe("version-AIRED");
    }

    [Fact]
    public async Task RegenerateJobAsync_RefreshPronunciation_NoDictionaryOnJob_StaysNull()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        // A refresh now reads the library even with no station dictionary, because the
        // platform-wide layer is resolved from the same read.
        _pronunciation.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([]));

        var result = await _sut.RegenerateJobAsync(
            new RegenerateJobCommand(job.JobId, RefreshPronunciation: true));

        result.Value.PronunciationLocator.ShouldBeNull();
        result.Value.GlobalPronunciationLocator.ShouldBeNull();
    }

    [Fact]
    public async Task RegenerateJobAsync_RefreshPronunciation_PicksUpTheCurrentPlatformWideLayer()
    {
        // A refresh resolves the platform-wide layer as whatever is ACTIVE now, not whatever the
        // job recorded — the whole point is to pick up the newest rules.
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        var global = PronunciationDictionary.Create(
            "Everywhere", null, "el-dict-global", "el-ver-global",
            [PronunciationRule.CreateAlias("NorthConnex", "North-Connex").Value],
            null, null, DateTime.UtcNow, "pd-global", PronunciationScope.Global).Value;
        _pronunciation.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([global]));

        var result = await _sut.RegenerateJobAsync(
            new RegenerateJobCommand(job.JobId, RefreshPronunciation: true));

        result.Value.GlobalPronunciationLocator!.DictionaryId.ShouldBe("el-dict-global");
    }

    [Fact]
    public async Task RegenerateJobAsync_FromApproved_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.Approved);
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId);

        var result = await _sut.RegenerateJobAsync(command);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.CannotRegenerateFromStatus);
    }

    [Fact]
    public async Task RegenerateJobAsync_FromFailed_Succeeds_ReturnsMessage()
    {
        // Failed is a valid regeneration source: it's the operator's retry path.
        var job = CreateTestJob(JobStatus.Failed);
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId);

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobId.ShouldBe(job.JobId);
        job.Status.ShouldBe(JobStatus.Requested);
    }

    [Fact]
    public async Task RegenerateJobAsync_FromFallbackUsed_Succeeds_ReturnsMessage()
    {
        // A fallback-covered slot is a valid regeneration source: the operator retries a
        // fresh bulletin instead of airing the standby.
        var job = CreateTestJob(JobStatus.WaitingApproval);
        job.TransitionTo(JobStatus.FallbackUsed, utcNow: DateTime.UtcNow);
        job.AudioUrl = Option<string>.Some("fallback_audio.wav");
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId);

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.JobId.ShouldBe(job.JobId);
        job.Status.ShouldBe(JobStatus.Requested);
        job.AudioUrl.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task RegenerateJobAsync_JobNotFound_ReturnsFailure()
    {
        SetupJobNotFound();
        var command = new RegenerateJobCommand("missing");

        var result = await _sut.RegenerateJobAsync(command);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task RegenerateJobAsync_ResetsAttemptCount()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        job.SetAttemptCount(5);
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId);

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        job.AttemptCount.ShouldBe(0);
    }

    [Fact]
    public async Task RegenerateJobAsync_WithTargetDurationOverride_AppliesToJobAndMessage()
    {
        var job = CreateTestJob(JobStatus.Rejected); // 30s default
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId, TargetDurationSec: Option<double>.Some(60));

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        job.TargetDurationSeconds.ShouldBe(60);
        result.Value.TargetDurationSeconds.ShouldBe(60);
    }

    [Fact]
    public async Task RegenerateJobAsync_WithoutTargetDuration_KeepsExistingTarget()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId);

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TargetDurationSeconds.ShouldBe(30);
    }

    [Fact]
    public async Task RegenerateJobAsync_WithExcludedIncidents_CarriedToMessage()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId,
            ExcludedIncidentKeys: Option<IReadOnlyList<string>>.Some(["inc-5"]));

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ExcludedIncidentKeys!.ShouldBe(["inc-5"]);
    }

    [Fact]
    public async Task RegenerateJobAsync_WithRevoiceOnly_CarriedToMessage()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId, RevoiceOnly: true);

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RevoiceOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task RegenerateJobAsync_UsesProvidedActor()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        var command = new RegenerateJobCommand(job.JobId);

        var result = await _sut.RegenerateJobAsync(command, "producer.jane@arn.com.au");

        result.IsSuccess.ShouldBeTrue();
        job.StatusHistory.Last().By.ShouldBe("producer.jane@arn.com.au");
    }

    [Fact]
    public async Task RegenerateJobAsync_SaveFails_ReturnsFailure()
    {
        var job = CreateTestJob(JobStatus.Rejected);
        SetupJobFound(job);
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Job was modified by another writer; please retry."));
        var command = new RegenerateJobCommand(job.JobId);

        var result = await _sut.RegenerateJobAsync(command);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("Job was modified by another writer; please retry.");
    }

    // ──────────────────────────────────────────────
    // PublishBroadcastJobAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PublishBroadcastJobAsync_PublishSucceeds_ReturnsSuccess()
    {
        var message = new BroadcastQueueMessage("job-1", "SYD", "SYD", 30, -33.8, 151.2, 25.0);
        _queuePublisher.PublishBroadcastMessageAsync(message, Arg.Any<CancellationToken>()).Returns(Result.Success());

        var result = await _sut.PublishBroadcastJobAsync("job-1", message);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task PublishBroadcastJobAsync_PublishFails_ReturnsFailure_MarksJobFailed()
    {
        var message = new BroadcastQueueMessage("job-1", "SYD", "SYD", 30, -33.8, 151.2, 25.0);
        _queuePublisher.PublishBroadcastMessageAsync(message, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Queue unreachable"));
        var job = CreateTestJob();
        SetupJobFound(job);

        var result = await _sut.PublishBroadcastJobAsync(job.JobId, message);

        result.IsFailure.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Failed);
        await _jobRepo.Received(1).SaveJobAsync(job, Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // MarkJobFailedAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task MarkJobFailedAsync_JobFound_Succeeds()
    {
        var job = CreateTestJob();
        SetupJobFound(job);

        var result = await _sut.MarkJobFailedAsync(job.JobId, "timeout");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Failed);
        await _jobRepo.Received(1).SaveJobAsync(job, Arg.Any<CancellationToken>());
        await _jobRepo.Received(1).AddLogAsync(job.JobId, "timeout", JobLogLevel.Error, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkJobFailedAsync_JobNotFound_ReturnsFailure()
    {
        SetupJobNotFound();

        var result = await _sut.MarkJobFailedAsync("missing", "error");

        result.IsFailure.ShouldBeTrue();
    }

    // ──────────────────────────────────────────────
    // AudioSha256 delivery passthrough
    // ──────────────────────────────────────────────

    [Fact]
    public async Task PublishJobAsync_FromApproved_WithAudioSha256_PassesHashToDelivery()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        job.AudioSha256 = "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210";
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        await _fileDeliveryService.Received(1)
            .DeliverAsync(job.AudioUrl.Value, job.Market, "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210", Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_FromApproved_WithNullAudioSha256_PassesNullToDelivery()
    {
        var job = CreateTestJob(JobStatus.Approved);
        job.ETag = "test-etag";
        job.AudioUrl = Option<string>.Some("broadcast_20260612_080000.wav");
        // AudioSha256 intentionally null — tests filler/legacy path
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        await _fileDeliveryService.Received(1)
            .DeliverAsync(job.AudioUrl.Value, job.Market, null, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }
}

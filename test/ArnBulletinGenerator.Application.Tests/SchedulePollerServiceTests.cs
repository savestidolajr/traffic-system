using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class SchedulePollerServiceTests
{
    /// <summary>Standalone padder for the fallback service ctor — no padding behaviour is under
    /// test here, so every dependency is a substitute.</summary>
    private static FallbackAudioPadder Padder() => new(
        Substitute.For<IFillerStatementsRepository>(),
        Substitute.For<IFillerAudioCacheService>(),
        Substitute.For<IAudioPipelineService>(),
        Substitute.For<IDurationEstimator>(),
        Substitute.For<IBroadcastOutput>(),
        Options.Create(new SpeechDurationOptions()),
        Substitute.For<ILogger<FallbackAudioPadder>>(),
        TimeProvider.System);

    private static readonly DateTime Now = new(2025, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly CoverageArea Coverage = CoverageArea.Create(-37.8136, 144.9631, 5.0).Value;
    private readonly ICronParser _cronParser = Substitute.For<ICronParser>();
    private readonly IJobRepository _jobRepo = Substitute.For<IJobRepository>();
    private readonly IJobService _jobService = Substitute.For<IJobService>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly ILogger<SchedulePollerService> _logger = Substitute.For<ILogger<SchedulePollerService>>();
    private readonly IModelSelector _modelSelector = Substitute.For<IModelSelector>();
    private readonly IVoiceProfileService _voiceProfileService = Substitute.For<IVoiceProfileService>();
    private readonly IVoiceProvider _voiceProvider = Substitute.For<IVoiceProvider>();
    private readonly IDeliveryStyleService _deliveryStyleService = Substitute.For<IDeliveryStyleService>();
    private readonly IPronunciationDictionaryService _pronunciationDictionaryService = Substitute.For<IPronunciationDictionaryService>();
    private readonly BroadcastOptions _options = new();
    private readonly IQueuePublisher _queuePublisher = Substitute.For<IQueuePublisher>();
    private readonly IScheduleRepository _scheduleRepo = Substitute.For<IScheduleRepository>();
    private readonly SchedulePollerService _sut;
    private readonly FakeTimeProvider _timeProvider = new();

    public SchedulePollerServiceTests()
    {
        _timeProvider.SetUtcNow(Now);
        _modelSelector.SelectModelAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<ModelSelectionUseCase>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult("model-1"));
        var defaultProfile = VoiceProfile.Create("Test Voice", null, "voice-1", "Test Voice", "model-1",
            VoiceSettings.Default, null, null, Now).Value;
        _voiceProfileService.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(defaultProfile));
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success([]));
        _cronParser.CalculateNextRun(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>())
            .Returns(Option<DateTime>.Some(Now.AddHours(1)));

        // No stale jobs or fallback candidates by default
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(Enumerable.Empty<Job>()));
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(Enumerable.Empty<Job>()));
        _jobRepo.ListJobsAsync(Arg.Any<IReadOnlyCollection<JobStatus>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(Enumerable.Empty<Job>()));
        _scheduleRepo.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(Enumerable.Empty<MarketSchedule>()));

        // Happy-path defaults
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepo.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobRepo.GetLatestBulletinForMarketTodayAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        _scheduleRepo.TryStartJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _scheduleRepo.CompleteJobAsync(Arg.Any<string>(), Arg.Any<Option<DateTime>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // The deadline sweep now DELIVERS what it covers a slot with — applying a
        // fallback only attached the audio to the job, and nothing wrote it to the
        // playout drop, so a covered slot still aired nothing unless an operator
        // published by hand. Every fallback path therefore signs off and publishes.
        _jobService.ApproveJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobService.PublishJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // Covering a slot finishes at the playout drop, and that sequence lives in
        // JobService.DeliverCoveredSlotAsync so no path can skip it. The sweep's job is
        // to CALL it; the sequence itself is pinned in JobServiceTests.
        _jobService.DeliverCoveredSlotAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // Both pronunciation layers are resolved from ONE library read.
        _pronunciationDictionaryService.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success([]));
        // Publish is revision-guarded, so the sweep re-reads the job between signing
        // off and delivering. Not found by default; a test that cares about delivery
        // stubs it with its own job.
        _jobRepo.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("Job not found"));

        var fallbackService = new BulletinFallbackService(
            _scheduleRepo, _jobRepo, Options.Create(_options),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(_options),
                Substitute.For<ILogger<PerVoiceFillerCartResolver>>()),
            Padder(),
            _timeProvider,
            Substitute.For<ILogger<BulletinFallbackService>>());

        _notificationService.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new SchedulePollerService(
            _scheduleRepo, _jobRepo, _jobService, _notificationService, fallbackService, _cronParser, _modelSelector, _voiceProfileService,
            _voiceProvider, _deliveryStyleService, Substitute.For<IBulletinPlaybookService>(),
            Substitute.For<IBookendService>(), _pronunciationDictionaryService,
            _queuePublisher, Options.Create(_options), _timeProvider, new NoOpBroadcastMetrics(), _logger);
    }

    private MarketSchedule MakeSchedule(string marketId = "mel", string voiceProfileId = "vp-1")
    {
        var schedule = MarketSchedule.Create(marketId, $"{marketId} AM", "3AW",
            "Australia/Melbourne", "0 8 * * 1-5",
            true, Coverage, voiceProfileId, "/out",
            45, 10,
            3,
            Now.AddDays(-7), Now.AddDays(-1)).Value;
        // A schedule is due up to ScheduleLeadTimeMinutes BEFORE its slot, so NextRunUtc — the
        // slot's air time — is normally still ahead of the clock when the poller reaches it.
        // Measured on a healthy dev job: created 12:01:02 for a 12:14:00 slot. A past-dated
        // NextRunUtc is an already-aired slot, which the poller now records as missed instead
        // of generating for, so it can no longer stand in for "due".
        schedule.NextRunUtc = Option<DateTime>.Some(Now.AddMinutes(13));
        return schedule;
    }

    private MarketSchedule MakeScheduleWithCron(string cron, string marketId = "mel")
    {
        var schedule = MarketSchedule.Create(marketId, $"{marketId} AM", "3AW",
            "Australia/Melbourne", cron,
            true, Coverage, "vp-1", "/out",
            45, 10,
            3,
            Now.AddDays(-7), Now.AddDays(-1)).Value;
        // A schedule is due up to ScheduleLeadTimeMinutes BEFORE its slot, so NextRunUtc — the
        // slot's air time — is normally still ahead of the clock when the poller reaches it.
        // Measured on a healthy dev job: created 12:01:02 for a 12:14:00 slot. A past-dated
        // NextRunUtc is an already-aired slot, which the poller now records as missed instead
        // of generating for, so it can no longer stand in for "due".
        schedule.NextRunUtc = Option<DateTime>.Some(Now.AddMinutes(13));
        return schedule;
    }

    private void SetupDueSchedules(params MarketSchedule[] schedules)
    {
        _scheduleRepo.GetDueSchedulesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(schedules));
    }

    // ==================== ProcessDueSchedulesAsync ====================

    // ── Expired slots (test env 2026-08-24: 21 missed, 20 of them generated first) ──
    // ScheduledForUtc is the slot's AIR time, and ZettaFileDeliveryService refuses to write to the
    // playout share once it has passed. A slot reached after that instant is therefore unreachable
    // before the first token is drafted, and job d06dfc18 proved it live: created 07:45:02 for a
    // 07:45:00 slot, it drafted, synthesised, took the filler cart, and was refused delivery.

    [Fact]
    public async Task ProcessDueSchedules_SlotAlreadyAired_IsNotDispatchedForGeneration()
    {
        var schedule = MakeSchedule();
        schedule.NextRunUtc = Option<DateTime>.Some(Now.AddSeconds(-2));
        SetupDueSchedules(schedule);

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.DidNotReceive()
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_SlotAlreadyAired_StillRecordsTheLostSlotAsMissed()
    {
        var schedule = MakeSchedule();
        schedule.NextRunUtc = Option<DateTime>.Some(Now.AddSeconds(-2));
        SetupDueSchedules(schedule);

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Missed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_SlotAlreadyAired_StillReleasesTheLockAndAdvancesTheSchedule()
    {
        // The slot is lost, but the STATION is not: skipping must not leave the schedule locked or
        // pinned to an occurrence that has gone, or every later slot is lost with it.
        var schedule = MakeSchedule();
        schedule.NextRunUtc = Option<DateTime>.Some(Now.AddSeconds(-2));
        SetupDueSchedules(schedule);

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _scheduleRepo.Received(1).CompleteJobAsync(
            "mel", Arg.Any<Option<DateTime>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_NoDueSchedules_NoProcessing()
    {
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.DidNotReceive()
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_GetDueFails_LogsErrorAndStops()
    {
        _scheduleRepo.GetDueSchedulesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Failure("storage down"));

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _scheduleRepo.DidNotReceive()
            .TryStartJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_OneDueSchedule_DispatchesIt()
    {
        SetupDueSchedules(MakeSchedule());

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.Received(1)
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_MultipleDueSchedules_ProcessesAll()
    {
        SetupDueSchedules(MakeSchedule(), MakeSchedule("syd"));

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.Received(2)
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_AppliesLeadTimeToThreshold()
    {
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        // Default ScheduleLeadTimeMinutes = 15
        await _scheduleRepo.Received(1)
            .GetDueSchedulesAsync(Now.AddMinutes(15), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_ExceptionInOne_ProcessesRemaining()
    {
        SetupDueSchedules(MakeSchedule(), MakeSchedule("syd"));

        int callCount = 0;
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1) throw new InvalidOperationException("first fails");
                return Result.Success();
            });
        _jobRepo.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("not found"));

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.Received(2)
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    // ==================== ProcessScheduleAsync ====================

    [Fact]
    public async Task ProcessSchedule_LockFails_SkipsSchedule()
    {
        _scheduleRepo.TryStartJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("already running"));

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _queuePublisher.DidNotReceive()
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_Success_SavesJobAndDispatches()
    {
        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
        await _queuePublisher.Received(1)
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_ProfileVoice_UsedInJob()
    {
        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.VoiceConfig.Value.VoiceName == "Test Voice"
                && j.VoiceConfig.Value.VoiceId == "voice-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_UnresolvableProfile_UsesProviderVoiceName()
    {
        _options.DefaultVoiceId = "default-voice";
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success(
                [new Voice("default-voice", "Provider Default Voice", Option<string>.None, Option<string>.None,
                    new Dictionary<string, string>())]));
        _voiceProfileService.GetAsync("missing", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("not found"));

        await _sut.ProcessScheduleAsync(MakeSchedule(voiceProfileId: "missing"), CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.VoiceConfig.Value.VoiceId == "default-voice"
                && j.VoiceConfig.Value.VoiceName == "Provider Default Voice"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_UnresolvableProfile_ProviderFailureUsesVoiceIdAsName()
    {
        _options.DefaultVoiceId = "default-voice";
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Failure("provider unavailable"));
        _voiceProfileService.GetAsync("missing", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("not found"));

        await _sut.ProcessScheduleAsync(MakeSchedule(voiceProfileId: "missing"), CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.VoiceConfig.Value.VoiceId == "default-voice"
                && j.VoiceConfig.Value.VoiceName == "default-voice"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_DispatchFails_MarksJobFailedAndNotifies()
    {
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("queue down"));

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _jobRepo.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed),
            Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedNotCovered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_Throws_MarksJobFailed()
    {
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));
        _jobRepo.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<Job>.Success(Job.Create(ci.ArgAt<string>(0), "3AW", "mel", 45, FixedTime).Value));

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _jobRepo.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_Exception_SendsFailedNotCoveredNotification()
    {
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));
        _jobRepo.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<Job>.Success(Job.Create(ci.ArgAt<string>(0), "3AW", "mel", 45, FixedTime).Value));

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Failed),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedNotCovered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_Exception_AlreadyTerminalJob_NoNotification()
    {
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("boom"));
        var alreadyFailed = Job.Create("job-at", "3AW", "mel", 45, FixedTime).Value;
        alreadyFailed.MarkFailed("already failed", FixedTime);
        _jobRepo.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(alreadyFailed));

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _jobRepo.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-at" && j.Status == JobStatus.Failed),
            Arg.Any<CancellationToken>());
        await _notificationService.DidNotReceive().SendJobNotificationAsync(
            Arg.Any<Job>(), Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedNotCovered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_Always_CompletesJobAndCalculatesNextRun()
    {
        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        _cronParser.Received(1).CalculateNextRun("0 8 * * 1-5", "Australia/Melbourne", Arg.Any<DateTime>());
        await _scheduleRepo.Received(1).CompleteJobAsync(
            "mel", Arg.Any<Option<DateTime>>(), Arg.Any<CancellationToken>());
    }

    // ==================== Poller Double-Fire Guard (2026-07-19/20) ====================

    [Fact]
    public async Task ProcessSchedule_LeadTimeExceedsCronPeriod_SkipsOccurrenceStillInsideLeadWindow()
    {
        // Root cause: with the default 15-min lead time and a 10-min cron, the naive "next
        // occurrence after the one that just fired" (+10 min) is STILL inside the lead window
        // measured from completion — the very next poller tick would see it as due too and fire
        // again seconds later (observed 2026-07-19/20: two jobs ~61s apart from one enable).
        // CalculateNextRun's own strictly-after contract (unmodified here) guarantees each skip
        // step lands on a genuinely later occurrence, so this can never revisit the one that
        // just fired.
        var schedule = MakeScheduleWithCron("*/10 * * * *");
        schedule.NextRunUtc = Option<DateTime>.Some(Now); // the occurrence firing on this tick

        _cronParser.CalculateNextRun("*/10 * * * *", "Australia/Melbourne", Now)
            .Returns(Option<DateTime>.Some(Now.AddMinutes(10))); // naive next — still inside lead
        _cronParser.CalculateNextRun("*/10 * * * *", "Australia/Melbourne", Now.AddMinutes(10))
            .Returns(Option<DateTime>.Some(Now.AddMinutes(20))); // genuinely clear of the lead window

        await _sut.ProcessScheduleAsync(schedule, CancellationToken.None);

        await _scheduleRepo.Received(1).CompleteJobAsync(
            "mel",
            Arg.Is<Option<DateTime>>(n => n.HasValue && n.Value == Now.AddMinutes(20)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessDueSchedules_LeadTimeExceedsCronPeriod_SecondPollerTickDoesNotRefire()
    {
        // Deterministic two-tick repro of the exact reported symptom via the real sweep
        // entrypoint: enabling a */10 schedule with an imminent occurrence under the default
        // 15-min lead produced two jobs ~61s apart. The schedule instance is mutated by
        // CompleteJobAsync exactly as the real table-storage repository would persist it, so
        // the second sweep's due-check sees genuinely updated state, not a stale mock.
        var schedule = MakeScheduleWithCron("*/10 * * * *");
        schedule.NextRunUtc = Option<DateTime>.Some(Now.AddMinutes(1)); // PUT set it to the imminent boundary

        _cronParser.CalculateNextRun("*/10 * * * *", "Australia/Melbourne", Now.AddMinutes(1))
            .Returns(Option<DateTime>.Some(Now.AddMinutes(11))); // naive next occurrence
        _cronParser.CalculateNextRun("*/10 * * * *", "Australia/Melbourne", Now.AddMinutes(11))
            .Returns(Option<DateTime>.Some(Now.AddMinutes(21))); // genuinely clear of the lead window

        _scheduleRepo.GetDueSchedulesAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var threshold = ci.Arg<DateTime>();
                bool due = schedule.NextRunUtc.HasValue && schedule.NextRunUtc.Value <= threshold;
                return Result<IEnumerable<MarketSchedule>>.Success(
                    due ? new[] { schedule } : Array.Empty<MarketSchedule>());
            });
        _scheduleRepo.CompleteJobAsync("mel", Arg.Any<Option<DateTime>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                schedule.NextRunUtc = ci.Arg<Option<DateTime>>();
                return Result.Success();
            });

        // Tick A: fires via lead time (occurrence is ~1 min away, well inside the 15-min lead).
        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);
        await _queuePublisher.Received(1)
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());

        // Tick B, ~61s later (the next poller tick) — must not see the schedule as due again.
        _timeProvider.SetUtcNow(Now.AddSeconds(61));
        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.Received(1)
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_PublisherThrows_StillCompletesJob()
    {
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Throws(new Exception("kaboom"));
        _jobRepo.GetJobAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("not found"));

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _scheduleRepo.Received(1).CompleteJobAsync(
            "mel", Arg.Any<Option<DateTime>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_CancellationToken_HandledGracefully()
    {
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Throws(new OperationCanceledException());

        // Should NOT throw — the schedule poller handles cancellation by logging
        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);
    }

    [Fact]
    public async Task ProcessSchedule_JobCreatedWithCorrectProperties()
    {
        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j =>
                j.Station == "3AW" &&
                j.Market == "mel" &&
                j.TargetDurationSeconds == 45 &&
                j.Status == JobStatus.Requested &&
                j.Latitude.Value == Coverage.Latitude &&
                j.Longitude.Value == Coverage.Longitude &&
                j.RadiusKm.Value == Coverage.RadiusKm &&
                j.VoiceConfig.Value.VoiceId == "voice-1" &&
                j.VoiceConfig.Value.Model == "model-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessSchedule_QueueMessageContainsCoverageAndStationConfig()
    {
        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.Coverage != null &&
                m.Coverage.Latitude == Coverage.Latitude &&
                m.Coverage.Longitude == Coverage.Longitude &&
                m.Location == "mel" &&
                m.StationId == "3AW" &&
                m.Model == "model-1"),
            Arg.Any<CancellationToken>());
    }

    // ==================== Stale Job Recovery ====================

    [Fact]
    public async Task RecoverStaleJobs_NoStaleJobs_DoesNotPublish()
    {
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.DidNotReceive()
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverStaleJobs_StaleJobWithGeoData_RepublishesToQueue()
    {
        var staleJob = Job.Create("stale-1", "3AW", "mel", 45, FixedTime,
            Option<double>.Some(-37.8), Option<double>.Some(144.9), Option<double>.Some(5.0),
            Option<string>.Some("Australia/Melbourne")).Value;
        staleJob.VoiceConfig = Option<JobVoiceConfig>.Some(
            new JobVoiceConfig("elevenlabs", "v1", "Voice", "model-1"));
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { staleJob }));
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // The deadline sweep now DELIVERS what it covers a slot with — applying a
        // fallback only attached the audio to the job, and nothing wrote it to the
        // playout drop, so a covered slot still aired nothing unless an operator
        // published by hand. Every fallback path therefore signs off and publishes.
        _jobService.ApproveJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobService.PublishJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // Covering a slot finishes at the playout drop, and that sequence lives in
        // JobService.DeliverCoveredSlotAsync so no path can skip it. The sweep's job is
        // to CALL it; the sequence itself is pinned in JobServiceTests.
        _jobService.DeliverCoveredSlotAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m => m.JobId == "stale-1" && m.MarketId == "mel"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverStaleJobs_PublishFails_MarksJobFailed()
    {
        var staleJob = Job.Create("stale-1", "3AW", "mel", 45, FixedTime,
            Option<double>.Some(-37.8), Option<double>.Some(144.9), Option<double>.Some(5.0)).Value;
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { staleJob }));
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("queue down"));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "stale-1" && j.Status == JobStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverStaleJobs_PublishFails_SendsFailedNotCoveredNotification()
    {
        var staleJob = Job.Create("stale-1", "3AW", "mel", 45, FixedTime,
            Option<double>.Some(-37.8), Option<double>.Some(144.9), Option<double>.Some(5.0)).Value;
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { staleJob }));
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("queue down"));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "stale-1"),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedNotCovered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverStaleJobs_NoGeoDataNoSchedule_MarksJobFailed()
    {
        var staleJob = Job.Create("stale-1", "3AW", "mel", 45, FixedTime).Value;
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { staleJob }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("not found"));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "stale-1" && j.Status == JobStatus.Failed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverStaleJobs_NoGeoData_SendsFailedNotCoveredNotification()
    {
        var staleJob = Job.Create("stale-1", "3AW", "mel", 45, FixedTime).Value;
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { staleJob }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("not found"));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "stale-1"),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedNotCovered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverStaleJobs_QueryFails_ContinuesGracefully()
    {
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Failure("db error"));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.DidNotReceive()
            .PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>());
    }

    // ==================== Fallback Processing ====================

    private static readonly DateTime FixedTime = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Fallback_JobWithNoAudio_AppliesFallbackFromPreviousJob()
    {
        var needsFallback = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        needsFallback.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        needsFallback.LastUpdatedAt = Now.AddMinutes(-3); // stale — past the in-flight worker grace
        var fallbackSource = Job.Create("fallback-1", "3AW", "mel", 45, FixedTime).Value;
        fallbackSource.AudioUrl = Option<string>.Some("https://audio.example.com/fallback.mp3");
        fallbackSource.ScriptText = Option<string>.Some("fallback script");
        // Put fallback source into a non-terminal state that ApplyFallback can work with,
        // then transition it to Published so it's a valid fallback source
        fallbackSource.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        fallbackSource.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        fallbackSource.Approve(FixedTime);
        fallbackSource.Publish(FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { needsFallback }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(fallbackSource)));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-1" && j.Status == JobStatus.FallbackUsed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_JobAlreadyFallbackUsed_Skipped()
    {
        var job = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        job.TransitionTo(JobStatus.FallbackUsed, utcNow: FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().GetLastSuccessfulJobForMarketAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Stranded coverage (RecoverUndeliveredCoveredSlotsAsync) ──────────────────────
    //
    // A healthy covered slot holds FallbackUsed for ~0.2s before system (deadline) approves and
    // publishes it (measured on deployed dev + test 2026-08-24). Anything still FallbackUsed on a
    // later tick had its delivery FAIL, and until this sweep existed nothing ever looked at it
    // again — ListJobsNeedingFallbackAsync filters Status ne 'FallbackUsed'.

    /// <summary>Builds a slot whose cover was attached but never delivered.</summary>
    private static Job StrandedCoveredSlot(DateTime appliedAt, Option<DateTime> scheduledForUtc)
    {
        var job = Job.Create("job-stranded", "3AW", "mel", 45, appliedAt).Value;
        job.AudioUrl = Option<string>.Some("https://audio.example.com/cart.wav");
        job.ScheduledForUtc = scheduledForUtc;
        job.TransitionTo(JobStatus.FallbackUsed, utcNow: appliedAt);
        job.FallbackAppliedAt = Option<DateTime>.Some(appliedAt);
        return job;
    }

    private void SetupStranded(params Job[] jobs)
    {
        _jobRepo.ListJobsAsync(
                Arg.Is<IReadOnlyCollection<JobStatus>>(s => s.Contains(JobStatus.FallbackUsed)),
                Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(jobs));
    }

    [Fact]
    public async Task StrandedCover_SlotStillAhead_RetriesDelivery()
    {
        SetupStranded(StrandedCoveredSlot(Now.AddMinutes(-2), Option<DateTime>.Some(Now.AddMinutes(5))));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).DeliverCoveredSlotAsync(
            "job-stranded", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StrandedCover_SlotJustPassed_AlertsAndDoesNotDeliver()
    {
        // Past its air time the cover can no longer be delivered (playout refuses it), but the
        // slot aired NOTHING — that is the one outcome an operator must be told about.
        _options.MissedAfterDeadlineMinutes = 30;
        SetupStranded(StrandedCoveredSlot(Now.AddMinutes(-10), Option<DateTime>.Some(Now.AddMinutes(-5))));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().DeliverCoveredSlotAsync(
            "job-stranded", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jobRepo.Received(1).AddLogAsync("job-stranded",
            Arg.Is<string>(m => m.StartsWith("Covered slot never delivered")),
            JobLogLevel.Error, Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "job-stranded"), NotificationKind.FailedNotCovered,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StrandedCover_WeeksOld_IsLeftEntirelyAlone()
    {
        // The 39 rows already stranded across dev and test on 2026-08-24 are residue from before
        // covers were delivered inline (fixed 2026-08-11); all were 12.7-28.7 days old. Re-alerting
        // on historical slots would page someone about weeks-dead air on the first tick after
        // deploy, so beyond the missed-slot grace this sweep says and does NOTHING.
        _options.MissedAfterDeadlineMinutes = 30;
        SetupStranded(StrandedCoveredSlot(Now.AddDays(-13), Option<DateTime>.Some(Now.AddDays(-13))));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().DeliverCoveredSlotAsync(
            "job-stranded", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().AddLogAsync("job-stranded",
            Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>());
        await _notificationService.DidNotReceive().SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "job-stranded"), Arg.Any<NotificationKind?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StrandedCover_AlreadyAlerted_DoesNotAlertTwice()
    {
        _options.MissedAfterDeadlineMinutes = 30;
        var job = StrandedCoveredSlot(Now.AddMinutes(-10), Option<DateTime>.Some(Now.AddMinutes(-5)));
        job.AddLog(new JobLog(Now.AddMinutes(-4), JobLogLevel.Error,
            "Covered slot never delivered: this slot was covered but the audio never reached playout."));
        SetupStranded(job);
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.DidNotReceive().SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "job-stranded"), Arg.Any<NotificationKind?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FallbackUsed_WithNoAppliedAtStamp_IsNotTreatedAsACoveredSlot()
    {
        var job = Job.Create("job-stranded", "3AW", "mel", 45, Now.AddMinutes(-2)).Value;
        job.ScheduledForUtc = Option<DateTime>.Some(Now.AddMinutes(5));
        job.TransitionTo(JobStatus.FallbackUsed, utcNow: Now.AddMinutes(-2));
        SetupStranded(job);
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().DeliverCoveredSlotAsync(
            "job-stranded", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_RejectedJob_Skipped()
    {
        var job = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        job.Reject(new RejectionEntry("bad", "notes", DateTime.UtcNow, "op"), FixedTime, "op");

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().GetLastSuccessfulJobForMarketAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A bulletin that already aired needs no cover.</summary>
    [Theory]
    [InlineData(JobStatus.Published)]
    public async Task Fallback_AlreadySettledJob_SkippedForStatus(JobStatus status)
    {
        var job = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        job.AudioUrl = Option<string>.Some("https://audio.example.com/valid.mp3");
        job.TransitionTo(status, utcNow: FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().GetLastSuccessfulJobForMarketAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The case the fallback chain exists for: nobody approved before the deadline.
    ///     Having audio is NOT coverage — audio sits in blob storage, and only a publish puts
    ///     it in the playout drop. This used to be skipped BECAUSE the audio existed, so the
    ///     slot got neither the same-day repeat nor the cart and the station received nothing,
    ///     against solution-design.md ("if any stage fails or no operator approves before the
    ///     deadline … a dedicated fallback-fill cart guarantees no dead air").
    /// </summary>
    [Fact]
    public async Task Fallback_WaitingApprovalWithAudioPastDeadline_IsCovered()
    {
        var job = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        job.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        job.AudioUrl = Option<string>.Some("https://audio.example.com/valid.mp3");
        job.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        // Nothing from today to repeat, so the chain must reach the cart.
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received().GetLastSuccessfulJobForMarketAsync(
            "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     Applying a fallback used to be the end of it: the audio was attached to the job
    ///     and nothing wrote it to the playout drop, because <c>PublishJobAsync</c> is the
    ///     only caller of the delivery service. A covered slot therefore still aired nothing
    ///     unless an operator approved and published by hand — including the standby cart,
    ///     which exists for exactly the moments nobody is watching.
    /// </summary>
    [Fact]
    public async Task Fallback_WhenApplied_IsDeliveredToPlayout()
    {
        // Failed, not mid-pipeline: a job still generating gets a grace window
        // (IsActivelyGenerating) and is deliberately left alone by the sweep.
        var job = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        job.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        job.MarkFailed("synthesis failed", FixedTime);

        var source = Job.Create("job-source", "3AW", "mel", 45, FixedTime).Value;
        source.AudioUrl = Option<string>.Some("https://audio.example.com/earlier.mp3");
        source.ScriptText = Option<string>.Some("earlier script");
        source.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        source.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        source.Approve(FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(source)));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        // Attributed to the deadline, not to a person: the audit trail must not imply an
        // operator was there. The approve → re-read → publish sequence itself is pinned in
        // JobServiceTests, where the ETag advance can be modelled honestly.
        await _jobService.Received(1).DeliverCoveredSlotAsync(
            "job-1", Arg.Any<string>(), "system (deadline)", Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     A covered slot whose audio does not reach playout must be visible on the console,
    ///     not just in telemetry: the file is missing from the drop while the record claims
    ///     coverage, which is the one failure an operator has to be able to see.
    /// </summary>
    [Fact]
    public async Task Fallback_WhenDeliveryFails_TheSweepStillReportsIt()
    {
        var job = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        job.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        job.MarkFailed("synthesis failed", FixedTime);

        var source = Job.Create("job-source", "3AW", "mel", 45, FixedTime).Value;
        source.AudioUrl = Option<string>.Some("https://audio.example.com/earlier.mp3");
        source.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        source.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        source.Approve(FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(source)));
        _jobService.DeliverCoveredSlotAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("the drop share refused the write"));
        SetupDueSchedules();

        // The sweep must not throw or abandon the remaining candidates on a delivery failure.
        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).DeliverCoveredSlotAsync(
            "job-1", Arg.Any<string>(), "system (deadline)", Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     Builds the exact shape the 2026-08-13 dead-air slot had: signed off by a human, audio in
    ///     storage, nobody clicked Publish.
    /// </summary>
    private static Job ApprovedUnpublishedJob(DateTime deadlineUtc, string jobId = "job-1")
    {
        var job = Job.Create(jobId, "3AW", "mel", 45, FixedTime).Value;
        job.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        job.FallbackDeadlineUtc = Option<DateTime>.Some(deadlineUtc);
        job.AudioUrl = Option<string>.Some("https://audio.example.com/valid.mp3");
        job.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        job.Approve(FixedTime);
        return job;
    }

    /// <summary>
    ///     An APPROVED bulletin is signed off, so the deadline DELIVERS it rather than superseding a
    ///     human's decision with a standby — and it does so BEFORE the deadline, which is the only
    ///     time playout accepts it. The candidate horizon runs ahead of now by
    ///     ApprovedDeliveryLeadSeconds precisely so this tick exists.
    /// </summary>
    [Fact]
    public async Task Fallback_ApprovedButUnpublishedInsideDeliveryLead_IsDeliveredBeforeTheDeadline()
    {
        var job = ApprovedUnpublishedJob(Now.AddSeconds(30));
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).PublishJobAsync(
            "job-1", "system (approval deadline)", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        // The slot is covered by the operator's own bulletin, never by a standby.
        await _jobRepo.DidNotReceive().GetLastSuccessfulJobForMarketAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The mechanism the fix turns on: the sweep asks for candidates against a horizon AHEAD of
    ///     now. Querying at `now` is what made the auto-delivery net unreachable — the first sighting
    ///     of an approved job was already past the deadline the playout guard enforces.
    /// </summary>
    [Fact]
    public async Task Fallback_CandidateQuery_LooksAheadByTheApprovedDeliveryLead()
    {
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received(1).ListJobsNeedingFallbackAsync(
            Now.AddSeconds(_options.ApprovedDeliveryLeadSeconds), Arg.Any<CancellationToken>());
        // MUST exceed the poller's own 60s tick (SchedulePollerTrigger "0 * * * * *"), or a deadline
        // on a minute boundary has no tick inside the window: -60s is too early to be a candidate,
        // +0s is too late to deliver. That is the exact off-by-one-tick that aired nothing.
        _options.ApprovedDeliveryLeadSeconds.ShouldBeGreaterThan(60);
    }

    /// <summary>
    ///     Past the deadline the playout share refuses the file outright, so the sweep must not keep
    ///     asking: it used to retry every minute for the whole missed-slot window, writing twenty
    ///     identical refusals into the log an operator reads. One entry, naming the real problem.
    /// </summary>
    [Fact]
    public async Task Fallback_ApprovedButPastDeliveryDeadline_IsNotAttemptedAndSaysSoOnce()
    {
        var job = ApprovedUnpublishedJob(Now.AddMinutes(-5));
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        // The sweep dedupes by re-reading the job's own log, so the stub has to persist entries the
        // way Table Storage does — otherwise every tick looks like the first one.
        _jobRepo.AddLogAsync(job.JobId, Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                job.AddLog(new JobLog(Now, ci.ArgAt<JobLogLevel>(2), ci.ArgAt<string>(1)));
                return Result.Success();
            });
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);
        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().PublishJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _jobRepo.Received(1).AddLogAsync(
            job.JobId,
            Arg.Is<string>(m => m.StartsWith("Playout delivery window closed", StringComparison.Ordinal)),
            JobLogLevel.Error, Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     A covered slot (FallbackAppliedAt set) only ever exists past its FallbackDeadlineUtc,
    ///     so writing it off at that clock gives a transiently-failed cover zero retry ticks.
    ///     Its real cutoff is the slot's air time: inside that window the sweep must keep trying.
    /// </summary>
    [Fact]
    public async Task Fallback_CoveredSlotPastFallbackDeadlineButBeforeAirTime_IsStillDelivered()
    {
        var job = ApprovedUnpublishedJob(Now.AddSeconds(-30));
        job.ScheduledForUtc = Option<DateTime>.Some(Now.AddSeconds(150));
        job.FallbackAppliedAt = Option<DateTime>.Some(Now.AddSeconds(-30));
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).PublishJobAsync(
            "job-1", "system (approval deadline)", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The extended window ends at the air time, hard: a covered slot whose slot has aired is
    ///     as undeliverable as any other stale bulletin — one closed-window entry, no attempts.
    /// </summary>
    [Fact]
    public async Task Fallback_CoveredSlotPastAirTime_IsNotAttemptedAndSaysSoOnce()
    {
        var job = ApprovedUnpublishedJob(Now.AddMinutes(-4));
        job.ScheduledForUtc = Option<DateTime>.Some(Now.AddMinutes(-1));
        job.FallbackAppliedAt = Option<DateTime>.Some(Now.AddMinutes(-4));
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        _jobRepo.AddLogAsync(job.JobId, Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                job.AddLog(new JobLog(Now, ci.ArgAt<JobLogLevel>(2), ci.ArgAt<string>(1)));
                return Result.Success();
            });
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);
        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().PublishJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _jobRepo.Received(1).AddLogAsync(
            job.JobId,
            Arg.Is<string>(m => m.StartsWith("Playout delivery window closed", StringComparison.Ordinal)),
            JobLogLevel.Error, Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The boundary itself: playout still accepts a file AT the deadline (the guard refuses only
    ///     strictly after it), and the poller's ticks land on the same minute boundaries deadlines do,
    ///     so this tick is a real delivery chance and must not be written off as too late.
    /// </summary>
    [Fact]
    public async Task Fallback_ApprovedExactlyAtTheDeadline_IsStillDelivered()
    {
        var job = ApprovedUnpublishedJob(Now);
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).PublishJobAsync(
            "job-1", "system (approval deadline)", Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     The widened horizon must not make the sweep premature: a bulletin still awaiting approval
    ///     whose deadline is seconds away is LIVE. Covering it with a standby would throw away a
    ///     valid bulletin an operator can still sign off.
    /// </summary>
    [Fact]
    public async Task Fallback_UnapprovedJobInsideDeliveryLead_IsLeftAlone()
    {
        var job = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        job.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        job.FallbackDeadlineUtc = Option<DateTime>.Some(Now.AddSeconds(30));
        job.LastUpdatedAt = Now.AddMinutes(-3); // stale worker: only the deadline keeps it safe
        job.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().GetLastSuccessfulJobForMarketAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.FallbackUsed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_ApprovedJobPastMissedWindow_IsNeverPublished()
    {
        var job = Job.Create("job-stale", "3AW", "mel", 45, Now.AddHours(-2)).Value;
        job.FallbackDeadlineUtc = Option<DateTime>.Some(Now.AddMinutes(-30));
        job.AudioUrl = Option<string>.Some("https://audio.example.com/stale.mp3");
        job.TransitionTo(JobStatus.AudioReady, utcNow: Now.AddHours(-2));
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: Now.AddHours(-2));
        job.Approve(Now.AddHours(-2));

        // Simulate the defensive path where the missed sweep did not return the job. The
        // fallback sweep itself must still refuse to publish stale traffic.
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().PublishJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_FallbackJobSameIdAsCurrent_Skipped()
    {
        var candidate = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        candidate.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        var sameIdFallback = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        sameIdFallback.AudioUrl = Option<string>.Some("https://audio.example.com/x.mp3");

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { candidate }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(sameIdFallback)));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.FallbackUsed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_FallbackJobNoAudioUrl_Skipped()
    {
        var candidate = Job.Create("job-1", "3AW", "mel", 45, FixedTime).Value;
        candidate.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        var noAudioFallback = Job.Create("fallback-1", "3AW", "mel", 45, FixedTime).Value; // no AudioUrl

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { candidate }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(noAudioFallback)));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.FallbackUsed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_FailedJobWithSameDayBulletin_AppliesFallback()
    {
        var failedJob = Job.Create("job-fail", "3AW", "mel", 45, FixedTime).Value;
        failedJob.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        failedJob.MarkFailed("orchestration error", FixedTime);

        var fallbackSource = Job.Create("fb-src", "3AW", "mel", 45, FixedTime).Value;
        fallbackSource.AudioUrl = Option<string>.Some("https://audio.example.com/fallback.mp3");
        fallbackSource.ScriptText = Option<string>.Some("fallback script");
        fallbackSource.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        fallbackSource.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        fallbackSource.Approve(FixedTime);
        fallbackSource.Publish(FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { failedJob }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(fallbackSource)));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received().SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-fail" && j.Status == JobStatus.FallbackUsed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_ApplyFallbackFails_DoesNotFireMetric()
    {
        // A Published job with no audio passes the skip checks but ApplyFallback
        // fails because Published is a terminal state for fallback.
        var publishedJob = Job.Create("job-pub", "3AW", "mel", 45, FixedTime).Value;
        publishedJob.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        publishedJob.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        publishedJob.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        publishedJob.Approve(FixedTime);
        publishedJob.Publish(FixedTime);
        // No AudioUrl set — fails IsValidAudio check, falls through to ApplyFallback

        var fallbackSource = Job.Create("fb-src", "3AW", "mel", 45, FixedTime).Value;
        fallbackSource.AudioUrl = Option<string>.Some("https://audio.example.com/fallback.mp3");
        fallbackSource.ScriptText = Option<string>.Some("fallback script");
        fallbackSource.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        fallbackSource.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        fallbackSource.Approve(FixedTime);
        fallbackSource.Publish(FixedTime);

        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { publishedJob }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(fallbackSource)));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.FallbackUsed),
            Arg.Any<CancellationToken>());
    }

    // ==================== Previous Broadcast Context ====================

    [Fact]
    public async Task PreviousContext_PreviousJobFound_SetsContextOnNewJob()
    {
        var previousJob = Job.Create("prev-1", "3AW", "mel", 45, FixedTime).Value;
        previousJob.ScriptText = Option<string>.Some("previous script text");
        _jobRepo.GetLatestBulletinForMarketTodayAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(previousJob)));

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j =>
                j.PreviousScriptContext.HasValue &&
                j.PreviousScriptContext.Value == "previous script text" &&
                j.PreviousJobId.HasValue &&
                j.PreviousJobId.Value == "prev-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviousContext_NoPreviousJob_NoContextOnNewJob()
    {
        // Default setup already returns None for GetLatestBulletinForMarketTodayAsync

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j =>
                j.PreviousScriptContext.IsNone &&
                j.PreviousJobId.IsNone),
            Arg.Any<CancellationToken>());
    }

    // ==================== Pronunciation dictionary resolution ====================

    private static PronunciationDictionary MakeDictionary(params PronunciationRule[] rules) =>
        PronunciationDictionary.Create(
            "Sydney Roads", null, "el-dict-1", "el-ver-1", rules, null, null, Now, "pd-1").Value;

    private static PronunciationDictionary MakeDictionaryWithAliasOnlyVersion(params PronunciationRule[] rules) =>
        PronunciationDictionary.Create(
            "Sydney Roads", null, "el-dict-1", "el-ver-1", rules, null, null, Now, "pd-1",
            aliasOnlyVersionId: "el-ver-alias").Value;

    private static PronunciationDictionary MakeGlobalDictionary(params PronunciationRule[] rules) =>
        PronunciationDictionary.Create(
            "Everywhere", null, "el-dict-global", "el-ver-global", rules, null, null, Now, "pd-global",
            PronunciationScope.Global).Value;

    private void SetupLibrary(params PronunciationDictionary[] dictionaries) =>
        _pronunciationDictionaryService.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<PronunciationDictionary>>.Success(dictionaries));

    private void SetupVoiceModel(string model)
    {
        var profile = VoiceProfile.Create("Test Voice", null, "voice-1", "Test Voice", model,
            VoiceSettings.Default, null, null, Now).Value;
        _voiceProfileService.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(profile));
    }

    private MarketSchedule ScheduleWithDictionary(string dictionaryId = "pd-1")
    {
        var schedule = MakeSchedule();
        schedule.PronunciationDictionaryId = dictionaryId;
        return schedule;
    }

    [Fact]
    public async Task Pronunciation_PhonemeRules_MultilingualVoice_DictionaryNotAttached()
    {
        // eleven_multilingual_v2 silently DROPS phoneme-matched words from the audio —
        // a dictionary containing phoneme rules must never be attached to it.
        SetupVoiceModel("eleven_multilingual_v2");
        SetupLibrary(MakeDictionary(
            PronunciationRule.CreateAlias("Ryde", "Ride").Value,
            PronunciationRule.CreatePhoneme("Warringah", "wəˈrɪŋɡə", "ipa").Value));

        await _sut.ProcessScheduleAsync(ScheduleWithDictionary(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m => m.PronunciationLocator == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pronunciation_PhonemeRules_MultilingualVoice_AttachesAliasOnlyVersion()
    {
        // Bug-bash 2026-08-05 finding 13(b): a single phoneme rule must not take the whole
        // layer off the read. When the dictionary has an alias-only version line, that locator
        // (same dictionary, alias rules only) is attached instead — every alias correction still
        // airs and the phoneme rules are dropped + logged.
        SetupVoiceModel("eleven_multilingual_v2");
        SetupLibrary(MakeDictionaryWithAliasOnlyVersion(
            PronunciationRule.CreateAlias("Warringah", "Warringa").Value,
            PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value));

        await _sut.ProcessScheduleAsync(ScheduleWithDictionary(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.PronunciationLocator != null &&
                m.PronunciationLocator.DictionaryId == "el-dict-1" &&
                m.PronunciationLocator.VersionId == "el-ver-alias"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pronunciation_PhonemeRules_TurboVoice_DictionaryAttached()
    {
        SetupVoiceModel("eleven_turbo_v2");
        SetupLibrary(MakeDictionary(PronunciationRule.CreatePhoneme("Warringah", "wəˈrɪŋɡə", "ipa").Value));

        await _sut.ProcessScheduleAsync(ScheduleWithDictionary(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.PronunciationLocator != null &&
                m.PronunciationLocator.DictionaryId == "el-dict-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pronunciation_AliasOnlyRules_MultilingualVoice_DictionaryAttached()
    {
        SetupVoiceModel("eleven_multilingual_v2");
        SetupLibrary(MakeDictionary(PronunciationRule.CreateAlias("Warringah", "Warringa").Value));

        await _sut.ProcessScheduleAsync(ScheduleWithDictionary(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.PronunciationLocator != null &&
                m.PronunciationLocator.DictionaryId == "el-dict-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pronunciation_StationAndGlobal_BothLayersDispatched()
    {
        SetupVoiceModel("eleven_v3");
        SetupLibrary(
            MakeDictionary(PronunciationRule.CreateAlias("Warringah", "Warringa").Value),
            MakeGlobalDictionary(PronunciationRule.CreateAlias("NorthConnex", "North-Connex").Value));

        await _sut.ProcessScheduleAsync(ScheduleWithDictionary(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.PronunciationLocator != null &&
                m.PronunciationLocator.DictionaryId == "el-dict-1" &&
                m.GlobalPronunciationLocator != null &&
                m.GlobalPronunciationLocator.DictionaryId == "el-dict-global"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pronunciation_UnassignedStation_StillReadsWithThePlatformWideLayer()
    {
        // The point of the platform-wide layer: a station with no dictionary of its own is no
        // longer left with nothing.
        SetupVoiceModel("eleven_v3");
        SetupLibrary(MakeGlobalDictionary(PronunciationRule.CreateAlias("NorthConnex", "North-Connex").Value));

        await _sut.ProcessScheduleAsync(MakeSchedule(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.PronunciationLocator == null &&
                m.GlobalPronunciationLocator != null &&
                m.GlobalPronunciationLocator.DictionaryId == "el-dict-global"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pronunciation_PhonemeCarryingGlobal_DoesNotDisqualifyACleanStationLayer()
    {
        // Per-layer guarding: a bad platform-wide dictionary must not take the station's clean
        // alias-only dictionary off the read.
        SetupVoiceModel("eleven_multilingual_v2");
        SetupLibrary(
            MakeDictionary(PronunciationRule.CreateAlias("Warringah", "Warringa").Value),
            MakeGlobalDictionary(PronunciationRule.CreatePhoneme("Lachlan", "ˈlɒklən", "ipa").Value));

        await _sut.ProcessScheduleAsync(ScheduleWithDictionary(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.PronunciationLocator != null &&
                m.PronunciationLocator.DictionaryId == "el-dict-1" &&
                m.GlobalPronunciationLocator == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pronunciation_ArchivedGlobal_IsNotAttached()
    {
        SetupVoiceModel("eleven_v3");
        var archivedGlobal = MakeGlobalDictionary(
            PronunciationRule.CreateAlias("NorthConnex", "North-Connex").Value);
        archivedGlobal.Archive(Now);
        SetupLibrary(MakeDictionary(PronunciationRule.CreateAlias("Warringah", "Warringa").Value), archivedGlobal);

        await _sut.ProcessScheduleAsync(ScheduleWithDictionary(), CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m => m.GlobalPronunciationLocator == null),
            Arg.Any<CancellationToken>());
    }

    // ==================== Stale Recovery — Schedule Fallback ====================

    [Fact]
    public async Task RecoverStaleJobs_NoGeoData_WithSchedule_UsesScheduleGeoData()
    {
        var staleJob = Job.Create("stale-1", "3AW", "mel", 45, FixedTime).Value; // No geo data
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { staleJob }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // The deadline sweep now DELIVERS what it covers a slot with — applying a
        // fallback only attached the audio to the job, and nothing wrote it to the
        // playout drop, so a covered slot still aired nothing unless an operator
        // published by hand. Every fallback path therefore signs off and publishes.
        _jobService.ApproveJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobService.PublishJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // Covering a slot finishes at the playout drop, and that sequence lives in
        // JobService.DeliverCoveredSlotAsync so no path can skip it. The sweep's job is
        // to CALL it; the sequence itself is pinned in JobServiceTests.
        _jobService.DeliverCoveredSlotAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.JobId == "stale-1" &&
                m.Latitude == Coverage.Latitude &&
                m.Longitude == Coverage.Longitude &&
                m.RadiusKm == Coverage.RadiusKm &&
                m.TimezoneId == "Australia/Melbourne"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecoverStaleJobs_NoVoiceConfig_UsesDefaultVoice()
    {
        _options.DefaultVoiceId = "default-voice";
        var staleJob = Job.Create("stale-1", "3AW", "mel", 45, FixedTime).Value; // No VoiceConfig, no geo
        _jobRepo.ListStaleRequestedJobsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { staleJob }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // The deadline sweep now DELIVERS what it covers a slot with — applying a
        // fallback only attached the audio to the job, and nothing wrote it to the
        // playout drop, so a covered slot still aired nothing unless an operator
        // published by hand. Every fallback path therefore signs off and publishes.
        _jobService.ApproveJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _jobService.PublishJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        // Covering a slot finishes at the playout drop, and that sequence lives in
        // JobService.DeliverCoveredSlotAsync so no path can skip it. The sweep's job is
        // to CALL it; the sequence itself is pinned in JobServiceTests.
        _jobService.DeliverCoveredSlotAsync(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _queuePublisher.Received(1).PublishBroadcastMessageAsync(
            Arg.Is<BroadcastQueueMessage>(m =>
                m.VoiceId == "default-voice"),
            Arg.Any<CancellationToken>());
    }

    // ==================== Filler-cart fallback ====================

    [Fact]
    public async Task ProcessFallbacks_NoSameDayBulletinButFillerConfigured_AppliesFiller()
    {
        _options.FillerCartAudioUrl = "http://filler/cart.wav";
        var job = Job.Create("fb-1", "3AW", "mel", 45, Now.AddMinutes(-3)).Value; // stale — past the in-flight grace
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        _scheduleRepo.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("none"));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        job.Status.ShouldBe(JobStatus.FallbackUsed);
        job.AudioUrl.Value.ShouldBe("http://filler/cart.wav");
        await _jobRepo.Received().SaveJobAsync(job, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessFallbacks_NoSameDayBulletinAndNoFiller_LeavesJobUntouched()
    {
        _options.FillerCartAudioUrl = "";
        var job = Job.Create("fb-1", "3AW", "mel", 45, Now.AddMinutes(-3)).Value; // stale — past the in-flight grace
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        _scheduleRepo.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("none"));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        job.Status.ShouldNotBe(JobStatus.FallbackUsed);
        await _jobRepo.DidNotReceive().SaveJobAsync(job, Arg.Any<CancellationToken>());
    }
    // ==================== Fallback Notification Decisions ====================

    private Job CreateFailedJob(string jobId, string market = "mel")
    {
        var job = Job.Create(jobId, "3AW", market, 45, FixedTime).Value;
        job.ScheduledForUtc = Option<DateTime>.Some(FixedTime);
        job.MarkFailed("orchestration error", FixedTime);
        return job;
    }

    private Job CreateFallbackSource()
    {
        var source = Job.Create("fb-src", "3AW", "mel", 45, FixedTime).Value;
        source.AudioUrl = Option<string>.Some("https://audio.example.com/fallback.mp3");
        source.ScriptText = Option<string>.Some("fallback script");
        source.TransitionTo(JobStatus.AudioReady, utcNow: FixedTime);
        source.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        source.Approve(FixedTime);
        source.Publish(FixedTime);
        return source;
    }

    private void SetupFallbackWithSameDayBulletin(Job candidateJob, Job? fallbackSource = null)
    {
        fallbackSource ??= CreateFallbackSource();
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { candidateJob }));
        _scheduleRepo.GetScheduleAsync(candidateJob.Market, Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule(candidateJob.Market)));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                candidateJob.Market, "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.Some(fallbackSource)));
        SetupDueSchedules();
    }

    [Fact]
    public async Task ProcessFallbacks_SameDayFallbackWithUrgentEscalationLog_SendsMissedDeadlineClosure()
    {
        var failedJob = CreateFailedJob("job-fail");
        failedJob.AddLog(new JobLog(FixedTime.AddMinutes(-2), JobLogLevel.Warn,
            "Job escalated: playout imminent — inside the escalation window"));

        SetupFallbackWithSameDayBulletin(failedJob);

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "job-fail"),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.MissedDeadlineClosure),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessFallbacks_SameDayFallbackWithoutUrgentLog_SendsFailedCovered()
    {
        var failedJob = CreateFailedJob("job-fail");
        // No urgent escalation log — just a routine error log
        failedJob.AddLog(new JobLog(FixedTime.AddMinutes(-2), JobLogLevel.Error,
            "Scheduled run failed: pipeline error"));

        SetupFallbackWithSameDayBulletin(failedJob);

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "job-fail"),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedCovered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessFallbacks_FillerCartFallback_SendsFailedNotCovered()
    {
        _options.FillerCartAudioUrl = "http://filler/cart.wav";
        var failedJob = CreateFailedJob("job-fb");

        // No same-day bulletin available
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { failedJob }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "job-fb"),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedNotCovered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessFallbacks_NoFallbackAvailable_SendsFailedNotCoveredOnceThenDedupes()
    {
        _options.FillerCartAudioUrl = "";
        var failedJob = CreateFailedJob("job-none");

        // No same-day bulletin, no filler cart
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { failedJob }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        SetupDueSchedules();

        var metrics = Substitute.For<IBroadcastMetrics>();
        var fallbackService = new BulletinFallbackService(
            _scheduleRepo, _jobRepo, Options.Create(_options),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(_options),
                Substitute.For<ILogger<PerVoiceFillerCartResolver>>()),
            Padder(),
            _timeProvider,
            Substitute.For<ILogger<BulletinFallbackService>>());
        var sut = new SchedulePollerService(
            _scheduleRepo, _jobRepo, _jobService, _notificationService, fallbackService, _cronParser, _modelSelector, _voiceProfileService,
            _voiceProvider, _deliveryStyleService, Substitute.For<IBulletinPlaybookService>(),
            Substitute.For<IBookendService>(), _pronunciationDictionaryService,
            _queuePublisher, Options.Create(_options), _timeProvider, metrics, _logger);

        // First poll cycle — notification should fire
        await sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "job-none"),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedNotCovered),
            Arg.Any<CancellationToken>());
        await _jobRepo.Received(1).AddLogAsync(
            "job-none",
            Arg.Is<string>(m => m.StartsWith("Fallback alert sent")),
            JobLogLevel.Warn,
            Arg.Any<CancellationToken>());
        metrics.Received(1).TrackPipelineStage("fallback_none", 0, "mel", false);

        // Simulate the log marker being persisted and re-read on the next poll cycle
        failedJob.AddLog(new JobLog(FixedTime, JobLogLevel.Warn,
            "Fallback alert sent: no same-day bulletin or filler available"));
        _notificationService.ClearReceivedCalls();

        // Second poll cycle — dedupe should suppress the notification
        await sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.DidNotReceive().SendJobNotificationAsync(
            Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>());
    }

    // ==================== Fallback Sweep — In-Flight Worker Grace (dispatcher model, 2026-07-21) ====================

    [Fact]
    public async Task ProcessFallbacks_DeadlinePassedButWorkerActive_DefersSweep()
    {
        // A freshly-dispatched job (active pipeline status, touched seconds ago) must NOT be
        // covered by the deadline sweep — the queue worker is still producing its fresh audio.
        var inFlight = Job.Create("job-active", "3AW", "mel", 45, Now.AddSeconds(-30)).Value; // Requested
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { inFlight }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().GetLastSuccessfulJobForMarketAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notificationService.DidNotReceive().SendJobNotificationAsync(
            Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessFallbacks_DeadlinePassedAndWorkerStale_SweepCovers()
    {
        // The same in-flight job untouched beyond the grace window is treated as a crashed
        // worker and covered — the dead-air guarantee survives with a bounded delay.
        _options.FillerCartAudioUrl = "";
        var stale = Job.Create("job-stale", "3AW", "mel", 45, Now.AddMinutes(-3)).Value; // Requested, stale
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { stale }));
        _scheduleRepo.GetScheduleAsync("mel", Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(MakeSchedule()));
        _jobRepo.GetLastSuccessfulJobForMarketAsync(
                "mel", "3AW", Arg.Any<DateTime>(), Arg.Any<DateTime>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<Job>>.Success(Option<Job>.None));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _notificationService.Received(1).SendJobNotificationAsync(
            Arg.Is<Job>(j => j.JobId == "job-stale"),
            Arg.Is<NotificationKind?>(k => k == NotificationKind.FailedNotCovered),
            Arg.Any<CancellationToken>());
    }

    // ==================== Voice rotation resolution ====================

    private VoiceProfile MakeProfile(string baseVoiceId, string name) =>
        VoiceProfile.Create(name, null, baseVoiceId, name, "model-1", VoiceSettings.Default, null, null, Now).Value;

    [Theory]
    [InlineData(0, "voice-A")]
    [InlineData(1, "voice-B")]
    [InlineData(2, "voice-A")] // wraps: 2 % 2 == 0
    [InlineData(3, "voice-B")]
    public async Task ResolveVoice_RoundRobin_PicksProfileByCursor(int cursor, string expectedVoiceId)
    {
        _voiceProfileService.GetAsync("vp-a", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(MakeProfile("voice-A", "Voice A")));
        _voiceProfileService.GetAsync("vp-b", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(MakeProfile("voice-B", "Voice B")));
        var schedule = MakeSchedule();
        schedule.VoiceRotation = Option<VoiceRotationPolicy>.Some(
            VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["vp-a", "vp-b"], null).Value);
        schedule.VoiceRotationCursor = cursor;

        await _sut.ProcessScheduleAsync(schedule, CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.VoiceConfig.Value.VoiceId == expectedVoiceId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveVoice_TimeOfDay_PicksWindowCoveringLocalTime()
    {
        // Now = 10:00 UTC; Australia/Melbourne in June is +10 → 20:00 local, inside 18:00–22:00.
        _voiceProfileService.GetAsync("vp-day", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(MakeProfile("voice-day", "Day")));
        _voiceProfileService.GetAsync("vp-eve", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Success(MakeProfile("voice-eve", "Evening")));
        var schedule = MakeSchedule();
        schedule.VoiceRotation = Option<VoiceRotationPolicy>.Some(
            VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null, [
                new VoiceWindow(new TimeOnly(6, 0), new TimeOnly(18, 0), ["vp-day"]),
                new VoiceWindow(new TimeOnly(18, 0), new TimeOnly(22, 0), ["vp-eve"])
            ]).Value);

        await _sut.ProcessScheduleAsync(schedule, CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.VoiceConfig.Value.VoiceId == "voice-eve"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveVoice_TimeOfDay_Gap_FallsBackToPrimaryProfile()
    {
        // 20:00 local falls outside the only window (06:00–09:00) → primary "vp-1" (voice-1).
        var schedule = MakeSchedule();
        schedule.VoiceRotation = Option<VoiceRotationPolicy>.Some(
            VoiceRotationPolicy.Create(VoiceSelectionMode.TimeOfDay, null,
                [new VoiceWindow(new TimeOnly(6, 0), new TimeOnly(9, 0), ["vp-morning"])]).Value);

        await _sut.ProcessScheduleAsync(schedule, CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.VoiceConfig.Value.VoiceId == "voice-1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveVoice_DanglingRotationMember_FallsBackToPrimaryProfile()
    {
        _voiceProfileService.GetAsync("vp-missing", Arg.Any<CancellationToken>())
            .Returns(Result<VoiceProfile>.Failure("not found"));
        var schedule = MakeSchedule(); // primary "vp-1" resolves to the default profile (voice-1)
        schedule.VoiceRotation = Option<VoiceRotationPolicy>.Some(
            VoiceRotationPolicy.Create(VoiceSelectionMode.RoundRobin, ["vp-missing"], null).Value);

        await _sut.ProcessScheduleAsync(schedule, CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.VoiceConfig.Value.VoiceId == "voice-1"), Arg.Any<CancellationToken>());
    }

    // ==================== Approval Escalation (QT-004) ====================

    private static Job MakeWaitingJob(string jobId, string market, DateTime createdAt,
        Option<DateTime> playoutUtc = default, Option<DateTime> fallbackDeadlineUtc = default)
    {
        var job = Job.Create(jobId, "3AW", market, 45, createdAt).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: createdAt);
        job.ScheduledForUtc = playoutUtc;
        job.FallbackDeadlineUtc = fallbackDeadlineUtc;
        return job;
    }

    private void SetupWaitingJobs(params Job[] jobs)
    {
        // Arg.Is keeps teeth: listing anything other than WaitingApproval returns empty.
        _jobRepo.ListJobsAsync(
                Arg.Is<IReadOnlyCollection<JobStatus>>(s => s.Contains(JobStatus.WaitingApproval)),
                Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(jobs));
    }

    private void SetupSchedules(params MarketSchedule[] schedules)
    {
        _scheduleRepo.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success(schedules));
    }

    [Fact]
    public async Task EscalateApprovals_PlayoutInsideEscalationWindow_SendsUrgentOnly()
    {
        // MakeSchedule: notify=10, escalation=3. Playout in 2 min → inside both windows;
        // the urgent tier must win and the reminder must not also fire.
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddMinutes(-30),
            Option<DateTime>.Some(Now.AddMinutes(2))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).EscalateJobAsync(
            "job-1", Arg.Is<string>(r => r.Contains("playout imminent")), Arg.Any<CancellationToken>());
        await _jobService.DidNotReceive().EscalateJobAsync(
            Arg.Any<string>(), Arg.Is<string>(r => r.Contains("approval reminder")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateApprovals_PlayoutPassedButWithinGrace_SendsUrgent()
    {
        // Playout exactly 10 min ago pins the strict > boundary: still inside grace, still pages.
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddMinutes(-30),
            Option<DateTime>.Some(Now.AddMinutes(-10))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).EscalateJobAsync(
            "job-1", Arg.Is<string>(r => r.Contains("playout imminent")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateApprovals_PlayoutInsideNotifyWindow_SendsReminder()
    {
        // Playout in 5 min → inside notify (10) but outside escalation (3); job is 5 min old.
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddMinutes(-5),
            Option<DateTime>.Some(Now.AddMinutes(5))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).EscalateJobAsync(
            "job-1", Arg.Is<string>(r => r.Contains("approval reminder")), Arg.Any<CancellationToken>());
        await _jobService.DidNotReceive().EscalateJobAsync(
            Arg.Any<string>(), Arg.Is<string>(r => r.Contains("playout imminent")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateApprovals_JobYoungerThanTwoMinutes_NoReminder()
    {
        // Same notify-window position, but the job was created 1 min ago — the freshly
        // sent WaitingApproval card must not be chased immediately.
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddMinutes(-1),
            Option<DateTime>.Some(Now.AddMinutes(5))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().EscalateJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateApprovals_PlayoutPassedBeyondGrace_NoEscalation()
    {
        // Playout 11 min ago → past the 10-min grace; a stale waiting job never pages.
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddMinutes(-60),
            Option<DateTime>.Some(Now.AddMinutes(-11))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().EscalateJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateApprovals_PlayoutOutsideNotifyWindow_NoEscalation()
    {
        // Playout in 30 min → both windows still closed; nothing may fire early.
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddMinutes(-30),
            Option<DateTime>.Some(Now.AddMinutes(30))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().EscalateJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateApprovals_NoScheduledPlayout_Skipped()
    {
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddMinutes(-30)));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().EscalateJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateApprovals_NoMatchingSchedule_Skipped()
    {
        // Job market "bri" has no schedule → no window config, so no escalation.
        SetupWaitingJobs(MakeWaitingJob("job-1", "bri", Now.AddMinutes(-30),
            Option<DateTime>.Some(Now.AddMinutes(2))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.DidNotReceive().EscalateJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EscalateApprovals_MarketMatchIsCaseInsensitive()
    {
        SetupWaitingJobs(MakeWaitingJob("job-1", "SYDNEY", Now.AddMinutes(-30),
            Option<DateTime>.Some(Now.AddMinutes(2))));
        SetupSchedules(MakeSchedule("sydney"));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobService.Received(1).EscalateJobAsync(
            "job-1", Arg.Is<string>(r => r.Contains("playout imminent")), Arg.Any<CancellationToken>());
    }

    // ==================== Missed-Slot Sweep (QT-008) ====================

    [Fact]
    public async Task MarkMissedWaitingApprovals_ScheduledJobPastDeadlineGrace_MarksMissedSilently()
    {
        // Default MissedAfterDeadlineMinutes is 10; the Zetta delivery deadline (FallbackDeadlineUtc)
        // 30 min ago is 20 min past that grace, so the slot is settled as missed.
        var deadline = Now.AddMinutes(-30);
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddHours(-25),
            Option<DateTime>.Some(Now.AddHours(-25)), Option<DateTime>.Some(deadline)));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-1" && j.Status == JobStatus.Missed
                && !j.RejectionHistory.Any()
                && j.MissedDeadlineUtc == Option<DateTime>.Some(deadline)),
            Arg.Any<CancellationToken>());
        // Silent hygiene - no Teams/operator notification for the missed-slot marking itself.
        await _notificationService.DidNotReceive().SendJobNotificationAsync(
            Arg.Any<Job>(), Arg.Any<NotificationKind>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkMissedExpiredSlots_ApprovedScheduledJobPastGrace_SettlesBeforeFallback()
    {
        var deadline = Now.AddMinutes(-30);
        var job = Job.Create("job-approved", "3AW", "mel", 45, Now.AddHours(-2)).Value;
        job.FallbackDeadlineUtc = Option<DateTime>.Some(deadline);
        job.AudioUrl = Option<string>.Some("https://audio.example.com/stale.mp3");
        job.TransitionTo(JobStatus.AudioReady, utcNow: Now.AddHours(-2));
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: Now.AddHours(-2));
        job.Approve(Now.AddHours(-2), "operator");

        _jobRepo.ListJobsAsync(Arg.Any<IReadOnlyCollection<JobStatus>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        _jobRepo.ListJobsNeedingFallbackAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<Job>>.Success(new[] { job }));
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-approved" && j.Status == JobStatus.Missed
                && j.MissedDeadlineUtc == Option<DateTime>.Some(deadline)
                && j.ApprovedBy == "operator"),
            Arg.Any<CancellationToken>());
        await _jobService.DidNotReceive().PublishJobAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await _jobService.DidNotReceive().DeliverCoveredSlotAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkMissedWaitingApprovals_ScheduledJobInsideDeadlineGrace_LeftAlone()
    {
        // Zetta delivery deadline 5 min ago, inside the default 10-min grace: still actionable.
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddHours(-1),
            Option<DateTime>.Some(Now.AddHours(-1)), Option<DateTime>.Some(Now.AddMinutes(-5))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Missed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkMissedWaitingApprovals_RespectsConfiguredDwellHours()
    {
        // 3h-old ad-hoc job would survive the default 24h dwell but not a 2h one -
        // proves ExpireWaitingApprovalHours (not a hardcoded value) drives the ad-hoc arm.
        _options.ExpireWaitingApprovalHours = 2;
        var createdAt = Now.AddHours(-3);
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", createdAt));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-1" && j.Status == JobStatus.Missed
                && !j.RejectionHistory.Any()
                && j.MissedDeadlineUtc == Option<DateTime>.Some(createdAt)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkMissedWaitingApprovals_AdHocJobWithNoDeadline_FallsBackToCreatedAt()
    {
        // Manual/ad-hoc trigger: no FallbackDeadlineUtc (only the schedule poller sets it), but
        // it's still 2h old - must age out via CreatedAt + the default 1h dwell, not be
        // skipped as "unschedulable".
        var createdAt = Now.AddHours(-2);
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", createdAt));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-1" && j.Status == JobStatus.Missed
                && !j.RejectionHistory.Any()
                && j.MissedDeadlineUtc == Option<DateTime>.Some(createdAt)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkMissedWaitingApprovals_FreshAdHocJobWithNoSchedule_LeftAlone()
    {
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddMinutes(-30)));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Missed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkMissedWaitingApprovals_ScheduledJobInsideConfiguredWindow_LeftAlone()
    {
        // A 60-min window keeps a job 30 min past its Zetta deadline actionable - proves
        // MissedAfterDeadlineMinutes (not a hardcoded grace) drives the scheduled arm.
        _options.MissedAfterDeadlineMinutes = 60;
        SetupWaitingJobs(MakeWaitingJob("job-1", "mel", Now.AddHours(-2),
            Option<DateTime>.Some(Now.AddHours(-2)), Option<DateTime>.Some(Now.AddMinutes(-30))));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.Status == JobStatus.Missed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkMissedWaitingApprovals_ZeroMissedAfterDeadlineMinutes_DisablesScheduledArm()
    {
        // 0 disables the scheduled arm: a scheduled job hours past its Zetta deadline is left
        // alone, while an ad-hoc job in the same tick still ages out via its dwell window.
        _options.MissedAfterDeadlineMinutes = 0;
        SetupWaitingJobs(
            MakeWaitingJob("job-1", "mel", Now.AddHours(-5),
                Option<DateTime>.Some(Now.AddHours(-5)), Option<DateTime>.Some(Now.AddHours(-4))),
            MakeWaitingJob("job-2", "mel", Now.AddHours(-30)));
        SetupSchedules(MakeSchedule());
        SetupDueSchedules();

        await _sut.ProcessDueSchedulesAsync(CancellationToken.None);

        await _jobRepo.Received(1).SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-2" && j.Status == JobStatus.Missed),
            Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().SaveJobAsync(
            Arg.Is<Job>(j => j.JobId == "job-1"), Arg.Any<CancellationToken>());
    }
}

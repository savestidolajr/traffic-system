using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Libraries;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class JobServiceAdditionalTests
{
    private static readonly DateTime FixedTime = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly IFileDeliveryService _fileDeliveryService = Substitute.For<IFileDeliveryService>();
    private readonly IJobRepository _jobRepo = Substitute.For<IJobRepository>();
    private readonly IScheduleRepository _scheduleRepository = Substitute.For<IScheduleRepository>();
    private readonly IModelSelector _modelSelector = Substitute.For<IModelSelector>();
    private readonly IVoiceProvider _voiceProvider = Substitute.For<IVoiceProvider>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly IQueuePublisher _queuePublisher = Substitute.For<IQueuePublisher>();
    private readonly IBroadcastMetrics _metrics = Substitute.For<IBroadcastMetrics>();
    private readonly JobService _sut;

    public JobServiceAdditionalTests()
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
        _scheduleRepository.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([]));

        _sut = new JobService(_jobRepo, _scheduleRepository, _queuePublisher, _modelSelector,
            Substitute.For<IVoiceProfileService>(),
            _voiceProvider,
            _notificationService, _fileDeliveryService,
            Substitute.For<IBroadcastOutput>(),
            Substitute.For<IPronunciationDictionaryService>(),
            Substitute.For<IDeliveryStyleService>(),
            Substitute.For<IBookendService>(),
            _metrics,
            Options.Create(new BroadcastOptions
            { DefaultVoiceId = "default-voice", DefaultLanguage = "en" }),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()),
                Substitute.For<ILogger<PerVoiceFillerCartResolver>>()),
            Substitute.For<IBulletinFallbackService>(), TimeProvider.System, Substitute.For<ILogger<JobService>>());
    }

    private Job CreateApprovedJobWithAudio(string audioUrl = "https://blob.test/audio.wav")
    {
        var job = Job.Create("job-pub", "SYD", "SYD", 30.0, FixedTime).Value;
        job.ETag = "test-etag";
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        job.Approve(FixedTime, "test");
        job.AudioUrl = Option<string>.Some(audioUrl);
        return job;
    }

    private void SetupJobFound(Job job)
    {
        _jobRepo.GetJobAsync(job.JobId, Arg.Any<CancellationToken>()).Returns(Result<Job>.Success(job));
    }

    // ──────────────────────────────────────────────
    // PublishJobAsync — AudioUrl delivery paths
    // ──────────────────────────────────────────────
    [Fact]
    public async Task PublishJobAsync_WithAudioUrl_DeliverySucceeds_LogsDelivery()
    {
        var job = CreateApprovedJobWithAudio();
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _fileDeliveryService.Received(1)
            .DeliverAsync("https://blob.test/audio.wav", "SYD", Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        await _jobRepo.Received().AddLogAsync(job.JobId, "Audio delivered to playout system", JobLogLevel.Info,
            Arg.Any<CancellationToken>());
        _metrics.Received(1).TrackZettaDeliverySuccess("SYD");
    }

    [Fact]
    public async Task PublishJobAsync_WithAudioUrl_DeliveryFails_ReturnsFailure_JobStaysApproved()
    {
        var job = CreateApprovedJobWithAudio();
        SetupJobFound(job);
        _fileDeliveryService.DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Result<FileDeliveryStatus>.Failure("SFTP unreachable"));

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe("SFTP unreachable");
        job.Status.ShouldBe(JobStatus.Approved);
        await _jobRepo.Received().AddLogAsync(job.JobId, Arg.Is<string>(s => s.Contains("File delivery failed")),
            JobLogLevel.Warn, Arg.Any<CancellationToken>());
        _metrics.Received(1).TrackZettaDeliveryFailure("SYD", "SFTP unreachable");
        await _jobRepo.DidNotReceive().SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_DeliveryDisabled_LogsHonestlyNotDelivered()
    {
        var job = CreateApprovedJobWithAudio();
        SetupJobFound(job);
        _fileDeliveryService.DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Result<FileDeliveryStatus>.Success(FileDeliveryStatus.SkippedDisabled));

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        // Honest audit: do NOT claim playout delivery when it was skipped.
        await _jobRepo.Received().AddLogAsync(job.JobId,
            Arg.Is<string>(s => s.Contains("disabled")), JobLogLevel.Info, Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().AddLogAsync(job.JobId, "Audio delivered to playout system",
            JobLogLevel.Info, Arg.Any<CancellationToken>());
        _metrics.DidNotReceive().TrackZettaDeliverySuccess(Arg.Any<string>());
    }

    [Fact]
    public async Task PublishJobAsync_WithoutAudioUrl_SkipsDelivery()
    {
        var job = Job.Create("job-noaudio", "SYD", "SYD", 30.0, FixedTime).Value;
        job.ETag = "test-etag";
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        job.Approve(FixedTime, "test");
        // AudioUrl defaults to None
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishJobAsync_FromFallbackUsed_Fails_DeliveryNeverCalled()
    {
        // P0: FallbackUsed jobs MUST be explicitly approved before they can be published;
        // direct publish is rejected.
        var job = Job.Create("job-fallback", "SYD", "SYD", 30.0, FixedTime).Value;
        job.ETag = "test-etag";
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        job.ApplyFillerFallback("https://filler.example/cart.wav", FixedTime);
        SetupJobFound(job);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ErrorMessages.Job.MustBeApprovedToPublish);
        job.Status.ShouldBe(JobStatus.FallbackUsed);
        await _fileDeliveryService.DidNotReceive()
            .DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // PublishJobAsync — per-station playout arming
    // ──────────────────────────────────────────────

    private void SetupStation(string marketId, bool playoutEnabled)
    {
        var schedule = MarketSchedule.Create(marketId, $"{marketId} Traffic", "KIIS 90s",
            "Australia/Sydney", "0 6 * * *", true, CoverageArea.Create(0, 0, 1).Value,
            "voice-1", "out/", 30, 15, 5, FixedTime, FixedTime,
            playoutEnabled: playoutEnabled).Value;
        _scheduleRepository.GetScheduleAsync(marketId, Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Success(schedule));
    }

    /// <summary>
    ///     The switch that lets two environments run playout-enabled side by side: a station held
    ///     back here still generates, reviews and publishes, but nothing is written to the share,
    ///     so it cannot race the environment that IS airing it under the same file name.
    /// </summary>
    [Fact]
    public async Task PublishJobAsync_StationPlayoutOff_PublishesWithoutTouchingTheShare()
    {
        var job = CreateApprovedJobWithAudio();
        SetupJobFound(job);
        SetupStation(job.Market, playoutEnabled: false);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        job.Status.ShouldBe(JobStatus.Published);
        await _fileDeliveryService.DidNotReceive().DeliverAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
        // Names the STATION, not the environment: the operator has to be able to tell "held back
        // here" from "this environment never delivers".
        await _jobRepo.Received().AddLogAsync(job.JobId,
            Arg.Is<string>(s => s.Contains("Send to playout is off for this station")),
            JobLogLevel.Info, Arg.Any<CancellationToken>());
        await _jobRepo.DidNotReceive().AddLogAsync(job.JobId, "Audio delivered to playout system",
            JobLogLevel.Info, Arg.Any<CancellationToken>());
        _metrics.DidNotReceive().TrackZettaDeliverySuccess(Arg.Any<string>());
    }

    [Fact]
    public async Task PublishJobAsync_StationPlayoutOn_DeliversToTheShare()
    {
        var job = CreateApprovedJobWithAudio();
        SetupJobFound(job);
        SetupStation(job.Market, playoutEnabled: true);

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _fileDeliveryService.Received(1).DeliverAsync(
            "https://blob.test/audio.wav", job.Market, Arg.Any<string?>(),
            Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>());
        _metrics.Received(1).TrackZettaDeliverySuccess(job.Market);
    }

    /// <summary>
    ///     Fails OPEN by design. Withholding a reviewed, approved bulletin from air because a
    ///     station row could not be read would turn a storage blip into dead air, and the
    ///     environment-wide gate plus the deadline refusal at the delivery boundary still apply.
    /// </summary>
    [Fact]
    public async Task PublishJobAsync_StationUnreadable_StillDelivers()
    {
        var job = CreateApprovedJobWithAudio();
        SetupJobFound(job);
        _scheduleRepository.GetScheduleAsync(job.Market, Arg.Any<CancellationToken>())
            .Returns(Result<MarketSchedule>.Failure("table unavailable"));

        var result = await _sut.PublishJobAsync(job.JobId, expectedEtag: "test-etag");

        result.IsSuccess.ShouldBeTrue();
        await _fileDeliveryService.Received(1).DeliverAsync(
            Arg.Any<string>(), job.Market, Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(),
            Arg.Any<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // RegenerateJobAsync — AdditionalContext & Rejection
    // ──────────────────────────────────────────────

    [Fact]
    public async Task RegenerateJobAsync_WithAdditionalContext_IncludesInMessage()
    {
        var job = Job.Create("job-regen1", "SYD", "SYD", 30.0, FixedTime).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        job.Reject(new RejectionEntry("pacing", "too fast", DateTime.UtcNow, "op"), FixedTime, "op");
        SetupJobFound(job);

        var command =
            new RegenerateJobCommand(job.JobId, AdditionalContext: Option<string>.Some("Focus on M5 corridor"));

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RejectionFeedback!.ShouldContain("Focus on M5 corridor");
    }

    [Fact]
    public async Task RegenerateJobAsync_WithRejectionReasonAndDescription_IncludesInFeedback()
    {
        var job = Job.Create("job-regen2", "SYD", "SYD", 30.0, FixedTime).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        job.Reject(new RejectionEntry("tone", "wrong tone", DateTime.UtcNow, "op"), FixedTime, "op");
        SetupJobFound(job);

        var command = new RegenerateJobCommand(
            job.JobId,
            RejectionReason: Option<string>.Some("Tone mismatch"),
            RejectionDescription: Option<string>.Some("Should be more upbeat"));

        var result = await _sut.RegenerateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        result.Value.RejectionFeedback!.ShouldContain("Tone mismatch");
        result.Value.RejectionFeedback!.ShouldContain("Should be more upbeat");
    }

    // ──────────────────────────────────────────────
    // CreateJobAsync — explicit voice overrides
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateJobAsync_WithExplicitVoiceOverrides_UsesProvidedValues()
    {
        var command = new CreateJobCommand("BNE", 25.0,
            VoiceId: Option<string>.Some("custom-voice-id"),
            VoiceName: Option<string>.Some("Custom Voice"),
            VoiceModel: Option<string>.Some("eleven_multilingual_v2"),
            VoiceProvider: Option<string>.Some("CustomProvider"));

        var result = await _sut.CreateJobAsync(command);

        result.IsSuccess.ShouldBeTrue();
        var vc = result.Value.VoiceConfig.Value;
        vc.VoiceId.ShouldBe("custom-voice-id");
        vc.VoiceName.ShouldBe("Custom Voice");
        vc.Model.ShouldBe("eleven_multilingual_v2");
        vc.Provider.ShouldBe("CustomProvider");
    }
}

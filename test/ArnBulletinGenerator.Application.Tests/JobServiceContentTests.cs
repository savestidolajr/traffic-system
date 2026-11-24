using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;

namespace ArnBulletinGenerator.Application.Tests;

/// <summary>
///     WS2 / SDD §4.2 behaviors on JobService: operator-curated regeneration selection
///     and the rejection audit contract (reason optional at the trigger, notes optional).
/// </summary>
public sealed class JobServiceContentTests
{
    private readonly IJobRepository _jobRepo = Substitute.For<IJobRepository>();
    private readonly JobService _sut;

    public JobServiceContentTests()
    {
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepo.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var notification = Substitute.For<INotificationService>();
        notification.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Covering a slot ends at the playout drop, so these lifecycle paths reach the
        // delivery service; an unstubbed substitute returns a null Task and throws.
        var delivery = Substitute.For<IFileDeliveryService>();
        delivery.DeliverAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Result<FileDeliveryStatus>.Success(FileDeliveryStatus.Delivered));
        var broadcastOutput = Substitute.For<IBroadcastOutput>();
        broadcastOutput.MarkPublishedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new JobService(_jobRepo, Substitute.For<IScheduleRepository>(), Substitute.For<IQueuePublisher>(), Substitute.For<IModelSelector>(),
            Substitute.For<IVoiceProfileService>(),
            Substitute.For<IVoiceProvider>(),
            notification, delivery, broadcastOutput,
            Substitute.For<IPronunciationDictionaryService>(),
            Substitute.For<IDeliveryStyleService>(),
            Substitute.For<IBookendService>(),
            Substitute.For<IBroadcastMetrics>(),
            Options.Create(new BroadcastOptions { FillerCartAudioUrl = "https://example.com/filler.mp3" }),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()),
                Substitute.For<ILogger<PerVoiceFillerCartResolver>>()),
            Substitute.For<IBulletinFallbackService>(),
            TimeProvider.System,
            Substitute.For<ILogger<JobService>>());
    }

    private Job CreateRejectedJobWithCorridors(params string[] roadNames)
    {
        var job = Job.Create("job-1", "SYD", "sydney", 30.0, DateTime.UtcNow).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        job.Reject(new RejectionEntry("reason", "notes", DateTime.UtcNow, "test"), DateTime.UtcNow, "test");
        job.SetCorridors(roadNames.Select((r, i) =>
            new JobCorridor(r, 2, 50 - i, "congestion", $"Delays on {r}", 1, 2.0, i + 1, true)));
        _jobRepo.GetJobAsync(job.JobId, Arg.Any<CancellationToken>()).Returns(Result<Job>.Success(job));
        return job;
    }

    // ── Curated regeneration (F5) ─────────────────────────────────────

    [Fact]
    public async Task Regenerate_WithValidSelection_CarriesSelectionOnQueueMessage()
    {
        CreateRejectedJobWithCorridors("M4", "M5", "Punt Road");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand("job-1",
            SelectedCorridors: Option<IReadOnlyList<string>>.Some(["Punt Road", "M4"])));

        result.IsSuccess.ShouldBeTrue();
        result.Value.SelectedCorridors.ShouldBe(["Punt Road", "M4"]);
    }

    [Fact]
    public async Task Regenerate_SelectionMatchingIsCaseInsensitiveAndTrimmed()
    {
        CreateRejectedJobWithCorridors("M4");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand("job-1",
            SelectedCorridors: Option<IReadOnlyList<string>>.Some(["  m4  "])));

        result.IsSuccess.ShouldBeTrue();
        result.Value.SelectedCorridors.ShouldBe(["m4"]);
    }

    [Fact]
    public async Task Regenerate_UnknownCorridor_FailsNamingIt()
    {
        CreateRejectedJobWithCorridors("M4");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand("job-1",
            SelectedCorridors: Option<IReadOnlyList<string>>.Some(["M4", "Imaginary Highway"])));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldStartWith("Invalid corridor selection");
        result.Error.ShouldContain("Imaginary Highway");
    }

    [Fact]
    public async Task Regenerate_EmptySelection_Fails()
    {
        CreateRejectedJobWithCorridors("M4");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand("job-1",
            SelectedCorridors: Option<IReadOnlyList<string>>.Some(["  ", ""])));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldStartWith("Invalid corridor selection");
    }

    [Fact]
    public async Task Regenerate_NoSelection_QueueMessageHasNone()
    {
        CreateRejectedJobWithCorridors("M4");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand("job-1"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.SelectedCorridors.ShouldBeNull();
    }

    [Fact]
    public async Task Regenerate_CarriesJobContentSelection()
    {
        var job = CreateRejectedJobWithCorridors("M4");
        job.ContentSelection = Option<BulletinContentSelection>.Some(
            new BulletinContentSelection(true, Weather: true));

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand("job-1"));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Content.ShouldNotBeNull();
        result.Value.Content!.Weather.ShouldBeTrue();
    }

    // ── Rejection audit contract ──────────────────────────────────────────

    [Fact]
    public async Task Reject_StoresAuditTrailWithReasonAndNotes()
    {
        var job = Job.Create("job-2", "SYD", "sydney", 30.0, DateTime.UtcNow).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        _jobRepo.GetJobAsync("job-2", Arg.Any<CancellationToken>()).Returns(Result<Job>.Success(job));

        var result = await _sut.RejectJobAsync(new RejectJobCommand(
            "job-2",
            RejectionReasonCode.Pronunciation,
            "Mispronounced Woolloomooloo",
            "test-operator"));

        result.IsSuccess.ShouldBeTrue();
        job.RejectionHistory.Single().Reason.ShouldBe(RejectionReasonCode.Pronunciation.ToString());
        await _jobRepo.Received().AddLogAsync(
            "job-2",
            Arg.Is<string>(m => m.Contains("Mispronounced Woolloomooloo", StringComparison.Ordinal)),
            JobLogLevel.Warn,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_WithEmptyNotes_SucceedsAndLogsReasonOnly()
    {
        var job = Job.Create("job-3", "SYD", "sydney", 30.0, DateTime.UtcNow).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: DateTime.UtcNow);
        _jobRepo.GetJobAsync("job-3", Arg.Any<CancellationToken>()).Returns(Result<Job>.Success(job));

        var result = await _sut.RejectJobAsync(new RejectJobCommand(
            "job-3",
            RejectionReasonCode.RevisionRequested,
            string.Empty,
            "test-operator"));

        result.IsSuccess.ShouldBeTrue();
        await _jobRepo.Received().AddLogAsync(
            "job-3",
            Arg.Is<string>(m => m.EndsWith("Job rejected: RevisionRequested.", StringComparison.Ordinal)
                || m == "Job rejected: RevisionRequested."),
            JobLogLevel.Warn,
            Arg.Any<CancellationToken>());
    }

}

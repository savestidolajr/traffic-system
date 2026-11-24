using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
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

/// <summary>
///     The batched operator change requests (feedback array) must reach the LLM. The frontend
///     POSTs them to /regenerate; until the wire field existed System.Text.Json silently dropped
///     the unknown member, so an operator instruction only ever reached the pipeline via the reject
///     notes (bug, 2026-08-06). These tests pin the folding into the producer-feedback channel.
/// </summary>
public sealed class JobServiceRegenerateFeedbackTests
{
    private static readonly DateTime FixedTime = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly IJobRepository _jobRepo = Substitute.For<IJobRepository>();
    private readonly JobService _sut;

    public JobServiceRegenerateFeedbackTests()
    {
        _jobRepo.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepo.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new JobService(_jobRepo, Substitute.For<IScheduleRepository>(),
            Substitute.For<IQueuePublisher>(),
            Substitute.For<IModelSelector>(), Substitute.For<IVoiceProfileService>(),
            Substitute.For<IVoiceProvider>(), Substitute.For<INotificationService>(),
            Substitute.For<IFileDeliveryService>(), Substitute.For<IBroadcastOutput>(),
            Substitute.For<IPronunciationDictionaryService>(),
            Substitute.For<IDeliveryStyleService>(), Substitute.For<IBookendService>(),
            Substitute.For<IBroadcastMetrics>(),
            Options.Create(new BroadcastOptions
            { DefaultVoiceId = "default-voice", DefaultLanguage = "en" }),
            new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(),
                Options.Create(new BroadcastOptions()),
                Substitute.For<ILogger<PerVoiceFillerCartResolver>>()),
            Substitute.For<IBulletinFallbackService>(), TimeProvider.System, Substitute.For<ILogger<JobService>>());
    }

    private Job CreateRejectedJob(string id)
    {
        var job = Job.Create(id, "SYD", "SYD", 30.0, FixedTime).Value;
        job.TransitionTo(JobStatus.WaitingApproval, utcNow: FixedTime);
        job.Reject(new RejectionEntry("pacing", "too fast", DateTime.UtcNow, "op"), FixedTime, "op");
        _jobRepo.GetJobAsync(id, Arg.Any<CancellationToken>()).Returns(Result<Job>.Success(job));
        return job;
    }

    [Fact]
    public async Task RegenerateJobAsync_WithFeedbackArray_FoldsItIntoProducerFeedback()
    {
        var job = CreateRejectedJob("job-fb-1");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            Feedback: Option<IReadOnlyList<RegenerateFeedbackDto>>.Some(
            [
                new RegenerateFeedbackDto("instruction", Text: "Drop the weather and lead with the closure")
            ])));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RejectionFeedback!.ShouldContain("Instruction: Drop the weather and lead with the closure");
    }

    [Fact]
    public async Task RegenerateJobAsync_FeedbackWithCategoryAndTarget_UsesTheAnchor()
    {
        var job = CreateRejectedJob("job-fb-2");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            Feedback: Option<IReadOnlyList<RegenerateFeedbackDto>>.Some(
            [
                new RegenerateFeedbackDto("pronunciation", Category: "Pronunciation",
                    Target: "Bermuda Street", Text: "ber-myu-dah")
            ])));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RejectionFeedback!.ShouldContain(
            "Pronunciation (re: \"Bermuda Street\"): ber-myu-dah");
    }

    [Fact]
    public async Task RegenerateJobAsync_FeedbackAndAdditionalContext_CombineBoth()
    {
        var job = CreateRejectedJob("job-fb-3");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            AdditionalContext: Option<string>.Some("Focus on the M5 corridor"),
            Feedback: Option<IReadOnlyList<RegenerateFeedbackDto>>.Some(
            [
                new RegenerateFeedbackDto("instruction", Text: "Re-read it more calmly")
            ])));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RejectionFeedback!.ShouldContain("Focus on the M5 corridor");
        result.Value.RejectionFeedback!.ShouldContain("Re-read it more calmly");
    }

    [Fact]
    public async Task RegenerateJobAsync_WithoutFeedback_PreservesExistingBehaviour()
    {
        // The regression guard for the bug fix: when the array is absent the feedback string is
        // byte-identical to what it was before the field existed.
        var job = CreateRejectedJob("job-fb-4");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            AdditionalContext: Option<string>.Some("Focus on the M5 corridor")));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RejectionFeedback!.ShouldContain("Previous version was rejected: too fast");
        result.Value.RejectionFeedback!.ShouldContain("Additional context: Focus on the M5 corridor");
        result.Value.RejectionFeedback!.ShouldNotContain("Additional context: Additional context:");
    }

    [Fact]
    public async Task RegenerateJobAsync_EmptyFeedbackItems_AreIgnored()
    {
        var job = CreateRejectedJob("job-fb-5");

        var result = await _sut.RegenerateJobAsync(new RegenerateJobCommand(job.JobId,
            Feedback: Option<IReadOnlyList<RegenerateFeedbackDto>>.Some(
            [
                new RegenerateFeedbackDto("instruction", Text: "   ")
            ])));

        result.IsSuccess.ShouldBeTrue();
        result.Value.RejectionFeedback!.ShouldNotContain("Instruction:");
    }
}

using System.Net;
using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class JobLifecycleHttpTriggerTests
{
    private readonly IJobRepository _jobRepository = Substitute.For<IJobRepository>();
    private readonly INotificationService _notificationService = Substitute.For<INotificationService>();
    private readonly IQueuePublisher _queuePublisher = Substitute.For<IQueuePublisher>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();

    private readonly JobLifecycleHttpTrigger _sut;

    public JobLifecycleHttpTriggerTests()
    {
        var modelSelector = Substitute.For<IModelSelector>();
        modelSelector.SelectModelAsync(Arg.Any<string>(), Arg.Any<Option<string>>(), Arg.Any<ModelSelectionUseCase>(),
            Arg.Any<CancellationToken>()).Returns(Task.FromResult("eleven_turbo_v2_5"));

        _notificationService.SendJobNotificationAsync(Arg.Any<Job>(), Arg.Any<NotificationKind?>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _queuePublisher.PublishBroadcastMessageAsync(Arg.Any<BroadcastQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _queuePublisher
            .PublishRegenerationMessageAsync(Arg.Any<RegenerationQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _sut = new JobLifecycleHttpTrigger(_jobRepository,
            new JobService(_jobRepository, Substitute.For<IScheduleRepository>(), _queuePublisher, modelSelector, Substitute.For<IVoiceProfileService>(),
                Substitute.For<IVoiceProvider>(),
                _notificationService,
                Substitute.For<IFileDeliveryService>(), Substitute.For<IBroadcastOutput>(),
                Substitute.For<IPronunciationDictionaryService>(),
                Substitute.For<IDeliveryStyleService>(),
                Substitute.For<IBookendService>(),
                Substitute.For<IBroadcastMetrics>(), Options.Create(new BroadcastOptions()),
                new PerVoiceFillerCartResolver(Substitute.For<IFillerAudioCacheService>(), Options.Create(new BroadcastOptions()),
                    Substitute.For<ILogger<PerVoiceFillerCartResolver>>()),
                Substitute.For<IBulletinFallbackService>(),
                TimeProvider.System,
                Substitute.For<ILogger<JobService>>()),
            _marketAccess,
            Substitute.For<ILogger<JobLifecycleHttpTrigger>>());
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    private static Job TestJob(string id, JobStatus status = JobStatus.WaitingApproval)
    {
        var job = Job.Create(id, "Station1", "Melbourne", 30, DateTime.UtcNow).Value;
        job.SetStatus(status);
        return job;
    }

    private void SetupJob(string id, JobStatus status = JobStatus.WaitingApproval, Action<Job>? configure = null)
    {
        var job = TestJob(id, status);
        configure?.Invoke(job);
        _jobRepository.GetJobAsync(id, Arg.Any<CancellationToken>()).Returns(Result<Job>.Success(job));
        _jobRepository.SaveJobAsync(Arg.Any<Job>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _jobRepository.AddLogAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<JobLogLevel>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }
    private JobLifecycleHttpTrigger CreateQueueEnabledTrigger(IJobService service)
    {
        return new JobLifecycleHttpTrigger(
            _jobRepository,
            service,
            _marketAccess,
            Substitute.For<ILogger<JobLifecycleHttpTrigger>>(),
            Options.Create(new DeliveryQueueOptions { Enabled = true }));
    }

    private void SetupPendingJob(string id)
    {
        SetupJob(id, JobStatus.DeliveryPending, job =>
        {
            job.ETag = "etag-pending";
            job.ScriptText = Option<string>.Some("Traffic update.");
            job.AudioUrl = Option<string>.Some("https://blob.test/job.wav");
            job.DeliveryKind = Option<DeliveryKind>.Some(DeliveryKind.Publish);
            job.DeliveryIdempotencyKey = $"{id}:etag-pending:Publish";
            job.DeliveryReviewedEtag = "etag-pending";
            job.DeliveryRequestedBy = "operator@arn.com.au";
        });
    }


    private void SetupJobNotFound(string id)
    {
        _jobRepository.GetJobAsync(id, Arg.Any<CancellationToken>()).Returns(Result<Job>.Failure("Job not found"));
    }

    // === ApproveJob ===

    [Fact]
    public async Task ApproveJob_Valid_ReturnsOk()
    {
        SetupJob("job-approve", configure: job => job.ETag = "etag");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ApproveJob(
                MockHttpRequestData.CreatePost(new ApproveJobRequest("etag"),
                    "http://localhost/api/jobs/job-approve/approve"),
                "job-approve", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("approved");
    }

    [Fact]
    public async Task ApproveJob_NonExistent_Returns404()
    {
        SetupJobNotFound("missing");
        var resp = await _sut.ApproveJob(
            MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/missing/approve"), "missing", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ApproveJob_MismatchedExpectedEtag_Returns428()
    {
        SetupJob("job-approve-etag", configure: job => job.ETag = "current-etag");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ApproveJob(
                MockHttpRequestData.CreatePost(new ApproveJobRequest("stale-etag"),
                    "http://localhost/api/jobs/job-approve-etag/approve"),
                "job-approve-etag", Ctx()));
        status.ShouldBe((HttpStatusCode)428);
        body.ShouldContain(ErrorMessages.Job.RevisionMismatch);
    }

    [Fact]
    public async Task ApproveJob_MatchingExpectedEtag_ReturnsOk()
    {
        SetupJob("job-approve-etag-ok", configure: job => job.ETag = "current-etag");
        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.ApproveJob(
                MockHttpRequestData.CreatePost(new ApproveJobRequest("current-etag"),
                    "http://localhost/api/jobs/job-approve-etag-ok/approve"),
                "job-approve-etag-ok", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ApproveJob_MissingEtag_Returns428()
    {
        var jobService = Substitute.For<IJobService>();
        jobService.ApproveJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(ErrorMessages.Job.EtagRequired));

        var sut = new JobLifecycleHttpTrigger(_jobRepository, jobService, _marketAccess,
            Substitute.For<ILogger<JobLifecycleHttpTrigger>>());

        SetupJob("job-approve-no-etag");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await sut.ApproveJob(
                MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/job-approve-no-etag/approve"),
                "job-approve-no-etag", Ctx()));
        status.ShouldBe((HttpStatusCode)428);
        body.ShouldContain(ErrorMessages.Job.EtagRequired);
    }

    // === RejectJob ===

    [Fact]
    public async Task RejectJob_WithNotes_ReturnsOk()
    {
        SetupJob("job-reject", JobStatus.WaitingApproval, j => j.ScriptText = Option<string>.Some("Test script"));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.RejectJob(
                MockHttpRequestData.CreatePost(new RejectJobRequest(RejectionReasonCode.RevisionRequested, "Shorten by 20 words"),
                    "http://localhost/api/jobs/job-reject/reject"),
                "job-reject", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("rejected");
    }

    [Fact]
    public async Task RejectJob_MissingNotes_ReturnsOk()
    {
        SetupJob("job-reject", JobStatus.WaitingApproval, j => j.ScriptText = Option<string>.Some("Test script"));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.RejectJob(
                MockHttpRequestData.CreatePost(new RejectJobRequest(RejectionReasonCode.RevisionRequested, ""),
                    "http://localhost/api/jobs/job-reject/reject"),
                "job-reject", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("rejected");
    }

    [Fact]
    public async Task RejectJob_EmptyBody_Returns400()
    {
        var resp = await _sut.RejectJob(
            MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/job-reject/reject"), "job-reject", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectJob_NonExistent_Returns404()
    {
        SetupJobNotFound("missing");
        var resp = await _sut.RejectJob(
            MockHttpRequestData.CreatePost(new RejectJobRequest(RejectionReasonCode.RevisionRequested, "Rework"),
                "http://localhost/api/jobs/missing/reject"),
            "missing", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RejectJob_NotesOverCap_ReturnsTyped400()
    {
        // VC-2: notes are stored verbatim in RejectionHistory AND echoed into LogsJson — both
        // share the job row's 32K-char per-column ceiling, so an unbounded note lets a single
        // rejection wedge the whole row. Oversized notes must be a typed 400, never a 500.
        var notes = new string('x', ConfigValidationLimits.MaxRejectionNotesLength + 1);
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.RejectJob(
                MockHttpRequestData.CreatePost(new RejectJobRequest(RejectionReasonCode.RevisionRequested, notes),
                    "http://localhost/api/jobs/job-reject/reject"),
                "job-reject", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain($"at most {ConfigValidationLimits.MaxRejectionNotesLength} characters");
    }

    [Fact]
    public async Task RejectJobToFallback_NotesOverCap_ReturnsTyped400()
    {
        var notes = new string('x', ConfigValidationLimits.MaxRejectionNotesLength + 1);
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.RejectJobToFallback(
                MockHttpRequestData.CreatePost(new RejectJobRequest(RejectionReasonCode.RevisionRequested, notes),
                    "http://localhost/api/jobs/job-reject-to-fallback/reject-to-fallback"),
                "job-reject-to-fallback", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain($"at most {ConfigValidationLimits.MaxRejectionNotesLength} characters");
    }

    [Fact]
    public async Task RejectJob_MissingReasonAndNotes_ReturnsOk()
    {
        SetupJob("job-reject-bare");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.RejectJob(
                MockHttpRequestData.CreatePost(
                    new RejectJobRequest(),
                    "http://localhost/api/jobs/job-reject-bare/reject"),
                "job-reject-bare", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        // A missing reason defaults to RevisionRequested so audit rows never carry blanks.
        body.ShouldContain("RevisionRequested");
    }

    [Fact]
    public async Task RejectJob_UnknownReasonCode_ReturnsTyped400()
    {
        // An unknown wire value never reaches the reject path: the case-insensitive enum
        // parser refuses it at the boundary, which surfaces as a 400 — never a silent default.
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.RejectJob(
                MockHttpRequestData.CreatePostRaw(
                    """{"reasonCode": "NotARealReason", "notes": "Bad reason value"}""",
                    "http://localhost/api/jobs/job-reject/reject"),
                "job-reject", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Invalid JSON format");
    }
    [Fact]
    public async Task PublishJob_QueueEnabled_ReturnsAcceptedCanonicalPendingJob()
    {
        var service = Substitute.For<IJobService>();
        service.PublishJobAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SetupPendingJob("job-publish-pending");
        var sut = CreateQueueEnabledTrigger(service);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await sut.PublishJob(
                MockHttpRequestData.CreatePost(new PublishJobRequest("etag-pending"),
                    "http://localhost/api/jobs/job-publish-pending/publish"),
                "job-publish-pending", Ctx()));

        status.ShouldBe(HttpStatusCode.Accepted);
        body.ShouldContain("DELIVERY_PENDING");
        body.ShouldContain("etag-pending");
    }
    [Fact]
    public async Task RejectJobToFallback_QueueEnabled_ReturnsAcceptedCanonicalPendingJob()
    {
        var service = Substitute.For<IJobService>();
        service.RejectToFallbackAsync(
                Arg.Is<RejectToFallbackCommand>(command => command.ExpectedEtag == "etag-pending"),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        SetupPendingJob("job-fallback-pending");
        var sut = CreateQueueEnabledTrigger(service);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await sut.RejectJobToFallback(
                MockHttpRequestData.CreatePost(
                    new RejectJobRequest(RejectionReasonCode.RevisionRequested, "cover", "etag-pending"),
                    "http://localhost/api/jobs/job-fallback-pending/reject-to-fallback"),
                "job-fallback-pending", Ctx()));

        status.ShouldBe(HttpStatusCode.Accepted);
        body.ShouldContain("DELIVERY_PENDING");
        body.ShouldContain("etag-pending");
    }



    // === PublishJob ===

    [Fact]
    public async Task PublishJob_Valid_ReturnsOk()
    {
        SetupJob("job-publish", JobStatus.Approved, configure: job => job.ETag = "etag");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.PublishJob(
                MockHttpRequestData.CreatePost(new PublishJobRequest("etag"),
                    "http://localhost/api/jobs/job-publish/publish"),
                "job-publish", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("published");
    }

    [Fact]
    public async Task PublishJob_NonExistent_Returns404()
    {
        SetupJobNotFound("missing");
        var resp = await _sut.PublishJob(
            MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/missing/publish"), "missing", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublishJob_MismatchedExpectedEtag_Returns428()
    {
        SetupJob("job-publish-etag", JobStatus.Approved, configure: job => job.ETag = "current-etag");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.PublishJob(
                MockHttpRequestData.CreatePost(new PublishJobRequest("stale-etag"),
                    "http://localhost/api/jobs/job-publish-etag/publish"),
                "job-publish-etag", Ctx()));
        status.ShouldBe((HttpStatusCode)428);
        body.ShouldContain(ErrorMessages.Job.RevisionMismatch);
    }

    [Fact]
    public async Task PublishJob_MatchingExpectedEtag_ReturnsOk()
    {
        SetupJob("job-publish-etag-ok", JobStatus.Approved, configure: job => job.ETag = "current-etag");
        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.PublishJob(
                MockHttpRequestData.CreatePost(new PublishJobRequest("current-etag"),
                    "http://localhost/api/jobs/job-publish-etag-ok/publish"),
                "job-publish-etag-ok", Ctx()));
        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PublishJob_MissingEtag_Returns428()
    {
        var jobService = Substitute.For<IJobService>();
        jobService.PublishJobAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(ErrorMessages.Job.EtagRequired));

        var sut = new JobLifecycleHttpTrigger(_jobRepository, jobService, _marketAccess,
            Substitute.For<ILogger<JobLifecycleHttpTrigger>>());

        SetupJob("job-publish-no-etag", JobStatus.Approved);
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await sut.PublishJob(
                MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/job-publish-no-etag/publish"),
                "job-publish-no-etag", Ctx()));
        status.ShouldBe((HttpStatusCode)428);
        body.ShouldContain(ErrorMessages.Job.EtagRequired);
    }

    // === DeleteJob ===

    [Fact]
    public async Task DeleteJob_PublishedStatus_Returns409()
    {
        // A bulletin that has already gone to air must not be deletable out from under it.
        // The revision precondition passes (the caller sends the loaded etag), so the status
        // guard answers 409.
        SetupJob("job-published", JobStatus.Published, configure: job => job.ETag = "etag");
        var resp = await _sut.DeleteJob(
            MockHttpRequestData.CreateDeleteRaw("{\"expectedEtag\":\"etag\"}",
                "http://localhost/api/jobs/job-published"),
            "job-published", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await _jobRepository.DidNotReceive().DeleteJobAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteJob_MissingEtag_Returns428()
    {
        // No body means "no revision precondition" — the same refusal approve/publish give,
        // so a delete can never attach to a bulletin the operator did not load (bug-bash
        // 2026-08-05 #12: a blind DELETE 204 won a race against an in-flight approve).
        SetupJob("job-no-etag", JobStatus.Approved);
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.DeleteJob(
                MockHttpRequestData.CreateDelete("http://localhost/api/jobs/job-no-etag"),
                "job-no-etag", Ctx()));
        status.ShouldBe((HttpStatusCode)428);
        body.ShouldContain(ErrorMessages.Job.EtagRequired);
        await _jobRepository.DidNotReceive().DeleteJobAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteJob_StaleEtag_Returns428()
    {
        // The job changed after the caller loaded it (e.g. an approve landed) — deleting the
        // stale revision would destroy the newer bulletin blind.
        SetupJob("job-stale", JobStatus.Approved, configure: job => job.ETag = "current-etag");
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.DeleteJob(
                MockHttpRequestData.CreateDeleteRaw("{\"expectedEtag\":\"stale-etag\"}",
                    "http://localhost/api/jobs/job-stale"),
                "job-stale", Ctx()));
        status.ShouldBe((HttpStatusCode)428);
        body.ShouldContain(ErrorMessages.Job.RevisionMismatch);
        await _jobRepository.DidNotReceive().DeleteJobAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteJob_RejectedStatus_Returns204()
    {
        SetupJob("job-rejected", JobStatus.Rejected, configure: job => job.ETag = "etag");
        _jobRepository.DeleteJobAsync("job-rejected", "etag", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var resp = await _sut.DeleteJob(
            MockHttpRequestData.CreateDeleteRaw("{\"expectedEtag\":\"etag\"}",
                "http://localhost/api/jobs/job-rejected"),
            "job-rejected", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await _jobRepository.Received(1).DeleteJobAsync("job-rejected", "etag",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteJob_LostRace_Returns409()
    {
        // Between the trigger's load (etag matched) and the storage-level delete, another
        // writer bumped the revision — the conditional delete surfaces a conflict, and the
        // operator retries against the fresh revision instead of losing the bulletin.
        SetupJob("job-race", JobStatus.Approved, configure: job => job.ETag = "etag");
        _jobRepository.DeleteJobAsync("job-race", "etag", Arg.Any<CancellationToken>())
            .Returns(Result.Failure(ErrorMessages.Job.Conflict));

        var resp = await _sut.DeleteJob(
            MockHttpRequestData.CreateDeleteRaw("{\"expectedEtag\":\"etag\"}",
                "http://localhost/api/jobs/job-race"),
            "job-race", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteJob_InFlightPipelineStatus_Returns409()
    {
        // Still actively being worked by the pipeline (pre-review) — deleting it out from
        // under the orchestrator would race a background write.
        SetupJob("job-inflight", JobStatus.SynthesizingAudio, configure: job => job.ETag = "etag");
        var resp = await _sut.DeleteJob(
            MockHttpRequestData.CreateDeleteRaw("{\"expectedEtag\":\"etag\"}",
                "http://localhost/api/jobs/job-inflight"),
            "job-inflight", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteJob_NonExistent_Returns404()
    {
        SetupJobNotFound("missing");
        var resp = await _sut.DeleteJob(
            MockHttpRequestData.CreateDelete("http://localhost/api/jobs/missing"), "missing", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteJob_EmptyJobId_Returns400(string jobId)
    {
        var resp = await _sut.DeleteJob(MockHttpRequestData.CreateDelete("http://localhost/api/jobs/x"), jobId,
            Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // === RegenerateJob ===

    [Fact]
    public async Task RegenerateJob_Valid_Returns202()
    {
        SetupJob("job-regen", JobStatus.Rejected, j =>
        {
            j.ScriptText = Option<string>.Some("Old script text");
            j.SetIncidents([new JobIncident("inc-1", 1, "Crash on M1", "M1 Freeway")]);
        });

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.RegenerateJob(
                MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/job-regen/regenerate"), "job-regen",
                Ctx()));
        status.ShouldBe(HttpStatusCode.Accepted);
        body.ShouldContain("regenerated");
    }

    [Fact]
    public async Task RegenerateJob_TonedScriptInBody_ReachesTheQueueMessage()
    {
        // The review UI places the operator's v3 audio tags and posts the tagged read
        // alongside the plain words. If the wire name drifts, the tone is silently dropped
        // and the bulletin airs undirected with nothing to show it happened.
        SetupJob("job-toned", JobStatus.Rejected, j =>
        {
            j.ScriptText = Option<string>.Some("The Monash is closed citybound.");
            j.SetIncidents([new JobIncident("inc-1", 1, "Crash on M1", "M1 Freeway")]);
        });

        var request = MockHttpRequestData.CreatePost(
            new
            {
                editedScript = "The Monash is closed citybound.",
                tonedScript = "[solemn] The Monash is closed citybound.",
                revoiceOnly = true
            },
            "http://localhost/api/jobs/job-toned/regenerate");

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.RegenerateJob(request, "job-toned", Ctx()));
        status.ShouldBe(HttpStatusCode.Accepted);
        await _queuePublisher.Received().PublishRegenerationMessageAsync(
            Arg.Is<RegenerationQueueMessage>(m =>
                m.TonedScript == "[solemn] The Monash is closed citybound."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegenerateJob_MalformedJsonBody_Returns400()
    {
        // A present-but-malformed body must be a client error, not silently swallowed into
        // the "no body supplied" default (which would ignore the operator's actual request).
        SetupJob("job-regen-bad", JobStatus.Rejected);
        var resp = await _sut.RegenerateJob(
            MockHttpRequestData.CreatePostRaw("{ this is not valid json",
                "http://localhost/api/jobs/job-regen-bad/regenerate"),
            "job-regen-bad", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegenerateJob_NonExistent_Returns404()
    {
        // Was wrongly 500: a missing job is a not-found, not a server error.
        SetupJobNotFound("missing");
        var resp = await _sut.RegenerateJob(
            MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/missing/regenerate"), "missing", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegenerateJob_WrongState_Returns409()
    {
        // A job that is neither rejected nor failed cannot be regenerated — a state conflict,
        // not a server error (was wrongly mapped to 500).
        SetupJob("job-published", JobStatus.Published);
        var resp = await _sut.RegenerateJob(
            MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/job-published/regenerate"),
            "job-published", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // === Empty/Whitespace jobId edge cases ===

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApproveJob_EmptyJobId_Returns400(string jobId)
    {
        var resp = await _sut.ApproveJob(MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/x/approve"),
            jobId, Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectJob_EmptyJobId_Returns400(string jobId)
    {
        var resp = await _sut.RejectJob(MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/x/reject"),
            jobId, Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PublishJob_EmptyJobId_Returns400(string jobId)
    {
        var resp = await _sut.PublishJob(MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/x/publish"),
            jobId, Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegenerateJob_EmptyJobId_Returns400(string jobId)
    {
        var resp = await _sut.RegenerateJob(
            MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/x/regenerate"), jobId, Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // === ApproveJob conflict (wrong state) ===

    [Fact]
    public async Task ApproveJob_TerminalState_Returns409()
    {
        SetupJob("job-published", JobStatus.Published, configure: job => job.ETag = "etag");
        var resp = await _sut.ApproveJob(
            MockHttpRequestData.CreatePost(new ApproveJobRequest("etag"),
                "http://localhost/api/jobs/job-published/approve"), "job-published",
            Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // === PublishJob non-"not found" failure ===

    [Fact]
    public async Task PublishJob_WrongState_Returns409()
    {
        SetupJob("job-waiting", configure: job => job.ETag = "etag");
        var resp = await _sut.PublishJob(
            MockHttpRequestData.CreatePost(new PublishJobRequest("etag"),
                "http://localhost/api/jobs/job-waiting/publish"), "job-waiting", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // === RegenerateJob with optional body params ===

    [Fact]
    public async Task RegenerateJob_WithOptionalBodyParams_Returns202()
    {
        SetupJob("job-regen-params", JobStatus.Rejected, j =>
        {
            j.ScriptText = Option<string>.Some("Old script");
            j.SetIncidents([new JobIncident("inc-1", 1, "Crash on M1", "M1 Freeway")]);
        });

        var req = MockHttpRequestData.CreatePost(
            new RegenerateJobRequest("More detail", 45.0, "Too short"),
            "http://localhost/api/jobs/job-regen-params/regenerate");

        (var status, string body) =
            await MockHttpRequestData.ReadResponseAsync(await _sut.RegenerateJob(req, "job-regen-params", Ctx()));
        status.ShouldBe(HttpStatusCode.Accepted);
        body.ShouldContain("regenerated");
    }

    // === RegenerateJob publish failure ===

    [Fact]
    public async Task RegenerateJob_PublishFailure_Returns500()
    {
        SetupJob("job-regen-fail", JobStatus.Rejected, j =>
        {
            j.ScriptText = Option<string>.Some("Old script");
            j.SetIncidents([new JobIncident("inc-1", 1, "Crash on M1", "M1 Freeway")]);
        });

        _queuePublisher
            .PublishRegenerationMessageAsync(Arg.Any<RegenerationQueueMessage>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Queue unavailable"));

        var resp = await _sut.RegenerateJob(
            MockHttpRequestData.CreatePostEmpty("http://localhost/api/jobs/job-regen-fail/regenerate"),
            "job-regen-fail", Ctx());
        resp.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    // ── Authenticated-user resolution (audit actor) ─────────────────────

    [Fact]
    public void GetAuthenticatedUser_PrincipalNameHeader_TakesPrecedence()
    {
        var req = MockHttpRequestData.CreateGet();
        req.Headers.Add("X-MS-CLIENT-PRINCIPAL-NAME", "header@arn.com.au");

        JobLifecycleHttpTrigger.GetAuthenticatedUserForTesting(req).ShouldBe("header@arn.com.au");
    }

    [Fact]
    public void GetAuthenticatedUser_EasyAuthBlob_ReadsNameClaim()
    {
        var req = MockHttpRequestData.CreateGet();
        req.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            """{"auth_typ":"aad","name_typ":"name","claims":[{"typ":"roles","val":"Admin"},{"typ":"name","val":"Local Dev"}]}""")));

        JobLifecycleHttpTrigger.GetAuthenticatedUserForTesting(req).ShouldBe("Local Dev");
    }

    [Fact]
    public void GetAuthenticatedUser_SwaPrincipal_ReadsUserDetails()
    {
        // The SWA linked backend forwards userDetails (not an Easy Auth name claim).
        var req = MockHttpRequestData.CreateGet();
        req.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(
            """{"identityProvider":"aad","userDetails":"operator@arn.com.au","userRoles":["authenticated"]}""")));

        JobLifecycleHttpTrigger.GetAuthenticatedUserForTesting(req).ShouldBe("operator@arn.com.au");
    }

    [Fact]
    public void GetAuthenticatedUser_NoIdentity_ReturnsUnknown()
    {
        JobLifecycleHttpTrigger.GetAuthenticatedUserForTesting(MockHttpRequestData.CreateGet()).ShouldBe("unknown");
    }
}

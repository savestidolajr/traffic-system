using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     HTTP trigger surface for job actions: approve, reject, reject-to-fallback, publish,
///     air-source, delete, and regenerate, market-scoped to the caller's access.
/// </summary>
public sealed class JobLifecycleHttpTrigger(
    IJobRepository jobRepository,
    IJobService jobService,
    IMarketAccessService marketAccess,
    ILogger<JobLifecycleHttpTrigger> logger,
    IOptions<DeliveryQueueOptions>? deliveryQueueOptions = null)
{
    [Function("ApproveJob")]
    public async Task<HttpResponseData> ApproveJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/approve")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbiddenApprove)
            return forbiddenApprove;

        // Body is optional — an absent/empty body means "no revision precondition" (legacy/
        // scripted callers); a present-but-malformed body is a genuine client error (matches
        // the regenerate handler's lenient-body pattern below).
        var parseResult = await HttpRequestParser.ParseAsync<ApproveJobRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure && parseResult.Error != ErrorMessages.Http.RequestBodyRequired)
            return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);
        var approveDto = parseResult.Match(v => v, _ => new ApproveJobRequest());

        logger.LogInformation("Approving job {JobId}", jobId);
        var actor = GetActor(req);
        var result = await jobService.ApproveJobAsync(
            jobId, actor.Name, approveDto.ExpectedEtag, actor.ObjectId, ctx.CancellationToken);
        if (result.IsFailure)
        {
            if (result.Error is ErrorMessages.Job.EtagRequired or ErrorMessages.Job.RevisionMismatch)
                return await HttpResponseHelper.PreconditionRequiredAsync(req, result.Error);
            return await HttpResponseHelper.MapFailureAsync(req, result.Error, HttpResponseHelper.ConflictAsync);
        }

        return await HttpResponseHelper.OkJsonAsync(req,
            new JobActionResponseDto("approved", jobId, "Job approved successfully"));
    }
    [Function("ProceedCurationJob")]
    public async Task<HttpResponseData> ProceedCurationJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/proceed")]
        HttpRequestData req, string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);
        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        var parseResult = await HttpRequestParser.ParseAsync<ProceedCurationRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure && parseResult.Error != ErrorMessages.Http.RequestBodyRequired)
            return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);
        var payload = parseResult.Match(v => v, _ => new ProceedCurationRequest());
        var actor = GetActor(req);
        var result = await jobService.ProceedPastCurationAsync(
            jobId, actor.Name, payload.ExpectedEtag, payload.EditedIncidents, ctx.CancellationToken);
        if (result.IsFailure)
        {
            if (result.Error is ErrorMessages.Job.EtagRequired or ErrorMessages.Job.RevisionMismatch)
                return await HttpResponseHelper.PreconditionRequiredAsync(req, result.Error);
            return await HttpResponseHelper.MapFailureAsync(req, result.Error, HttpResponseHelper.ConflictAsync);
        }

        return await HttpResponseHelper.OkJsonAsync(req,
            new JobActionResponseDto("proceeded", jobId, "Curation proceeded and generation queued"));
    }

    [Function("RejectJob")]
    public async Task<HttpResponseData> RejectJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/reject")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbiddenReject)
            return forbiddenReject;

        logger.LogInformation("Rejecting job {JobId}", jobId);
        var parseResult = await HttpRequestParser.ParseAsync<RejectJobRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);

        var payload = parseResult.Value;
        string? validationError = ValidateRejection(payload);
        if (validationError is not null)
            return await HttpResponseHelper.BadRequestAsync(req, validationError);

        var reasonCode = payload.ReasonCode ?? RejectionReasonCode.RevisionRequested;
        var command = new RejectJobCommand(
            jobId, reasonCode, payload.Notes?.Trim() ?? string.Empty, GetAuthenticatedUser(req));
        var result = await jobService.RejectJobAsync(command, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(req, result.Error, HttpResponseHelper.ConflictAsync);

        return await HttpResponseHelper.OkJsonAsync(req,
            new JobActionResponseDto("rejected", jobId, $"Job rejected: {reasonCode}"));
    }

    [Function("RejectJobToFallback")]
    public async Task<HttpResponseData> RejectJobToFallback(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/reject-to-fallback")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbiddenFallback)
            return forbiddenFallback;

        logger.LogInformation("Rejecting job {JobId} to fallback", jobId);
        var parseResult = await HttpRequestParser.ParseAsync<RejectJobRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);

        var payload = parseResult.Value;
        string? validationError = ValidateRejection(payload);
        if (validationError is not null)
            return await HttpResponseHelper.BadRequestAsync(req, validationError);
        var command = new RejectToFallbackCommand(
            jobId,
            payload.ReasonCode ?? RejectionReasonCode.RevisionRequested,
            payload.Notes?.Trim() ?? string.Empty,
            GetAuthenticatedUser(req),
            payload.ExpectedEtag);

        var result = await jobService.RejectToFallbackAsync(command, ctx.CancellationToken);
        if (result.IsFailure)
        {
            if (result.Error is ErrorMessages.Job.EtagRequired or ErrorMessages.Job.RevisionMismatch)
                return await HttpResponseHelper.PreconditionRequiredAsync(req, result.Error);
            return await HttpResponseHelper.MapFailureAsync(req, result.Error, HttpResponseHelper.ConflictAsync);
        }

        if (deliveryQueueOptions?.Value.Enabled == true)
            return await DeliveryPendingAcceptedAsync(req, jobId, ctx.CancellationToken);
        return await HttpResponseHelper.OkJsonAsync(req,
            new JobActionResponseDto("fallback-applied", jobId,
                "Bulletin rejected; fallback applied to cover the slot."));
    }

    [Function("PublishJob")]
    public async Task<HttpResponseData> PublishJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/publish")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbiddenPublish)
            return forbiddenPublish;

        // Publishing requires an ETag when delivery is queue-backed, but that precondition must
        // never hide the more fundamental result that the requested job does not exist. Resolve
        // existence at the HTTP boundary before the service can enforce its ETag guard.
        var existingJob = await jobRepository.GetJobAsync(jobId, ctx.CancellationToken);
        if (existingJob.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, existingJob.Error);

        // Body is optional — an absent/empty body means "no revision precondition" (legacy/
        // scripted callers); a present-but-malformed body is a genuine client error (matches
        // the regenerate handler's lenient-body pattern below).
        var parseResult = await HttpRequestParser.ParseAsync<PublishJobRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure && parseResult.Error != ErrorMessages.Http.RequestBodyRequired)
            return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);
        var publishDto = parseResult.Match(v => v, _ => new PublishJobRequest());
        var actor = GetActor(req);

        var result = await jobService.PublishJobAsync(
            jobId, actor.Name, publishDto.ExpectedEtag, actor.ObjectId, ctx.CancellationToken);
        if (result.IsFailure)
        {
            if (result.Error is ErrorMessages.Job.EtagRequired or ErrorMessages.Job.RevisionMismatch)
                return await HttpResponseHelper.PreconditionRequiredAsync(req, result.Error);
            return await HttpResponseHelper.MapFailureAsync(req, result.Error, HttpResponseHelper.ConflictAsync);
        }
        if (deliveryQueueOptions?.Value.Enabled == true)
            return await DeliveryPendingAcceptedAsync(req, jobId, ctx.CancellationToken);
        return await HttpResponseHelper.OkJsonAsync(req,
            new JobActionResponseDto("published", jobId, "Job published and queued for broadcasting"));

    }
    [Function("SetJobAirSource")]
    public async Task<HttpResponseData> SetJobAirSource(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/air-source")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbiddenAirSource)
            return forbiddenAirSource;

        logger.LogInformation("Setting air source for job {JobId}", jobId);

        // The body is REQUIRED here (unlike approve/publish, which tolerate legacy bodiless
        // callers): there is no sensible default for "what should air" — the operator must name
        // the source, so a missing body is a genuine client error.
        var parseResult = await HttpRequestParser.ParseAsync<SetAirSourceRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);
        var payload = parseResult.Value;

        // The wire values are exactly "repeat" and "cart" — anything else is a 400 naming the
        // accepted values, never a silent fallback to one of them.
        AirSourceKind kind;
        string kindToken = payload.Kind?.Trim() ?? string.Empty;
        if (kindToken.Equals("repeat", StringComparison.OrdinalIgnoreCase))
        {
            kind = AirSourceKind.Repeat;
        }
        else if (kindToken.Equals("cart", StringComparison.OrdinalIgnoreCase))
        {
            kind = AirSourceKind.Cart;
        }
        else
        {
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Invalid 'kind' '{payload.Kind}': accepted values are \"repeat\" and \"cart\".");
        }

        var command = new SetAirSourceCommand(
            jobId, kind, payload.SourceJobId, GetAuthenticatedUser(req), payload.ExpectedEtag);
        var result = await jobService.SetAirSourceAsync(command, ctx.CancellationToken);
        if (result.IsFailure)
        {
            // Same revision-precondition mapping as approve/publish: missing or stale ETag → 428.
            if (result.Error is ErrorMessages.Job.EtagRequired or ErrorMessages.Job.RevisionMismatch)
                return await HttpResponseHelper.PreconditionRequiredAsync(req, result.Error);
            return await HttpResponseHelper.MapFailureAsync(req, result.Error, HttpResponseHelper.ConflictAsync);
        }

        string message = kind == AirSourceKind.Repeat
            ? $"This slot will air a repeat of bulletin {payload.SourceJobId}."
            : "This slot will air the standby cart.";
        return await HttpResponseHelper.OkJsonAsync(req,
            new JobActionResponseDto("air-source-set", jobId, message));
    }

    [Function("DeleteJob")]
    public async Task<HttpResponseData> DeleteJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "jobs/{jobId}")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        // Every other lifecycle action scopes to the caller's markets; delete was the one that did
        // not. A no-op today because DELETE resolves to Admin in RouteRoleMatrix and admins are
        // unrestricted — but the guard belongs here, not in the role table's current shape.
        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbiddenDelete)
            return forbiddenDelete;

        logger.LogInformation("Deleting job {JobId}", jobId);

        // Body is optional — an absent/empty body means "no revision precondition" (legacy/
        // scripted callers), which the precondition below refuses with 428; a
        // present-but-malformed body is a genuine client error (matches the approve/publish
        // lenient-body pattern).
        var parseResult = await HttpRequestParser.ParseAsync<DeleteJobRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure && parseResult.Error != ErrorMessages.Http.RequestBodyRequired)
            return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);
        var deleteDto = parseResult.Match(v => v, _ => new DeleteJobRequest());

        var jobResult = await jobRepository.GetJobAsync(jobId, ctx.CancellationToken);
        if (jobResult.IsFailure)
            return await HttpResponseHelper.NotFoundAsync(req, jobResult.Error);

        // Revision precondition, checked BEFORE the status guard (mirrors approve/publish): the
        // deletion must attach to the exact revision the operator reviewed. Missing or stale →
        // 428, and the delete never proceeds — a just-approved bulletin cannot be destroyed blind
        // by a delete issued against an older revision (observed live 2026-08-05: DELETE 204 won
        // a race against an in-flight approve). The storage-level delete below additionally
        // carries the etag, so even a race between this check and the delete surfaces a conflict.
        if (string.IsNullOrEmpty(deleteDto.ExpectedEtag))
            return await HttpResponseHelper.PreconditionRequiredAsync(req, ErrorMessages.Job.EtagRequired);
        if (!string.Equals(deleteDto.ExpectedEtag, jobResult.Value.ETag, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Delete rejected for job {JobId}: revision mismatch (expected ETag {ExpectedEtag}, actual {ActualEtag})",
                jobId, deleteDto.ExpectedEtag, jobResult.Value.ETag);
            return await HttpResponseHelper.PreconditionRequiredAsync(req, ErrorMessages.Job.RevisionMismatch);
        }

        if (!IsDeletable(jobResult.Value.Status))
            return await HttpResponseHelper.ConflictAsync(req,
                $"Cannot delete job in status: {jobResult.Value.Status}. " +
                "Only jobs awaiting a decision, approved, rejected, fallback-covered, or failed " +
                "may be deleted; a published bulletin is a delivery record and is kept.");

        var result = await jobRepository.DeleteJobAsync(jobId, deleteDto.ExpectedEtag, ctx.CancellationToken);
        if (result.IsFailure)
        {
            // A lost race (the row changed between this trigger's load and the conditional
            // delete) is retryable and MUST be a 409 — the conflict message carries no literal
            // "conflict" text, so MapFailureAsync alone would fall through to a 500.
            if (result.Error == ErrorMessages.Job.Conflict)
                return await HttpResponseHelper.ConflictAsync(req, result.Error);
            return await HttpResponseHelper.MapFailureAsync(req, result.Error,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));
        }

        return HttpResponseHelper.NoContent(req);
    }

    // Mirrors the other lifecycle guards (Approve/Reject/Publish): a job that has already gone
    // to air (Published) or is still being actively worked by the pipeline (the
    // pre-review stages) must not be deleted out from under it. Anything that has reached a
    // decision point (waiting/approved) or a settled outcome (rejected/fallback/failed) is safe
    // to discard.
    private static bool IsDeletable(JobStatus status) =>
        status is JobStatus.WaitingApproval or JobStatus.Approved or JobStatus.Rejected
            or JobStatus.FallbackUsed or JobStatus.Failed;

    [Function("RegenerateJob")]
    public async Task<HttpResponseData> RegenerateJob(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "jobs/{jobId}/regenerate")]
        HttpRequestData req,
        string jobId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.JobIdRequired);

        if (await marketAccess.EnsureJobAccessAsync(req, jobId, ctx.CancellationToken) is { } forbiddenRegenerate)
            return forbiddenRegenerate;

        logger.LogInformation("Regenerating job {JobId}", jobId);
        var ct = ctx.CancellationToken;

        // Body is optional for regeneration — an absent/empty body means "re-run with existing
        // settings"; a present-but-malformed body is a genuine client error and must not be
        // silently swallowed into that same default (matches reject/reject-to-fallback below).
        var parseResult = await HttpRequestParser.ParseAsync<RegenerateJobRequest>(req, ct);
        if (parseResult.IsFailure && parseResult.Error != ErrorMessages.Http.RequestBodyRequired)
            return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);
        var dto = parseResult.Match(v => v, _ => new RegenerateJobRequest());

        // Per-incident picks: only the operator's EXCLUSIONS (Included=false) survive a regenerate
        // — they drop the incident's HERE id from the next fresh fetch (cleared ones already gone).
        var excludedIncidentKeys = (dto.EditedIncidents ?? [])
            .Where(e => !e.Included && !string.IsNullOrWhiteSpace(e.IncidentId))
            .Select(e => e.IncidentId.Trim())
            .ToList();

        var command = new RegenerateJobCommand(
            jobId,
            Option.OfNullable(dto.TargetDurationSec),
            Option.OfWhitespace(dto.Context),
            Option.OfWhitespace(dto.RejectionReason),
            Option.OfWhitespace(dto.RejectionDescription),
            Option.OfWhitespace(dto.EditedScript),
            dto.SelectedCorridors is { Count: > 0 }
                ? Option<IReadOnlyList<string>>.Some(dto.SelectedCorridors)
                : Option<IReadOnlyList<string>>.None,
            Option.OfWhitespace(dto.DeliveryIntent),
            Option.OfWhitespace(dto.TonedScript),
            dto.SelectedGoodRuns is { Count: > 0 }
                ? Option<IReadOnlyList<string>>.Some(dto.SelectedGoodRuns)
                : Option<IReadOnlyList<string>>.None,
            excludedIncidentKeys.Count > 0
                ? Option<IReadOnlyList<string>>.Some(excludedIncidentKeys)
                : Option<IReadOnlyList<string>>.None,
            dto.RevoiceOnly,
            dto.RefreshPronunciation,
            dto.RefreshDelivery,
            dto.Feedback is { Count: > 0 }
                ? Option<IReadOnlyList<RegenerateFeedbackDto>>.Some(dto.Feedback)
                : Option<IReadOnlyList<RegenerateFeedbackDto>>.None,
            Option.OfWhitespace(dto.VoiceProfileId));

        var result = await jobService.RegenerateJobAsync(command, GetAuthenticatedUser(req), ct);
        if (result.IsFailure)
        {
            // An unresolvable voice override is request validation — the operator named a voice
            // profile that cannot be resolved (mirrors CreateJobAsync's hard-fail contract).
            // This MUST be checked before the generic "not found" mapping below: the profile
            // service's own message contains "not found" (e.g. "Voice profile 'vp-x' not found."),
            // which would otherwise surface as a 404 about the JOB and tell a client the job id
            // was wrong, when the request was simply malformed.
            if (result.Error.Contains("could not be resolved", StringComparison.OrdinalIgnoreCase))
                return await HttpResponseHelper.BadRequestAsync(req, result.Error);
            if (result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return await HttpResponseHelper.NotFoundAsync(req, result.Error);
            if (result.Error == ErrorMessages.Job.CannotRegenerateFromStatus)
                return await HttpResponseHelper.ConflictAsync(req, result.Error);
            // A lost race is retryable and MUST NOT be a 400: that tells a client its request was
            // malformed and should not be repeated, which is the opposite of the required action.
            // Approve/publish already answer 409 for the same condition.
            if (result.Error == ErrorMessages.Job.Conflict)
                return await HttpResponseHelper.ConflictAsync(req, result.Error);
            // Remaining failures are request validation (corridor selection) — never server errors.
            return await HttpResponseHelper.BadRequestAsync(req, result.Error);
        }

        var publishResult = await jobService.PublishRegenerationJobAsync(jobId, result.Value, ct);
        if (publishResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, "Failed to queue regeneration for processing.");

        logger.LogInformation("Queued regeneration for JobId: {JobId}", jobId);

        return await HttpResponseHelper.AcceptedJsonAsync(req,
            new JobActionResponseDto("regenerated", jobId, "Job regeneration has been queued for processing."));
    }

    private async Task<HttpResponseData> DeliveryPendingAcceptedAsync(
        HttpRequestData req, string jobId, CancellationToken cancellationToken)
    {
        var loaded = await jobRepository.GetJobAsync(jobId, cancellationToken);
        if (loaded.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, loaded.Error);
        var job = loaded.Value;
        var dto = job.ToDetailDto(
            job.AudioUrl.GetValueOr(string.Empty),
            job.SupersededTake.Match(t => t.AudioUrl.GetValueOr(string.Empty), () => string.Empty));
        return await HttpResponseHelper.AcceptedJsonAsync(req, dto);
    }

    private static string? ValidateRejection(RejectJobRequest payload)
    {
        if (payload.ReasonCode.HasValue && !Enum.IsDefined(payload.ReasonCode.Value))
            return "Rejection reason is invalid.";

        if (payload.Notes is { Length: > ConfigValidationLimits.MaxRejectionNotesLength })
            return $"Rejection notes must be at most {ConfigValidationLimits.MaxRejectionNotesLength} characters "
                   + $"(found {payload.Notes.Length}).";

        return null;
    }

    /// <summary>
    ///     Resolves the authenticated user's identity for audit stamping. Prefers the
    ///     Easy Auth convenience header X-MS-CLIENT-PRINCIPAL-NAME; otherwise parses the
    ///     X-MS-CLIENT-PRINCIPAL blob via ClientPrincipal (Easy Auth name claim or the
    ///     SWA-forwarded userDetails). Returns "unknown" when neither is present —
    ///     audit entries must never be empty.
    /// </summary>
    private static string GetAuthenticatedUser(HttpRequestData req)
    {
        // Easy Auth injects the convenience header; the SWA proxy does not, so fall back
        // to the principal blob (ClientPrincipal handles both the Easy Auth name claim and
        // the SWA userDetails). Audit entries must carry the actor, never be empty.
        if (req.Headers.TryGetValues("X-MS-CLIENT-PRINCIPAL-NAME", out var headerValues) &&
            headerValues.FirstOrDefault() is { Length: > 0 } headerName)
            return headerName;

        if (ClientPrincipal.Parse(req)?.Name is { Length: > 0 } principalName)
            return principalName;

        return "unknown";
    }

    internal static string GetAuthenticatedUserForTesting(HttpRequestData req)
        => GetAuthenticatedUser(req);

    /// <summary>
    ///     Resolves both the authenticated user's display name (see <see cref="GetAuthenticatedUser"/>)
    ///     and their immutable Entra object id, for audit fields (<see cref="Job.ApprovedByObjectId"/>,
    ///     <see cref="Job.PublishedByObjectId"/>) that must survive a display-name change. Mirrors
    ///     <see cref="ConfigTransferHttpTrigger"/>'s Actor helper. ObjectId is empty when no
    ///     principal is present (e.g. anonymous/dev access).
    /// </summary>
    private static (string Name, string ObjectId) GetActor(HttpRequestData req)
    {
        var principal = ClientPrincipal.Parse(req);
        return (GetAuthenticatedUser(req), principal?.ObjectId ?? string.Empty);
    }
}

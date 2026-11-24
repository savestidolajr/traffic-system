using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Constants;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Messages;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Metrics;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Application.Jobs;

/// <summary>
///     Application service for job lifecycle operations.
///     Handles approve, reject, publish, regenerate, and escalation workflows.
/// </summary>
public sealed class JobService(
    IJobRepository jobRepository,
    IScheduleRepository scheduleRepository,
    IQueuePublisher queuePublisher,
    IModelSelector modelSelector,
    IVoiceProfileService voiceProfileService,
    IVoiceProvider voiceProvider,
    INotificationService notificationService,
    IFileDeliveryService fileDeliveryService,
    IBroadcastOutput broadcastOutput,
    IPronunciationDictionaryService pronunciationDictionaryService,
    IDeliveryStyleService deliveryStyleService,
    IBookendService bookendService,
    IBroadcastMetrics metrics,
    IOptions<BroadcastOptions> broadcastOptions,
    PerVoiceFillerCartResolver perVoiceFillerCart,
    IBulletinFallbackService bulletinFallbackService,
    TimeProvider timeProvider,
    ILogger<JobService> logger,
    IOptions<DeliveryQueueOptions>? deliveryQueueOptions = null) : IJobService, IJobDeliveryProcessor
{
    private readonly BroadcastOptions _options = broadcastOptions.Value;
    private readonly DeliveryQueueOptions _deliveryQueueOptions =
        deliveryQueueOptions?.Value ?? new();

    /// <summary>
    ///     Approves a job, transitioning it from WaitingApproval to Approved.
    /// </summary>
    public async Task<Result> ApproveJobAsync(string jobId, string by = "unknown", string? expectedEtag = null,
        string? byObjectId = null, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Approving job {JobId}", jobId);
        var jobResult = await GetJobOrFailAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result.Failure(jobResult.Error);

        var job = jobResult.Value;

        if (string.IsNullOrEmpty(expectedEtag))
            return Result.Failure(ErrorMessages.Job.EtagRequired);

        if (!string.Equals(expectedEtag, job.ETag, StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Approve rejected for job {JobId}: revision mismatch (expected ETag {ExpectedEtag}, actual {ActualEtag})",
                jobId, expectedEtag, job.ETag);
            return Result.Failure(ErrorMessages.Job.RevisionMismatch);
        }

        if (job.Status != JobStatus.WaitingApproval && job.Status != JobStatus.FallbackUsed)
            // Two distinct reasons wore the same words: a bulletin still generating has not reached
            // review, but a Published/Rejected/Failed one plainly HAS finished — telling an operator
            // a bulletin that already aired "has not finished generating" is simply wrong.
            return Result.Failure(string.Format(
                IsSettled(job.Status)
                    ? ErrorMessages.Job.CannotApproveSettled
                    : ErrorMessages.Job.OnlyWaitingCanBeApproved,
                job.Status));

        var approveResult = job.Approve(timeProvider.GetUtcNow().UtcDateTime, by, byObjectId: byObjectId);
        if (approveResult.IsFailure)
            return approveResult;

        var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveResult.IsFailure)
            return saveResult;

        // Name the actor. The deadline sweep signs a covered slot off as
        // "system (deadline)", and a log line reading "approved by operator" for it told
        // the audit trail a person had been there when nobody had.
        await jobRepository.AddLogAsync(jobId, $"Job approved by {by}", JobLogLevel.Info, cancellationToken);

        return Result.Success();
    }
    public async Task<Result> ProceedPastCurationAsync(string jobId, string by = "unknown",
        string? expectedEtag = null, IReadOnlyList<EditedIncidentDto>? editedIncidents = null,
        CancellationToken cancellationToken = default)
    {
        var jobResult = await GetJobOrFailAsync(jobId, cancellationToken);
        if (jobResult.IsFailure) return Result.Failure(jobResult.Error);
        var job = jobResult.Value;
        if (string.IsNullOrEmpty(expectedEtag))
            return Result.Failure(ErrorMessages.Job.EtagRequired);
        if (!string.Equals(expectedEtag, job.ETag, StringComparison.Ordinal))
            return Result.Failure(ErrorMessages.Job.RevisionMismatch);
        if (job.Status != JobStatus.AwaitingCuration)
            return Result.Failure($"Job is not awaiting curation: {job.Status}");
        if (!job.VoiceConfig.HasValue)
            return Result.Failure("Job has no resolved voice configuration.");
        if (job.GeneratorInput is not null && editedIncidents is { Count: > 0 })
            job.GeneratorInput = job.GeneratorInput with { EditedIncidents = editedIncidents };

        var voice = job.VoiceConfig.Value;
        job.TransitionTo(JobStatus.IncidentsSelected, by,
            Option<string>.Some("Proceeded by operator"), timeProvider.GetUtcNow().UtcDateTime);
        var save = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (save.IsFailure) return save;

        var message = new BroadcastQueueMessage(
            job.JobId, job.Market, job.Market, (int)job.TargetDurationSeconds,
            job.Latitude.GetValueOr(0), job.Longitude.GetValueOr(0), job.RadiusKm.GetValueOr(1),
            voice.VoiceId, Model: voice.Model,
            VoiceSettings: voice.Settings.Match(s => (VoiceSettings?)s, () => null),
            TimezoneId: job.TimezoneId.Match(v => (string?)v, () => null),
            Coverage: job.Coverage.Match(c => (CoverageArea?)c, () => null),
            Content: job.ContentSelection.Match<BulletinContentSelection?>(c => c, () => null),
            PaceBoostWpm: voice.PaceBoostWpm,
            StationId: job.Station,
            Phase: Phase.Generate);
        var publish = await queuePublisher.PublishBroadcastMessageAsync(message, cancellationToken);
        if (publish.IsFailure) return publish;
        await jobRepository.AddLogAsync(jobId, $"Curation proceeded by {by}", JobLogLevel.Info, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    ///     Escalates a job that has not been approved within the allowed window.
    ///     Deduplicated PER REASON so the two alert tiers (approval reminder at
    ///     NotifyBeforePlayoutMinutes, urgent at EscalationBeforePlayoutMinutes) each fire
    ///     exactly once — callers pass a stable reason token.
    /// </summary>
    public async Task<Result> EscalateJobAsync(string jobId, string reason,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Escalating job {JobId}: {Reason}", jobId, reason);
        var jobResult = await GetJobOrFailAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result.Failure(jobResult.Error);

        var job = jobResult.Value;
        if (job.Status != JobStatus.WaitingApproval)
            return Result.Failure(ErrorMessages.Job.OnlyWaitingCanEscalate);

        if (job.Logs.Any(l => l.Message.StartsWith($"Job escalated: {reason}", StringComparison.Ordinal)))
        {
            logger.LogInformation("Job {JobId} already escalated ({Reason}) — skipping duplicate notification",
                jobId, reason);
            return Result.Success();
        }

        await jobRepository.AddLogAsync(jobId, $"Job escalated: {reason}", JobLogLevel.Warn, cancellationToken);

        var tier = reason.Contains("reminder", StringComparison.OrdinalIgnoreCase)
            ? NotificationKind.ApprovalReminder
            : NotificationKind.UrgentApproval;
        await SendNotificationSafeAsync(job, tier, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    ///     Creates a new job from a validated orchestration request.
    /// </summary>
    public async Task<Result<Job>> CreateJobAsync(CreateJobCommand command,
        CancellationToken cancellationToken = default)
    {
        string jobId = Guid.NewGuid().ToString("N");

        // A trigger that names a voice profile gets the profile's CURRENT tuning resolved
        // server-side — the same resolution the schedule poller performs — so per-profile
        // behaviour with no per-request field (PaceBoostWpm) cannot silently drop off the
        // ad-hoc path (observed live 2026-07-22: a Trigger Studio job ignored a +40 boost).
        // Explicit request fields still win; the profile only fills what the caller omitted.
        // A named profile that cannot be resolved is a HARD failure: the previous fail-soft
        // continued with the configured default voice, so a typo'd or archived profile aired
        // an unassigned voice instead of telling the caller.
        var paceBoostWpm = 0d;
        var profileVoice = Option<(string Id, string Name, string Model, VoiceSettings Settings)>.None;
        string? requestedProfileId = command.VoiceProfileId.Match(v => (string?)v, () => null);
        int? requestedProfileVersion = null;
        if (!string.IsNullOrWhiteSpace(requestedProfileId))
        {
            var profileResult = await voiceProfileService.GetAsync(requestedProfileId, cancellationToken);
            if (profileResult.IsFailure || profileResult.Value.Versions.Count == 0)
            {
                // TrimEnd: the repository's own error already ends in a period ("… not found."),
                // which read as "not found.." on the wire (dev, 2026-08-07).
                string reason = (profileResult.IsFailure ? profileResult.Error : "it has no versions").TrimEnd('.');
                logger.LogWarning("Trigger named voice profile {ProfileId} but it could not be resolved ({Error}).",
                    requestedProfileId, reason);
                return Result<Job>.Failure($"Voice profile '{requestedProfileId}' could not be resolved: {reason}.");
            }

            var profile = profileResult.Value;
            var current = profile.Versions[^1];
            paceBoostWpm = current.PaceBoostWpm;
            requestedProfileVersion = current.Version;
            profileVoice = Option<(string, string, string, VoiceSettings)>.Some(
                (profile.BaseVoiceId, profile.Name, current.Model, current.Settings));
        }

        // No configured-default fallback. A job with no voice is not a job we can air, and the
        // old default silently paired a hardcoded display name with a voice id that nobody had
        // assigned — the name drifted out of sync with the provider and every such bulletin was
        // mislabelled. Callers must name a voice; POST /api/broadcast rejects a request without
        // one before it reaches here, and this guard covers every other caller.
        var resolvedVoiceId = command.VoiceId.Or(profileVoice.Map(p => p.Id));
        if (resolvedVoiceId.IsNone)
            return Result<Job>.Failure(
                "A voice is required: supply VoiceProfileId (preferred, resolves the profile's full tuning) or VoiceId.");
        string voiceId = resolvedVoiceId.Value;

        string provider = command.VoiceProvider.GetValueOr(ProviderNames.ElevenLabs);
        string voiceModel = command.VoiceModel.HasValue
            ? command.VoiceModel.Value
            : await profileVoice.Match(
                p => Task.FromResult(p.Model),
                () => modelSelector.SelectModelAsync(voiceId, Option<string>.Some(_options.DefaultLanguage),
                    cancellationToken: cancellationToken));
        var voiceSettings = command.VoiceSettings.Match(
            s => Option<VoiceSettings>.Some(s),
            () => profileVoice.Match(p => Option<VoiceSettings>.Some(p.Settings), () => Option<VoiceSettings>.None));

        string voiceName = await ResolveVoiceNameAsync(voiceId, profileVoice.Map(p => p.Name), command.VoiceName,
            cancellationToken);
        var marketResolution = await ResolveMarketAsync(command.Location, cancellationToken);
        // The job's Station must be the SCHEDULE's station name whenever one backs the market —
        // the same value the schedule poller writes — because station-keyed corpora (future
        // lessons) are read back by schedule station. Raw location text is the unmatched fallback.
        var jobResult = Job.Create(jobId, marketResolution.Station ?? command.Location,
            marketResolution.MarketId, command.TargetDurationSeconds,
            timeProvider.GetUtcNow().UtcDateTime,
            command.Latitude, command.Longitude, command.RadiusKm, command.TimezoneId, command.Coverage);
        if (jobResult.IsFailure)
            return jobResult;

        var job = jobResult.Value;
        job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
            provider,
            voiceId,
            voiceName,
            voiceModel,
            voiceSettings,
            paceBoostWpm));
        // Voice profile provenance pins (parity with the regenerate override): which profile's
        // CURRENT tuning produced this VoiceConfig. None for a bare VoiceId request — the voice
        // was resolved another way and a pin would be a fiction.
        if (requestedProfileId is not null && requestedProfileVersion is not null)
        {
            job.VoiceProfileId = Option<string>.Some(requestedProfileId);
            job.VoiceProfileVersion = Option<int>.Some(requestedProfileVersion.Value);
        }
        job.ContentSelection = command.ContentSelection;

        var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveResult.IsFailure)
            return Result<Job>.Failure(saveResult.Error);

        logger.LogInformation("Created job {JobId} for location {Location}", job.JobId, command.Location);

        return Result<Job>.Success(job);
    }

    /// <summary>
    ///     Resolves the voice's DISPLAY name, which is never taken on trust from the caller when
    ///     a better source exists. Precedence: the named voice profile, then the TTS provider's
    ///     own name for the voice id, then the caller-supplied name, then the voice id itself.
    ///     The old path stamped a hardcoded <c>Broadcast:DefaultVoiceName</c> onto the job; that
    ///     string was a snapshot of a provider-side label that later changed, so every default
    ///     bulletin was recorded, logged and shown to operators under a voice name that existed
    ///     nowhere but config (dev, 2026-06-16 to 2026-07-29: "ARN Hudson" for a voice ElevenLabs
    ///     calls "Peter Michael - Australian Male VO"). Resolving from the provider means the
    ///     label cannot drift again. Fail-soft: a provider outage falls through to the
    ///     caller-supplied name, then the id, so naming never blocks a broadcast.
    /// </summary>
    private async Task<string> ResolveVoiceNameAsync(string voiceId, Option<string> profileName,
        Option<string> requestedName, CancellationToken cancellationToken)
    {
        if (profileName.HasValue)
            return profileName.Value;

        var voicesResult = await voiceProvider.GetVoicesAsync(cancellationToken);
        if (voicesResult.IsSuccess)
        {
            var match = voicesResult.Value.FirstOrDefault(v =>
                string.Equals(v.VoiceId, voiceId, StringComparison.Ordinal));
            if (match is not null)
                return match.Name;
        }
        else
        {
            logger.LogWarning(
                "[CreateJob] Could not resolve a display name for voice {VoiceId} (provider lookup failed: {Error}).",
                voiceId, voicesResult.Error);
        }

        return requestedName.GetValueOr(voiceId);
    }

    /// <summary>
    ///     Ad-hoc broadcasts carry only a free-text <see cref="CreateJobCommand.Location" /> —
    ///     never the canonical <see cref="MarketSchedule.MarketId" /> the schedule poller sets.
    ///     Passing Location straight into <see cref="Job.Create" />'s market argument silently
    ///     breaks every market-scoped lookup keyed by the canonical id (operator access, filler
    ///     statements, priority roads, guidance): e.g. "Sydney, NSW, Australia" never matches
    ///     the "sydney" row. Resolution is delegated to the single shared
    ///     <see cref="MarketLocationResolver" /> — the SAME implementation the broadcast trigger
    ///     uses — so the job record, the queue message and the orchestrator can never disagree
    ///     about which market a bulletin serves. A location with no backing schedule resolves to
    ///     the raw location text itself (deliberately NOT a slug, which could collide with a real
    ///     market id and make the record claim a market the bulletin was generated without).
    ///     Fail-soft: a repository error degrades to the raw location rather than blocking job
    ///     creation.
    /// </summary>
    private async Task<MarketResolution> ResolveMarketAsync(string location, CancellationToken cancellationToken)
    {
        var schedulesResult = await scheduleRepository.ListSchedulesAsync(cancellationToken);
        if (schedulesResult.IsFailure)
        {
            logger.LogWarning(
                "[CreateJob] Could not resolve MarketId for ad-hoc Location {Location} (schedule lookup failed: {Error}); using Location as-is.",
                location, schedulesResult.Error);
            return new MarketResolution(location, false);
        }

        return MarketLocationResolver.Resolve(schedulesResult.Value, location);
    }

    /// <summary>Rejects a job; reason and notes are operational audit data only.</summary>
    public async Task<Result> RejectJobAsync(RejectJobCommand command, CancellationToken cancellationToken = default)
    {
        var jobResult = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result.Failure(jobResult.Error);

        var job = jobResult.Value;
        var rejectionEntry = new RejectionEntry(
            command.ReasonCode.ToString(),
            command.Notes,
            timeProvider.GetUtcNow().UtcDateTime,
            command.By,
            job.ScriptText);

        var rejectResult = job.Reject(rejectionEntry, timeProvider.GetUtcNow().UtcDateTime, command.By);
        if (rejectResult.IsFailure)
            return rejectResult;

        var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveResult.IsFailure)
            return saveResult;

        await jobRepository.AddLogAsync(command.JobId,
            string.IsNullOrWhiteSpace(rejectionEntry.Notes)
                ? $"Job rejected: {rejectionEntry.Reason}."
                : $"Job rejected: {rejectionEntry.Reason}. Notes: {rejectionEntry.Notes}",
            JobLogLevel.Warn,
            cancellationToken);

        return Result.Success();
    }

    /// <summary>
    ///     Renders one batched operator change request into a producer-feedback line. The label is
    ///     the rejection-style category when the operator supplied one, else the change family
    ///     (instruction / comment / pronunciation); the optional anchor names the script text the
    ///     change refers to, so a pronunciation fix lands on the exact word.
    /// </summary>
    private static string FormatRegenerateFeedback(RegenerateFeedbackDto item)
    {
        if (string.IsNullOrWhiteSpace(item.Text))
            return string.Empty;
        string label = string.IsNullOrWhiteSpace(item.Category) ? item.Kind : item.Category;
        string anchor = string.IsNullOrWhiteSpace(item.Target)
            ? string.Empty
            : $" (re: \"{item.Target.Trim()}\")";
        return $"{label}{anchor}: {item.Text.Trim()}";
    }

    /// <summary>Claims a covered fallback and queues it for durable delivery.</summary>
    public async Task<Result> RejectToFallbackAsync(RejectToFallbackCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_deliveryQueueOptions.Enabled)
            return await RejectToFallbackSynchronouslyAsync(command, cancellationToken);
        if (string.IsNullOrEmpty(command.ExpectedEtag))
            return Result.Failure(ErrorMessages.Job.EtagRequired);
        var loaded = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure(loaded.Error);
        var job = loaded.Value;
        if (job.Status == JobStatus.DeliveryPending
            && string.Equals(job.DeliveryReviewedEtag, command.ExpectedEtag, StringComparison.Ordinal))
            return await EnqueueFallbackCommandAsync(job, command, null, null, cancellationToken);
        if (job.Status != JobStatus.WaitingApproval)
            return Result.Failure(ErrorMessages.Job.CannotRejectFromStatus);
        if (!string.Equals(job.ETag, command.ExpectedEtag, StringComparison.Ordinal))
            return Result.Failure(ErrorMessages.Job.RevisionMismatch);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var referenceUtc = job.ScheduledForUtc.GetValueOr(now);
        var fallbackTask = jobRepository.GetLastSuccessfulJobForMarketAsync(
            job.Market, job.Station, referenceUtc.Date, referenceUtc.Date.AddDays(1),
            job.JobId, cancellationToken);
        var fallback = fallbackTask is null ? null : await fallbackTask;
        string? sourceJobId = null;
        string? audioUrl = null;
        double? duration = null;
        if (fallback is { IsSuccess: true } && fallback.Value.HasValue
            && fallback.Value.Value.JobId != job.JobId && fallback.Value.Value.AudioUrl.HasValue)
            sourceJobId = fallback.Value.Value.JobId;
        else
        {
            var clipTask = perVoiceFillerCart.ResolveAsync(job, cancellationToken);
            var clip = clipTask is null
                ? Option<ResolvedCartClip>.None
                : await clipTask;
            if (clip.HasValue)
            {
                audioUrl = clip.Value.Url;
                duration = clip.Value.DurationSeconds;
            }
            else if (!string.IsNullOrWhiteSpace(_options.FillerCartAudioUrl))
                audioUrl = _options.FillerCartAudioUrl;
            else
                return Result.Failure(
                    "No same-day bulletin is available and no filler cart is configured; cannot apply fallback.");
        }

        var commandWithKey = new DeliveryQueueCommand(
            command.JobId, command.ExpectedEtag!,
            BuildDeliveryIdempotencyKey(command.JobId, command.ExpectedEtag!, DeliveryKind.Fallback),
            command.By, null, DeliveryKind.Fallback, sourceJobId, audioUrl,
            FallbackReasons.OperatorRejected, command.Notes, duration);
        var claim = await ClaimDeliveryAsync(commandWithKey, cancellationToken);
        if (claim.IsFailure)
            return Result.Failure(claim.Error);
        return await EnqueueFallbackCommandAsync(job, command, commandWithKey, claim.Value,
            cancellationToken);
    }

    private async Task<Result> RejectToFallbackSynchronouslyAsync(
        RejectToFallbackCommand command, CancellationToken cancellationToken)
    {
        var loaded = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure(loaded.Error);
        var job = loaded.Value;
        if (job.Status != JobStatus.WaitingApproval)
            return Result.Failure(ErrorMessages.Job.CannotRejectFromStatus);
        if (!string.IsNullOrWhiteSpace(command.ExpectedEtag)
            && !string.Equals(command.ExpectedEtag, job.ETag, StringComparison.Ordinal))
            return Result.Failure(ErrorMessages.Job.RevisionMismatch);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        job.AddRejection(new RejectionEntry(
            command.ReasonCode.ToString(), command.Notes, now, command.By, job.ScriptText));
        var reference = job.ScheduledForUtc.GetValueOr(now);
        var previousTask = jobRepository.GetLastSuccessfulJobForMarketAsync(
            job.Market, job.Station, reference.Date, reference.Date.AddDays(1),
            job.JobId, cancellationToken);
        var previous = previousTask is null ? null : await previousTask;
        string fallbackKind;
        double? measured = null;
        Result applied;
        if (previous is { IsSuccess: true } && previous.Value.HasValue
            && previous.Value.Value.JobId != job.JobId && previous.Value.Value.AudioUrl.HasValue)
        {
            applied = job.ApplyFallback(previous.Value.Value, now, command.By,
                FallbackReasons.OperatorRejected);
            measured = job.MeasuredDurationSec.HasValue
                ? job.MeasuredDurationSec.Value
                : null;
            fallbackKind = "reuse_same_day";
        }
        else
        {
            applied = await ApplySynchronousFillerFallbackAsync(
                job, FallbackReasons.OperatorRejected, command.By, cancellationToken);
            fallbackKind = "filler_cart";
        }
        if (applied.IsFailure)
            return applied;
        var saved = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saved.IsFailure)
            return saved;
        metrics.TrackFallbackActivated(job.Market,
            fallbackKind == "reuse_same_day"
                ? "operator_rejected_same_day" : "operator_rejected_filler");

        var approval = await ApproveJobAsync(command.JobId, command.By, job.ETag,
            byObjectId: null, cancellationToken);
        if (approval.IsFailure)
            return await ReportUndeliveredAsync(command.JobId, fallbackKind, approval.Error,
                cancellationToken);

        // Approval writes a new table revision. Reload before publishing so the internal
        // synchronous path observes the same optimistic-concurrency contract as the HTTP
        // approve-then-publish sequence.
        var approved = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (approved.IsFailure)
            return await ReportUndeliveredAsync(command.JobId, fallbackKind, approved.Error,
                cancellationToken);
        var republish = await PublishSynchronouslyAsync(command.JobId, command.By,
            approved.Value.ETag, null, cancellationToken);
        if (republish.IsFailure)
            return await ReportUndeliveredAsync(command.JobId, fallbackKind, republish.Error,
                cancellationToken);

        await jobRepository.AddLogAsync(command.JobId,
            string.IsNullOrWhiteSpace(command.Notes)
                ? $"Operator rejected to fallback ({fallbackKind}): {command.ReasonCode}."
                : $"Operator rejected to fallback ({fallbackKind}): {command.ReasonCode}. Notes: {command.Notes}",
            JobLogLevel.Warn, cancellationToken);
        if (measured.HasValue && job.TargetDurationSeconds - measured.Value
            > FallbackAudioPadder.MateriallyShortUnderrunSeconds)
            await jobRepository.AddLogAsync(command.JobId,
                $"Fallback covers {measured.Value:F1}s of the {job.TargetDurationSeconds:F0}s slot.",
                JobLogLevel.Warn, cancellationToken);
        await jobRepository.AddLogAsync(command.JobId, $"Slot covered by {fallbackKind}.",
            JobLogLevel.Warn, cancellationToken);
        return Result.Success();
    }

    /// <summary>
    ///     Applies a filler cart for the legacy synchronous path. The production fallback service
    ///     owns cart resolution, padding, persistence, and coverage diagnostics. Some legacy
    ///     adapters (including tests) do not implement that collaborator and return no result;
    ///     keep the synchronous contract useful by resolving the same per-voice/evergreen chain
    ///     locally rather than dereferencing a null task/result.
    /// </summary>
    private async Task<Result> ApplySynchronousFillerFallbackAsync(
        Job job, string reason, string by, CancellationToken cancellationToken)
    {
        var serviceTask = bulletinFallbackService.ApplyFillerCartAsync(
            job, reason, by, cancellationToken);
        var serviceResult = serviceTask is null ? null : await serviceTask;
        if (serviceResult is not null)
            return serviceResult;

        var clipTask = perVoiceFillerCart.ResolveAsync(job, cancellationToken);
        var clip = clipTask is null
            ? Option<ResolvedCartClip>.None
            : await clipTask;
        string? audioUrl = clip.HasValue ? clip.Value.Url : _options.FillerCartAudioUrl;
        if (string.IsNullOrWhiteSpace(audioUrl))
            return Result.Failure(
                "No filler cart is configured for this market, and no per-voice cart could be produced.");

        double? measuredDurationSec = clip.HasValue ? clip.Value.DurationSeconds : null;
        var applied = job.ApplyFillerFallback(
            audioUrl,
            timeProvider.GetUtcNow().UtcDateTime,
            by,
            reason,
            measuredDurationSec);
        if (applied.IsFailure)
            return applied;

        if (measuredDurationSec.HasValue)
        {
            double gap = job.TargetDurationSeconds - measuredDurationSec.Value;
            if (gap > FallbackAudioPadder.MateriallyShortUnderrunSeconds)
                await jobRepository.AddLogAsync(job.JobId,
                    $"Filler cart covers {measuredDurationSec.Value:F1}s of {job.TargetDurationSeconds:F0}s slot — " +
                    $"{gap:F1}s of dead air if approved as-is. Regenerate or approve knowing the slot is under-covered.",
                    JobLogLevel.Warn, cancellationToken);
        }
        else
        {
            await jobRepository.AddLogAsync(job.JobId,
                $"Evergreen filler cart applied — its duration could not be verified, so the {job.TargetDurationSeconds:F0}s slot " +
                "may be under-covered. Listen before approving.",
                JobLogLevel.Warn, cancellationToken);
        }

        return Result.Success();
    }


    private async Task<Result> EnqueueFallbackCommandAsync(
        Job job, RejectToFallbackCommand request, DeliveryQueueCommand? command,
        JobStatusUpdateResult? claim, CancellationToken cancellationToken)
    {
        command ??= new DeliveryQueueCommand(
            job.JobId, request.ExpectedEtag!,
            BuildDeliveryIdempotencyKey(job.JobId, request.ExpectedEtag!, DeliveryKind.Fallback),
            request.By, null, DeliveryKind.Fallback, job.DeliveryFallbackSourceJobId,
            job.DeliveryFallbackAudioUrl, FallbackReasons.OperatorRejected, request.Notes,
            job.DeliveryFallbackDurationSeconds);
        return await queuePublisher.PublishDeliveryMessageAsync(command, cancellationToken);
    }

    /// <summary>Queues an operator-selected repeat or filler source for durable delivery.</summary>
    public async Task<Result> SetAirSourceAsync(SetAirSourceCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!_deliveryQueueOptions.Enabled)
            return await SetAirSourceSynchronouslyAsync(command, cancellationToken);
        if (string.IsNullOrEmpty(command.ExpectedEtag))
            return Result.Failure(ErrorMessages.Job.EtagRequired);
        var loaded = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure(loaded.Error);
        var job = loaded.Value;
        if (job.Status != JobStatus.WaitingApproval)
            return Result.Failure(ErrorMessages.Job.CannotApproveFromStatus);
        if (!string.Equals(command.ExpectedEtag, job.ETag, StringComparison.Ordinal))
            return Result.Failure(ErrorMessages.Job.RevisionMismatch);

        string? sourceJobId = null;
        string? audioUrl = null;
        double? duration = null;
        string what;
        if (command.Kind == AirSourceKind.Repeat)
        {
            if (string.IsNullOrWhiteSpace(command.SourceJobId))
                return Result.Failure("A source bulletin is required for a repeat — choose which bulletin to air.");
            var sourceResult = await GetJobOrFailAsync(command.SourceJobId!, cancellationToken);
            if (sourceResult.IsFailure)
                return Result.Failure(sourceResult.Error);
            var source = sourceResult.Value;
            if (source.JobId == job.JobId)
                return Result.Failure("A bulletin cannot repeat itself — choose a different job.");
            if (!string.Equals(source.Market, job.Market, StringComparison.Ordinal)
                || !string.Equals(source.Station, job.Station, StringComparison.Ordinal))
                return Result.Failure(
                    "The source bulletin is for a different market or station and cannot air in this slot.");
            if (!source.AudioUrl.HasValue)
                return Result.Failure("The source bulletin has no audio and cannot be repeated.");
            if (source.Status is not (JobStatus.Approved or JobStatus.Published))
                return Result.Failure(
                    "The source bulletin must be approved (or already on air) before it can be repeated.");
            var schedule = await scheduleRepository.GetScheduleAsync(job.Market, cancellationToken);
            var timezone = schedule.IsSuccess ? schedule.Value.Timezone : "UTC";
            var (dayStart, dayEnd) = TimeZoneHelper.GetLocalDayWindowUtc(
                job.ScheduledForUtc.GetValueOr(timeProvider.GetUtcNow().UtcDateTime), timezone);
            if (source.CreatedAt < dayStart || source.CreatedAt >= dayEnd)
                return Result.Failure(
                    "The source bulletin is from a previous broadcast day and cannot be repeated for this slot.");
            sourceJobId = source.JobId;
            what = $"a repeat of bulletin {source.JobId}";
        }
        else
        {
            var clip = await perVoiceFillerCart.ResolveAsync(job, cancellationToken);
            if (clip.HasValue)
            {
                audioUrl = clip.Value.Url;
                duration = clip.Value.DurationSeconds;
            }
            else if (!string.IsNullOrWhiteSpace(_options.FillerCartAudioUrl))
                audioUrl = _options.FillerCartAudioUrl;
            else
                return Result.Failure("No filler cart is configured.");
            what = "the standby cart";
        }

        var queued = new DeliveryQueueCommand(
            command.JobId, command.ExpectedEtag!,
            BuildDeliveryIdempotencyKey(command.JobId, command.ExpectedEtag!, DeliveryKind.Fallback),
            command.RequestedBy, null, DeliveryKind.Fallback, sourceJobId, audioUrl,
            $"{command.RequestedBy} manually chose {what}", null, duration);
        var claim = await ClaimDeliveryAsync(queued, cancellationToken);
        if (claim.IsFailure)
            return Result.Failure(claim.Error);
        await jobRepository.AddLogAsync(command.JobId,
            $"{command.RequestedBy} set the air source for this slot to {what}.",
            JobLogLevel.Info, cancellationToken);
        return await queuePublisher.PublishDeliveryMessageAsync(queued, cancellationToken);
    }

    private async Task<Result> SetAirSourceSynchronouslyAsync(
        SetAirSourceCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.ExpectedEtag))
            return Result.Failure(ErrorMessages.Job.EtagRequired);
        var loaded = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure(loaded.Error);
        var job = loaded.Value;
        if (job.Status != JobStatus.WaitingApproval)
            return Result.Failure(ErrorMessages.Job.CannotApproveFromStatus);
        if (!string.Equals(command.ExpectedEtag, job.ETag, StringComparison.Ordinal))
            return Result.Failure(ErrorMessages.Job.RevisionMismatch);

        Result applied;
        string what;
        if (command.Kind == AirSourceKind.Repeat)
        {
            if (string.IsNullOrWhiteSpace(command.SourceJobId))
                return Result.Failure("A source bulletin is required for a repeat — choose which bulletin to air.");
            var sourceResult = await GetJobOrFailAsync(command.SourceJobId!, cancellationToken);
            if (sourceResult.IsFailure)
                return Result.Failure(sourceResult.Error);
            var source = sourceResult.Value;
            if (source.JobId == job.JobId)
                return Result.Failure("A bulletin cannot repeat itself — choose a different job.");
            if (!string.Equals(source.Market, job.Market, StringComparison.Ordinal)
                || !string.Equals(source.Station, job.Station, StringComparison.Ordinal))
                return Result.Failure(
                    "The source bulletin is for a different market or station and cannot air in this slot.");
            if (!source.AudioUrl.HasValue)
                return Result.Failure("The source bulletin has no audio and cannot be repeated.");
            if (source.Status is not (JobStatus.Approved or JobStatus.Published))
                return Result.Failure(
                    "The source bulletin must be approved (or already on air) before it can be repeated.");
            var scheduleTask = scheduleRepository.GetScheduleAsync(job.Market, cancellationToken);
            var schedule = scheduleTask is null ? null : await scheduleTask;
            var timezone = schedule is { IsSuccess: true } && schedule.Value is not null
                ? schedule.Value.Timezone : "UTC";
            var (dayStart, dayEnd) = TimeZoneHelper.GetLocalDayWindowUtc(
                job.ScheduledForUtc.GetValueOr(timeProvider.GetUtcNow().UtcDateTime), timezone);
            if (source.CreatedAt < dayStart || source.CreatedAt >= dayEnd)
                return Result.Failure(
                    "The source bulletin is from a previous broadcast day and cannot be repeated for this slot.");
            what = $"a repeat of bulletin {source.JobId}";
            applied = job.ApplyFallback(source, timeProvider.GetUtcNow().UtcDateTime,
                command.RequestedBy, $"{command.RequestedBy} manually chose {what}");
        }
        else
        {
            what = "the standby cart";
            applied = await ApplySynchronousFillerFallbackAsync(
                job, $"{command.RequestedBy} manually chose {what}",
                command.RequestedBy, cancellationToken);
        }
        if (applied.IsFailure)
            return applied;
        var saved = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saved.IsFailure)
            return saved;
        await jobRepository.AddLogAsync(command.JobId,
            $"{command.RequestedBy} set the air source for this slot to {what}.",
            JobLogLevel.Info, cancellationToken);
        var approve = await ApproveJobAsync(command.JobId, command.RequestedBy, job.ETag,
            byObjectId: null, cancellationToken);
        if (approve.IsFailure)
            return await ReportUndeliveredAsync(command.JobId, what, approve.Error, cancellationToken);
        var publish = await PublishSynchronouslyAsync(command.JobId, command.RequestedBy,
            job.ETag, null, cancellationToken);
        if (publish.IsFailure)
            return await ReportUndeliveredAsync(command.JobId, what, publish.Error, cancellationToken);
        logger.LogInformation("Job {JobId} air source set to {What} by {By}",
            command.JobId, what, command.RequestedBy);
        return Result.Success();
    }

    /// <summary>
    /// Compatibility seam for deadline and recovery callers. It only creates a pending delivery
    /// command; the queue worker performs all external writes and terminal status transitions.
    /// </summary>
    public async Task<Result> DeliverCoveredSlotAsync(
        string jobId, string what, string by, CancellationToken cancellationToken = default)
    {
        if (!_deliveryQueueOptions.Enabled)
            return await DeliverCoveredSlotSynchronouslyAsync(jobId, what, by, cancellationToken);
        var loaded = await GetJobOrFailAsync(jobId, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure(loaded.Error);
        var job = loaded.Value;
        if (job.AudioUrl.IsNone)
            return await ReportUndeliveredAsync(jobId, what,
                "the slot carries no audio to deliver", cancellationToken);
        if (job.Status == JobStatus.Approved)
            return await PublishJobAsync(jobId, by, job.ETag, null, cancellationToken);
        if (job.Status is not (JobStatus.FallbackUsed or JobStatus.DeliveryPending))
            return Result.Failure($"Covered slot cannot be delivered from status {job.Status}.");
        var reviewed = job.DeliveryReviewedEtag ?? job.ETag;
        if (string.IsNullOrWhiteSpace(reviewed))
            return Result.Failure(ErrorMessages.Job.EtagRequired);
        var command = new DeliveryQueueCommand(
            jobId,
            reviewed!,
            job.DeliveryIdempotencyKey
                ?? BuildDeliveryIdempotencyKey(jobId, reviewed!, DeliveryKind.Fallback),
            by, null, DeliveryKind.Fallback,
            job.DeliveryFallbackSourceJobId ?? job.FallbackSourceJobId.GetValueOr(null!),
            job.DeliveryFallbackAudioUrl ?? job.AudioUrl.GetValueOr(null!),
            job.DeliveryFallbackReason ?? FallbackReasons.OperatorRejected,
            job.DeliveryFallbackNotes, job.DeliveryFallbackDurationSeconds);
        var claim = await ClaimDeliveryAsync(command, cancellationToken);
        if (claim.IsFailure)
            return Result.Failure(claim.Error);
        await jobRepository.AddLogAsync(jobId, $"Slot covered by {what}.",
            JobLogLevel.Warn, cancellationToken);
        return await queuePublisher.PublishDeliveryMessageAsync(command, cancellationToken);
    }

    private async Task<Result> DeliverCoveredSlotSynchronouslyAsync(
        string jobId, string what, string by, CancellationToken cancellationToken)
    {
        var current = await GetJobOrFailAsync(jobId, cancellationToken);
        if (current.IsFailure)
            return Result.Failure(current.Error);
        if (current.Value.AudioUrl.IsNone)
            return await ReportUndeliveredAsync(jobId, what,
                "the slot carries no audio to deliver", cancellationToken);
        if (current.Value.Status == JobStatus.Approved)
            return await PublishSynchronouslyAsync(jobId, by, current.Value.ETag,
                null, cancellationToken);

        var approve = await ApproveJobAsync(jobId, by, current.Value.ETag,
            byObjectId: null, cancellationToken);
        if (approve.IsFailure)
            return await ReportUndeliveredAsync(jobId, what, approve.Error, cancellationToken);
        var fresh = await GetJobOrFailAsync(jobId, cancellationToken);
        if (fresh.IsFailure)
            return await ReportUndeliveredAsync(jobId, what, fresh.Error, cancellationToken);
        var publish = await PublishSynchronouslyAsync(jobId, by, fresh.Value.ETag,
            null, cancellationToken);
        if (publish.IsFailure)
            return await ReportUndeliveredAsync(jobId, what, publish.Error, cancellationToken);
        await jobRepository.AddLogAsync(jobId, $"Slot covered by {what}.",
            JobLogLevel.Warn, cancellationToken);
        return Result.Success();
    }


    /// <summary>
    ///     The slot's record says it is covered but the audio did not reach the station. That
    ///     split is the one failure an operator MUST see, so it goes to the job's own log as well
    ///     as to telemetry, and the caller gets a failure naming it rather than a false success.
    /// </summary>
    private async Task<Result> ReportUndeliveredAsync(
        string jobId, string what, string? error, CancellationToken cancellationToken)
    {
        string message = $"Slot covered by {what}, but the audio did NOT reach playout: {error}";
        logger.LogWarning(
            "Covered slot {JobId} ({What}) was not delivered: {Error}", jobId, what, error);
        await jobRepository.AddLogAsync(jobId, message, JobLogLevel.Error, cancellationToken);
        return Result.Failure(message);
    }

    /// <summary>
    ///     Publishes an approved job — marks it as final for broadcast.
    ///     The audio has already been generated and approved; no re-generation needed.
    /// </summary>
    /// <summary>
    ///     Claims the reviewed job revision and enqueues delivery. The HTTP path never calls the
    ///     external playout system; the queue worker below is the sole owner of delivery completion.
    /// </summary>
    public async Task<Result> PublishJobAsync(string jobId, string by = "unknown", string? expectedEtag = null,
        string? byObjectId = null, CancellationToken cancellationToken = default)
    {
        if (!_deliveryQueueOptions.Enabled)
            return await PublishSynchronouslyAsync(jobId, by, expectedEtag, byObjectId,
                cancellationToken);
        if (string.IsNullOrEmpty(expectedEtag))
            return Result.Failure(ErrorMessages.Job.EtagRequired);
        var jobResult = await GetJobOrFailAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result.Failure(jobResult.Error);
        if (jobResult.Value.Status != JobStatus.Approved
            && !(jobResult.Value.Status == JobStatus.DeliveryPending
                && string.Equals(jobResult.Value.DeliveryReviewedEtag, expectedEtag,
                    StringComparison.Ordinal)))
            return Result.Failure(jobResult.Value.Status == JobStatus.DeliveryPending
                ? $"Conflict: job {jobId} already has a different delivery claim."
                : ErrorMessages.Job.MustBeApprovedToPublish);

        var command = new DeliveryQueueCommand(
            jobId,
            expectedEtag,
            BuildDeliveryIdempotencyKey(jobId, expectedEtag, DeliveryKind.Publish),
            by,
            byObjectId,
            DeliveryKind.Publish);
        var claim = await ClaimDeliveryAsync(command, cancellationToken);
        if (claim.IsFailure)
            return Result.Failure(claim.Error);

        var enqueue = await queuePublisher.PublishDeliveryMessageAsync(command, cancellationToken);
        if (enqueue.IsFailure)
            logger.LogError("Delivery claim {Key} for job {JobId} was not enqueued: {Error}",
                command.IdempotencyKey, jobId, enqueue.Error);
        return enqueue;
    }

    private async Task<Result> PublishSynchronouslyAsync(
        string jobId, string by, string? expectedEtag, string? byObjectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(expectedEtag))
            return Result.Failure(ErrorMessages.Job.EtagRequired);
        var loaded = await GetJobOrFailAsync(jobId, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure(loaded.Error);
        var job = loaded.Value;
        if (job.Status != JobStatus.Approved)
            return Result.Failure(ErrorMessages.Job.MustBeApprovedToPublish);
        if (!string.Equals(job.ETag, expectedEtag, StringComparison.Ordinal))
            return Result.Failure(ErrorMessages.Job.RevisionMismatch);

        // Keep the job Approved until the external write succeeds. A failed delivery must not
        // leave a Published row that claims a bulletin reached playout.
        Option<RevoiceObservation> learnObservation = Option<RevoiceObservation>.None;
        FileDeliveryStatus? deliveryStatus = null;
        if (job.AudioUrl.HasValue)
        {
            var delivered = await DeliverAudioAsync(job, cancellationToken);
            if (delivered.IsFailure)
            {
                await jobRepository.AddLogAsync(jobId,
                    $"File delivery failed: {delivered.Error}",
                    JobLogLevel.Warn, cancellationToken);
                return Result.Failure(delivered.Error);
            }

            deliveryStatus = delivered.Value;
            if (deliveryStatus == FileDeliveryStatus.Delivered
                && job.PendingRevoiceObservation.HasValue && job.ScriptText.HasValue)
            {
                learnObservation = job.PendingRevoiceObservation;
                job.PendingRevoiceObservation = Option<RevoiceObservation>.None;
            }
        }

        var published = job.Publish(timeProvider.GetUtcNow().UtcDateTime, by, byObjectId);
        if (published.IsFailure)
            return published;
        var saved = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saved.IsFailure)
            return saved;
        if (!deliveryStatus.HasValue)
        {
            await jobRepository.AddLogAsync(jobId, "Job published without audio",
                JobLogLevel.Info, cancellationToken);
        }
        else
        {
            await jobRepository.AddLogAsync(jobId,
                deliveryStatus.Value == FileDeliveryStatus.Delivered
                    ? "Audio delivered to playout system"
                    : deliveryStatus.Value == FileDeliveryStatus.SkippedStationDisabled
                        ? "Send to playout is off for this station — audio retained in storage, not sent to playout."
                        : "Playout delivery is disabled in this environment — audio retained in storage, not sent to playout.",
                JobLogLevel.Info, cancellationToken);
        }

        if (learnObservation.HasValue)
            await EnqueueLessonLearningSafeAsync(jobId, job, learnObservation.Value, cancellationToken);
        return Result.Success();
    }

    /// <summary>Processes one durable delivery command. Queue retries are safe by command key.</summary>
    public async Task<Result> ProcessDeliveryAsync(
        DeliveryQueueCommand command,
        CancellationToken cancellationToken = default)
    {
        var loaded = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (loaded.IsFailure)
            return Result.Failure(loaded.Error);
        var job = loaded.Value;

        if (job.Status is JobStatus.Published)
            return Result.Success();
        if (job.Status is JobStatus.Cancelled or JobStatus.Failed)
            return Result.Failure($"Delivery command is no longer actionable: {job.Status}");
        if (job.Status != JobStatus.DeliveryPending
            || !string.Equals(job.DeliveryIdempotencyKey, command.IdempotencyKey, StringComparison.Ordinal)
            || !string.Equals(job.DeliveryReviewedEtag, command.ReviewedEtag, StringComparison.Ordinal))
            return Result.Failure("Delivery command does not match the pending claim.");

        if (job.DeliveryAttemptCount >= Math.Max(1, _deliveryQueueOptions.MaxAttempts))
            return await FailPendingDeliveryAsync(job, "Delivery attempt budget exhausted", cancellationToken);

        job.DeliveryAttemptCount++;
        job.DeliveryLastAttemptAt = timeProvider.GetUtcNow();
        job.DeliveryLastError = null;
        var attemptSave = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (attemptSave.IsFailure)
            return attemptSave;

        try
        {
            if (command.Kind == DeliveryKind.Fallback)
            {
                var fallback = await ApplyQueuedFallbackAsync(job, command, cancellationToken);
                if (fallback.IsFailure)
                    return await FailPendingDeliveryAsync(job, fallback.Error, cancellationToken);
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_deliveryQueueOptions.DeadlineSeconds, 1, 300)));
            var deliveryStatus = await DeliverAudioAsync(job, deadline.Token);
            if (deliveryStatus.IsFailure)
                return await FailPendingDeliveryAsync(
                    job, deliveryStatus.Error, cancellationToken, metricAlreadyTracked: true);

            Option<RevoiceObservation> learnObservation = Option<RevoiceObservation>.None;
            if (deliveryStatus.Value == FileDeliveryStatus.Delivered
                && job.PendingRevoiceObservation.HasValue && job.ScriptText.HasValue)
            {
                learnObservation = job.PendingRevoiceObservation;
                job.PendingRevoiceObservation = Option<RevoiceObservation>.None;
            }

            var complete = job.CompleteDelivery(timeProvider.GetUtcNow().UtcDateTime,
                job.DeliveryRequestedBy ?? command.RequestedBy,
                job.DeliveryRequestedByObjectId ?? command.RequestedByObjectId);
            if (complete.IsFailure)
                return complete;
            job.DeliveryLastError = null;
            var save = await jobRepository.SaveJobAsync(job, cancellationToken);
            if (save.IsFailure)
                return save;

            if (deliveryStatus.Value == FileDeliveryStatus.SkippedDisabled)
                await jobRepository.AddLogAsync(command.JobId,
                    "Playout delivery is disabled in this environment — audio retained in storage, not sent to playout.",
                    JobLogLevel.Info, cancellationToken);
            else if (deliveryStatus.Value == FileDeliveryStatus.SkippedStationDisabled)
                await jobRepository.AddLogAsync(command.JobId,
                    "Send to playout is off for this station — audio retained in storage, not sent to playout.",
                    JobLogLevel.Info, cancellationToken);
            else
                await jobRepository.AddLogAsync(command.JobId, "Audio delivered to playout system",
                    JobLogLevel.Info, cancellationToken);
            await jobRepository.AddLogAsync(command.JobId, "Job published for broadcast",
                JobLogLevel.Info, cancellationToken);

            if (learnObservation.HasValue)
                await EnqueueLessonLearningSafeAsync(
                    command.JobId, job, learnObservation.Value, cancellationToken);
            return Result.Success();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return await FailPendingDeliveryAsync(job, "Delivery deadline exceeded", CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delivery worker failed for job {JobId}", command.JobId);
            return await FailPendingDeliveryAsync(job, ex.Message, cancellationToken);
        }
    }

    private async Task EnqueueLessonLearningSafeAsync(
        string jobId, Job job, RevoiceObservation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = new LessonLearningQueueMessage(
                jobId,
                job.Station,
                observation.OriginalScript,
                job.ScriptText.Value,
                observation.CapturedAtUtc,
                observation.By);
            var publishTask = queuePublisher.PublishLessonLearningMessageAsync(
                message, cancellationToken);
            var result = publishTask is null ? null : await publishTask;
            if (result is null)
            {
                logger.LogWarning(
                    "[LessonLearning] Job {JobId} observation was not enqueued: publisher returned no result",
                    jobId);
            }
            else if (result.IsFailure)
            {
                logger.LogWarning("[LessonLearning] Job {JobId} observation not enqueued: {Error}",
                    jobId, result.Error);
            }
        }
        catch (Exception ex)
        {
            // Lesson learning is post-delivery telemetry. A queue outage must not turn an
            // already-published bulletin into a retryable delivery failure.
            logger.LogWarning(ex, "[LessonLearning] Job {JobId} observation could not be enqueued",
                jobId);
        }
    }


    private async Task<Result<JobStatusUpdateResult>> ClaimDeliveryAsync(
        DeliveryQueueCommand command, CancellationToken cancellationToken)
    {
        if (jobRepository is IJobStatusRepository sparse)
            return await sparse.TryClaimDeliveryAsync(command.JobId, command.ReviewedEtag,
                command, cancellationToken);

        // Legacy/unit adapters may only implement IJobRepository. Keep their behaviour correct,
        // while production Table Storage uses the atomic sparse merge above.
        var loaded = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (loaded.IsFailure)
            return Result<JobStatusUpdateResult>.Failure(loaded.Error);
        var job = loaded.Value;
        if (job.Status == JobStatus.DeliveryPending
            && string.Equals(job.DeliveryIdempotencyKey, command.IdempotencyKey, StringComparison.Ordinal))
            return Result<JobStatusUpdateResult>.Success(new JobStatusUpdateResult(
                job.Status, job.Status, job.ETag));
        var expected = command.Kind == DeliveryKind.Fallback ? JobStatus.WaitingApproval : JobStatus.Approved;
        if (job.Status != expected || !string.Equals(job.ETag, command.ReviewedEtag, StringComparison.Ordinal))
            return Result<JobStatusUpdateResult>.Failure(ErrorMessages.Job.RevisionMismatch);
        job.DeliveryIdempotencyKey = command.IdempotencyKey;
        job.DeliveryReviewedEtag = command.ReviewedEtag;
        job.DeliveryKind = Option<DeliveryKind>.Some(command.Kind);
        job.DeliveryRequestedBy = command.RequestedBy;
        job.DeliveryRequestedByObjectId = command.RequestedByObjectId;
        job.DeliveryFallbackSourceJobId = command.FallbackSourceJobId;
        job.DeliveryFallbackAudioUrl = command.FallbackAudioUrl;
        job.DeliveryFallbackReason = command.FallbackReason;
        job.DeliveryFallbackNotes = command.FallbackNotes;
        job.DeliveryFallbackDurationSeconds = command.FallbackDurationSeconds;
        job.TransitionTo(JobStatus.DeliveryPending, command.RequestedBy,
            Option<string>.Some($"Delivery claimed ({command.Kind})"),
            timeProvider.GetUtcNow().UtcDateTime);
        var saved = await jobRepository.SaveJobAsync(job, cancellationToken);
        return saved.IsFailure
            ? Result<JobStatusUpdateResult>.Failure(saved.Error)
            : Result<JobStatusUpdateResult>.Success(new JobStatusUpdateResult(
                expected, JobStatus.DeliveryPending, job.ETag));
    }

    private async Task<Result> ApplyQueuedFallbackAsync(
        Job job, DeliveryQueueCommand command, CancellationToken cancellationToken)
    {
        // Applying the fallback is itself persisted while the command remains pending. A queue
        // redelivery must not capture the already-covered take or append a second rejection.
        if (job.FallbackAppliedAt.HasValue)
            return Result.Success();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        job.AddRejection(new RejectionEntry(
            command.FallbackReason ?? "Operator rejected to fallback",
            command.FallbackNotes ?? string.Empty,
            now,
            command.RequestedBy,
            job.ScriptText));

        Result applied;
        if (!string.IsNullOrWhiteSpace(command.FallbackSourceJobId))
        {
            var source = await GetJobOrFailAsync(command.FallbackSourceJobId!, cancellationToken);
            if (source.IsFailure)
                return Result.Failure(source.Error);
            applied = job.ApplyFallback(source.Value, now, command.RequestedBy, command.FallbackReason);
        }
        else if (!string.IsNullOrWhiteSpace(command.FallbackAudioUrl))
        {
            applied = job.ApplyFillerFallback(command.FallbackAudioUrl!, now,
                command.RequestedBy, command.FallbackReason, command.FallbackDurationSeconds);
        }
        else
            return Result.Failure("Delivery fallback command has no audio source.");
        if (applied.IsFailure)
            return applied;
        job.DeliveryIdempotencyKey = command.IdempotencyKey;
        job.DeliveryReviewedEtag = command.ReviewedEtag;
        job.DeliveryKind = Option<DeliveryKind>.Some(command.Kind);
        job.TransitionTo(JobStatus.DeliveryPending, command.RequestedBy,
            Option<string>.Some("Fallback prepared for delivery"), now);
        return await jobRepository.SaveJobAsync(job, cancellationToken);
    }

    private async Task<Result<FileDeliveryStatus>> DeliverAudioAsync(
        Job job, CancellationToken cancellationToken)
    {
        if (job.AudioUrl.IsNone)
            return Result<FileDeliveryStatus>.Failure("Job has no audio to deliver.");
        if (!await StationPlayoutEnabledAsync(job.Market, cancellationToken))
            return Result<FileDeliveryStatus>.Success(FileDeliveryStatus.SkippedStationDisabled);
        var cutoff = job.FallbackAppliedAt.HasValue ? job.ScheduledForUtc : job.FallbackDeadlineUtc;
        DateTimeOffset? deadline = cutoff.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(cutoff.Value, DateTimeKind.Utc))
            : null;
        var result = await fileDeliveryService.DeliverAsync(
            job.AudioUrl.Value, job.Market, job.AudioSha256, deadline, cancellationToken);
        if (result.IsFailure)
        {
            metrics.TrackZettaDeliveryFailure(job.Market, result.Error);
            return result;
        }
        await MarkAudioPublishedSafeAsync(job, cancellationToken);
        if (result.Value != FileDeliveryStatus.SkippedDisabled)
            metrics.TrackZettaDeliverySuccess(job.Market);
        return result;
    }

    private async Task<Result> FailPendingDeliveryAsync(
        Job job, string error, CancellationToken cancellationToken,
        bool metricAlreadyTracked = false)
    {
        job.DeliveryLastError = error;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var maxAttempts = Math.Max(1, _deliveryQueueOptions.MaxAttempts);

        // A queue failure is retryable until the current attempt consumes the configured
        // budget. Keep the claim pending so the Functions host can redeliver the same command.
        // Never move an already-completed in-memory job backwards: a retry after Published must
        // be an idempotent no-op rather than another external playout write.
        if (job.Status == JobStatus.Published)
            return Result.Failure(error);

        if (job.DeliveryAttemptCount < maxAttempts)
        {
            if (job.Status != JobStatus.DeliveryPending)
                return Result.Failure(error);

            var retrySave = await jobRepository.SaveJobAsync(job, cancellationToken);
            if (retrySave.IsFailure)
                return retrySave;
            await jobRepository.AddLogAsync(job.JobId, $"File delivery failed: {error}",
                JobLogLevel.Warn, cancellationToken);
            if (!metricAlreadyTracked)
                metrics.TrackZettaDeliveryFailure(job.Market, error);
            return Result.Failure(error);
        }

        var failed = job.MarkFailed(error, now);
        if (failed.IsFailure)
            return failed;
        var save = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (save.IsFailure)
            return save;
        await jobRepository.AddLogAsync(job.JobId, $"File delivery failed: {error}",
            JobLogLevel.Warn, cancellationToken);
        if (!metricAlreadyTracked)
            metrics.TrackZettaDeliveryFailure(job.Market, error);
        return Result.Failure(error);
    }

    private static string BuildDeliveryIdempotencyKey(string jobId, string reviewedEtag,
        DeliveryKind kind) => $"delivery:{jobId}:{kind}:{reviewedEtag}";

    /// <summary>
    ///     Whether THIS station is currently armed for playout in this environment
    ///     (<see cref="MarketSchedule.PlayoutEnabled" />). Fails OPEN: a schedule that cannot be
    ///     read is treated as armed, because the alternative is silently withholding a reviewed,
    ///     approved bulletin from air on a storage blip. The station flag is the second of two
    ///     gates — the environment-wide <c>Zetta:Enabled</c> check lives at the delivery boundary
    ///     and refuses independently of anything decided here.
    /// </summary>
    private async Task<bool> StationPlayoutEnabledAsync(string marketId, CancellationToken cancellationToken)
    {
        if (scheduleRepository is null)
            return true;

        var scheduleResult = await scheduleRepository.GetScheduleAsync(marketId, cancellationToken);
        if (scheduleResult is null || scheduleResult.IsFailure || scheduleResult.Value is null)
        {
            if (scheduleResult is not null && scheduleResult.IsFailure)
            {
                logger.LogWarning(
                    "Could not read schedule for {MarketId} to check playout arming ({Error}) — treating the station "
                    + "as armed so a reviewed bulletin is not silently withheld from air.",
                    marketId, scheduleResult.Error);
            }
            return true;
        }

        return scheduleResult.Value.PlayoutEnabled;
    }

    private async Task MarkAudioPublishedSafeAsync(Job job, CancellationToken cancellationToken)
    {
        try
        {
            var tagResult = await broadcastOutput.MarkPublishedAsync(job.AudioUrl.Value, cancellationToken);
            if (tagResult.IsFailure)
                logger.LogWarning("[Retention] Failed to tag audio {AudioFileName} as published for job {JobId}: {Error}",
                    job.AudioUrl.Value, job.JobId, tagResult.Error);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Retention] Unexpected failure tagging audio as published for job {JobId}",
                job.JobId);
        }
    }

    /// <summary>
    ///     Regenerates a rejected, failed, or fallback-covered job — a fresh
    ///     orchestration attempt. Returns a queue message for async processing instead
    ///     of running inline.
    /// </summary>
    public async Task<Result<RegenerationQueueMessage>> RegenerateJobAsync(RegenerateJobCommand command,
        string by = "unknown", CancellationToken cancellationToken = default)
    {
        var jobResult = await GetJobOrFailAsync(command.JobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result<RegenerationQueueMessage>.Failure(jobResult.Error);

        var job = jobResult.Value;
        if (job.Status is not (JobStatus.Rejected or JobStatus.Failed or JobStatus.FallbackUsed))
            return Result<RegenerationQueueMessage>.Failure(ErrorMessages.Job.CannotRegenerateFromStatus);

        // Operator-curated traffic selection (WS2: reorder/include/exclude, then regenerate).
        // Validated against the job's corridors BEFORE RequestRegeneration clears them.
        int originalCorridorCount = job.Corridors.Count;
        var selectedCorridors = Option<IReadOnlyList<string>>.None;
        if (command.SelectedCorridors.HasValue)
        {
            IReadOnlyList<string> selection = command.SelectedCorridors.Value
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToList();

            if (selection.Count == 0)
                return Result<RegenerationQueueMessage>.Failure(ErrorMessages.Job.CorridorSelectionEmpty);

            var knownRoads = new HashSet<string>(
                job.Corridors.Select(c => c.RoadName.Trim()), StringComparer.OrdinalIgnoreCase);
            var unknown = selection.Where(s => !knownRoads.Contains(s)).ToList();
            if (unknown.Count > 0)
                return Result<RegenerationQueueMessage>.Failure(
                    string.Format(ErrorMessages.Job.CorridorSelectionUnknown, string.Join(", ", unknown)));

            selectedCorridors = Option<IReadOnlyList<string>>.Some(selection);
        }

        // Operator-curated good-run (Flow tab) selection. Validated against the job's persisted
        // flow pool BEFORE RequestRegeneration clears it. Data-backed: only roads HERE flow
        // confirmed flowing are accepted; the policy re-filters against fresh flow at generation.
        var selectedGoodRuns = Option<IReadOnlyList<string>>.None;
        if (command.SelectedGoodRuns.HasValue)
        {
            IReadOnlyList<string> goodRunSelection = command.SelectedGoodRuns.Value
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (goodRunSelection.Count > 0)
            {
                var knownFlowRoads = new HashSet<string>(
                    job.FlowRoads.Select(f => f.RoadName.Trim()), StringComparer.OrdinalIgnoreCase);
                var unknownGoodRuns = goodRunSelection.Where(s => !knownFlowRoads.Contains(s)).ToList();
                if (unknownGoodRuns.Count > 0)
                    return Result<RegenerationQueueMessage>.Failure(
                        string.Format(ErrorMessages.Job.GoodRunSelectionUnknown, string.Join(", ", unknownGoodRuns)));

                selectedGoodRuns = Option<IReadOnlyList<string>>.Some(goodRunSelection);
            }
        }

        // Operator change requests (instructions, pronunciation fixes, comments) are operator
        // direction with the same authority as a rejection note, so they ride the same
        // producer-feedback channel — the draft prompt's <producer-feedback> block treats them as
        // authoritative for wording and selection, never for new facts. The frontend always POSTed
        // this array to /regenerate; until the wire field existed System.Text.Json silently
        // discarded it and an instruction only reached the LLM via the reject notes (bug, 2026-08-06).
        Option<string> feedbackContext = command.Feedback.Map(items =>
        {
            var lines = items.Select(FormatRegenerateFeedback).Where(l => l.Length > 0).ToList();
            return lines.Count > 0 ? string.Join("\n", lines) : string.Empty;
        });

        Option<string> additionalContext = (command.AdditionalContext, feedbackContext) switch
        {
            (_, var fb) when fb.HasValue && !string.IsNullOrWhiteSpace(fb.Value) =>
                command.AdditionalContext.Map(ctx => $"{ctx}\n\n{fb.Value}").Or(fb),
            _ => command.AdditionalContext
        };

        // Build aggregated rejection feedback including inline reason/description
        string rejectionFeedback = JobMessageBuilder.BuildRejectionFeedback(
            job.RejectionHistory,
            additionalContext,
            command.RejectionReason,
            command.RejectionDescription);

        if (command.EditedScript.HasValue)
        {
            const string editNote =
                "The operator manually edited the script (provided as the previous script); preserve their wording and only adjust for timing or synthesis quality.";
            rejectionFeedback = string.IsNullOrWhiteSpace(rejectionFeedback)
                ? editNote
                : $"{rejectionFeedback} {editNote}";
        }

        // Operator changed the slot length on this regeneration — apply it so the new bulletin
        // (and the persisted job) target the requested duration, not the original.
        if (command.TargetDurationSec.HasValue)
            job.TargetDurationSeconds = command.TargetDurationSec.Value;

        // A re-voice re-renders words that are already fixed: it never re-fetches traffic, so
        // the existing corridor/incident/script snapshot still describes exactly what is about
        // to be synthesised. Clearing it stranded the operator with a bulletin reciting eight
        // road claims and "0 incidents cited" to check them against (dev, 2026-07-28), and
        // wiped the very ScriptText the queue message reads as the re-voice source.
        bool revoice = command.RevoiceOnly;

        // Voice override: the operator picked a DIFFERENT voice during review. The profile's
        // CURRENT tuning is resolved server-side — the same trust rule as CreateJobAsync — never
        // a client-supplied snapshot, so per-profile behaviour (PaceBoostWpm, settings) cannot
        // silently drop off the regeneration path. An unresolvable profile is a HARD failure for
        // the same reason it is at creation (CreateJobAsync comment, 2026-07-22): a fail-soft
        // would air the old voice while the operator believes their change is queued. MUST
        // resolve before the reset below — a hard failure here must leave the job exactly as it
        // was (still Rejected/Failed), not half-way through a regeneration.
        var voiceOverride = Option<(string ProfileId, string BaseVoiceId, string Name, string Model,
            VoiceSettings Settings, int Version, double PaceBoostWpm)>.None;
        // Captured BEFORE any override replaces the config: the model-cross rule below compares
        // the NEW model against the one the job's existing tags were tuned for.
        string? previousVoiceModel = job.VoiceConfig.Match(vc => (string?)vc.Model, () => null);
        string? requestedVoiceProfileId = command.VoiceProfileId.Match(v => (string?)v, () => null);
        if (!string.IsNullOrWhiteSpace(requestedVoiceProfileId))
        {
            var profileResult = await voiceProfileService.GetAsync(requestedVoiceProfileId, cancellationToken);
            if (profileResult.IsFailure || profileResult.Value.Versions.Count == 0)
            {
                string reason = (profileResult.IsFailure ? profileResult.Error : "it has no versions").TrimEnd('.');
                logger.LogWarning(
                    "Regeneration of job {JobId} named voice profile {ProfileId} but it could not be resolved ({Error}).",
                    command.JobId, requestedVoiceProfileId, reason);
                return Result<RegenerationQueueMessage>.Failure(
                    $"Voice profile '{requestedVoiceProfileId}' could not be resolved: {reason}.");
            }

            var profile = profileResult.Value;
            var current = profile.Versions[^1];
            voiceOverride = Option<(
                string ProfileId, string BaseVoiceId, string Name, string Model,
                VoiceSettings Settings, int Version, double PaceBoostWpm)>.Some((
                profile.Id, profile.BaseVoiceId, profile.Name, current.Model,
                current.Settings, current.Version, current.PaceBoostWpm));
        }

        // MUST resolve before the reset: a full regenerate clears DeliveryStyleId/Version and
        // BookendId/Version, so reading them afterwards silently yields the neutral default — the
        // same trap that destroyed ScriptText. (PronunciationDictionaryId survives the reset, which
        // is why the pronunciation lookup can sit further down.)
        var delivery = await ResolveDeliveryStyleAsync(job, command.RefreshDelivery, cancellationToken);
        var bookends = await ResolveBookendsAsync(job, cancellationToken);

        // Reset job for re-orchestration but preserve rejection history
        var regenResult = job.RequestRegeneration(timeProvider.GetUtcNow().UtcDateTime, by,
            Option<string>.Some(revoice ? "Re-voice requested" : "Regeneration requested"),
            preserveEvidence: revoice);
        if (regenResult.IsFailure)
            return Result<RegenerationQueueMessage>.Failure(regenResult.Error);

        logger.LogInformation("Resetting AttemptCount to 0 for job {JobId} before regeneration", command.JobId);

        if (revoice && command.EditedScript.HasValue && job.ScriptText.HasValue
            && !ScriptDiff.NormalizedEquals(job.ScriptText.Value, command.EditedScript.Value))
        {
            // Repeat edit before a publish: keep the FIRST original, learn against the final words.
            string original = job.PendingRevoiceObservation.Match(
                o => o.OriginalScript, () => job.ScriptText.Value);
            job.PendingRevoiceObservation = Option<RevoiceObservation>.Some(new RevoiceObservation(
                timeProvider.GetUtcNow().UtcDateTime, by, original, command.EditedScript.Value));
        }

        // Persist the operator's chosen voice on the row BEFORE enqueueing — the persist-before-
        // enqueue ordering is deliberate: a later FAILED retry replays job.VoiceConfig into a
        // fresh queue message, so the chosen voice must already be on the row or the retry
        // silently re-airs the old voice. Same RISK-31 precedent as AudioFinalizer persisting
        // the edited script before a re-voice synthesis that may fail.
        if (voiceOverride.HasValue)
        {
            var (profileId, baseVoiceId, name, model, settings, version, paceBoostWpm) = voiceOverride.Value;
            job.VoiceConfig = Option<JobVoiceConfig>.Some(new JobVoiceConfig(
                ProviderNames.ElevenLabs,
                baseVoiceId,
                name,
                model,
                Option<VoiceSettings>.Some(settings),
                paceBoostWpm));
            job.VoiceProfileId = Option<string>.Some(profileId);
            job.VoiceProfileVersion = Option<int>.Some(version);
        }

        var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveResult.IsFailure)
            return Result<RegenerationQueueMessage>.Failure(saveResult.Error);

        await jobRepository.AddLogAsync(command.JobId,
            selectedCorridors.HasValue
                ? $"Job queued for regeneration with operator-curated traffic ({selectedCorridors.Value.Count} of {originalCorridorCount} corridors, in producer order)"
                : $"Job queued for regeneration (AttemptCount reset to 0) with feedback from {job.RejectionHistory.Count} rejection(s)",
            JobLogLevel.Info, cancellationToken);

        if (voiceOverride.HasValue)
        {
            var (_, _, name, _, _, version, _) = voiceOverride.Value;
            await jobRepository.AddLogAsync(command.JobId,
                $"Voice changed to '{name}' (profile {voiceOverride.Value.ProfileId} v{version}) for this regeneration",
                JobLogLevel.Info, cancellationToken);
        }


        // The operator changed a pronunciation rule during review. Without this the message
        // carries the job's PINNED version and synthesis replays the old rules, so the fix
        // would be a silent no-op — the same failure mode as a duplicate alias rule.
        var pronunciation = command.RefreshPronunciation
            ? await ResolveCurrentPronunciationStackAsync(job, cancellationToken)
            : null;

        // The operator's v3 audio tags are tuned per voice AND per model: an override that also
        // swaps the model would carry tags written for the old model into the new read, where
        // they steer differently or fall silent. Drop the toned script in that case — the re-voice
        // path re-runs the delivery director, which re-derives tags for the NEW model from the
        // same approved words. Same-model overrides keep the operator's tags verbatim.
        Option<string> tonedScript = command.TonedScript;
        if (voiceOverride.HasValue
            && !string.Equals(previousVoiceModel, voiceOverride.Value.Model, StringComparison.Ordinal))
            tonedScript = Option<string>.None;

        // Build queue message with existing traffic data and rejection feedback
        var queueMessage = JobMessageBuilder.BuildRegenerationQueueMessage(job, rejectionFeedback,
            command.EditedScript, selectedCorridors, command.DeliveryIntent, tonedScript,
            selectedGoodRuns, command.ExcludedIncidentKeys, command.RevoiceOnly, pronunciation,
            delivery, bookends);

        return Result<RegenerationQueueMessage>.Success(queueMessage);
    }

    /// <summary>
    ///     The dictionaries a regeneration should synthesise with when the operator changed a rule
    ///     during review. The station layer is the CURRENT version of the dictionary this job
    ///     aired with, matched on the provider dictionary id the job pinned; it falls back to the
    ///     pinned locator when the library cannot be read, the library item no longer exists, or
    ///     it has no usable version — never dropping the dictionary entirely.
    ///     <para>
    ///         The platform-wide layer is resolved as the currently ACTIVE global rather than the
    ///         one the job recorded: the whole point of a refresh is to pick up the newest rules,
    ///         and if the global was demoted or archived since the bulletin aired it should stop
    ///         applying.
    ///     </para>
    /// </summary>
    private async Task<PronunciationStack> ResolveCurrentPronunciationStackAsync(
        Job job, CancellationToken cancellationToken)
    {
        var pinnedStation = PronunciationStack.Locator(
            job.PronunciationDictionaryId, job.PronunciationVersionId);
        var pinnedGlobal = PronunciationStack.Locator(
            job.GlobalPronunciationDictionaryId, job.GlobalPronunciationVersionId);

        var listResult = await pronunciationDictionaryService.ListAsync(cancellationToken);
        if (listResult.IsFailure)
        {
            logger.LogWarning(
                "Job {JobId}: could not refresh pronunciation ({Error}); replaying the pinned versions",
                job.JobId, listResult.Error);
            return PronunciationStack.Of(pinnedStation, pinnedGlobal);
        }

        var library = listResult.Value;
        var station = pinnedStation;
        string? voiceModel = job.VoiceConfig.Match(c => c.Model, () => (string?)null);
        if (job.PronunciationDictionaryId.HasValue)
        {
            string providerId = job.PronunciationDictionaryId.Value;
            var match = library.FirstOrDefault(d =>
                string.Equals(d.DictionaryId, providerId, StringComparison.OrdinalIgnoreCase));
            if (match is not null && match.CurrentLocator.HasValue)
            {
                station = voiceModel is null ? match.CurrentLocator : match.LocatorFor(voiceModel);
                if (voiceModel is not null)
                    LogDroppedPhonemeRules(job, match, voiceModel);
            }
        }

        var activeGlobal = library.FirstOrDefault(d => d.IsGlobal && d.IsActive);
        var global = activeGlobal is null
            ? Option<PronunciationLocator>.None
            : voiceModel is null ? activeGlobal.CurrentLocator : activeGlobal.LocatorFor(voiceModel);
        if (activeGlobal is not null && voiceModel is not null)
            LogDroppedPhonemeRules(job, activeGlobal, voiceModel);

        return PronunciationStack.Of(station, global);
    }

    /// <summary>
    ///     A phoneme rule on eleven_multilingual_v2 would silently DELETE matched words from the
    ///     regenerated audio; the stack attaches the alias-only version instead. Log exactly which
    ///     rules were dropped so the divergence is visible in the job trace.
    /// </summary>
    private void LogDroppedPhonemeRules(Job job, PronunciationDictionary dictionary, string voiceModel)
    {
        var dropped = dictionary.UnsupportedRuleWordsFor(voiceModel);
        if (dropped.Count > 0)
            logger.LogWarning(
                "Job {JobId}: pronunciation dictionary {DictionaryId} has phoneme rule(s) ({Words}) that " +
                "{Model} would drop from the audio; attaching the alias-only version",
                job.JobId, dictionary.Id, string.Join(", ", dropped), voiceModel);
    }

    /// <summary>
    ///     The delivery-style policy to synthesise with. The job pins an id and a version but not
    ///     the policy itself, so it must be looked up: by default the exact version the bulletin
    ///     aired with, or — when the operator edited the style during this review — the style's
    ///     current version. Falls back to all-null (neutral) only when the job has no style or
    ///     the library item is gone, never silently substituting a different version.
    /// </summary>
    private async Task<ResolvedDeliveryStyle> ResolveDeliveryStyleAsync(
        Job job, bool refresh, CancellationToken cancellationToken)
    {
        if (!job.DeliveryStyleId.HasValue)
            return default;

        string styleId = job.DeliveryStyleId.Value;
        var styleResult = await deliveryStyleService.GetAsync(styleId, cancellationToken);
        if (styleResult.IsFailure || styleResult.Value.Versions.Count == 0)
        {
            logger.LogWarning(
                "Job {JobId}: delivery style {StyleId} could not be resolved; the regenerated read "
                + "will use the neutral default.", job.JobId, styleId);
            return default;
        }

        var style = styleResult.Value;
        if (refresh)
            return new ResolvedDeliveryStyle(style.CurrentPolicy, style.Id, style.CurrentVersion);

        // Faithful replay: the exact version the job aired with, so a stale-recovery republish
        // reproduces the same read. A version that no longer exists falls back to current.
        int pinned = job.DeliveryStyleVersion.GetValueOr(style.CurrentVersion);
        var match = style.Versions.FirstOrDefault(v => v.Version == pinned);
        return match is null
            ? new ResolvedDeliveryStyle(style.CurrentPolicy, style.Id, style.CurrentVersion)
            : new ResolvedDeliveryStyle(match.Policy, style.Id, match.Version);
    }

    /// <summary>
    ///     The bookend pair the regenerated read must keep. Same faithful-replay contract as
    ///     <see cref="ResolveDeliveryStyleAsync" />: the job pins an id and a version but not the
    ///     text, so the pinned VERSION's template is looked up and re-substituted with the station's
    ///     current tokens. Without this a regenerate silently strips the station's opener and
    ///     closer, and nothing in the console would say why.
    /// </summary>
    private async Task<ResolvedBookends?> ResolveBookendsAsync(Job job, CancellationToken cancellationToken)
    {
        if (!job.BookendId.HasValue)
            return null;

        string id = job.BookendId.Value;
        var result = await bookendService.GetAsync(id, cancellationToken);
        if (result.IsFailure || result.Value.Versions.Count == 0)
        {
            logger.LogWarning(
                "Job {JobId}: bookend {BookendId} could not be resolved; the regenerated read will not carry it.",
                job.JobId, id);
            return null;
        }

        var bookend = result.Value;
        // The exact version the bulletin aired with, so a regenerate reproduces the same wording.
        // A version that no longer exists falls back to current, never to a different entry.
        int pinned = job.BookendVersion.GetValueOr(bookend.CurrentVersion);
        var match = bookend.Versions.FirstOrDefault(v => v.Version == pinned);
        var template = match?.Template ?? bookend.CurrentTemplate;
        int version = match?.Version ?? bookend.CurrentVersion;

        // Tokens are per-station and live on the schedule, not the job.
        var schedulesResult = await scheduleRepository.ListSchedulesAsync(cancellationToken);
        var schedule = schedulesResult.IsSuccess
            ? schedulesResult.Value.FirstOrDefault(s =>
                string.Equals(s.MarketId, job.Market, StringComparison.OrdinalIgnoreCase))
            : null;
        var tokens = schedule?.BookendTokens;
        string marketId = schedule?.MarketId ?? job.Market ?? "";
        string station = schedule?.Station ?? job.Station ?? "";

        var intro = BookendTokenResolver.Resolve(template.IntroText, marketId, station, tokens);
        var outro = BookendTokenResolver.Resolve(template.OutroText, marketId, station, tokens);
        if (!intro.IsComplete || !outro.IsComplete)
        {
            logger.LogWarning(
                "Job {JobId}: bookend {BookendId} has unresolved tokens; dropped from the regenerated read.",
                job.JobId, id);
            return null;
        }

        if (string.IsNullOrWhiteSpace(intro.Text) && string.IsNullOrWhiteSpace(outro.Text))
            return null;

        return new ResolvedBookends(intro.Text, outro.Text, bookend.Id, version);
    }

    /// <summary>
    ///     Publishes a broadcast queue message, marking the job as failed if publishing fails.
    /// </summary>
    public async Task<Result> PublishBroadcastJobAsync(string jobId, BroadcastQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        return await PublishAndFailOnErrorAsync(jobId,
            () => queuePublisher.PublishBroadcastMessageAsync(message, cancellationToken), cancellationToken);
    }

    /// <summary>
    ///     Publishes a regeneration queue message, marking the job as failed if publishing fails.
    /// </summary>
    public async Task<Result> PublishRegenerationJobAsync(string jobId, RegenerationQueueMessage message,
        CancellationToken cancellationToken = default)
    {
        return await PublishAndFailOnErrorAsync(jobId,
            () => queuePublisher.PublishRegenerationMessageAsync(message, cancellationToken), cancellationToken);
    }

    #region Private Helpers

    private async Task<Result> PublishAndFailOnErrorAsync(string jobId, Func<Task<Result>> publish,
        CancellationToken cancellationToken)
    {
        var publishResult = await publish();
        if (publishResult.IsFailure)
        {
            logger.LogError("Failed to queue job {JobId} — {Error}", jobId, publishResult.Error);
            await MarkJobFailedAsync(jobId, $"Queue publishing failed: {publishResult.Error}", cancellationToken);
        }

        return publishResult;
    }

    /// <summary>
    ///     Marks a job as failed with the provided error message.
    /// </summary>
    public async Task<Result> MarkJobFailedAsync(string jobId, string error,
        CancellationToken cancellationToken = default)
    {
        var jobResult = await GetJobOrFailAsync(jobId, cancellationToken);
        if (jobResult.IsFailure)
            return Result.Failure(jobResult.Error);

        var job = jobResult.Value;
        var failResult = job.MarkFailed(error, timeProvider.GetUtcNow().UtcDateTime);
        var saveResult = await jobRepository.SaveJobAsync(job, cancellationToken);
        if (saveResult.IsFailure)
            return saveResult;

        await jobRepository.AddLogAsync(jobId, error, JobLogLevel.Error, cancellationToken);

        // Failure notification (SoW: "failure notifications") — only when the job
        // actually transitioned (an already-terminal job stays silent).
        if (failResult.IsSuccess)
            await SendNotificationSafeAsync(job, NotificationKind.FailedNotCovered, cancellationToken);

        return Result.Success();
    }

    /// <summary>
    ///     Fetches a job by ID, returning a typed failure if not found.
    ///     Eliminates repeated fetch-and-guard boilerplate across all job operations.
    /// </summary>
    private async Task<Result<Job>> GetJobOrFailAsync(string jobId, CancellationToken cancellationToken)
    {
        var result = await jobRepository.GetJobAsync(jobId, cancellationToken);
        return result.IsFailure
            ? Result<Job>.Failure($"Job '{jobId}' not found")
            : result;
    }

    private async Task SendNotificationSafeAsync(Job job, NotificationKind kind, CancellationToken ct)
    {
        logger.LogInformation("Sending {Kind} notification for job {JobId}", kind, job.JobId);
        var result = await notificationService.SendJobNotificationAsync(job, kind, ct);
        if (result.IsFailure)
            logger.LogWarning("Notification failed for job {JobId}: {Error}", job.JobId, result.Error);
        else
            logger.LogInformation("Notification sent for job {JobId}", job.JobId);
    }

    /// <summary>
    ///     Statuses where the bulletin has finished generating and a decision has been recorded, as
    ///     opposed to the in-flight pipeline states. Only affects which refusal message an operator
    ///     reads — approval is refused for both groups.
    /// </summary>
    private static bool IsSettled(JobStatus status) =>
        status is JobStatus.Approved or JobStatus.Rejected or JobStatus.Published
            or JobStatus.Failed or JobStatus.Missed;

    #endregion
}

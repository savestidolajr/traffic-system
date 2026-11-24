using ArnBulletinGenerator.Application;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Application.Mapping;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Functions.Http;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Auth;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     HTTP trigger for ad-hoc broadcasts: takes a free-text location, resolves a matching
///     schedule's market id, and orchestrates the bulletin synchronously.
public sealed class BroadcastHttpTrigger(
    IJobService jobService,
    IJobRepository jobRepository,
    ICoverageResolver coverageResolver,
    IScheduleRepository scheduleRepository,
    IVoiceProfileService voiceProfiles,
    IVoiceProvider voiceProvider,
    IAdHocStationConfigResolver adHocStationConfig,
    IMarketAccessService marketAccess,
    ILogger<BroadcastHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null)
{
    private readonly IInteractiveWorkGate _interactiveWorkGate =
        interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance;
    [Function("orchestrate")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "broadcast")]
        HttpRequestData request,
        FunctionContext functionContext)
    {
        logger.LogInformation("Processing orchestration request");
        var ct = functionContext.CancellationToken;

        var parseResult = await HttpRequestParser.ParseAsync<OrchestrationRequestDto>(request, ct);
        if (parseResult.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(request, parseResult.Error);

        OrchestrationRequestDto dto = parseResult.Value;
        var preflightKey = BroadcastInteractiveWorkKeys.ForRequest(request, dto);
        var preflight = await _interactiveWorkGate.ExecuteLlmAsync(
            preflightKey,
            gateCancellationToken => RunPreflightAsync(dto, gateCancellationToken),
            ct);
        if (preflight.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(request, preflight.Error);

        var validated = preflight.Value.Validated;
        string marketId = preflight.Value.MarketId;
        bool hasSchedule = preflight.Value.HasSchedule;

        // The market guard that makes POST /api/broadcast safe at Operator (RouteRoleMatrix).
        // It runs BEFORE CreateJobAsync so a refused caller leaves no job behind, and it is keyed
        // on the RESOLVED market rather than the free-text location, so "Sydnye" cannot smuggle a
        // request past an assignment. An operator with no matching assignment gets 403; admins and
        // unscoped callers are unrestricted, exactly as on the job and schedule routes.
        if (await marketAccess.EnsureMarketAccessAsync(request, marketId, logger, ct) is { } forbidden)
            return forbidden;

        var jobResult = await jobService.CreateJobAsync(new CreateJobCommand(
            validated.Location,
            validated.TargetDurationSeconds,
            Option<double>.Some(validated.Coverage.Latitude),
            Option<double>.Some(validated.Coverage.Longitude),
            Option<double>.Some(validated.Coverage.RadiusKm),
            VoiceId: validated.VoiceId,
            VoiceName: validated.VoiceName,
            VoiceProvider: validated.VoiceProvider,
            VoiceModel: validated.VoiceModel,
            VoiceSettings: validated.VoiceSettings,
            ContentSelection: Option.OfNullable(validated.Content),
            Coverage: Option<CoverageArea>.Some(validated.Coverage),
            VoiceProfileId: validated.VoiceProfileId), ct);

        if (jobResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(request, jobResult.Error);

        var job = jobResult.Value;

        // A manual trigger for a configured station must sound like that station: same delivery
        // style, same pronunciation dictionaries, same bookends, same fill order the poller would
        // have resolved. Without this the console's "Trigger now" aired a platform-default read
        // and said nothing about it (2026-08-10 UI audit). Voice stays as requested — the studio
        // pre-fills the station's profile and the operator may deliberately change it.
        string? effectiveModel = job.VoiceConfig.Match(vc => (string?)vc.Model, () => null)
            ?? validated.VoiceModel.Match(v => (string?)v, () => null);
        var station = hasSchedule
            ? await adHocStationConfig.ResolveAsync(marketId, effectiveModel, ct)
            : AdHocStationConfig.None;

        var publishResult = await jobService.PublishBroadcastJobAsync(
            job.JobId, validated.ToBroadcastQueueMessage(job, marketId, station), ct);
        if (publishResult.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(request, "Failed to queue broadcast for processing.");

        logger.LogInformation("Queued broadcast for JobId: {JobId}", job.JobId);

        // A Location matching no schedule still generates a real bulletin, but with none of that
        // market's priority roads, filler statements, guidance, pronunciation dictionary, delivery
        // style or bookends — a typo ("Sydnye") produced an orphan market and a config-less
        // bulletin with nothing on screen to say so (observed live on dev 2026-07-28). The
        // endpoint is deliberately free-text for ad-hoc and eval use, so this warns rather than
        // rejects.
        string? warning = null;
        if (!hasSchedule)
        {
            warning =
                $"'{validated.Location}' does not match any configured station, so this bulletin was "
                + "generated without that market's priority roads, filler statements, guidance, "
                + "pronunciation dictionary, delivery style or bookends. Check the spelling if you "
                + "expected a configured market.";
            logger.LogWarning(
                "[Orchestrate] Job {JobId} generated for UNCONFIGURED market '{Market}' (Location '{Location}')",
                job.JobId, marketId, validated.Location);
            await jobRepository.AddLogAsync(job.JobId, warning, JobLogLevel.Warn, ct);
        }

        return await HttpResponseHelper.AcceptedJsonAsync(request, new OrchestrationQueuedResponseDto(
            "Queued", job.JobId,
            "Broadcast orchestration has been queued for processing.",
            warning));
    }

    private async Task<Result<BroadcastPreflightResult>> RunPreflightAsync(
        OrchestrationRequestDto dto,
        CancellationToken cancellationToken)
    {
        var coverageResult = await coverageResolver.ResolveCoverageAsync(dto, cancellationToken);
        if (coverageResult.IsFailure)
            return Result<BroadcastPreflightResult>.Failure(coverageResult.Error);

        var validationResult = OrchestrationValidation.Validate(coverageResult.Value);
        if (validationResult.IsFailure)
            return Result<BroadcastPreflightResult>.Failure(validationResult.Error);

        ValidatedOrchestrationRequestDto validated = validationResult.Value;
        var voiceResult = await ValidateVoiceAsync(validated, cancellationToken);
        if (voiceResult.IsFailure)
            return Result<BroadcastPreflightResult>.Failure(voiceResult.Error);

        var (marketId, hasSchedule) = await ResolveMarketIdAsync(
            validated.Location, cancellationToken);
        return Result<BroadcastPreflightResult>.Success(
            new BroadcastPreflightResult(validated, marketId, hasSchedule));
    }

    /// <summary>
    ///     Referential integrity for the requested voice, mirroring the same check
    ///     <see cref="ScheduleHttpTrigger" /> applies to a schedule's VoiceProfileId. A dangling
    ///     reference used to be discovered deep in the run — after traffic fetch, generation,
    ///     review and safety — or worse, not at all, because job creation quietly substituted the
    ///     configured default voice. Reject it in the first 200ms instead.
    ///     <para>
    ///         The bare-VoiceId check is deliberately fail-soft on a provider outage: if the voice
    ///         list cannot be fetched at all we cannot distinguish "unknown voice" from "ElevenLabs
    ///         is down", and blocking every broadcast on a transient provider failure is worse than
    ///         letting synthesis surface the error. A list we DID fetch that lacks the id is
    ///         conclusive, so that rejects.
    ///     </para>
    /// </summary>
    private async Task<Result> ValidateVoiceAsync(
        ValidatedOrchestrationRequestDto validated,
        CancellationToken ct)
    {
        if (validated.VoiceProfileId.HasValue)
        {
            string profileId = validated.VoiceProfileId.Value;
            if ((await voiceProfiles.GetAsync(profileId, ct)).IsFailure)
                return Result.Failure($"Voice profile '{profileId}' does not exist.");
            return Result.Success();
        }

        string voiceId = validated.VoiceId.Value;
        var voicesResult = await voiceProvider.GetVoicesAsync(ct);
        if (voicesResult.IsFailure)
        {
            logger.LogWarning(
                "[Orchestrate] Could not verify voice {VoiceId} (provider lookup failed: {Error}); proceeding.",
                voiceId, voicesResult.Error);
            return Result.Success();
        }

        return voicesResult.Value.Any(v => string.Equals(v.VoiceId, voiceId, StringComparison.Ordinal))
            ? Result.Success()
            : Result.Failure($"Voice '{voiceId}' does not exist at the TTS provider.");
    }

    /// <summary>
    ///     Ad-hoc broadcasts (this endpoint) carry only a free-text <c>Location</c> — never the
    ///     canonical <see cref="MarketSchedule.MarketId" /> the schedule poller sets. Left
    ///     unresolved, <c>Location</c> was previously duplicated straight into
    ///     <see cref="Domain.Contracts.Messages.BroadcastQueueMessage.MarketId" />, silently
    ///     breaking every per-market admin lookup keyed by the canonical id (filler statements,
    ///     priority roads, market guidance) whenever a caller's display text doesn't match the
    ///     table's lowercase short id — observed live: "Mildura Drive Traffic" never matched the
    ///     "mildura" row, so 5 configured filler statements never topped up short reads even
    ///     though the gap fit them easily. Resolution is delegated to the single shared
    ///     <see cref="MarketLocationResolver" /> so this trigger, job creation and the queue
    ///     message can never disagree (see <see cref="MarketLocationResolver.Resolve" /> for the
    ///     match fields — MarketId, Name or the Station display name — and the no-match fallback).
    ///     Fail-soft: a repository error degrades to the old behaviour rather than blocking the
    ///     broadcast.
    /// </summary>
    /// <returns>
    ///     The canonical market id, and whether a configured schedule actually backed it.
    ///     A false <c>HasSchedule</c> means every per-market lookup will find nothing, so the
    ///     caller warns instead of letting the gap pass unremarked. A repository failure reports
    ///     <c>true</c> deliberately: we could not read the schedules, so we do not know the
    ///     market is unconfigured and must not claim it is.
    /// </returns>
    private async Task<(string MarketId, bool HasSchedule)> ResolveMarketIdAsync(
        string location, CancellationToken cancellationToken)
    {
        var schedulesResult = await scheduleRepository.ListSchedulesAsync(cancellationToken);
        if (schedulesResult.IsFailure)
        {
            logger.LogWarning(
                "[Orchestrate] Could not resolve MarketId for ad-hoc Location {Location} (schedule lookup failed: {Error}); using Location as-is.",
                location, schedulesResult.Error);
            return (location, true);
        }

        var resolution = MarketLocationResolver.Resolve(schedulesResult.Value, location);
        return (resolution.MarketId, resolution.HasSchedule);
    }
}
internal sealed record BroadcastPreflightResult(
    ValidatedOrchestrationRequestDto Validated,
    string MarketId,
    bool HasSchedule);

internal static class BroadcastInteractiveWorkKeys
{
    internal static InteractiveWorkKey ForRequest(
        HttpRequestData request,
        OrchestrationRequestDto dto)
    {
        string principalId = ClientPrincipal.Parse(request)?.ObjectId ?? "anonymous";
        return new InteractiveWorkKey(
            principalId,
            "preflight",
            "request",
            System.Text.Json.JsonSerializer.Serialize(dto),
            Model: dto.VoiceModel ?? dto.ModelId,
            Settings: dto.VoiceSettings is null
                ? string.Empty
                : System.Text.Json.JsonSerializer.Serialize(dto.VoiceSettings),
            Dictionary: dto.VoiceProfileId,
            Style: dto.VoiceName);
    }
}

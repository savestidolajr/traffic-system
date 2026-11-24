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
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.Scheduling;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using StorageKey = ArnBulletinGenerator.Functions.Http.StorageKey;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     HTTP trigger surface for market schedules: list, get, create-or-update, and delete,
///     scoping results to the caller's markets.
/// </summary>
public sealed class ScheduleHttpTrigger(
    IScheduleRepository scheduleRepository,
    IScheduleService scheduleService,
    IMarketAccessService marketAccess,
    IVoiceProfileService voiceProfiles,
    IDeliveryStyleService deliveryStyles,
    IBulletinPlaybookService playbooks,
    IPronunciationDictionaryService pronunciationDictionaries,
    IBookendService bookends,
    IOptions<BroadcastOptions> broadcastOptions,
    ILogger<ScheduleHttpTrigger> logger)
{
    // Substrings that mark a CreateOrUpdate failure as request validation (→ 400) rather than
    // a server/storage error (→ 500). Covers MarketSchedule.Create + service cron/timezone checks.
    private static readonly string[] ValidationTokens =
        ["invalid", "required", "must ", "not an allowed", "missing", "limits", "does not exist"];

    [Function("ListSchedules")]
    public async Task<HttpResponseData> ListSchedules(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "schedules")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        logger.LogInformation("Listing schedules");
        var result = await scheduleRepository.ListSchedulesAsync(ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var schedules = result.Value.ToList();

        // Market scoping: scoped operators see only their assigned markets.
        var principal = ClientPrincipal.Parse(req);
        if (principal is not null)
        {
            var allowed = await marketAccess.GetAllowedMarketsAsync(
                principal.ObjectId, principal.IsAdmin, ctx.CancellationToken);
            if (allowed.HasValue)
            {
                var allowedMarkets = new HashSet<string>(
                    allowed.Value.Select(m => m.Trim()), StringComparer.OrdinalIgnoreCase);
                schedules = schedules.Where(s => allowedMarkets.Contains(s.MarketId.Trim())).ToList();
            }
        }

        var items = schedules.Select(s => s.ToDto()).ToList();
        return await HttpResponseHelper.OkJsonAsync(req, new ScheduleListResponseDto(items, items.Count));
    }

    [Function("GetSchedule")]
    public async Task<HttpResponseData> GetSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "schedules/{marketId}")]
        HttpRequestData req,
        string marketId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(marketId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.MarketIdRequired);

        if (await marketAccess.EnsureMarketAccessAsync(req, marketId, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        logger.LogInformation("Getting schedule for market {MarketId}", marketId);
        var result = await scheduleRepository.GetScheduleAsync(marketId, ctx.CancellationToken);
        return result.IsFailure
            ? await HttpResponseHelper.NotFoundAsync(req, result.Error)
            : await HttpResponseHelper.OkJsonAsync(req, result.Value.ToDto());
    }

    [Function("CreateOrUpdateSchedule")]
    public async Task<HttpResponseData> CreateOrUpdateSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "schedules/{marketId}")]
        HttpRequestData req,
        string marketId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(marketId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.MarketIdRequired);
        if (StorageKey.RejectIfInvalid(req, "marketId", marketId) is { } badKey)
            return await badKey;

        if (await marketAccess.EnsureMarketAccessAsync(req, marketId, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        logger.LogInformation("Creating/updating schedule for market {MarketId}", marketId);

        var parseResult = await HttpRequestParser.ParseAsync<UpsertScheduleRequest>(req, ctx.CancellationToken);
        if (parseResult.IsFailure) return await HttpResponseHelper.BadRequestAsync(req, parseResult.Error);

        var dto = parseResult.Value;

        // The URL segment is the id of record. A body MarketId that disagrees was silently
        // discarded under a 200, so an operator editing the wrong row got no signal.
        if (!string.IsNullOrWhiteSpace(dto.MarketId)
            && !string.Equals(dto.MarketId.Trim(), marketId.Trim(), StringComparison.OrdinalIgnoreCase))
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Body MarketId '{dto.MarketId}' does not match the URL market '{marketId}'. "
                + "Omit MarketId, or send the one being edited.");

        // Blank Name/Station meant "not supplied", so clearing a required field returned 200 with
        // the edit discarded. Shared with the import planner so both write paths agree.
        var fieldsValid = scheduleService.ValidateEditableFields(dto.Name, dto.Station);
        if (fieldsValid.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, fieldsValid.Error);

        var coverage = Option<CoverageArea>.None;
        if (dto.Coverage is not null)
        {
            // An explicitly supplied coverage MUST parse; silently keeping the
            // previous shape would make the save lie to the operator.
            var covResult = CoverageMapping.ToDomain(dto.Coverage);
            if (covResult.IsFailure)
                return await HttpResponseHelper.BadRequestAsync(req, covResult.Error);

            // A geographic sanity bound on LOCATION (the Create* factories only check WGS84
            // ranges and SIZE, and must stay permissive enough to reconstruct persisted rows).
            // Enforced here, at the operator-input boundary: a coverage on the wrong continent
            // would silently fetch and air the wrong region's incidents.
            if (!CoverageArea.WithinOperatingRegion(covResult.Value))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Coverage must lie inside the platform's operating region "
                    + $"(lat {CoverageArea.MinOperatingLatitude}..{CoverageArea.MaxOperatingLatitude}, "
                    + $"lng {CoverageArea.MinOperatingLongitude}..{CoverageArea.MaxOperatingLongitude}); this area is outside it.");

            coverage = Option<CoverageArea>.Some(covResult.Value);
        }

        var contentSelection = Option<BulletinContentSelection>.None;
        if (dto.Content is not null)
        {
            var selection = dto.Content.ToSelection();
            if (!selection.HasAnySource)
                return await HttpResponseHelper.BadRequestAsync(req,
                    "At least one content source (traffic, weather, fuel) must be enabled.");
            // Required implies selected: a fixture flag on a source that is not enabled could
            // never air, which is the exact contract violation "always include" exists to prevent.
            if (selection.WeatherRequired && !selection.Weather)
                return await HttpResponseHelper.BadRequestAsync(req,
                    "WeatherRequired is set but weather is not enabled as a content source.");
            if (selection.FuelRequired && !selection.Fuel)
                return await HttpResponseHelper.BadRequestAsync(req,
                    "FuelRequired is set but fuel is not enabled as a content source.");
            contentSelection = Option<BulletinContentSelection>.Some(selection);
        }

        // Referential integrity: resolve all requested references in one bounded read per
        // library. The batch calls start together so a schedule PUT does not serialize four
        // independent point-read bursts. The current row's status is authoritative; a missing or
        // archived item is never admitted from a cached display summary.
        Result<VoiceRotationPolicy>? voiceRotationResult =
            dto.VoiceRotation is null ? null : VoiceRotationMapping.ToDomain(dto.VoiceRotation);
        var voiceIds = new List<string>();
        AddUniqueId(voiceIds, dto.VoiceProfileId);
        if (voiceRotationResult?.IsSuccess == true)
            foreach (var id in voiceRotationResult.Value.AllReferencedVoiceIds())
                AddUniqueId(voiceIds, id);

        var styleIds = new List<string>();
        AddUniqueId(styleIds, dto.DeliveryStyleId);
        var playbookIds = new List<string>();
        AddUniqueId(playbookIds, dto.PlaybookId);
        var dictionaryIds = new List<string>();
        AddUniqueId(dictionaryIds, dto.PronunciationDictionaryId);

        var voiceTask = voiceIds.Count == 0
            ? Task.FromResult(Result<IReadOnlyDictionary<string, Option<VoiceProfile>>>.Success(
                new Dictionary<string, Option<VoiceProfile>>(StringComparer.Ordinal)))
            : voiceProfiles.GetManyAsync(voiceIds, includeHistory: false,
                cancellationToken: ctx.CancellationToken);
        var styleTask = styleIds.Count == 0
            ? Task.FromResult(Result<IReadOnlyDictionary<string, Option<DeliveryStyle>>>.Success(
                new Dictionary<string, Option<DeliveryStyle>>(StringComparer.Ordinal)))
            : deliveryStyles.GetManyAsync(styleIds, includeHistory: false,
                cancellationToken: ctx.CancellationToken);
        var playbookTask = playbookIds.Count == 0
            ? Task.FromResult(Result<IReadOnlyDictionary<string, Option<BulletinPlaybook>>>.Success(
                new Dictionary<string, Option<BulletinPlaybook>>(StringComparer.Ordinal)))
            : playbooks.GetManyAsync(playbookIds, includeHistory: false,
                ctx.CancellationToken);
        var dictionaryTask = dictionaryIds.Count == 0
            ? Task.FromResult(Result<IReadOnlyDictionary<string, Option<PronunciationDictionary>>>.Success(
                new Dictionary<string, Option<PronunciationDictionary>>(StringComparer.Ordinal)))
            : pronunciationDictionaries.GetManyAsync(dictionaryIds, includeHistory: false,
                cancellationToken: ctx.CancellationToken);

        var voices = await voiceTask;
        var styles = await styleTask;
        var resolvedPlaybooks = await playbookTask;
        var dictionaries = await dictionaryTask;

        if (!string.IsNullOrWhiteSpace(dto.VoiceProfileId))
        {
            if (!TryGetBatchValue(voices, dto.VoiceProfileId, out var voice))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Voice profile '{dto.VoiceProfileId}' does not exist.");
            if (!string.Equals(voice.Status, "active", StringComparison.OrdinalIgnoreCase))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Voice profile '{dto.VoiceProfileId}' is archived and cannot be assigned.");
        }

        if (!string.IsNullOrWhiteSpace(dto.DeliveryStyleId))
        {
            if (!TryGetBatchValue(styles, dto.DeliveryStyleId, out var style))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Delivery style '{dto.DeliveryStyleId}' does not exist.");
            if (!string.Equals(style.Status, "active", StringComparison.OrdinalIgnoreCase))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Delivery style '{dto.DeliveryStyleId}' is archived and cannot be assigned.");
        }

        if (!string.IsNullOrWhiteSpace(dto.PlaybookId))
        {
            if (!TryGetBatchValue(resolvedPlaybooks, dto.PlaybookId, out var playbook))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Bulletin playbook '{dto.PlaybookId}' does not exist.");
            if (!string.Equals(playbook.Status, "active", StringComparison.OrdinalIgnoreCase))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Bulletin playbook '{dto.PlaybookId}' is archived and cannot be assigned.");
        }

        if (!string.IsNullOrWhiteSpace(dto.PronunciationDictionaryId))
        {
            if (!TryGetBatchValue(dictionaries, dto.PronunciationDictionaryId, out var dictionary))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Pronunciation dictionary '{dto.PronunciationDictionaryId}' does not exist.");
            if (!string.Equals(dictionary.Status, "active", StringComparison.OrdinalIgnoreCase))
                return await HttpResponseHelper.BadRequestAsync(req,
                    $"Pronunciation dictionary '{dto.PronunciationDictionaryId}' is archived and cannot be assigned.");
        }

        // Map + validate the voice-rotation policy and reject any rotation member (round-robin
        // set or window) that points at a voice profile that does not exist — a dangling member
        // would silently degrade to the primary/default voice at airtime. All members were
        // included in the single authoritative voice batch above.
        var rotation = Option<VoiceRotationPolicy>.None;
        if (dto.VoiceRotation is not null)
        {
            if (voiceRotationResult!.IsFailure)
                return await HttpResponseHelper.BadRequestAsync(req, voiceRotationResult.Error);
            foreach (var id in voiceRotationResult.Value.AllReferencedVoiceIds())
            {
                if (!TryGetBatchValue(voices, id, out var member))
                    return await HttpResponseHelper.BadRequestAsync(req,
                        $"Voice profile '{id}' does not exist.");
                if (!string.Equals(member.Status, "active", StringComparison.OrdinalIgnoreCase))
                    return await HttpResponseHelper.BadRequestAsync(req,
                        $"Voice profile '{id}' is archived and cannot be assigned.");
            }
            rotation = Option<VoiceRotationPolicy>.Some(voiceRotationResult.Value);
        }

        // Bookend assignments must pass referential integrity before anything is persisted:
        // an unknown pair, an archived pair, or a rotation member that does not exist would
        // silently degrade at airtime the same way a dangling voice-rotation member does. There
        // is ONE pair library, so a wrong-kind assignment is impossible by construction — resolve
        // every requested pair once with the authoritative keyed batch read.
        var bookendRotation = Option<BookendRotationPolicy>.None;
        if (dto.BookendRotation is not null)
        {
            var rotationResult = BookendRotationMapping.ToDomain(dto.BookendRotation);
            if (rotationResult.IsFailure)
                return await HttpResponseHelper.BadRequestAsync(req, rotationResult.Error);
            bookendRotation = Option<BookendRotationPolicy>.Some(rotationResult.Value);
        }

        // Every bookend id that could air in a slot: the primary pair plus any rotation member
        // (a TimeOfDay rotation falls back to the primary, so it is referenced too).
        var bookendIds = new List<string>();
        AddUniqueId(bookendIds, dto.BookendId?.Trim());
        if (bookendRotation.HasValue)
            foreach (var id in bookendRotation.Value.AllReferencedBookendIds())
                AddUniqueId(bookendIds, id);

        // The station/token values the bookend validation AND the fit advisory see are the
        // EFFECTIVE post-save values, not just the request's delta: a rotation-only edit (no
        // BookendTokens/Station in the body) used to be rejected demanding values the schedule
        // already stores.
        string bookendStation = dto.Station ?? "";
        IReadOnlyDictionary<string, string>? effectiveTokens = dto.BookendTokens;
        IReadOnlyDictionary<string, Bookend>? bookendsById = null;

        if (bookendIds.Count > 0)
        {
            var requestedBookends = await bookends.GetManyAsync(
                bookendIds, includeHistory: false, ctx.CancellationToken);
            if (requestedBookends.IsFailure)
                return await HttpResponseHelper.InternalErrorAsync(req, requestedBookends.Error);

            bookendsById = requestedBookends.Value
                .Where(pair => pair.Value.HasValue)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);

            var bookendError = ValidateBookendReferences(bookendIds, bookendsById);
            if (bookendError is not null)
                return await HttpResponseHelper.BadRequestAsync(req, bookendError);

            // A station must never be saved into a state that would air a bookend with an
            // unfillable token: the resolver SILENTLY drops an unknown token, so the sentence
            // would broadcast with a word missing. Every referenced bookend's current intro AND
            // outro must be fully resolvable against the built-ins plus this request's token
            // overrides.
            if (effectiveTokens is null || string.IsNullOrWhiteSpace(bookendStation))
            {
                var existingSchedule = await scheduleRepository.GetScheduleAsync(marketId, ctx.CancellationToken);
                if (existingSchedule.IsSuccess)
                {
                    var stored = existingSchedule.Value;
                    if (string.IsNullOrWhiteSpace(bookendStation))
                        bookendStation = stored.Station;
                    effectiveTokens ??= stored.BookendTokens;
                }
            }

            var tokenValues = BookendTokenResolver.BuildValues(marketId, bookendStation, effectiveTokens);
            var tokenError = ValidateBookendTokens(bookendIds, bookendsById, tokenValues);
            if (tokenError is not null)
                return await HttpResponseHelper.BadRequestAsync(req, tokenError);
        }

        var calendar = Option<ScheduleCalendar>.None;
        if (dto.Calendar is not null)
        {
            var calendarResult = dto.Calendar.ToDomain();
            if (calendarResult.IsFailure)
                return await HttpResponseHelper.BadRequestAsync(req, calendarResult.Error);
            calendar = Option<ScheduleCalendar>.Some(calendarResult.Value);
        }

        var fillOrder = dto.FillOrder is { Count: > 0 }
            ? Option<IReadOnlyList<FillSource>>.Some(
                dto.FillOrder.Select(ScheduleMappingExtensions.WireToFillSource).ToList())
            : Option<IReadOnlyList<FillSource>>.None;

        var result = await scheduleService.CreateOrUpdateAsync(
            ScheduleCommandBuilder.BuildUpdateCommand(marketId, dto, coverage, contentSelection, rotation, fillOrder,
                bookendRotation, calendar),
            ScheduleCommandBuilder.BuildCreateCommand(marketId, dto, coverage, contentSelection, rotation, fillOrder,
                bookendRotation, calendar),
            ctx.CancellationToken);

        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(req, result.Error, (r, err) =>
            {
                // Request-validation failures (cron, timezone, domain field rules, missing create
                // data, disallowed model) are client errors → 400. Anything else (e.g. a storage
                // save failure) is a genuine server error → 500.
                bool isValidation = ValidationTokens.Any(t => err.Contains(t, StringComparison.OrdinalIgnoreCase));
                return isValidation
                    ? HttpResponseHelper.BadRequestAsync(r, err)
                    : HttpResponseHelper.InternalErrorAsync(r, err);
            });

        // Assignment-time advisory (NOT a rejection): when a bookend pair cannot share the slot
        // with a minimum viable traffic read, generation drops the bookend and airs the traffic
        // (finding-4 policy, BookendSlotFitPlanner), so this save is allowed — but the operator
        // who assigns the pair must learn AT SAVE TIME that this station will not read it on air.
        // Evaluated with the SAME policy constant and pace model as the engine, and against the
        // SAVED schedule's actual slot, so the message can never disagree with what generation
        // will do. The primary pair (bookendIds[0]) is the one that airs in the default single
        // mode and is the fallback for every rotation policy.
        string? bookendFitWarning = null;
        if (bookendsById is not null && bookendsById.TryGetValue(bookendIds[0], out var primaryBookend))
        {
            var intro = BookendTokenResolver.Resolve(
                primaryBookend.CurrentTemplate.IntroText, marketId, bookendStation, effectiveTokens);
            var outro = BookendTokenResolver.Resolve(
                primaryBookend.CurrentTemplate.OutroText, marketId, bookendStation, effectiveTokens);
            var pair = new ResolvedBookends(intro.Text, outro.Text, primaryBookend.Id, primaryBookend.CurrentVersion);
            int wordCount = BulletinBookendComposer.WordCount(Option<ResolvedBookends>.Some(pair));
            double secondsPerWord = 60.0 / Math.Max(1, broadcastOptions.Value.WordsPerMinute);
            var fit = BookendSlotFitPlanner.Evaluate(
                wordCount, result.Value.Schedule.TargetDurationSeconds, secondsPerWord);
            if (!fit.Fits)
            {
                bookendFitWarning =
                    $"the {wordCount}-word bookend pair needs {fit.RequiredSeconds:0.#}s of the "
                    + $"{fit.SlotSeconds:0.#}s slot (bookends {fit.BookendSeconds:0.#}s + min viable traffic "
                    + $"{fit.MinimumViableTrafficSeconds:0.#}s) — it will not fit; the bulletin will drop the "
                    + "bookend and air the traffic.";
            }
        }

        var responseDto = result.Value.Schedule.ToDto();
        return bookendFitWarning is null
            ? await HttpResponseHelper.OkJsonAsync(req, responseDto)
            : await HttpResponseHelper.OkJsonAsync(req, responseDto with { BookendFitWarning = bookendFitWarning });
    }

    [Function("DeleteSchedule")]
    public async Task<HttpResponseData> DeleteSchedule(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "schedules/{marketId}")]
        HttpRequestData req,
        string marketId, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(marketId))
            return await HttpResponseHelper.BadRequestAsync(req, ErrorMessages.Http.MarketIdRequired);
        if (StorageKey.RejectIfInvalid(req, "marketId", marketId) is { } badKey)
            return await badKey;

        if (await marketAccess.EnsureMarketAccessAsync(req, marketId, logger, ctx.CancellationToken) is { } forbidden)
            return forbidden;

        logger.LogInformation("Deleting schedule for market {MarketId}", marketId);
        var result = await scheduleRepository.DeleteScheduleAsync(marketId, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.MapFailureAsync(req, result.Error,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e), extraNotFoundNeedle: "does not exist");

        return HttpResponseHelper.NoContent(req);
    }

    private static void AddUniqueId(List<string> ids, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && !ids.Contains(id, StringComparer.Ordinal))
            ids.Add(id);
    }

    private static bool TryGetBatchValue<T>(
        Result<IReadOnlyDictionary<string, Option<T>>> result,
        string id,
        out T value)
        where T : class
    {
        value = default!;
        if (result.IsFailure
            || !result.Value.TryGetValue(id, out var option)
            || option.IsNone)
            return false;

        value = option.Value;
        return true;
    }

    /// <summary>
    ///     Market scoping guard: returns a 403 response when the calling non-admin is not
    ///     assigned the given market; null when the action may proceed. Admins are unrestricted,
    ///     while missing and empty assignments are also unrestricted.
    /// </summary>

    /// <summary>
    ///     Validates that every id a slot could air is a known, ACTIVE bookend pair. Returns an
    ///     error message for the first violation, or null when all pass.
    /// </summary>
    private static string? ValidateBookendReferences(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, Bookend> bookendsById)
    {
        foreach (var id in ids)
        {
            if (!bookendsById.TryGetValue(id, out var bookend))
                return $"Bookend '{id}' does not exist.";

            if (!string.Equals(bookend.Status, "active", StringComparison.OrdinalIgnoreCase))
                return $"Bookend '{id}' is archived and cannot be assigned.";
        }

        return null;
    }

    /// <summary>
    ///     Validates that every token in each referenced bookend's current intro AND outro text can
    ///     be filled for this station (built-in or supplied override). Returns an error message
    ///     naming the unresolved tokens, or null when all resolve. The resolver drops unknown
    ///     tokens at airtime, so an unfillable token here would broadcast a sentence with a
    ///     missing word.
    /// </summary>
    private static string? ValidateBookendTokens(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, Bookend> bookendsById,
        IReadOnlyDictionary<string, string> tokenValues)
    {
        foreach (var id in ids)
        {
            var template = bookendsById[id].CurrentTemplate;
            var unresolved = BookendTokenResolver.ExtractTokens(template.IntroText)
                .Concat(BookendTokenResolver.ExtractTokens(template.OutroText))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(t => !tokenValues.ContainsKey(t))
                .ToList();
            if (unresolved.Count > 0)
                return $"Bookend '{id}' uses token(s) "
                       + $"{{{string.Join("}, {", unresolved)}}} that have no value for this station; "
                       + "add them to BookendTokens.";
        }

        return null;
    }
}

using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Mapping;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     The shared voice library: named, versioned tunings ("variants") of a base ElevenLabs voice
///     that stations read with. CRUD + version restore is delegated to <see cref="IVoiceProfileService" />;
///     each version's measured calibration is surfaced from the calibration store. Preview/remix
///     audition endpoints live in <see cref="VoiceProfilePreviewHttpTrigger" />.
/// </summary>
public sealed class VoiceProfilesHttpTrigger(
    IVoiceProfileService voiceProfiles,
    IVoiceCalibrationRepository calibrationRepository,
    IVoiceProvider voiceProvider,
    IOptions<ElevenLabsOptions> elevenLabsOptions)
{
    /// <summary>
    ///     Provider voice id to its CURRENT name, fetched at most once per request. Null until the
    ///     first lookup; an empty map means the provider could not be reached and every profile
    ///     falls back to its stored name. The trigger is resolved per invocation, so this never
    ///     outlives a single request.
    /// </summary>
    private IReadOnlyDictionary<string, string>? _providerVoiceNames;

    [Function("ListVoiceProfiles")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "voices/profiles")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var query = VersionedLibraryListQueryParser.Parse(req);
        if (query.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, query.Error);

        Result<VersionedLibraryListPage<VoiceProfile>>? result =
            await voiceProfiles.ListAsync(query.Value, ctx.CancellationToken);
        if (result is null)
        {
            // Keep lightweight/legacy service doubles usable while the paged contract rolls out.
            var legacy = await voiceProfiles.ListAsync(ctx.CancellationToken);
            if (legacy is null)
                return await HttpResponseHelper.InternalErrorAsync(req, "Voice profile list failed.");
            result = legacy.IsFailure
                ? legacy.PropagateFailure<VersionedLibraryListPage<VoiceProfile>>()
                : Result<VersionedLibraryListPage<VoiceProfile>>.Success(
                    new VersionedLibraryListPage<VoiceProfile>(legacy.Value, legacy.Value.Count));
        }

        var calibrationBatch = await ReadCalibrationBatchAsync(result.Value.Items, ctx.CancellationToken);
        var items = new List<VoiceProfileDto>(result.Value.Items.Count);
        foreach (var profile in result.Value.Items)
            items.Add(await ToDtoAsync(profile, ctx.CancellationToken, calibrationBatch));
        // Keep the historical object shape unless a bounded page was explicitly requested.
        if (!query.Value.IsPaged)
            return await HttpResponseHelper.OkJsonAsync(req, new VoiceProfilesListResponseDto(items, result.Value.Total));
        return await HttpResponseHelper.OkJsonAsync(req, new
        {
            Items = items,
            Total = result.Value.Total,
            NextCursor = result.Value.NextCursor
        });
    }

    [Function("CreateVoiceProfile")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voices/profiles")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var parse = await HttpRequestParser.ParseAsync<CreateVoiceProfileRequestDto>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if ((dto.Name?.Length ?? 0) > ConfigValidationLimits.VoiceProfileNameMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Name must be at most {ConfigValidationLimits.VoiceProfileNameMax} characters.");
        if ((dto.Description?.Length ?? 0) > ConfigValidationLimits.VoiceProfileDescriptionMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Description must be at most {ConfigValidationLimits.VoiceProfileDescriptionMax} characters.");
        // The version note is the only unbounded field a profile version carries; capped here so
        // an oversized note is a typed 400 and never reaches the provider mint.
        if ((dto.ChangeNote?.Length ?? 0) > ConfigValidationLimits.VoiceProfileChangeNoteMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Change note must be at most {ConfigValidationLimits.VoiceProfileChangeNoteMax} characters.");

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var existingForDuplicateCheck = await voiceProfiles.ListAsync(ctx.CancellationToken);
            if (existingForDuplicateCheck.IsSuccess && existingForDuplicateCheck.Value.Any(p =>
                    string.Equals(p.Name, dto.Name, StringComparison.OrdinalIgnoreCase)))
                return await HttpResponseHelper.ConflictAsync(req,
                    $"A voice profile named '{dto.Name}' already exists.");
        }

        if (dto.Settings is null)
            return await HttpResponseHelper.BadRequestAsync(req, "Settings are required.");

        var settings = dto.Settings.ToDomain();
        if (settings.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, settings.Error);

        var model = string.IsNullOrWhiteSpace(dto.Model) ? elevenLabsOptions.Value.ModelId : dto.Model;

        // Adoption mints the previewed voice inside the service, which pre-flights the storage
        // ceiling and rolls the mint back if the local save fails — never a billed orphan here.
        var input = new CreateVoiceProfileInput(dto.Name ?? string.Empty, dto.Description,
            dto.BaseVoiceId ?? string.Empty, dto.BaseVoiceName ?? string.Empty, model, settings.Value,
            dto.ChangeNote, dto.PaceBoostWpm,
            RemixedVoiceId: dto.RemixedVoiceId, RemixDescription: dto.RemixDescription);

        var result = await voiceProfiles.CreateAsync(input, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await RespondWithProfileAsync(req, result, ctx.CancellationToken);
    }

    [Function("GetVoiceProfile")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "voices/profiles/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        // Defensive: literal sibling routes ("preview") should never reach the {id} handler,
        // but if routing precedence ever changes, fail clearly rather than 404-on-lookup.
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A voice profile id is required.");

        var result = await voiceProfiles.GetAsync(id, ctx.CancellationToken);
        return await RespondWithProfileAsync(req, result, ctx.CancellationToken);
    }

    [Function("UpdateVoiceProfile")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "voices/profiles/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A voice profile id is required.");

        var parse = await HttpRequestParser.ParseAsync<UpdateVoiceProfileRequestDto>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if ((dto.Name?.Length ?? 0) > ConfigValidationLimits.VoiceProfileNameMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Name must be at most {ConfigValidationLimits.VoiceProfileNameMax} characters.");
        if ((dto.Description?.Length ?? 0) > ConfigValidationLimits.VoiceProfileDescriptionMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Description must be at most {ConfigValidationLimits.VoiceProfileDescriptionMax} characters.");
        if ((dto.ChangeNote?.Length ?? 0) > ConfigValidationLimits.VoiceProfileChangeNoteMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Change note must be at most {ConfigValidationLimits.VoiceProfileChangeNoteMax} characters.");

        if (dto.Settings is null)
            return await HttpResponseHelper.BadRequestAsync(req, "Settings are required.");

        var settings = dto.Settings.ToDomain();
        if (settings.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, settings.Error);

        var model = string.IsNullOrWhiteSpace(dto.Model) ? elevenLabsOptions.Value.ModelId : dto.Model;

        // Adoption mints the previewed voice inside the service, which checks the profile exists,
        // pre-flights the storage ceiling, and rolls the mint back if the local save fails.
        var input = new UpdateVoiceProfileInput(dto.Name ?? string.Empty, dto.Description, model, settings.Value,
            dto.ChangeNote,
            BaseVoiceName: dto.BaseVoiceName, RemixedVoiceId: dto.RemixedVoiceId,
            RemixDescription: dto.RemixDescription, PaceBoostWpm: dto.PaceBoostWpm);

        var result = await voiceProfiles.UpdateAsync(id, input, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await RespondWithProfileAsync(req, result, ctx.CancellationToken);
    }

    [Function("RestoreVoiceProfileVersion")]
    public async Task<HttpResponseData> Restore(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voices/profiles/{id}/versions/{version}/restore")]
        HttpRequestData req,
        string id, string version, FunctionContext ctx)
    {
        if (!int.TryParse(version, out var versionNumber))
            return await HttpResponseHelper.BadRequestAsync(req, "Version must be an integer.");

        var result = await voiceProfiles.RestoreVersionAsync(id, versionNumber, ClientPrincipal.Parse(req)?.Name,
            ctx.CancellationToken);
        return await RespondWithProfileAsync(req, result, ctx.CancellationToken);
    }

    [Function("ArchiveVoiceProfile")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voices/profiles/{id}/archive")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A voice profile id is required.");

        var result = await voiceProfiles.ArchiveAsync(id, ctx.CancellationToken);
        return await RespondWithProfileAsync(req, result, ctx.CancellationToken);
    }

    [Function("UnarchiveVoiceProfile")]
    public async Task<HttpResponseData> Unarchive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voices/profiles/{id}/unarchive")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A voice profile id is required.");

        var result = await voiceProfiles.UnarchiveAsync(id, ctx.CancellationToken);
        return await RespondWithProfileAsync(req, result, ctx.CancellationToken);
    }

    [Function("DeleteVoiceProfile")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "voices/profiles/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A voice profile id is required.");

        var result = await voiceProfiles.DeleteAsync(id, ctx.CancellationToken);
        if (result.IsFailure)
            // Typed mapping: an in-use item returns a Conflict whose message names the
            // stations, and message matching alone would send that to a 500.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        return HttpResponseHelper.NoContent(req);
    }

    private async Task<HttpResponseData> RespondWithProfileAsync(
        HttpRequestData req, Result<VoiceProfile> result, CancellationToken ct)
    {
        if (result.IsFailure)
            // Typed disposition first: the trimmer's over-ceiling refusal is an InvalidInput
            // (a permanent client-correctable input error → 400), which message matching alone
            // would send to a misleading 500. Untyped failures keep the historical message
            // classification below.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) => e.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                          e.Contains("must be between", StringComparison.OrdinalIgnoreCase) ||
                          e.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase)
                    ? HttpResponseHelper.BadRequestAsync(r, e)
                    : HttpResponseHelper.InternalErrorAsync(r, e));

        return await HttpResponseHelper.OkJsonAsync(req, await ToDtoAsync(result.Value, ct));
    }

    /// <summary>
    ///     Reads all calibration keys needed by a profile-list response once, deduplicating repeated
    ///     versions before the repository performs its bounded point reads. A null result keeps the
    ///     legacy per-version fail-soft path used by lightweight migration/test repositories.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, Option<VoiceCalibration>>?> ReadCalibrationBatchAsync(
        IReadOnlyList<VoiceProfile> profiles, CancellationToken ct)
    {
        var lookups = profiles
            .SelectMany(profile => profile.Versions.Select(version =>
                new VoiceCalibrationLookup(profile.BaseVoiceId, version.Settings, version.Model)))
            .Distinct()
            .ToArray();
        if (lookups.Length == 0)
            return new Dictionary<string, Option<VoiceCalibration>>(StringComparer.Ordinal);

        var batchTask = calibrationRepository.GetManyAsync(lookups, ct);
        if (batchTask is null)
            return null;
        var result = await batchTask;
        return result is { IsSuccess: true } ? result.Value : null;
    }

    /// <summary>
    ///     The base voice's display name, taken from the provider rather than from the copy stored
    ///     on the profile. <see cref="VoiceProfile.BaseVoiceName" /> is a snapshot written when the
    ///     variant was created and only changed by an explicit base-voice adoption, so a
    ///     provider-side rename would leave every profile referencing that voice showing a name it
    ///     no longer has — the same drift that let a config default read "ARN Hudson" for six weeks
    ///     while ElevenLabs called the voice "Peter Michael - Australian Male VO". Fail-soft: an
    ///     unreachable provider or an unknown id keeps the stored name.
    /// </summary>
    private async Task<string> ResolveBaseVoiceNameAsync(VoiceProfile profile, CancellationToken ct)
    {
        if (_providerVoiceNames is null)
        {
            var voices = await voiceProvider.GetVoicesAsync(ct);
            _providerVoiceNames = voices.IsSuccess
                ? voices.Value.ToDictionary(v => v.VoiceId, v => v.Name, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return _providerVoiceNames.TryGetValue(profile.BaseVoiceId, out string? current)
            ? current
            : profile.BaseVoiceName;
    }

    private static VoiceCalibrationDto ToCalibrationDto(VoiceCalibration c) =>
        new(c.WordsPerMinute, c.SyllablesPerSecond, c.SyllablesPerWord, c.RunCount,
            c.CalibratedAt == default ? null : c.CalibratedAt.ToString("o"));

    private async Task<VoiceProfileDto> ToDtoAsync(
        VoiceProfile profile,
        CancellationToken ct,
        IReadOnlyDictionary<string, Option<VoiceCalibration>>? calibrationBatch = null)
    {
        var versions = new List<VoiceProfileVersionDto>(profile.Versions.Count);
        foreach (var v in profile.Versions)
        {
            VoiceCalibrationDto? calibration = null;
            var lookup = new VoiceCalibrationLookup(profile.BaseVoiceId, v.Settings, v.Model);
            if (calibrationBatch is not null && calibrationBatch.TryGetValue(lookup.Key, out var cached))
            {
                if (cached.HasValue)
                    calibration = ToCalibrationDto(cached.Value);
            }
            else
            {
                Result<Option<VoiceCalibration>>? calResult =
                    await calibrationRepository.GetCalibrationAsync(profile.BaseVoiceId, v.Settings, ct, v.Model);
                // NSubstitute/migration repositories may not implement the optional model-aware
                // member yet. Preserve their legacy point-read behavior during rollout.
                calResult ??= await calibrationRepository.GetCalibrationAsync(profile.BaseVoiceId, v.Settings, ct);
                if (calResult.IsSuccess && calResult.Value.HasValue)
                    calibration = ToCalibrationDto(calResult.Value.Value);
            }

            versions.Add(new VoiceProfileVersionDto(
                v.Version,
                new VoiceSettingsDto(v.Settings.Speed, v.Settings.Stability, v.Settings.SimilarityBoost,
                    v.Settings.Style, v.Settings.UseSpeakerBoost),
                v.Model,
                v.ChangeNote,
                v.CreatedAt.ToString("o"),
                v.CreatedBy,
                v.SampleUrl,
                v.ConfigurationKey,
                calibration,
                [],
                v.PaceBoostWpm));
        }

        return new VoiceProfileDto(
            profile.Id,
            profile.Name,
            profile.Description,
            profile.BaseVoiceId,
            await ResolveBaseVoiceNameAsync(profile, ct),
            profile.Provider,
            profile.Status,
            profile.CurrentVersion,
            profile.CreatedAt.ToString("o"),
            profile.UpdatedAt.ToString("o"),
            profile.CreatedBy,
            profile.AssignedStations.ToList(),
            versions);
    }
}

// ─── Request DTOs (arrive PascalCase — the SPA pascalizes bodies before sending) ───

public sealed record CreateVoiceProfileRequestDto(
    string? Name = null,
    string? Description = null,
    string? BaseVoiceId = null,
    string? BaseVoiceName = null,
    string? Model = null,
    VoiceSettingsDto? Settings = null,
    string? ChangeNote = null,
    string? RemixedVoiceId = null,
    string? RemixDescription = null,
    double PaceBoostWpm = 0);

public sealed record UpdateVoiceProfileRequestDto(
    string? Name = null,
    string? Description = null,
    string? Model = null,
    VoiceSettingsDto? Settings = null,
    string? ChangeNote = null,
    string? BaseVoiceName = null,
    string? RemixedVoiceId = null,
    string? RemixDescription = null,
    double PaceBoostWpm = 0);

public sealed record RemixVoiceRequestDto(
    string? BaseVoiceId = null,
    string? VoiceDescription = null,
    double? PromptStrength = null);

public sealed record PreviewVoiceRequestDto(
    string? BaseVoiceId = null,
    string? Model = null,
    VoiceSettingsDto? Settings = null,
    double PaceBoostWpm = 0);

// ─── Response DTOs (PascalCase; the SPA's Zod schemas camelize on receipt) ───

public sealed record VoiceCalibrationDto(
    double WordsPerMinute,
    double SyllablesPerSecond,
    double SyllablesPerWord,
    int RunCount,
    string? CalibratedAt);

public sealed record VoiceRunSampleDto(
    string JobId,
    string? Station,
    string AudioUrl,
    double MeasuredDurationSec,
    string AiredAt);

public sealed record VoiceProfileVersionDto(
    int Version,
    VoiceSettingsDto Settings,
    string Model,
    string? ChangeNote,
    string CreatedAt,
    string? CreatedBy,
    string? SampleUrl,
    string ConfigurationKey,
    VoiceCalibrationDto? Calibration,
    IReadOnlyList<VoiceRunSampleDto> RunSamples,
    double PaceBoostWpm);

public sealed record VoiceProfileDto(
    string Id,
    string Name,
    string? Description,
    string BaseVoiceId,
    string BaseVoiceName,
    string Provider,
    string Status,
    int CurrentVersion,
    string CreatedAt,
    string UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<string> AssignedStations,
    IReadOnlyList<VoiceProfileVersionDto> Versions);

public sealed record VoiceProfilesListResponseDto(
    IReadOnlyList<VoiceProfileDto> Items,
    int Total);

public sealed record VoicePreviewResponseDto(
    string Url,
    double EstimatedDurationSec,
    double WordsPerMinute,
    double EffectivePaceBoostWpm);

public sealed record VoiceRemixPreviewResponseDto(
    string Url,
    string GeneratedVoiceId,
    double EstimatedDurationSec);

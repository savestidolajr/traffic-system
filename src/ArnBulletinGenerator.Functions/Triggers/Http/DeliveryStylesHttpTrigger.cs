using System.Globalization;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     The shared delivery-style library: named, versioned ElevenLabs v3 audio-tag policies that
///     stations read with. CRUD + version restore delegate to <see cref="IDeliveryStyleService" />;
///     preview runs the real director over a standard sample and synthesises it so an operator can
///     audition the tone (mirrors the voice-profile library).
/// </summary>
public sealed class DeliveryStylesHttpTrigger(
    IDeliveryStyleService deliveryStyles,
    IScriptDirector scriptDirector,
    IAudioSynthesizer audioSynthesizer,
    IBroadcastOutput broadcastOutput,
    IAudioUrlProvider audioUrlProvider,
    IAudioProcessor audioProcessor,
    IOptions<ElevenLabsOptions> elevenLabsOptions,
    IOptions<BroadcastOptions> broadcastOptions,
    TimeProvider timeProvider,
    ILogger<DeliveryStylesHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null,
    IPreviewAudioCleanup? previewAudioCleanup = null)
{
    // A neutral, on-air-length sample so a preview audibly demonstrates the tone.
    private const string PreviewScript =
        "Good morning — here is your traffic update. The Ring Road eastbound is the worst of it, " +
        "with roadworks and a lane blocked, and that's copping heavy delays. The run to the airport " +
        "is looking clear. Allow a few extra minutes through the tunnel, and drive safely out there.";

    [Function("ListDeliveryStyles")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "delivery-styles")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var query = VersionedLibraryListQueryParser.Parse(req);
        if (query.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, query.Error);

        var result = await deliveryStyles.ListAsync(query.Value, ctx.CancellationToken);
        if (result.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var items = result.Value.Items.Select(ToDto).ToList();
        if (!query.Value.IsPaged)
            return await HttpResponseHelper.OkJsonAsync(req,
                new DeliveryStylesListResponseDto(items, result.Value.Total));
        return await HttpResponseHelper.OkJsonAsync(req, new
        {
            Items = items,
            Total = result.Value.Total,
            NextCursor = result.Value.NextCursor
        });
    }

    // Distinct literal route so it always wins over delivery-styles/{id}.
    [Function("PreviewDeliveryStyle")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delivery-styles/preview")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var parse = await HttpRequestParser.ParseAsync<PreviewDeliveryStyleRequestDto>(req, ct);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if (dto.Policy is null || string.IsNullOrWhiteSpace(dto.Policy.ToneDescription))
            return await HttpResponseHelper.BadRequestAsync(req, "A delivery policy with a tone description is required.");

        var policy = dto.Policy.ToDomain();
        // Delivery tags are interpreted only by eleven_v3, so the preview auditions on the model
        // the STATION will actually air with (an explicit Model wins; otherwise the configured
        // default, preserving the historical behaviour). On a non-v3 model the director runs
        // punctuation-only — the tags are stripped before synthesis — and the response says
        // TagsSupported=false so the console can warn instead of showing a working audition for
        // cues that will never air.
        var model = string.IsNullOrWhiteSpace(dto.Model)
            ? elevenLabsOptions.Value.ModelId
            : dto.Model.Trim();
        var voiceId = string.IsNullOrWhiteSpace(dto.BaseVoiceId)
            ? broadcastOptions.Value.DefaultVoiceId
            : dto.BaseVoiceId!;
        logger.LogInformation("[DeliveryPreview] Auditioning style on voice {VoiceId} ({Tags} tags allowed)",
            voiceId, policy.AllowedTags.Count);

        string policyKey = string.Join(
            "\u001f",
            policy.ToneDescription,
            string.Join(",", policy.AllowedTags),
            policy.Intensity.ToString(CultureInfo.InvariantCulture));
        var gate = interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance;
        var directed = await gate.ExecuteLlmAsync(
            new InteractiveWorkKey(
                PrincipalKey(req),
                "delivery-style-direct",
                policyKey,
                $"{PreviewScript}\u001fintent={dto.Intent}",
                model,
                string.Empty,
                string.Empty,
                policyKey),
            gateCt => scriptDirector.DirectAsync(
                PreviewScript, policy, dto.Intent, model, cancellationToken: gateCt),
            ct);
        var synthScript = directed.IsSuccess && !string.IsNullOrWhiteSpace(directed.Value)
            ? directed.Value
            : PreviewScript;

        var ttsKey = new InteractiveWorkKey(
            PrincipalKey(req),
            "delivery-style-preview",
            policyKey,
            $"{synthScript}\u001fintent={dto.Intent}",
            model,
            string.Empty,
            string.Empty,
            policyKey);
        var operation = await gate.ExecuteTtsAsync(
            ttsKey,
            async operationCt =>
            {
                var synthResult = await audioSynthesizer.SynthesizeAsync(
                    synthScript, Option<VoiceSettings>.None, Option<string>.Some(voiceId),
                    Option<string>.Some(model), operationCt, Option<string>.None);
                if (synthResult.IsFailure)
                    return synthResult.PropagateFailure<DeliveryStylePreviewResponseDto>();

                var audio = synthResult.Value.Audio;
                operationCt.ThrowIfCancellationRequested();
                var fileName = $"deliverypreview_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss_fff}";
                var save = await broadcastOutput.SaveAsync(audio, fileName, operationCt);
                if (save.IsFailure)
                    return save.PropagateFailure<DeliveryStylePreviewResponseDto>();

                operationCt.ThrowIfCancellationRequested();
                var urlResult = await audioUrlProvider.GetAudioUrlAsync($"{fileName}.wav", operationCt);
                var duration = audioProcessor.GetDuration(audio);

                return Result<DeliveryStylePreviewResponseDto>.Success(
                    new DeliveryStylePreviewResponseDto(
                        urlResult.IsSuccess ? urlResult.Value : string.Empty,
                        Math.Round(duration),
                        DeliveryTags.Extract(synthScript).Count,
                        DeliveryTags.SupportsTags(model)));
            },
            ct);

        if (operation.IsFailure)
            return await HttpResponseHelper.MapTypedFailureAsync(req, operation.Error,
                operation.FailureMetadata, (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        if (previewAudioCleanup is not null && !string.IsNullOrWhiteSpace(operation.Value.Url))
            await previewAudioCleanup.PinAsync(operation.Value.Url, cancellationToken: CancellationToken.None);

        return await HttpResponseHelper.OkJsonAsync(req, operation.Value);
    }

    [Function("CreateDeliveryStyle")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delivery-styles")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var parse = await HttpRequestParser.ParseAsync<CreateDeliveryStyleRequestDto>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if ((dto.Name?.Length ?? 0) > ConfigValidationLimits.DeliveryStyleNameMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Name must be at most {ConfigValidationLimits.DeliveryStyleNameMax} characters.");
        if ((dto.Description?.Length ?? 0) > ConfigValidationLimits.DeliveryStyleDescriptionMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Description must be at most {ConfigValidationLimits.DeliveryStyleDescriptionMax} characters.");
        if (dto.Policy is null)
            return await HttpResponseHelper.BadRequestAsync(req, "A delivery policy is required.");
        if (dto.Policy.Intensity < DeliveryStylePolicy.MinIntensity || dto.Policy.Intensity > DeliveryStylePolicy.MaxIntensity)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Intensity must be between {DeliveryStylePolicy.MinIntensity} and {DeliveryStylePolicy.MaxIntensity}.");

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            var existingForDuplicateCheck = await deliveryStyles.ListAsync(ctx.CancellationToken);
            if (existingForDuplicateCheck.IsSuccess && existingForDuplicateCheck.Value.Any(s =>
                    string.Equals(s.Name, dto.Name, StringComparison.OrdinalIgnoreCase)))
                return await HttpResponseHelper.ConflictAsync(req,
                    $"A delivery style named '{dto.Name}' already exists.");
        }

        var input = new CreateDeliveryStyleInput(dto.Name ?? string.Empty, dto.Description, dto.Policy.ToDomain(),
            dto.ChangeNote);
        var result = await deliveryStyles.CreateAsync(input, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("GetDeliveryStyle")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "delivery-styles/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A delivery style id is required.");

        var result = await deliveryStyles.GetAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("UpdateDeliveryStyle")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "delivery-styles/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A delivery style id is required.");

        var parse = await HttpRequestParser.ParseAsync<UpdateDeliveryStyleRequestDto>(req, ctx.CancellationToken);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if ((dto.Name?.Length ?? 0) > ConfigValidationLimits.DeliveryStyleNameMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Name must be at most {ConfigValidationLimits.DeliveryStyleNameMax} characters.");
        if ((dto.Description?.Length ?? 0) > ConfigValidationLimits.DeliveryStyleDescriptionMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Description must be at most {ConfigValidationLimits.DeliveryStyleDescriptionMax} characters.");
        if (dto.Policy is null)
            return await HttpResponseHelper.BadRequestAsync(req, "A delivery policy is required.");
        if (dto.Policy.Intensity < DeliveryStylePolicy.MinIntensity || dto.Policy.Intensity > DeliveryStylePolicy.MaxIntensity)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Intensity must be between {DeliveryStylePolicy.MinIntensity} and {DeliveryStylePolicy.MaxIntensity}.");

        var input = new UpdateDeliveryStyleInput(dto.Name ?? string.Empty, dto.Description, dto.Policy.ToDomain(),
            dto.ChangeNote);
        var result = await deliveryStyles.UpdateAsync(id, input, ClientPrincipal.Parse(req)?.Name, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("RestoreDeliveryStyleVersion")]
    public async Task<HttpResponseData> Restore(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delivery-styles/{id}/versions/{version}/restore")]
        HttpRequestData req,
        string id, string version, FunctionContext ctx)
    {
        if (!int.TryParse(version, out var versionNumber))
            return await HttpResponseHelper.BadRequestAsync(req, "Version must be an integer.");

        var result = await deliveryStyles.RestoreVersionAsync(id, versionNumber, ClientPrincipal.Parse(req)?.Name,
            ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("ArchiveDeliveryStyle")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delivery-styles/{id}/archive")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A delivery style id is required.");

        var result = await deliveryStyles.ArchiveAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("UnarchiveDeliveryStyle")]
    public async Task<HttpResponseData> Unarchive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "delivery-styles/{id}/unarchive")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id))
            return await HttpResponseHelper.BadRequestAsync(req, "A delivery style id is required.");

        var result = await deliveryStyles.UnarchiveAsync(id, ctx.CancellationToken);
        return await Respond(req, result);
    }

    [Function("DeleteDeliveryStyle")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "delivery-styles/{id}")]
        HttpRequestData req,
        string id, FunctionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "preview")
            return await HttpResponseHelper.BadRequestAsync(req, "A delivery style id is required.");

        var result = await deliveryStyles.DeleteAsync(id, ctx.CancellationToken);
        if (result.IsFailure)
            // Typed mapping: an in-use item returns a Conflict whose message names the
            // stations, and message matching alone would send that to a 500.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        return HttpResponseHelper.NoContent(req);
    }

    private static async Task<HttpResponseData> Respond(HttpRequestData req, Result<DeliveryStyle> result)
    {
        if (result.IsFailure)
            // A TYPED disposition (e.g. an over-ceiling single version that VersionHistoryTrimmer
            // marks InvalidInput) must map to 400 — message matching alone cannot see it and would
            // send a permanent client-input error to a 500. The existing required/Invalid message
            // fallback is kept in front of the server-error default.
            return await HttpResponseHelper.MapTypedFailureAsync(req, result.Error, result.FailureMetadata,
                (r, e) =>
                    e.Contains("required", StringComparison.OrdinalIgnoreCase) ||
                    e.StartsWith("Invalid", StringComparison.OrdinalIgnoreCase)
                        ? HttpResponseHelper.BadRequestAsync(r, e)
                        : HttpResponseHelper.InternalErrorAsync(r, e));

        return await HttpResponseHelper.OkJsonAsync(req, ToDto(result.Value));
    }

    private static DeliveryStyleDto ToDto(DeliveryStyle style)
    {
        var versions = style.Versions.Select(v => new DeliveryStyleVersionDto(
            v.Version,
            new DeliveryStylePolicyDto(v.Policy.ToneDescription, v.Policy.AllowedTags.ToList(), v.Policy.Intensity),
            v.ChangeNote,
            v.CreatedAt.ToString("o"),
            v.CreatedBy)).ToList();

        return new DeliveryStyleDto(
            style.Id,
            style.Name,
            style.Description,
            style.Status,
            style.CurrentVersion,
            style.CreatedAt.ToString("o"),
            style.UpdatedAt.ToString("o"),
            style.CreatedBy,
            style.AssignedStations.ToList(),
            versions);
    }
    private static string PrincipalKey(HttpRequestData request) =>
        ClientPrincipal.Parse(request)?.ObjectId is { Length: > 0 } objectId
            ? objectId
            : "anonymous";

}

// ─── Request DTOs (arrive PascalCase — the SPA pascalizes bodies before sending) ───

public sealed record CreateDeliveryStyleRequestDto(
    string? Name = null,
    string? Description = null,
    DeliveryStylePolicyDto? Policy = null,
    string? ChangeNote = null);

public sealed record UpdateDeliveryStyleRequestDto(
    string? Name = null,
    string? Description = null,
    DeliveryStylePolicyDto? Policy = null,
    string? ChangeNote = null);

public sealed record PreviewDeliveryStyleRequestDto(
    DeliveryStylePolicyDto? Policy = null,
    string? BaseVoiceId = null,
    string? Intent = null,
    string? Model = null);

// ─── Response DTOs (PascalCase; the SPA's Zod schemas camelize on receipt) ───

public sealed record DeliveryStyleVersionDto(
    int Version,
    DeliveryStylePolicyDto Policy,
    string? ChangeNote,
    string CreatedAt,
    string? CreatedBy);

public sealed record DeliveryStyleDto(
    string Id,
    string Name,
    string? Description,
    string Status,
    int CurrentVersion,
    string CreatedAt,
    string UpdatedAt,
    string? CreatedBy,
    IReadOnlyList<string> AssignedStations,
    IReadOnlyList<DeliveryStyleVersionDto> Versions);

public sealed record DeliveryStylesListResponseDto(
    IReadOnlyList<DeliveryStyleDto> Items,
    int Total);

public sealed record DeliveryStylePreviewResponseDto(
    string Url,
    double EstimatedDurationSec,
    int TagCount,
    bool TagsSupported);

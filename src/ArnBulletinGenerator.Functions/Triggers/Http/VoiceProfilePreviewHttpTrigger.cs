using System.Globalization;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts.Mapping;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Audition endpoints for the shared voice library: preview synthesizes a standard sample
///     through the real audio pipeline so loudness/format match on-air output, and remix mints a
///     candidate ElevenLabs voice variant for operator review before it's adopted via
public sealed class VoiceProfilePreviewHttpTrigger(
    IAudioSynthesizer audioSynthesizer,
    IAudioProcessor audioProcessor,
    IBroadcastOutput broadcastOutput,
    IAudioUrlProvider audioUrlProvider,
    IVoiceRemixer voiceRemixer,
    IOptions<SpeechDurationOptions> speechDurationOptions,
    TimeProvider timeProvider,
    ILogger<VoiceProfilePreviewHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null,
    IPreviewAudioCleanup? previewAudioCleanup = null)
{
    // A neutral, on-air-length sample so a preview's measured WPM/duration is representative.
    private const string PreviewScript =
        "Good morning — here is your traffic update. The motorway is flowing freely citybound with no " +
        "major delays. An earlier incident on the harbour bridge has cleared, and the run to the airport " +
        "is looking clear. Allow a few extra minutes through the tunnel, and drive safely out there.";

    // Declared as a distinct literal route so it always wins over voices/profiles/{id}.
    [Function("PreviewVoiceProfile")]
    public async Task<HttpResponseData> Preview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voices/profiles/preview")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var parse = await HttpRequestParser.ParseAsync<PreviewVoiceRequestDto>(req, ct);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if (string.IsNullOrWhiteSpace(dto.BaseVoiceId))
            return await HttpResponseHelper.BadRequestAsync(req, "BaseVoiceId is required.");
        if (dto.Settings is null)
            return await HttpResponseHelper.BadRequestAsync(req, "Settings are required.");

        var settings = dto.Settings.ToDomain();
        if (settings.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, settings.Error);

        var model = string.IsNullOrWhiteSpace(dto.Model) ? Option<string>.None : Option<string>.Some(dto.Model);

        logger.LogInformation("[VoicePreview] Synthesizing sample for voice {VoiceId}", dto.BaseVoiceId);

        string selectedModel = model.GetValueOr(string.Empty);
        var previewKey = new InteractiveWorkKey(
            PrincipalKey(req),
            "voice-profile-preview",
            "preview",
            $"{PreviewScript}\u001fpace={dto.PaceBoostWpm.ToString(CultureInfo.InvariantCulture)}",
            selectedModel,
            settings.Value.GetConfigurationKey(dto.BaseVoiceId),
            string.Empty,
            string.Empty);

        var operation = await (interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance).ExecuteTtsAsync(
            previewKey,
            async operationCt =>
            {
                // A preview is "listen to these settings", not a duration-fitted bulletin —
                // synthesize raw (no duration gate) at the requested settings.
                var synthResult = await audioSynthesizer.SynthesizeAsync(
                    PreviewScript,
                    Option<VoiceSettings>.Some(settings.Value),
                    Option<string>.Some(dto.BaseVoiceId),
                    model,
                    operationCt,
                    Option<string>.None);
                if (synthResult.IsFailure)
                    return synthResult.PropagateFailure<VoicePreviewResponseDto>();

                var audio = synthResult.Value.Audio;
                operationCt.ThrowIfCancellationRequested();
                var wordCount = PreviewScript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                var naturalDuration = audioProcessor.GetDuration(audio);
                var naturalWpm = naturalDuration > 0 ? wordCount / (naturalDuration / 60.0) : 0;

                // Demonstrate the delivery-pace boost the same way production applies it:
                // compress toward the boosted rate (capped, same as a real bulletin).
                double effectivePaceBoostWpm = 0;
                if (dto.PaceBoostWpm > 0 && naturalWpm > 0)
                {
                    double effectiveFactor = WordTargetCalculator.ComputeEffectiveMaxSpeedUpFactor(
                        naturalWpm, dto.PaceBoostWpm, speechDurationOptions.Value.MaxSpeedUpFactor,
                        speechDurationOptions.Value.AbsolutePaceBoostSpeedUpCeiling);
                    double compressedDuration = naturalDuration / effectiveFactor;
                    operationCt.ThrowIfCancellationRequested();
                    audio = audioProcessor.AdjustSpeed(audio, compressedDuration);
                    effectivePaceBoostWpm = (effectiveFactor - 1.0) * naturalWpm;
                }

                operationCt.ThrowIfCancellationRequested();
                var fileName = $"voicepreview_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss_fff}";
                var save = await broadcastOutput.SaveAsync(audio, fileName, operationCt);
                if (save.IsFailure)
                    return save.PropagateFailure<VoicePreviewResponseDto>();

                operationCt.ThrowIfCancellationRequested();
                var urlResult = await audioUrlProvider.GetAudioUrlAsync($"{fileName}.wav", operationCt);
                var duration = audioProcessor.GetDuration(audio);
                var wpm = duration > 0 ? Math.Round(wordCount / (duration / 60.0)) : 0;

                return Result<VoicePreviewResponseDto>.Success(
                    new VoicePreviewResponseDto(
                        urlResult.IsSuccess ? urlResult.Value : string.Empty,
                        Math.Round(duration),
                        wpm,
                        Math.Round(effectivePaceBoostWpm, 1)));
            },
            ct);

        if (operation.IsFailure)
            return await HttpResponseHelper.MapTypedFailureAsync(req, operation.Error,
                operation.FailureMetadata, (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));

        if (previewAudioCleanup is not null && !string.IsNullOrWhiteSpace(operation.Value.Url))
            await previewAudioCleanup.PinAsync(operation.Value.Url, cancellationToken: CancellationToken.None);

        return await HttpResponseHelper.OkJsonAsync(req, operation.Value);
    }

    // Declared as a distinct literal route so it always wins over voices/profiles/{id}.
    [Function("RemixVoiceProfile")]
    public async Task<HttpResponseData> Remix(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voices/profiles/remix")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        var ct = ctx.CancellationToken;
        var parse = await HttpRequestParser.ParseAsync<RemixVoiceRequestDto>(req, ct);
        if (parse.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(req, parse.Error);

        var dto = parse.Value;
        if (string.IsNullOrWhiteSpace(dto.BaseVoiceId))
            return await HttpResponseHelper.BadRequestAsync(req, "BaseVoiceId is required.");
        if (string.IsNullOrWhiteSpace(dto.VoiceDescription))
            return await HttpResponseHelper.BadRequestAsync(req, "A voice description is required.");
        if (dto.VoiceDescription.Length > ConfigValidationLimits.VoiceProfileDescriptionMax)
            return await HttpResponseHelper.BadRequestAsync(req,
                $"Voice description must be at most {ConfigValidationLimits.VoiceProfileDescriptionMax} characters.");

        logger.LogInformation("[VoiceRemix] Remixing voice {VoiceId}", dto.BaseVoiceId);

        var remixKey = new InteractiveWorkKey(
            PrincipalKey(req),
            "voice-remix",
            dto.BaseVoiceId,
            $"{dto.VoiceDescription}\u001f{PreviewScript}\u001fstrength={dto.PromptStrength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

        var operation = await (interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance).ExecuteTtsAsync(
            remixKey,
            async operationCt =>
            {
                var remix = await voiceRemixer.RemixVoiceAsync(
                    dto.BaseVoiceId, dto.VoiceDescription, PreviewScript, dto.PromptStrength, operationCt);
                if (remix.IsFailure)
                    return remix.PropagateFailure<VoiceRemixPreviewResponseDto>();
                if (remix.Value.Count == 0)
                    return Result<VoiceRemixPreviewResponseDto>.Failure("Voice remix returned no previews.");

                // p0 is the most reliable preview slot; surface it for audition.
                var preview = remix.Value[0];
                operationCt.ThrowIfCancellationRequested();
                var fileName = $"voiceremix_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss_fff}";
                var save = await broadcastOutput.SaveAsync(preview.Audio, fileName, operationCt);
                if (save.IsFailure)
                    return save.PropagateFailure<VoiceRemixPreviewResponseDto>();

                operationCt.ThrowIfCancellationRequested();
                var urlResult = await audioUrlProvider.GetAudioUrlAsync($"{fileName}.wav", operationCt);
                return Result<VoiceRemixPreviewResponseDto>.Success(
                    new VoiceRemixPreviewResponseDto(
                        urlResult.IsSuccess ? urlResult.Value : string.Empty,
                        preview.GeneratedVoiceId,
                        Math.Round(preview.DurationSeconds)));
            },
            ct);

        if (operation.IsFailure)
            // ElevenLabs rejects ineligible voices / bad prompts here (e.g. "Only your own
            // custom voices can be remixed.") — an operator-correctable input error, not a 500.
            return await HttpResponseHelper.BadRequestAsync(req, operation.Error);

        if (previewAudioCleanup is not null && !string.IsNullOrWhiteSpace(operation.Value.Url))
            await previewAudioCleanup.PinAsync(operation.Value.Url, cancellationToken: CancellationToken.None);

        return await HttpResponseHelper.OkJsonAsync(req, operation.Value);

    }
    private static string PrincipalKey(HttpRequestData request) =>
        ClientPrincipal.Parse(request)?.ObjectId is { Length: > 0 } objectId
            ? objectId
            : "anonymous";

}

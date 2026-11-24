using System.Globalization;
using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Application.Broadcasting;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Audio-only synthesis endpoint for local testing.
///     Takes a script and voice config, returns synthesized audio metadata.
///     The audio is saved to blob storage like a normal broadcast.
/// </summary>

public sealed class SynthesizeHttpTrigger(
    IAudioPipelineService audioPipeline,
    IBroadcastOutput broadcastOutput,
    IOptions<ElevenLabsOptions> elevenLabsOptions,
    TimeProvider timeProvider,
    ILogger<SynthesizeHttpTrigger> logger,
    IInteractiveWorkGate? interactiveWorkGate = null,
    IPreviewAudioCleanup? previewAudioCleanup = null)
{
    [Function("Synthesize")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "synthesize")]
        HttpRequestData request,
        FunctionContext functionContext)
    {
        var ct = functionContext.CancellationToken;

        var parseResult = await HttpRequestParser.ParseAsync<SynthesizeRequestDto>(request, ct);
        if (parseResult.IsFailure)
            return await HttpResponseHelper.BadRequestAsync(request, parseResult.Error);

        var dto = parseResult.Value;
        if (string.IsNullOrWhiteSpace(dto.Script))
            return await HttpResponseHelper.BadRequestAsync(request, "Script text is required.");

        // Required, same as Script. Without this the request reached the synthesiser, failed with
        // "VoiceId is required. Each schedule or job must specify a voice." and was mapped to a
        // 500 — a client input error reported as a server fault (observed on dev 2026-07-28).
        if (string.IsNullOrWhiteSpace(dto.VoiceId))
            return await HttpResponseHelper.BadRequestAsync(request,
                "VoiceId is required: name the voice to synthesise with.");

        // The only check on Script was IsNullOrWhiteSpace, so a 15,200-character script passed
        // validation (observed on dev 2026-07-28) and one call could bill an arbitrarily large
        // ElevenLabs job. The cap is ~6 minutes of speech — far past any bulletin slot.
        if (dto.Script.Length > ConfigValidationLimits.MaxSynthesisScriptLength)
            return await HttpResponseHelper.BadRequestAsync(request,
                $"Script must be at most {ConfigValidationLimits.MaxSynthesisScriptLength} characters "
                + $"(received {dto.Script.Length}).");

        string script = dto.Script;
        double targetDuration = dto.TargetDurationSeconds ?? 30;

        logger.LogInformation(
            "[Synthesize] Starting audio-only synthesis — {WordCount} words, target {TargetDuration}s",
            script.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, targetDuration);

        var voiceId = string.IsNullOrEmpty(dto.VoiceId)
            ? Option<string>.None
            : Option<string>.Some(dto.VoiceId);

        var voiceSettings = dto.Stability.HasValue || dto.SimilarityBoost.HasValue
            ? VoiceSettings.Create(
                dto.Stability ?? 0.5,
                dto.SimilarityBoost ?? 0.8,
                dto.Style ?? 0.2,
                dto.UseSpeakerBoost ?? true,
                dto.Speed ?? 1.0).Match(
                    vs => Option<VoiceSettings>.Some(vs),
                    _ => Option<VoiceSettings>.None)
            : Option<VoiceSettings>.None;

        var model = string.IsNullOrEmpty(dto.Model)
            ? Option<string>.None
            : Option<string>.Some(dto.Model);

        var marketOption = string.IsNullOrEmpty(dto.Market)
            ? Option<string>.None
            : Option<string>.Some(dto.Market);

        string effectiveModel = model.GetValueOr(elevenLabsOptions.Value.ModelId);
        string settingsKey = voiceSettings.HasValue
            ? voiceSettings.Value.GetConfigurationKey(voiceId.Value)
            : "default";
        var key = new InteractiveWorkKey(
            PrincipalKey(request),
            "synthesize",
            string.Empty,
            $"{script}\u001ftarget={targetDuration.ToString("R", CultureInfo.InvariantCulture)}" +
            $"\u001fmarket={marketOption.GetValueOr(string.Empty)}",
            effectiveModel,
            settingsKey,
            string.Empty,
            string.Empty);

        var operation = await (interactiveWorkGate ?? NoOpInteractiveWorkGate.Instance).ExecuteTtsAsync(
            key,
            async operationCt =>
            {
                // Use raw synthesizer for audio-only testing — no duration gate enforcement.
                // The duration target is used for speed hinting only, not as a hard constraint.
                var audioResult = await audioPipeline.SynthesizeWithDurationTargetAsync(
                    script, targetDuration, voiceId, voiceSettings, model, operationCt,
                    initialSpeed: 0, market: marketOption);

                byte[] audioBytes;
                double actualDuration;
                string finalScript;

                if (audioResult.IsSuccess)
                {
                    audioBytes = audioResult.Value.Audio;
                    actualDuration = audioResult.Value.DurationSeconds;
                    finalScript = audioResult.Value.FinalScript;
                }
                else
                {
                    // Duration gate may have rejected — log the failure but still try to get audio
                    // by falling back to the raw synthesizer without duration targeting.
                    logger.LogWarning(
                        "[Synthesize] Duration-targeted synthesis failed ({Error}), falling back to raw synthesis",
                        audioResult.Error);

                    var rawResult = await audioPipeline.SynthesizeWithDurationTargetAsync(
                        script, 0, voiceId, voiceSettings, model, operationCt,
                        initialSpeed: 1.0, market: marketOption);

                    if (rawResult.IsFailure)
                    {
                        logger.LogError("[Synthesize] Raw synthesis also failed: {Error}", rawResult.Error);
                        return rawResult.PropagateFailure<SynthesizeOperationResult>();
                    }

                    audioBytes = rawResult.Value.Audio;
                    actualDuration = rawResult.Value.DurationSeconds;
                    finalScript = rawResult.Value.FinalScript;
                }

                // Never begin an upload after the request/deadline has been cancelled. The linked
                // gate token also stops an in-flight blob upload and URL lookup.
                operationCt.ThrowIfCancellationRequested();
                string fileName =
                    $"synthesize_{timeProvider.GetUtcNow().UtcDateTime:yyyyMMdd_HHmmss}";
                var saveResult = await broadcastOutput.SaveAsync(audioBytes, fileName, operationCt);
                if (saveResult.IsFailure)
                    return saveResult.PropagateFailure<SynthesizeOperationResult>();
                operationCt.ThrowIfCancellationRequested();

                return Result<SynthesizeOperationResult>.Success(new SynthesizeOperationResult(
                    finalScript,
                    $"{fileName}.wav",
                    actualDuration,
                    targetDuration,
                    audioBytes.Length,
                    finalScript.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length,
                    dto.VoiceId ?? string.Empty,
                    effectiveModel));
            },
            ct);

        if (operation.IsFailure)
            return await HttpResponseHelper.InternalErrorAsync(request, operation.Error);

        var result = operation.Value;
        if (previewAudioCleanup is not null && !string.IsNullOrWhiteSpace(result.AudioUrl))
            await previewAudioCleanup.PinAsync(result.AudioUrl, cancellationToken: CancellationToken.None);

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(new
        {
            status = "success",
            script = result.Script,
            audioUrl = result.AudioUrl,
            durationSec = result.DurationSec,
            targetDurationSec = result.TargetDurationSec,
            audioSizeBytes = result.AudioSizeBytes,
            wordCount = result.WordCount,
            voiceId = result.VoiceId,
            model = result.Model
        }));
        return response;
    }

    private static string PrincipalKey(HttpRequestData request) =>
        ClientPrincipal.Parse(request)?.ObjectId is { Length: > 0 } objectId
            ? objectId
            : "anonymous";

    private sealed record SynthesizeOperationResult(
        string Script,
        string AudioUrl,
        double DurationSec,
        double TargetDurationSec,
        int AudioSizeBytes,
        int WordCount,
        string VoiceId,
        string Model);
}

public sealed record SynthesizeRequestDto(
    string? Script = null,
    double? TargetDurationSeconds = null,
    string? VoiceId = null,
    string? Model = null,
    double? Stability = null,
    double? SimilarityBoost = null,
    double? Style = null,
    bool? UseSpeakerBoost = null,
    double? Speed = null,
    string? Market = null);

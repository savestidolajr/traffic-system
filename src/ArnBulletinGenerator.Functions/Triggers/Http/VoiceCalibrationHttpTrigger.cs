using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     Triggers voice calibration — synthesizes a reference phrase and measures
///     actual speech rate. Used to seed calibration data for accurate word-count targeting.
/// </summary>
public sealed class VoiceCalibrationHttpTrigger(
    IVoiceCalibrationService calibrationService,
    ILogger<VoiceCalibrationHttpTrigger> logger)
{
    [Function("CalibrateVoice")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "voices/{voiceId}/calibrate")]
        HttpRequestData request,
        string voiceId,
        FunctionContext functionContext)
    {
        var ct = functionContext.CancellationToken;

        if (string.IsNullOrWhiteSpace(voiceId))
            return await HttpResponseHelper.BadRequestAsync(request, "Voice ID is required.");

        logger.LogInformation("[Calibrate] Starting calibration for voice {VoiceId}", voiceId);

        var hasExisting = await calibrationService.HasValidCalibrationAsync(voiceId, cancellationToken: ct);

        var result = await calibrationService.CalibrateVoiceAsync(voiceId, cancellationToken: ct);

        if (result.IsFailure)
        {
            logger.LogError("[Calibrate] Calibration failed for voice {VoiceId}: {Error}", voiceId, result.Error);
            // A provider 4xx (e.g. an unknown voice id) is operator-correctable input: the
            // synthesizer returns a typed InvalidInput with a sanitised message, so map it to
            // 400; anything else (provider outage, quota) stays a 500.
            return await HttpResponseHelper.MapTypedFailureAsync(request, result.Error,
                result.FailureMetadata, (r, e) => HttpResponseHelper.InternalErrorAsync(r, e));
        }

        var calibration = result.Value;
        logger.LogInformation(
            "[Calibrate] Voice {VoiceId} calibrated: {Sps:F2} syll/s, {Spw:F2} syll/word, {Runs} runs",
            voiceId, calibration.SyllablesPerSecond, calibration.SyllablesPerWord, calibration.RunCount);

        var response = request.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(new
        {
            status = "success",
            voiceId,
            hadExistingCalibration = hasExisting,
            syllablesPerSecond = calibration.SyllablesPerSecond,
            syllablesPerWord = calibration.SyllablesPerWord,
            wordsPerMinute = calibration.SyllablesPerSecond * 60.0 / calibration.SyllablesPerWord,
            runCount = calibration.RunCount
        }));
        return response;
    }
}

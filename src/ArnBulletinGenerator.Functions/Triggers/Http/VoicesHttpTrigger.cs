using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Functions.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ArnBulletinGenerator.Functions.Http;

namespace ArnBulletinGenerator.Functions.Triggers.Http;

/// <summary>
///     HTTP trigger that lists the ElevenLabs voice catalog with each voice's resolved TTS
///     model id for the configured broadcast model.
/// </summary>
public sealed class VoicesHttpTrigger(
    IVoiceProvider voiceProvider,
    IOptions<ModelSelectionOptions> modelOptions,
    ILogger<VoicesHttpTrigger> logger)
{
    [Function("ListVoices")]
    public async Task<HttpResponseData> ListVoices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "voices")]
        HttpRequestData req,
        FunctionContext ctx)
    {
        logger.LogInformation("Fetching available voices");
        var result = await voiceProvider.GetVoicesAsync(ctx.CancellationToken);
        if (result.IsFailure) return await HttpResponseHelper.InternalErrorAsync(req, result.Error);

        var voices = result.Value.Select(v => new VoiceDto(
                v.VoiceId,
                v.Name,
                v.Category.GetValueOr(string.Empty),
                v.Description.GetValueOr(string.Empty),
                v.Labels
            )
            {
                ModelIds = v.ModelIds,
                ResolvedModelId = VoiceModelResolver.Resolve(v.ModelIds, modelOptions.Value.BroadcastModel),
                CanRemix = v.CanRemix
            }).ToList();

        return await HttpResponseHelper.OkJsonAsync(req, new VoiceListResponse(voices));
    }
}

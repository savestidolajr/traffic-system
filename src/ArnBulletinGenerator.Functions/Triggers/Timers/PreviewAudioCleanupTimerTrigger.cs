using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Timers;

/// <summary>Hourly bounded sweep for expired synchronous preview audio.</summary>
public sealed class PreviewAudioCleanupTimerTrigger(
    IPreviewAudioCleanup cleanup,
    IOptions<PreviewAudioCleanupOptions> options,
    ILogger<PreviewAudioCleanupTimerTrigger> logger)
{
    [Function("PreviewAudioCleanup")]
    public async Task Run(
        [TimerTrigger("0 15 * * * *")] TimerInfo timerInfo,
        FunctionContext functionContext)
    {
        if (!options.Value.Enabled)
        {
            logger.LogDebug("PreviewAudioCleanup disabled via configuration; skipping tick");
            return;
        }

        try
        {
            var result = await cleanup.CleanupAsync(functionContext.CancellationToken);
            if (result.IsFailure)
                logger.LogError("Preview audio cleanup failed: {Error}; next tick will retry", result.Error);
        }
        catch (OperationCanceledException) when (functionContext.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Preview audio cleanup timer tick failed; next tick will retry");
        }
    }
}

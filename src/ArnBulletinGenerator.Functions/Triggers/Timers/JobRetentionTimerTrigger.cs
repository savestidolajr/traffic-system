using ArnBulletinGenerator.Application.Jobs;
using ArnBulletinGenerator.Domain.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Timers;

/// <summary>Daily bounded retention sweep; disabled by default for migration safety.</summary>
public sealed class JobRetentionTimerTrigger(
    IJobRetentionService retentionService,
    IOptions<JobRetentionOptions> options,
    ILogger<JobRetentionTimerTrigger> logger)
{
    [Function("JobRetention")]
    public async Task Run(
        [TimerTrigger("0 30 2 * * *")] TimerInfo timerInfo,
        FunctionContext functionContext)
    {
        if (options.Value.Disable || options.Value.RetentionDays <= 0)
        {
            logger.LogDebug("JobRetention disabled via configuration; skipping tick");
            return;
        }

        try
        {
            var result = await retentionService.RunAsync(functionContext.CancellationToken);
            if (result.IsFailure)
                logger.LogError("JobRetention failed: {Error}. Next tick will retry.", result.Error);
            else
                logger.LogInformation(
                    "JobRetention examined {Examined}, protected {Protected}, deleted {Deleted}, failed {Failed}, orphans {Orphans}; checkpoint {Checkpoint}",
                    result.Value.Examined, result.Value.Protected, result.Value.Deleted,
                    result.Value.Failed, result.Value.Orphans, result.Value.ContinuationToken);
        }
        catch (OperationCanceledException) when (functionContext.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JobRetention timer tick failed; next tick will retry");
        }
    }
}

using ArnBulletinGenerator.Application;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Application.Scheduling;

namespace ArnBulletinGenerator.Functions.Triggers.Timers;

/// <summary>
///     Timer-triggered function that polls for due schedules and invokes the broadcast orchestrator.
///     Runs every minute. Delegates processing to SchedulePollerService.
/// </summary>
public sealed class SchedulePollerTrigger(
    ISchedulePollerService schedulePollerService,
    ILogger<SchedulePollerTrigger> logger)
{
    [Function("SchedulePoller")]
    public async Task Run(
        [TimerTrigger("0 * * * * *")] TimerInfo timerInfo,
        FunctionContext functionContext)
    // Note: timer triggers are host-singleton by the Functions platform — no Singleton attribute needed.
    {
        try
        {
            await schedulePollerService.ProcessDueSchedulesAsync(functionContext.CancellationToken);
        }
        catch (OperationCanceledException) when (functionContext.CancellationToken.IsCancellationRequested)
        {
            // Host shutdown — let it propagate as cancellation, not failure.
            throw;
        }
        catch (Exception ex)
        {
            // Swallow so a single tick failure does not poison the timer trigger's
            // health metrics. The catch-up logic on next tick will pick up missed
            // schedules. Surface as an error log + alert metric.
            logger.LogError(ex,
                "SchedulePollerTrigger tick failed at {Utc:o}; next tick will retry. Reason: {Reason}",
                DateTime.UtcNow, ex.Message);
        }
    }
}

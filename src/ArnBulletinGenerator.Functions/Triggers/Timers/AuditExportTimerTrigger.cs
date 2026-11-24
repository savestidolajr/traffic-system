using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Functions.Triggers.Timers;

/// <summary>
///     Daily snapshot of the job table (scripts + approval evidence) into the
///     audit-exports blob container. The platform is the system of record for this
///     evidence (CC-002; ARN technology lead design review 10 Jun 2026) and Azure Tables carry
///     no soft delete or point-in-time restore — the blob snapshot does.
///     Runs at 16:00 UTC (02:00 AEST), outside bulletin hours.
/// </summary>
public sealed class AuditExportTimerTrigger(
    IAuditExporter auditExporter,
    IOptions<AuditExportOptions> options,
    ILogger<AuditExportTimerTrigger> logger)
{
    [Function("AuditExport")]
    public async Task Run(
        [TimerTrigger("0 0 16 * * *")] TimerInfo timerInfo,
        FunctionContext functionContext)
    {
        if (!options.Value.Enabled)
        {
            logger.LogDebug("AuditExport disabled via configuration; skipping tick");
            return;
        }

        try
        {
            var result = await auditExporter.ExportJobsAsync(functionContext.CancellationToken);
            if (result.IsFailure)
                logger.LogError("AuditExport failed: {Error}. Next tick will retry.", result.Error);
        }
        catch (OperationCanceledException) when (functionContext.CancellationToken.IsCancellationRequested)
        {
            // Host shutdown — let it propagate as cancellation, not failure.
            throw;
        }
        catch (Exception ex)
        {
            // Swallow so a single tick failure does not poison the timer trigger's
            // health metrics; surfaced as an error log + alert metric.
            logger.LogError(ex, "AuditExportTimerTrigger tick failed at {Utc:o}; next tick will retry. Reason: {Reason}",
                DateTime.UtcNow, ex.Message);
        }
    }
}

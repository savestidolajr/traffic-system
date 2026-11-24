using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Infrastructure.Fakes;

/// <summary>
///     Deterministic ARN_E2E stand-in for Teams notifications: logs each job/alert notification
///     instead of sending it, and escalates only Critical alerts to the application logger.
/// </summary>
public sealed class FakeNotificationService(FakeExternalCallLog log, ILogger<FakeNotificationService> logger) : INotificationService
{
    public Task<Result> SendJobNotificationAsync(Job job, NotificationKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"notification job={job.JobId} status={job.Status} kind={kind?.ToString() ?? "auto"}");
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SendAlertNotificationAsync(
        string market,
        string station,
        string alertMessage,
        AlertSeverity severity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        log.Add($"notification alert market={market} station={station} severity={severity}");
        if (severity == AlertSeverity.Critical)
        {
            logger.LogCritical(
                "[E2E-Alert] market={Market} station={Station} severity={Severity} message={Message}",
                market, station, severity, alertMessage);
        }
        return Task.FromResult(Result.Success());
    }
}

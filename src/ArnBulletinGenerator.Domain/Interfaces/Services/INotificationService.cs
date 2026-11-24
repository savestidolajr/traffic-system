using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Sends operational notifications to external channels.
/// </summary>
public interface INotificationService
{
    /// <summary>
    ///     Sends a rich notification for a job lifecycle event.
    ///     <paramref name="kind" /> selects the card tier; when null it is derived from
    ///     the job's status (WaitingApproval → ReadyForReview, Failed → FailedNotCovered,
    ///     everything else → Suppressed). Suppressed kinds send nothing and succeed.
    /// </summary>
    Task<Result> SendJobNotificationAsync(Job job, NotificationKind? kind = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Sends a general operational alert for a market/station.
    /// </summary>
    Task<Result> SendAlertNotificationAsync(
        string market, string station, string alertMessage, AlertSeverity severity,
        CancellationToken cancellationToken = default);
}

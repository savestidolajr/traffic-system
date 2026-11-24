using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Contract for the scheduled broadcast sweep: finds due market schedules and drives each
///     one through job creation and queueing.
/// </summary>
public interface ISchedulePollerService
{
    Task ProcessDueSchedulesAsync(CancellationToken cancellationToken);
    Task ProcessScheduleAsync(MarketSchedule schedule, CancellationToken cancellationToken);
}

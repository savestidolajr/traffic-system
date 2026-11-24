using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Resolves a schedule's next UTC occurrence, including optional calendar overlays.</summary>
public interface IScheduleOccurrenceResolver
{
    Option<DateTime> NextRunUtc(MarketSchedule schedule, DateTime afterUtc);
}

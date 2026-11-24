using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Persists per-market bulletin schedules and provides the atomic due-schedule claim (TryStartJobAsync/CompleteJobAsync) the poller uses to avoid double-triggering a run.</summary>
public interface IScheduleRepository
{
    Task<Result<IEnumerable<MarketSchedule>>> ListSchedulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reads only schedule assignment fields for irreversible library-reference checks.
    ///     Implementations should use a storage projection; the default keeps lightweight test and
    ///     migration repositories compatible while the optimized repository is rolled out.
    /// </summary>
    async Task<Result<IEnumerable<ScheduleReferenceSummary>>> ListScheduleReferenceSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var schedules = await ListSchedulesAsync(cancellationToken);
        return schedules.IsFailure
            ? Result<IEnumerable<ScheduleReferenceSummary>>.Failure(schedules.Error)
            : Result<IEnumerable<ScheduleReferenceSummary>>.Success(
                schedules.Value.Select(ScheduleReferenceSummary.FromSchedule));
    }

    Task<Result<IEnumerable<MarketSchedule>>> GetDueSchedulesAsync(DateTime thresholdUtc,
        CancellationToken cancellationToken = default);

    Task<Result<MarketSchedule>> GetScheduleAsync(string marketId, CancellationToken cancellationToken = default);
    Task<Result> SaveScheduleAsync(MarketSchedule schedule, CancellationToken cancellationToken = default);
    Task<Result> DeleteScheduleAsync(string marketId, CancellationToken cancellationToken = default);
    Task<Result> TryStartJobAsync(string marketId, string jobId, CancellationToken cancellationToken = default);

    Task<Result> CompleteJobAsync(string marketId, Option<DateTime> nextRunUtc,
        CancellationToken cancellationToken = default);
}

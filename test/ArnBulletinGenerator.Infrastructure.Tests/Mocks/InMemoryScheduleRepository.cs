using System.Collections.Concurrent;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Serialization;

namespace ArnBulletinGenerator.Infrastructure.Tests.Mocks;

/// <summary>
///     In-memory schedule repository for local development.
///     Does not persist schedules across restarts.
///     Stores and returns deep copies to prevent external mutation of repository state.
/// </summary>
public sealed class InMemoryScheduleRepository(ICronParser cronParser) : IScheduleRepository
{
    private readonly ConcurrentDictionary<string, MarketSchedule> _schedules = new();
    private readonly Lock _startJobLock = new();

    public Task<Result<IEnumerable<MarketSchedule>>> ListSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var schedules = _schedules.Values.Select(Clone).OrderBy(s => s.Name).ToList();
        return Task.FromResult(Result<IEnumerable<MarketSchedule>>.Success(schedules));
    }

    public Task<Result<IEnumerable<MarketSchedule>>> GetDueSchedulesAsync(DateTime thresholdUtc,
        CancellationToken cancellationToken = default)
    {
        var dueSchedules = _schedules.Values
            .Where(s => s.Enabled
                        && s.NextRunUtc.HasValue
                        && s.NextRunUtc.Value <= thresholdUtc
                        && s.CurrentJobId.IsNone)
            .Select(Clone)
            .ToList();
        return Task.FromResult(Result<IEnumerable<MarketSchedule>>.Success(dueSchedules));
    }

    public Task<Result<MarketSchedule>> GetScheduleAsync(string marketId, CancellationToken cancellationToken = default)
    {
        if (_schedules.TryGetValue(marketId, out var schedule))
            return Task.FromResult(Result<MarketSchedule>.Success(Clone(schedule)));
        return Task.FromResult(Result<MarketSchedule>.Failure("Schedule not found"));
    }

    public Task<Result> SaveScheduleAsync(MarketSchedule schedule, CancellationToken cancellationToken = default)
    {
        var copy = Clone(schedule);

        if (copy.Enabled && !copy.NextRunUtc.HasValue)
            copy.ScheduleNextRun(cronParser.CalculateNextRun(copy.CronExpression, copy.Timezone));

        copy.UpdatedAt = DateTime.UtcNow;
        _schedules[copy.MarketId] = copy;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> DeleteScheduleAsync(string marketId, CancellationToken cancellationToken = default)
    {
        if (_schedules.TryRemove(marketId, out _)) return Task.FromResult(Result.Success());
        return Task.FromResult(Result.Failure("Schedule not found"));
    }

    public Task<Result> TryStartJobAsync(string marketId, string jobId, CancellationToken cancellationToken = default)
    {
        lock (_startJobLock)
        {
            if (!_schedules.TryGetValue(marketId, out var schedule))
                return Task.FromResult(Result.Failure("Schedule not found"));

            if (schedule.CurrentJobId.HasValue)
                return Task.FromResult(Result.Failure($"Schedule already running job {schedule.CurrentJobId.Value}"));

            schedule.StartRun(jobId, DateTime.UtcNow);
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result> CompleteJobAsync(string marketId, Option<DateTime> nextRunUtc,
        CancellationToken cancellationToken = default)
    {
        if (!_schedules.TryGetValue(marketId, out var schedule))
            return Task.FromResult(Result.Failure("Schedule not found"));

        var effectiveNextRun = nextRunUtc.HasValue
            ? nextRunUtc
            : cronParser.CalculateNextRun(schedule.CronExpression, schedule.Timezone);
        schedule.CompleteRun(DateTime.UtcNow, effectiveNextRun);
        return Task.FromResult(Result.Success());
    }

    private static MarketSchedule Clone(MarketSchedule schedule)
    {
        return JsonSerializer.Deserialize<MarketSchedule>(
            JsonSerializer.Serialize(schedule, JsonDefaults.Storage),
            JsonDefaults.Storage)!;
    }
}

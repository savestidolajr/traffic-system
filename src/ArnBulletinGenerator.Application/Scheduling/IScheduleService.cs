using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Contract for market schedule lifecycle operations: validated creation, update, and
///     combined create-or-update.
/// </summary>
public interface IScheduleService
{
    Result<MarketSchedule> CreateSchedule(CreateScheduleCommand command);
    Result<MarketSchedule> UpdateSchedule(MarketSchedule schedule, UpdateScheduleCommand command);

    /// <summary>
    ///     Cron parseability and timezone resolvability — the two rules the domain model cannot
    ///     check itself. Every write path MUST call this, or the planner and the applier disagree.
    /// </summary>
    Result ValidateCronAndTimezone(string? cronExpression, string? timezone);

    /// <summary>
    ///     Operator-editable text fields. <c>null</c> means "not supplied" and is always valid;
    ///     an empty or whitespace value is an explicit attempt to CLEAR a required field, which
    ///     returned 200 while silently keeping the old value (reproduced on dev 2026-07-28).
    ///     Every write path MUST call this so the API and the import planner agree.
    /// </summary>
    Result ValidateEditableFields(string? name, string? station);

    Task<Result<(MarketSchedule Schedule, bool IsUpdate)>> CreateOrUpdateAsync(
        UpdateScheduleCommand updateCommand,
        Option<CreateScheduleCommand> createCommand,
        CancellationToken cancellationToken = default);
}

using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Validation;
using ArnBulletinGenerator.Application.Commands;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;

namespace ArnBulletinGenerator.Application.Scheduling;

/// <summary>
///     Application service for schedule lifecycle operations.
///     Handles schedule creation and updates with validation performed at the edge.
/// </summary>
public sealed class ScheduleService(
    IScheduleRepository scheduleRepository,
    ICronParser cronParser,
    TimeProvider timeProvider,
    ILogger<ScheduleService> logger) : IScheduleService
{
    private readonly ScheduleOccurrenceResolver _occurrenceResolver = new(cronParser, logger);
    /// <summary>
    ///     Validates the two fields the domain model cannot check for itself: cron parseability and
    ///     timezone resolvability. Shared so every write path agrees.
    ///     <para>
    ///     Config import used to dry-run with <see cref="CreateSchedule" /> (domain field rules only)
    ///     and then apply with <see cref="CreateOrUpdateAsync" /> (which does check these). A bundle
    ///     carrying a garbage cron or timezone therefore planned cleanly, failed on apply, and the
    ///     failure was swallowed — the import reported Creates=1 for a schedule that was never
    ///     written (reproduced on dev 2026-07-28).
    ///     </para>
    /// </summary>
    public Result ValidateCronAndTimezone(string? cronExpression, string? timezone)
    {
        if (cronExpression is not null && !cronParser.TryParseCron(cronExpression))
            return Result.Failure("Invalid cron expression");
        if (timezone is not null && TimeZoneHelper.TryResolve(timezone).IsFailure)
            return Result.Failure($"Invalid timezone: {timezone}");
        return cronExpression is null
            ? Result.Success()
            : ValidateCronInterval(cronExpression, timezone);
    }

    /// <summary>Occurrences to walk when measuring the tightest gap a cron produces.</summary>
    private const int CronProbeOccurrences = 200;

    /// <summary>
    ///     A Monday well clear of any DST transition, so the probe is deterministic regardless of
    ///     when validation runs.
    /// </summary>
    private static readonly DateTime CronProbeStart = new(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    ///     Rejects a cron that fires more often than <see cref="ConfigValidationLimits.MinCronIntervalMinutes" />.
    ///     Syntax was the only check, so <c>* * * * *</c> was accepted: across 13 markets that is
    ///     ~19,000 LLM generations plus ElevenLabs syntheses a day from one typo. Measured by
    ///     walking real occurrences rather than pattern-matching the minute field, so compound
    ///     (newline-separated) expressions and step/list syntax are all judged on what they
    ///     actually do.
    /// </summary>
    private Result ValidateCronInterval(string cronExpression, string? timezone)
    {
        string probeZone = string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone;
        var floor = TimeSpan.FromMinutes(ConfigValidationLimits.MinCronIntervalMinutes);

        var first = cronParser.CalculateNextRun(cronExpression, probeZone, CronProbeStart);
        if (first.IsNone)
            return Result.Success(); // Parses but never fires — not this check's business.

        var previous = first.Value;
        for (int i = 0; i < CronProbeOccurrences; i++)
        {
            var next = cronParser.CalculateNextRun(cronExpression, probeZone, previous);
            if (next.IsNone)
                break;

            var gap = next.Value - previous;
            if (gap < floor)
                return Result.Failure(
                    $"Cron must not schedule bulletins more often than every "
                    + $"{ConfigValidationLimits.MinCronIntervalMinutes} minutes; this expression runs "
                    + $"every {gap.TotalMinutes:0.#} minute(s).");

            previous = next.Value;
        }

        return Result.Success();
    }

    private Result ValidateCalendar(Option<ScheduleCalendar> calendar, string? timezone)
    {
        if (calendar.IsNone)
            return Result.Success();

        foreach (var pattern in calendar.Value.Patterns)
        {
            var check = ValidateCronAndTimezone(pattern.CronExpression, timezone);
            if (check.IsFailure)
                return Result.Failure(
                    $"Calendar pattern '{pattern.Id}' has an invalid cron configuration: {check.Error}");
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public Result ValidateEditableFields(string? name, string? station)
    {
        var check = ValidateEditableText(name, "Schedule name", ConfigValidationLimits.ScheduleNameMax);
        return check.IsFailure
            ? check
            : ValidateEditableText(station, "Station", ConfigValidationLimits.ScheduleStationMax);
    }

    private static Result ValidateEditableText(string? value, string label, int maxLength)
    {
        if (value is null)
            return Result.Success(); // Field omitted entirely: leave the stored value alone.

        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure(
                $"{label} is required and cannot be cleared. Omit the field to leave it unchanged.");

        if (value.Length > maxLength)
            return Result.Failure(
                $"{label} must be at most {maxLength} characters (received {value.Length}).");

        return OperatorTextGuard.HasControlOrFormatCharacters(value)
            ? Result.Failure($"{label} {OperatorTextGuard.RejectionMessage}.")
            : Result.Success();
    }

    /// <summary>
    ///     Creates a new schedule for the specified market.
    public Result<MarketSchedule> CreateSchedule(CreateScheduleCommand command)
    {
        var calendarValid = ValidateCalendar(command.Calendar, command.Timezone);
        if (calendarValid.IsFailure)
            return Result<MarketSchedule>.Failure(calendarValid.Error);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var createResult = MarketSchedule.Create(
            command.MarketId,
            command.Name,
            command.Station,
            command.Timezone,
            command.CronExpression,
            command.Enabled,
            command.Coverage,
            command.VoiceProfileId,
            command.OutputPath,
            command.TargetDurationSeconds,
            command.NotifyBeforePlayoutMinutes,
            command.EscalationBeforePlayoutMinutes,
            now,
            now,
            command.DeliveryStyleId,
            command.PronunciationDictionaryId,
            command.BookendId,
            command.BookendTokens.Match<IReadOnlyDictionary<string, string>?>(v => v, () => null),
            command.CurationEnabled,
            command.PlayoutEnabled,
            command.PlaybookId,
            command.ContentSelection);
        if (createResult.IsFailure)
            return createResult;
        var schedule = createResult.Value;
        schedule.ContentSelection = command.ContentSelection;
        schedule.VoiceRotation = command.VoiceRotation;
        schedule.BookendRotation = command.BookendRotation;
        schedule.FillOrder = command.FillOrder.GetValueOr(schedule.FillOrder);
        schedule.Calendar = command.Calendar;

        logger.LogInformation("Created schedule for market {MarketId}", command.MarketId);
        return Result<MarketSchedule>.Success(schedule);
    }

    /// <summary>
    ///     Updates an existing schedule with the provided data.
    ///     Returns a new immutable schedule instance with the applied changes.
    /// </summary>
    public Result<MarketSchedule> UpdateSchedule(MarketSchedule schedule, UpdateScheduleCommand command)
    {
        var calendarValid = ValidateCalendar(command.Calendar,
            command.Timezone.GetValueOr(schedule.Timezone));
        if (calendarValid.IsFailure)
            return Result<MarketSchedule>.Failure(calendarValid.Error);

        var updated = ApplyUpdate(schedule, command);
        if (updated.IsSuccess)
            logger.LogDebug("Updated schedule for market {MarketId}", updated.Value.MarketId);
        return updated;
    }

    /// <summary>
    ///     Creates or updates a schedule based on whether it exists.
    ///     Returns a tuple of (schedule, isUpdate).
    /// </summary>
    public async Task<Result<(MarketSchedule Schedule, bool IsUpdate)>> CreateOrUpdateAsync(
        UpdateScheduleCommand updateCommand,
        Option<CreateScheduleCommand> createCommand,
        CancellationToken cancellationToken = default)
    {
        var updateValid = ValidateCronAndTimezone(
            updateCommand.CronExpression.GetValueOr(null!), updateCommand.Timezone.GetValueOr(null!));
        if (updateValid.IsFailure)
            return Result<(MarketSchedule, bool)>.Failure(updateValid.Error);

        if (createCommand.HasValue)
        {
            var create = createCommand.Value;
            var createValid = ValidateCronAndTimezone(create.CronExpression, create.Timezone);
            if (createValid.IsFailure)
                return Result<(MarketSchedule, bool)>.Failure(createValid.Error);
        }

        // Check if schedule exists
        var existingResult = await scheduleRepository.GetScheduleAsync(updateCommand.MarketId, cancellationToken);
        bool isUpdate = existingResult.IsSuccess;

        Result<MarketSchedule> scheduleResult;
        if (isUpdate)
        {
            scheduleResult = UpdateSchedule(existingResult.Value, updateCommand);
        }
        else
        {
            if (createCommand.IsNone)
                return Result<(MarketSchedule, bool)>.Failure("Schedule does not exist and create data is missing.");

            scheduleResult = CreateSchedule(createCommand.Value);
        }

        if (scheduleResult.IsFailure)
            return Result<(MarketSchedule, bool)>.Failure(scheduleResult.Error);

        var schedule = scheduleResult.Value;

        // Calculate next run if enabled, clear if disabled
        if (schedule.Enabled)
            schedule.ScheduleNextRun(_occurrenceResolver.NextRunUtc(schedule,
                timeProvider.GetUtcNow().UtcDateTime));
        else
            schedule.ScheduleNextRun(Option<DateTime>.None);

        // Save
        var saveResult = await scheduleRepository.SaveScheduleAsync(schedule, cancellationToken);
        if (saveResult.IsFailure)
            return Result<(MarketSchedule, bool)>.Failure(saveResult.Error);

        return Result<(MarketSchedule, bool)>.Success((schedule, isUpdate));
    }

    /// <summary>
    ///     Applies the command's overrides and returns a new validated schedule instance.
    ///     The original schedule is left unchanged. Invalid override values fail
    ///     instead of being written through.
    /// </summary>
    private Result<MarketSchedule> ApplyUpdate(MarketSchedule schedule, UpdateScheduleCommand command)
    {
        var updated = schedule.Clone();
        var result = updated.ApplyUpdates(
            command.Name,
            command.Station,
            command.Timezone,
            command.CronExpression,
            command.Enabled,
            command.Coverage,
            command.VoiceProfileId,
            command.OutputPath,
            command.TargetDurationSeconds,
            command.NotifyBeforePlayoutMinutes,
            command.EscalationBeforePlayoutMinutes,
            command.DeliveryStyleId,
            command.PronunciationDictionaryId,
            command.ContentSelection,
            command.FillOrder,
            command.VoiceRotation,
            command.BookendId,
            command.BookendRotation,
            command.BookendTokens,
            command.Calendar,
            timeProvider.GetUtcNow().UtcDateTime,
            command.CurationEnabled,
            command.PlayoutEnabled,
            command.PlaybookId);

        return result.IsFailure
            ? Result<MarketSchedule>.Failure(result.Error)
            : Result<MarketSchedule>.Success(updated);
    }
}

using System.Globalization;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Application.ConfigTransfer;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>
///     Response mapping: MarketSchedule → ScheduleSummaryDto for API responses.
/// </summary>
public static class ScheduleMappingExtensions
{
    public static ScheduleSummaryDto ToDto(this MarketSchedule schedule)
    {
        return new ScheduleSummaryDto(
            schedule.MarketId,
            schedule.Name,
            schedule.Station,
            schedule.Timezone,
            schedule.CronExpression,
            schedule.Enabled,
            schedule.TargetDurationSeconds,
            schedule.NotifyBeforePlayoutMinutes,
            schedule.EscalationBeforePlayoutMinutes,
            schedule.VoiceProfileId,
            schedule.NextRunUtc.Match(dt => (DateTime?)dt, () => null),
            schedule.CurrentJobId.Match(id => (string?)id, () => null),
            schedule.CreatedAt,
            schedule.UpdatedAt,
            schedule.Coverage is { } c
                ? CoverageMapping.ToDto(c)
                : null,
            schedule.ContentSelection.Match(cs => (ContentSelectionDto?)cs.ToDto(), () => null),
            schedule.DeliveryStyleId,
            schedule.PlaybookId,
            schedule.PronunciationDictionaryId,
            schedule.VoiceRotation.Match(p => (VoiceRotationDto?)VoiceRotationMapping.ToDto(p), () => null),
            schedule.FillOrder.Select(FillSourceToWire).ToList(),
            NullIfEmpty(schedule.BookendId),
            schedule.BookendRotation.Match(p => (BookendRotationDto?)BookendRotationMapping.ToDto(p), () => null),
            schedule.BookendTokens.Count > 0 ? schedule.BookendTokens : null,
            null,
            schedule.Calendar.Match(c => (ScheduleCalendarDto?)c.ToDto(), () => null),
            schedule.CurationEnabled,
            schedule.PlayoutEnabled);
    }

    /// <summary>MarketSchedule → ConfigScheduleDto for the admin config-bundle export/import diff.</summary>
    public static ConfigScheduleDto ToConfigScheduleDto(this MarketSchedule s) => new(
        s.MarketId, s.Name, s.Station, s.Timezone, s.CronExpression, s.Enabled,
        s.TargetDurationSeconds, s.NotifyBeforePlayoutMinutes, s.EscalationBeforePlayoutMinutes,
        NullIfEmpty(s.VoiceProfileId), s.OutputPath,
        s.Coverage is { } c ? CoverageMapping.ToDto(c) : null,
        s.ContentSelection.Match(cs => (ContentSelectionDto?)cs.ToDto(), () => null),
        NullIfEmpty(s.DeliveryStyleId), NullIfEmpty(s.PlaybookId), NullIfEmpty(s.PronunciationDictionaryId),
        s.VoiceRotation.Match(p => (VoiceRotationDto?)VoiceRotationMapping.ToDto(p), () => null),
        s.FillOrder.Select(FillSourceToWire).ToList(),
        NullIfEmpty(s.BookendId),
        s.BookendRotation.Match(p => (BookendRotationDto?)BookendRotationMapping.ToDto(p), () => null),
        s.BookendTokens.Count > 0 ? s.BookendTokens : null,
        s.Calendar.Match(cal => (ScheduleCalendarDto?)cal.ToDto(), () => null),
        s.CurationEnabled,
        s.PlayoutEnabled);

    public static ContentSelectionDto ToDto(this BulletinContentSelection selection)
    {
        return new ContentSelectionDto(
            selection.Traffic,
            selection.Weather,
            selection.Fuel,
            selection.WeatherRequired,
            selection.FuelRequired,
            selection.SegmentOrder?.Select(segment => segment.ToString()).ToList(),
            selection.TrafficOrder.ToString());
    }

    /// <summary>Nulls fall back to the traffic-only defaults; required flags default to false.</summary>
    public static BulletinContentSelection ToSelection(this ContentSelectionDto dto)
    {
        var segmentOrder = dto.SegmentOrder?.Select(ParseSegment).ToList();
        TrafficRunningOrder trafficOrder = Enum.TryParse(
            dto.TrafficOrder, ignoreCase: true, out TrafficRunningOrder parsedTrafficOrder)
            ? parsedTrafficOrder
            : dto.TrafficOrder is null
                ? TrafficRunningOrder.WorstFirst
                : (TrafficRunningOrder)(-1);

        return new BulletinContentSelection(
            dto.Traffic ?? true,
            dto.Weather ?? false,
            dto.Fuel ?? false,
            dto.WeatherRequired ?? false,
            dto.FuelRequired ?? false,
            segmentOrder,
            trafficOrder);
    }

    private static BulletinSegment ParseSegment(string value) =>
        Enum.TryParse(value, ignoreCase: true, out BulletinSegment segment)
            ? segment
            : (BulletinSegment)(-1);

    public static string FillSourceToWire(FillSource f) => f switch
    {
        FillSource.Weather => "weather",
        FillSource.Fuel => "fuel",
        FillSource.MinorTraffic => "minorTraffic",
        FillSource.Filler => "filler",
        _ => "weather"
    };

    public static FillSource WireToFillSource(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "fuel" => FillSource.Fuel,
        "minortraffic" => FillSource.MinorTraffic,
        "filler" => FillSource.Filler,
        _ => FillSource.Weather
    };

    internal static VoiceSettingsDto ToDto(this VoiceSettings vs)
    {
        return new VoiceSettingsDto(
            vs.Speed,
            vs.Stability,
            vs.SimilarityBoost,
            vs.Style,
            vs.UseSpeakerBoost);
    }


    /// <summary>Maps and validates the wire calendar, including exact station-local date parsing.</summary>
    public static Result<ScheduleCalendar> ToDomain(this ScheduleCalendarDto dto)
    {
        if (dto.Patterns is null)
            return Result<ScheduleCalendar>.Failure("Calendar Patterns are required.");
        if (dto.Assignments is null)
            return Result<ScheduleCalendar>.Failure("Calendar Assignments are required.");

        var patterns = new List<SchedulePattern>(dto.Patterns.Count);
        foreach (var pattern in dto.Patterns)
        {
            if (pattern is null)
                return Result<ScheduleCalendar>.Failure("Calendar Patterns must not contain null entries.");
            patterns.Add(new SchedulePattern(pattern.Id, pattern.Name, pattern.CronExpression));
        }

        var assignments = new List<ScheduleAssignment>(dto.Assignments.Count);
        foreach (var assignment in dto.Assignments)
        {
            if (assignment is null)
                return Result<ScheduleCalendar>.Failure("Calendar Assignments must not contain null entries.");
            if (!DateOnly.TryParseExact(assignment.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var start))
                return Result<ScheduleCalendar>.Failure(
                    $"Calendar assignment StartDate '{assignment.StartDate}' must use yyyy-MM-dd.");
            if (!DateOnly.TryParseExact(assignment.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var end))
                return Result<ScheduleCalendar>.Failure(
                    $"Calendar assignment EndDate '{assignment.EndDate}' must use yyyy-MM-dd.");
            assignments.Add(new ScheduleAssignment(start, end, assignment.PatternId, assignment.Suppress,
                assignment.Note));
        }

        return ScheduleCalendar.Create(patterns, assignments);
    }

    private static ScheduleCalendarDto ToDto(this ScheduleCalendar calendar) =>
        new(
            calendar.Patterns.Select(p => new SchedulePatternDto(p.Id, p.Name, p.CronExpression)).ToList(),
            calendar.Assignments.Select(a => new ScheduleAssignmentDto(
                a.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                a.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                a.PatternId, a.Suppress, a.Note)).ToList());
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

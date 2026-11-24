using System.Globalization;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Converts between the wire-level filler-availability DTO (dates as "yyyy-MM-dd", times as
///     "HH:mm", days as .NET day names) and the validated <see cref="FillerAvailability" />
///     domain model. Mirrors <see cref="VoiceRotationMapping" /> so the filler-statement PUT
///     route and the config-bundle import share ONE converter — a malformed window must fail
///     identically through both write paths, and the round-trip must be byte-stable.
/// </summary>
public static class FillerAvailabilityMapping
{
    /// <summary>Parses a wire DTO into a validated availability window; <c>null</c> stays null (always eligible).</summary>
    public static Result<FillerAvailability?> ToDomain(FillerAvailabilityDto? dto)
    {
        if (dto is null)
            return Result<FillerAvailability?>.Success(null);

        DateOnly? startDate = null;
        if (!string.IsNullOrWhiteSpace(dto.StartDate))
        {
            if (!DateOnly.TryParseExact(dto.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                return Result<FillerAvailability?>.Failure(
                    $"Invalid availability start date '{dto.StartDate}' (expected yyyy-MM-dd).");
            startDate = value;
        }

        DateOnly? endDate = null;
        if (!string.IsNullOrWhiteSpace(dto.EndDate))
        {
            if (!DateOnly.TryParseExact(dto.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                return Result<FillerAvailability?>.Failure(
                    $"Invalid availability end date '{dto.EndDate}' (expected yyyy-MM-dd).");
            endDate = value;
        }

        TimeOnly? startLocal = null;
        if (!string.IsNullOrWhiteSpace(dto.StartLocal))
        {
            if (!TryParseTime(dto.StartLocal, out var value))
                return Result<FillerAvailability?>.Failure(
                    $"Invalid availability start time '{dto.StartLocal}' (expected HH:mm).");
            startLocal = value;
        }

        TimeOnly? endLocal = null;
        if (!string.IsNullOrWhiteSpace(dto.EndLocal))
        {
            if (!TryParseTime(dto.EndLocal, out var value))
                return Result<FillerAvailability?>.Failure(
                    $"Invalid availability end time '{dto.EndLocal}' (expected HH:mm).");
            endLocal = value;
        }

        var days = new List<DayOfWeek>();
        if (dto.Days is not null)
        {
            foreach (var day in dto.Days)
            {
                if (string.IsNullOrWhiteSpace(day) || !Enum.TryParse<DayOfWeek>(day.Trim(), ignoreCase: true, out var parsed))
                    return Result<FillerAvailability?>.Failure(
                        $"Unknown availability day '{day}'. Use full day names (Sunday through Saturday).");
                days.Add(parsed);
            }
        }

        return FillerAvailability.Create(startDate, endDate, days, startLocal, endLocal)
            .Map<FillerAvailability?>(a => a);
    }

    /// <summary>Serialises a window for the wire; <c>null</c> stays null so a statement with no
    /// window round-trips as no window.</summary>
    public static FillerAvailabilityDto? ToDto(FillerAvailability? availability)
    {
        if (availability is null)
            return null;

        return new FillerAvailabilityDto(
            availability.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            availability.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            availability.Days.Count > 0
                ? availability.Days.Select(d => d.ToString()).ToList()
                : null,
            availability.StartLocal?.ToString("HH\\:mm", CultureInfo.InvariantCulture),
            availability.EndLocal?.ToString("HH\\:mm", CultureInfo.InvariantCulture));
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

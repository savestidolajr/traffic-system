using System.Globalization;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Converts between the wire-level bookend-rotation DTO (mode discriminated by a string,
///     window times as "HH:mm") and the validated <see cref="BookendRotationPolicy" /> domain
///     model. Mirrors <see cref="VoiceRotationMapping" /> — including the mode-alias tolerance —
///     so the intro/outro rotations behave exactly like the voice rotation at the boundary.
/// </summary>
public static partial class BookendRotationMapping
{
    public static Result<BookendRotationPolicy> ToDomain(BookendRotationDto dto)
    {
        var mode = dto.Mode?.Trim().ToLowerInvariant() switch
        {
            "single" => (BookendSelectionMode?)BookendSelectionMode.Single,
            "roundrobin" or "rotate" or "round-robin" => BookendSelectionMode.RoundRobin,
            "timeofday" or "time-of-day" => BookendSelectionMode.TimeOfDay,
            _ => null
        };
        if (mode is null)
            return Result<BookendRotationPolicy>.Failure(
                $"Unknown bookend selection mode '{dto.Mode}'. Supported: single, roundRobin, timeOfDay.");

        IReadOnlyList<BookendWindow>? windows = null;
        if (dto.Windows is { Count: > 0 })
        {
            var built = new List<BookendWindow>(dto.Windows.Count);
            foreach (var w in dto.Windows)
            {
                if (!TryParseTime(w.Start, out var start))
                    return Result<BookendRotationPolicy>.Failure(
                        $"Invalid window start time '{w.Start}' (expected HH:mm).");
                if (!TryParseTime(w.End, out var end))
                    return Result<BookendRotationPolicy>.Failure(
                        $"Invalid window end time '{w.End}' (expected HH:mm).");
                built.Add(new BookendWindow(start, end, w.BookendIds ?? []));
            }

            windows = built;
        }

        return BookendRotationPolicy.Create(mode.Value, dto.BookendIds, windows);
    }

    public static BookendRotationDto ToDto(BookendRotationPolicy policy)
    {
        return new BookendRotationDto(
            policy.Mode switch
            {
                BookendSelectionMode.RoundRobin => "roundRobin",
                BookendSelectionMode.TimeOfDay => "timeOfDay",
                _ => "single"
            },
            policy.BookendIds.Count > 0 ? policy.BookendIds : null,
            policy.Windows.Count > 0
                ? policy.Windows
                    .Select(w => new BookendWindowDto(
                        w.StartLocal.ToString("HH\\:mm", CultureInfo.InvariantCulture),
                        w.EndLocal.ToString("HH\\:mm", CultureInfo.InvariantCulture),
                        w.BookendIds))
                    .ToList()
                : null);
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

using System.Globalization;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Converts between the wire-level voice-rotation DTO (mode discriminated by a string, window
///     times as "HH:mm") and the validated <see cref="VoiceRotationPolicy" /> domain model.
/// </summary>
public static partial class VoiceRotationMapping
{
    public static Result<VoiceRotationPolicy> ToDomain(VoiceRotationDto dto)
    {
        var mode = dto.Mode?.Trim().ToLowerInvariant() switch
        {
            "single" => (VoiceSelectionMode?)VoiceSelectionMode.Single,
            "roundrobin" or "rotate" => VoiceSelectionMode.RoundRobin,
            "timeofday" or "time-of-day" => VoiceSelectionMode.TimeOfDay,
            _ => null
        };
        if (mode is null)
            return Result<VoiceRotationPolicy>.Failure(
                $"Unknown voice selection mode '{dto.Mode}'. Supported: single, roundRobin, timeOfDay.");

        IReadOnlyList<VoiceWindow>? windows = null;
        if (dto.Windows is { Count: > 0 })
        {
            var built = new List<VoiceWindow>(dto.Windows.Count);
            foreach (var w in dto.Windows)
            {
                if (!TryParseTime(w.Start, out var start))
                    return Result<VoiceRotationPolicy>.Failure(
                        $"Invalid window start time '{w.Start}' (expected HH:mm).");
                if (!TryParseTime(w.End, out var end))
                    return Result<VoiceRotationPolicy>.Failure(
                        $"Invalid window end time '{w.End}' (expected HH:mm).");
                built.Add(new VoiceWindow(start, end, w.VoiceProfileIds ?? []));
            }

            windows = built;
        }

        return VoiceRotationPolicy.Create(mode.Value, dto.VoiceProfileIds, windows);
    }

    public static VoiceRotationDto ToDto(VoiceRotationPolicy policy)
    {
        return new VoiceRotationDto(
            policy.Mode switch
            {
                VoiceSelectionMode.RoundRobin => "roundRobin",
                VoiceSelectionMode.TimeOfDay => "timeOfDay",
                _ => "single"
            },
            policy.VoiceProfileIds.Count > 0 ? policy.VoiceProfileIds : null,
            policy.Windows.Count > 0
                ? policy.Windows
                    .Select(w => new VoiceWindowDto(
                        w.StartLocal.ToString("HH\\:mm", CultureInfo.InvariantCulture),
                        w.EndLocal.ToString("HH\\:mm", CultureInfo.InvariantCulture),
                        w.VoiceProfileIds))
                    .ToList()
                : null);
    }

    private static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}

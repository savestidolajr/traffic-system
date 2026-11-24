using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>How a station chooses its bulletin voice for each broadcast.</summary>
public enum VoiceSelectionMode
{
    /// <summary>Always the schedule's primary voice profile — identical to having no rotation.</summary>
    Single = 0,

    /// <summary>Cycle the ordered voice set, one profile per consecutive bulletin.</summary>
    RoundRobin = 1,

    /// <summary>Use the profile (or set) whose local-time window covers the broadcast.</summary>
    TimeOfDay = 2
}

/// <summary>
///     A station-local time window mapping to a fixed voice (one id) or a rotation set (many ids).
///     Half-open <c>[StartLocal, EndLocal)</c>. <c>EndLocal &lt;= StartLocal</c> wraps past midnight
///     (e.g. 22:00–06:00). Times are interpreted in the schedule's timezone.
/// </summary>
public sealed record VoiceWindow(TimeOnly StartLocal, TimeOnly EndLocal, IReadOnlyList<string> VoiceProfileIds)
{
    /// <summary>True when this window covers the given station-local time.</summary>
    public bool Contains(TimeOnly localNow) =>
        TimeWindowOverlap.Contains(StartLocal, EndLocal, localNow);
}

/// <summary>
///     How a station selects its voice each broadcast, beyond a single fixed profile. Resolved
///     live by the poller at airtime. The schedule's <c>VoiceProfileId</c> stays the primary /
///     fallback voice: <see cref="VoiceSelectionMode.Single" /> uses it, and
///     <see cref="VoiceSelectionMode.TimeOfDay" /> falls back to it for any uncovered time.
///     Structurally validated by <see cref="Create" />; profile-existence is checked at the API
///     boundary.
/// </summary>
public sealed record VoiceRotationPolicy
{
    public const int MaxVoices = 12;
    public const int MaxWindows = 12;

    [JsonConstructor]
    private VoiceRotationPolicy(
        VoiceSelectionMode mode,
        IReadOnlyList<string> voiceProfileIds,
        IReadOnlyList<VoiceWindow> windows)
    {
        Mode = mode;
        VoiceProfileIds = voiceProfileIds;
        Windows = windows;
    }

    public VoiceSelectionMode Mode { get; }

    /// <summary>The ordered rotation set (RoundRobin). Empty for Single and TimeOfDay.</summary>
    public IReadOnlyList<string> VoiceProfileIds { get; }

    /// <summary>The non-overlapping local-time windows (TimeOfDay). Empty for Single and RoundRobin.</summary>
    public IReadOnlyList<VoiceWindow> Windows { get; }

    /// <summary>Validates and normalises a policy. The only construction path.</summary>
    public static Result<VoiceRotationPolicy> Create(
        VoiceSelectionMode mode,
        IReadOnlyList<string>? voiceProfileIds,
        IReadOnlyList<VoiceWindow>? windows)
    {
        var ids = voiceProfileIds ?? [];
        var wins = windows ?? [];

        switch (mode)
        {
            case VoiceSelectionMode.Single:
                // Single ignores the set/windows; resolution uses the schedule's primary voice.
                return Result<VoiceRotationPolicy>.Success(new VoiceRotationPolicy(mode, [], []));

            case VoiceSelectionMode.RoundRobin:
                if (wins.Count > 0)
                    return Result<VoiceRotationPolicy>.Failure(
                        "A round-robin rotation must not define time-of-day windows.");
                var setResult = NormalizeSet(ids, "rotation");
                return setResult.IsFailure
                    ? Result<VoiceRotationPolicy>.Failure(setResult.Error)
                    : Result<VoiceRotationPolicy>.Success(new VoiceRotationPolicy(mode, setResult.Value, []));

            case VoiceSelectionMode.TimeOfDay:
                if (ids.Count > 0)
                    return Result<VoiceRotationPolicy>.Failure(
                        "A time-of-day rotation must not define a top-level voice set.");
                if (wins.Count == 0)
                    return Result<VoiceRotationPolicy>.Failure(
                        "A time-of-day rotation needs at least one window.");
                if (wins.Count > MaxWindows)
                    return Result<VoiceRotationPolicy>.Failure(
                        $"A time-of-day rotation supports at most {MaxWindows} windows.");

                var normalized = new List<VoiceWindow>(wins.Count);
                foreach (var w in wins)
                {
                    if (w.StartLocal == w.EndLocal)
                        return Result<VoiceRotationPolicy>.Failure(
                            "A time-of-day window must not start and end at the same time.");
                    var winSet = NormalizeSet(w.VoiceProfileIds, "window");
                    if (winSet.IsFailure)
                        return Result<VoiceRotationPolicy>.Failure(winSet.Error);
                    normalized.Add(new VoiceWindow(w.StartLocal, w.EndLocal, winSet.Value));
                }

                if (HasOverlap(normalized))
                    return Result<VoiceRotationPolicy>.Failure("Time-of-day windows must not overlap.");

                return Result<VoiceRotationPolicy>.Success(new VoiceRotationPolicy(mode, [], normalized));

            default:
                return Result<VoiceRotationPolicy>.Failure($"Unknown voice selection mode '{mode}'.");
        }
    }

    /// <summary>
    ///     The ordered candidate profile ids active at the given station-local time. RoundRobin
    ///     returns the whole set; TimeOfDay returns the matched window's ids (or the fallback when
    ///     no window covers the time); Single returns just the fallback primary.
    /// </summary>
    public IReadOnlyList<string> CandidatesAt(TimeOnly localNow, string fallbackPrimary)
    {
        switch (Mode)
        {
            case VoiceSelectionMode.RoundRobin when VoiceProfileIds.Count > 0:
                return VoiceProfileIds;
            case VoiceSelectionMode.TimeOfDay:
                foreach (var w in Windows)
                    if (w.Contains(localNow))
                        return w.VoiceProfileIds;
                return Fallback(fallbackPrimary);
            default:
                return Fallback(fallbackPrimary);
        }
    }

    /// <summary>Every distinct profile id this policy can resolve to, for referential-integrity checks.</summary>
    public IReadOnlyList<string> AllReferencedVoiceIds()
    {
        var seen = new List<string>();
        void Add(IReadOnlyList<string> ids)
        {
            foreach (var id in ids)
                if (!seen.Contains(id))
                    seen.Add(id);
        }

        Add(VoiceProfileIds);
        foreach (var w in Windows)
            Add(w.VoiceProfileIds);
        return seen;
    }

    private static IReadOnlyList<string> Fallback(string primary) =>
        string.IsNullOrWhiteSpace(primary) ? [] : [primary];

    private static Result<IReadOnlyList<string>> NormalizeSet(IReadOnlyList<string> ids, string label)
    {
        var cleaned = new List<string>();
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id))
                return Result<IReadOnlyList<string>>.Failure($"A {label} voice id must not be blank.");
            var trimmed = id.Trim();
            if (cleaned.Contains(trimmed))
                return Result<IReadOnlyList<string>>.Failure(
                    $"A {label} must not list the same voice twice ('{trimmed}').");
            cleaned.Add(trimmed);
        }

        if (cleaned.Count == 0)
            return Result<IReadOnlyList<string>>.Failure($"A {label} needs at least one voice.");
        if (cleaned.Count > MaxVoices)
            return Result<IReadOnlyList<string>>.Failure($"A {label} supports at most {MaxVoices} voices.");
        return Result<IReadOnlyList<string>>.Success(cleaned);
    }

    private static bool HasOverlap(IReadOnlyList<VoiceWindow> windows) =>
        TimeWindowOverlap.HasOverlap([.. windows.Select(w => (w.StartLocal, w.EndLocal))]);
}

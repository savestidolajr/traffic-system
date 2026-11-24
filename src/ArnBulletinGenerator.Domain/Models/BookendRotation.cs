using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>How a station chooses which intro (or outro) to read for each broadcast.</summary>
public enum BookendSelectionMode
{
    /// <summary>Always the station's primary bookend id — identical to having no rotation.</summary>
    Single = 0,

    /// <summary>Cycle the ordered set, one bookend per consecutive bulletin, so the phrasing varies.</summary>
    RoundRobin = 1,

    /// <summary>Use the bookend (or set) whose local-time window covers the broadcast.</summary>
    TimeOfDay = 2
}

/// <summary>
///     A station-local time window mapping to a fixed bookend (one id) or a rotation set (many
///     ids). Half-open <c>[StartLocal, EndLocal)</c>. <c>EndLocal &lt;= StartLocal</c> wraps past
///     midnight (e.g. 22:00–06:00). Times are interpreted in the schedule's timezone.
/// </summary>
public sealed record BookendWindow(TimeOnly StartLocal, TimeOnly EndLocal, IReadOnlyList<string> BookendIds)
{
    /// <summary>True when this window covers the given station-local time.</summary>
    public bool Contains(TimeOnly localNow) =>
        TimeWindowOverlap.Contains(StartLocal, EndLocal, localNow);
}

/// <summary>
///     How a station selects which bookend PAIR to read each broadcast, beyond a single fixed one.
///     Resolved live by the poller at airtime, exactly like <see cref="VoiceRotationPolicy" />.
///     The schedule's primary <c>BookendId</c> stays the fallback:
///     <see cref="BookendSelectionMode.Single" /> uses it, and
///     <see cref="BookendSelectionMode.TimeOfDay" /> falls back to it for any uncovered time.
///     Structurally validated by <see cref="Create" />; bookend existence is checked at the API
///     boundary.
/// </summary>
public sealed record BookendRotationPolicy
{
    public const int MaxBookends = 12;
    public const int MaxWindows = 12;

    [JsonConstructor]
    private BookendRotationPolicy(
        BookendSelectionMode mode,
        IReadOnlyList<string> bookendIds,
        IReadOnlyList<BookendWindow> windows)
    {
        Mode = mode;
        BookendIds = bookendIds;
        Windows = windows;
    }

    public BookendSelectionMode Mode { get; }

    /// <summary>The ordered rotation set (RoundRobin). Empty for Single and TimeOfDay.</summary>
    public IReadOnlyList<string> BookendIds { get; }

    /// <summary>The non-overlapping local-time windows (TimeOfDay). Empty for Single and RoundRobin.</summary>
    public IReadOnlyList<BookendWindow> Windows { get; }

    /// <summary>Validates and normalises a policy. The only construction path.</summary>
    public static Result<BookendRotationPolicy> Create(
        BookendSelectionMode mode,
        IReadOnlyList<string>? bookendIds,
        IReadOnlyList<BookendWindow>? windows)
    {
        var ids = bookendIds ?? [];
        var wins = windows ?? [];

        switch (mode)
        {
            case BookendSelectionMode.Single:
                // Single ignores the set/windows; resolution uses the schedule's primary bookend.
                return Result<BookendRotationPolicy>.Success(new BookendRotationPolicy(mode, [], []));

            case BookendSelectionMode.RoundRobin:
                if (wins.Count > 0)
                    return Result<BookendRotationPolicy>.Failure(
                        "A round-robin rotation must not define time-of-day windows.");
                var setResult = NormalizeSet(ids, "rotation");
                return setResult.IsFailure
                    ? Result<BookendRotationPolicy>.Failure(setResult.Error)
                    : Result<BookendRotationPolicy>.Success(new BookendRotationPolicy(mode, setResult.Value, []));

            case BookendSelectionMode.TimeOfDay:
                if (ids.Count > 0)
                    return Result<BookendRotationPolicy>.Failure(
                        "A time-of-day rotation must not define a top-level bookend set.");
                if (wins.Count == 0)
                    return Result<BookendRotationPolicy>.Failure(
                        "A time-of-day rotation needs at least one window.");
                if (wins.Count > MaxWindows)
                    return Result<BookendRotationPolicy>.Failure(
                        $"A time-of-day rotation supports at most {MaxWindows} windows.");

                var normalized = new List<BookendWindow>(wins.Count);
                foreach (var w in wins)
                {
                    if (w.StartLocal == w.EndLocal)
                        return Result<BookendRotationPolicy>.Failure(
                            "A time-of-day window must not start and end at the same time.");
                    var winSet = NormalizeSet(w.BookendIds, "window");
                    if (winSet.IsFailure)
                        return Result<BookendRotationPolicy>.Failure(winSet.Error);
                    normalized.Add(new BookendWindow(w.StartLocal, w.EndLocal, winSet.Value));
                }

                if (TimeWindowOverlap.HasOverlap([.. normalized.Select(w => (w.StartLocal, w.EndLocal))]))
                    return Result<BookendRotationPolicy>.Failure("Time-of-day windows must not overlap.");

                return Result<BookendRotationPolicy>.Success(new BookendRotationPolicy(mode, [], normalized));

            default:
                return Result<BookendRotationPolicy>.Failure($"Unknown bookend selection mode '{mode}'.");
        }
    }

    /// <summary>
    ///     The ordered candidate bookend ids active at the given station-local time. RoundRobin
    ///     returns the whole set; TimeOfDay returns the matched window's ids (or the fallback when
    ///     no window covers the time); Single returns just the fallback primary.
    /// </summary>
    public IReadOnlyList<string> CandidatesAt(TimeOnly localNow, string fallbackPrimary)
    {
        switch (Mode)
        {
            case BookendSelectionMode.RoundRobin when BookendIds.Count > 0:
                return BookendIds;
            case BookendSelectionMode.TimeOfDay:
                foreach (var w in Windows)
                    if (w.Contains(localNow))
                        return w.BookendIds;
                return Fallback(fallbackPrimary);
            default:
                return Fallback(fallbackPrimary);
        }
    }

    /// <summary>Every distinct bookend id this policy can resolve to, for referential-integrity checks.</summary>
    public IReadOnlyList<string> AllReferencedBookendIds()
    {
        var seen = new List<string>();
        void Add(IReadOnlyList<string> ids)
        {
            foreach (var id in ids)
                if (!seen.Contains(id))
                    seen.Add(id);
        }

        Add(BookendIds);
        foreach (var w in Windows)
            Add(w.BookendIds);
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
                return Result<IReadOnlyList<string>>.Failure($"A {label} bookend id must not be blank.");
            var trimmed = id.Trim();
            if (cleaned.Contains(trimmed))
                return Result<IReadOnlyList<string>>.Failure(
                    $"A {label} must not list the same bookend twice ('{trimmed}').");
            cleaned.Add(trimmed);
        }

        if (cleaned.Count == 0)
            return Result<IReadOnlyList<string>>.Failure($"A {label} needs at least one bookend.");
        if (cleaned.Count > MaxBookends)
            return Result<IReadOnlyList<string>>.Failure($"A {label} supports at most {MaxBookends} bookends.");
        return Result<IReadOnlyList<string>>.Success(cleaned);
    }
}

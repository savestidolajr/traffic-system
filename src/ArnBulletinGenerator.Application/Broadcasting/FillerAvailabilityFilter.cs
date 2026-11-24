using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Filters loaded filler statements to those whose <see cref="FillerAvailability" /> window
///     (if any) covers the station-local now. Shared by the generation path
///     (<see cref="MarketContentFetcher.GetFillerStatementsSafeAsync" />) and the fallback padder
///     (<see cref="FallbackAudioPadder.TryPadAsync" />) so the two load boundaries can never
///     drift apart. Deliberately NOT part of <see cref="FillerStatementSelector" />: that class
///     promises no clock and no I/O, and this filter is evaluated at load time against the
///     current station-local time instead.
/// </summary>
internal static class FillerAvailabilityFilter
{
    /// <summary>
    ///     Returns the statements eligible at <paramref name="localDate" /> /
    ///     <paramref name="localTime" /> (null availability = always eligible) and the count
    ///     excluded purely by their availability window — so a caller can log "N excluded by
    ///     window" and tell an all-expired library apart from an unconfigured one (an
    ///     information-level concern: a station whose windows have silently lapsed looks
    ///     identical to a station that never set any up). Exclusion counting is deliberately
    ///     post-<c>Enabled</c>: a statement the operator already disabled is not "excluded by
    ///     its window", and disabled statements stay in the returned pool for callers whose
    ///     selector applies the Enabled rule itself.
    /// </summary>
    public static (IReadOnlyList<FillerStatement> Eligible, int ExcludedByWindow) Apply(
        IReadOnlyList<FillerStatement> statements, DateOnly localDate, TimeOnly localTime)
    {
        var eligible = new List<FillerStatement>(statements.Count);
        int excluded = 0;
        foreach (var s in statements)
        {
            if (!s.Enabled)
            {
                eligible.Add(s);
                continue;
            }

            if (s.Availability is null || s.Availability.IsActiveAt(localDate, localTime))
                eligible.Add(s);
            else
                excluded++;
        }

        return (eligible, excluded);
    }
}

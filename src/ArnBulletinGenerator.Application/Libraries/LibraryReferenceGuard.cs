using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Libraries;

/// <summary>
///     Referential-integrity guard for the shared libraries a schedule points at — voice profiles,
///     delivery styles and pronunciation dictionaries.
///     <para>
///     Assignment was already validated in one direction: pointing a schedule at an id that does not
///     exist is rejected. Deletion was not, so an item could be removed out from under a station that
///     still referenced it. Proven on dev 2026-07-28 — deleting melbourne's in-use pronunciation
///     dictionary returned 204 with no warning, the schedule kept the dangling id, and the damage was
///     then invisible: <c>StationConfigResolver</c> treats an unloadable dictionary as "none", so the
///     next bulletin airs with no pronunciation corrections and no error. Recovery was manual and
///     lossy, because recreating mints a new id and the version history is gone.
///     </para>
/// </summary>
internal static class LibraryReferenceGuard
{
    /// <summary>
    ///     Fails when any schedule still references <paramref name="id" />, naming the stations so the
    ///     operator knows exactly what to unassign. A single primary reference; see the plural
    ///     overload for references that can come from more than one source (e.g. a rotation set).
    ///     <para>
    ///     Fails CLOSED when the schedule list cannot be read: this gates an irreversible delete, so
    ///     "we could not check" must block rather than wave it through.
    ///     </para>
    /// </summary>
    public static Task<Result> EnsureNotAssignedAsync(
        IScheduleRepository schedules,
        string library,
        string id,
        Func<ScheduleReferenceSummary, string?> reference,
        CancellationToken cancellationToken) =>
        EnsureNotAssignedAsync(schedules, library, id,
            s =>
            {
                var single = reference(s);
                return single is null ? [] : [single];
            },
            cancellationToken);

    /// <summary>
    ///     Fails when any schedule still references <paramref name="id" /> through ANY of the ids
    ///     <paramref name="references" /> yields (a primary id, a rotation set, window ids, ...),
    ///     naming the stations so the operator knows exactly what to unassign.
    ///     <para>
    ///     The repository supplies a narrow projection, so a delete does not materialize schedule
    ///     coverage/calendar/content payloads. Repositories without the projection use the
    ///     interface's compatibility implementation, which still preserves the fail-closed guard.
    ///     </para>
    ///     <para>
    ///     Fails CLOSED when the schedule list cannot be read: this gates an irreversible delete, so
    ///     "we could not check" must block rather than wave it through.
    ///     </para>
    /// </summary>
    public static async Task<Result> EnsureNotAssignedAsync(
        IScheduleRepository schedules,
        string library,
        string id,
        Func<ScheduleReferenceSummary, IEnumerable<string>> references,
        CancellationToken cancellationToken)
    {
        // NSubstitute and a few migration-only repositories return null for an unconfigured
        // optional interface member. Fall back to the legacy read in that case; the production
        // Table Storage implementation always returns the projected result above.
        var projectedTask = schedules.ListScheduleReferenceSummariesAsync(cancellationToken);
        var all = projectedTask is null
            ? null
            : await projectedTask;
        if (all is null)
        {
            var legacy = await schedules.ListSchedulesAsync(cancellationToken);
            if (legacy.IsFailure)
                return Result.Failure(OperationalFailure.Retryable(
                    "library.reference_check_failed",
                    $"Could not verify whether any station still uses this {library}, so it was not "
                    + $"deleted. Try again in a moment. ({legacy.Error})"));

            all = Result<IEnumerable<ScheduleReferenceSummary>>.Success(
                legacy.Value.Select(ScheduleReferenceSummary.FromSchedule));
        }

        if (all.IsFailure)
            return Result.Failure(OperationalFailure.Retryable(
                "library.reference_check_failed",
                $"Could not verify whether any station still uses this {library}, so it was not "
                + $"deleted. Try again in a moment. ({all.Error})"));

        var assigned = all.Value
            .Where(s => (references(s) ?? []).Any(r =>
                r is not null && string.Equals(r.Trim(), id.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Select(s => string.IsNullOrWhiteSpace(s.Station) ? s.MarketId : $"{s.Station} ({s.MarketId})")
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (assigned.Count == 0)
            return Result.Success();

        return Result.Failure(OperationalFailure.Conflict(
            "library.in_use",
            $"This {library} is still assigned to {string.Join(", ", assigned)}. "
            + "Unassign it from every station before deleting."));
    }
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Decides whether a HERE incident is a current disruption or a long-term worksite, from HERE's
///     own <c>startTime</c>/<c>endTime</c> window.
///     <para>
///         RISK-38. Both fields are present on 100% of live Sydney closures and neither is read today,
///         so a closure scheduled 2025-02-04 → 2026-12-30 (693 days) is presented as this morning's
///         news and collects "still" continuity every cycle. Measured live 2026-08-14: windows of 693,
///         166, 165, 131, 97 and 97 days were all being treated as current.
///     </para>
///     The window classification itself lives in <see cref="IncidentWindow" />; this type is the
///     editorial reading of it.
/// </summary>
public static class IncidentCurrencyPolicy
{
    /// <summary>
    ///     A window at or beyond this length is a worksite, not a traffic event. The exact threshold is
    ///     an ARN editorial decision; the tests pin the behaviour, not the number.
    /// </summary>
    public const int LongTermWindowDays = 7;

    /// <summary>
    ///     True when HERE's own schedule says this is a long-running planned condition rather than a
    ///     current disruption.
    /// </summary>
    /// <param name="incident">The incident, carrying HERE's <c>startTime</c>/<c>endTime</c>.</param>
    /// <param name="nowUtc">Evaluation time.</param>
    public static bool IsLongTermWorks(TrafficIncident incident, DateTimeOffset nowUtc) =>
        IncidentWindow.Classify(
            incident.StartTimeUtc, incident.EndTimeUtc, incident.EntryTimeUtc, nowUtc) == WindowClass.Scheduled;

    /// <summary>
    ///     True when this incident may carry continuity language ("still", "again"). A long-term
    ///     worksite never may: it has not changed since the last bulletin and it will not change before
    ///     the next one.
    /// </summary>
    public static bool MayCarryContinuity(TrafficIncident incident, DateTimeOffset nowUtc) =>
        !IsLongTermWorks(incident, nowUtc);
}

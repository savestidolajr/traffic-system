namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Stable cause tokens recorded on a job when a standby/fallback bulletin is applied, so the
///     operator console can explain WHY the slot was covered rather than just "fallback used".
///     The token is the cause; the fallback KIND (re-used same-day bulletin vs generic filler cart)
///     is derived from whether <see cref="Job.FallbackSourceJobId" /> is set. The console maps these
///     to operator-friendly copy; unknown tokens degrade to a generic explanation.
/// </summary>
public static class FallbackReasons
{
    /// <summary>The market was too quiet to fill the slot within the slow-down cap (content-limited underrun).</summary>
    public const string ContentLimited = "content-limited";

    /// <summary>The bulletin missed its airtime window or had no usable audio when the deadline poller swept it.</summary>
    public const string MissedWindow = "missed-window";

    /// <summary>An operator rejected the generated bulletin and sent the slot to fallback.</summary>
    public const string OperatorRejected = "operator-rejected";

    /// <summary>
    ///     Generation produced a script the quality gates refused to air (final grounding hard gate
    ///     exhausted its refine budget, or content safety rejected it). The slot is covered by the
    ///     standby chain rather than left for the deadline sweep — see BroadcastOrchestrator.
    /// </summary>
    public const string QualityGateFailed = "quality-gate-failed";
}

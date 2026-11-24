using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     The kind of fallback applied to a job that could not produce a fresh bulletin.
/// </summary>
public enum FallbackKind
{
    /// <summary>No fallback was applied (none available).</summary>
    None,

    /// <summary>The last successful same-day bulletin for the market/station was re-used.</summary>
    SameDayBulletin,

    /// <summary>The configured generic filler cart was applied.</summary>
    FillerCart
}

/// <summary>Outcome of an attempt to apply a fallback bulletin to a job.</summary>
public readonly record struct FallbackOutcome(FallbackKind Kind)
{
    public bool Applied => Kind != FallbackKind.None;
}

/// <summary>
///     Applies the broadcast fallback chain to a single job — last good same-day bulletin for the
///     market/station, else the configured filler cart. Transitions the job to
///     <see cref="JobStatus.FallbackUsed" /> and persists it. Shared by the schedule poller's
///     deadline-driven sweep and the orchestrator's synchronous content-limited path so the chain
///     lives in exactly one place.
/// </summary>
public interface IBulletinFallbackService
{
    /// <param name="reason">A <see cref="FallbackReasons" /> cause token recorded on the job for the console.</param>
    Task<FallbackOutcome> TryApplyFallbackAsync(
        Job job, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Applies the filler cart to a job: the per-voice cart synthesized for the job's own
    ///     voice (padded toward the slot when it falls materially short — finding 32), else the
    ///     evergreen WAV, then persists the <see cref="JobStatus.FallbackUsed" /> transition.
    ///     <para>
    ///         The cart resolution and padding live HERE so the deadline sweep
    ///         (<see cref="TryApplyFallbackAsync" />) and an operator's manual air-source override
    ///         (<c>JobService.SetAirSourceAsync</c>) can never diverge on which cart airs.
    ///         <paramref name="by" /> names who chose the cart — the sweep passes the default
    ///         (<c>system</c>); the operator path passes the requesting operator so the job's
    ///         status history credits the human, not the clock.
    ///     </para>
    /// </summary>
    Task<Result> ApplyFillerCartAsync(
        Job job, string reason, string by = "system", CancellationToken cancellationToken = default);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Append-only store of measured spoken-duration samples, captured at synthesis time.
///     <para>
///         Feeds the per-voice scale in the spoken-duration calibration without the lossy offline
///         reconstruction it is fitted from today (see <see cref="SpokenDurationSample" /> for the
///         measured loss rate). Write-only on the broadcast path and deliberately fail-soft:
///         telemetry for a duration model must never be able to fail a bulletin.
///     </para>
/// </summary>
public interface ISpokenDurationSampleRepository
{
    /// <summary>Appends one measured sample. Implementations MUST NOT throw.</summary>
    Task RecordAsync(SpokenDurationSample sample, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Every sample for one calibration fingerprint, newest first. Used by the fit and by the
    ///     diagnostics that compare a live scale against the shipped one.
    /// </summary>
    Task<IReadOnlyList<SpokenDurationSample>> GetSamplesAsync(
        string fingerprint,
        CancellationToken cancellationToken = default);
}

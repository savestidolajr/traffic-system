using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Measures and maintains a voice's real speaking rate so duration prediction and slot fitting can be based on actual output rather than a generic default.</summary>
public interface IVoiceCalibrationService
{
    /// <summary>
    ///     Resolves the voice's measured rate, recalibrating when the row is missing or stale.
    ///     <paramref name="modelOverride" /> is forwarded to that recalibration so it measures on the
    ///     model the station airs on rather than the configured default.
    /// </summary>
    Task<CalibrationMetrics> GetCalibrationMetricsAsync(
        string voiceId,
        Option<VoiceSettings> settings = default,
        bool triggerCalibrationIfMissing = true,
        CancellationToken cancellationToken = default,
        Option<string> modelOverride = default);

    /// <summary>
    ///     Measures the voice against the service's fixed reference phrase. <paramref name="modelOverride" />
    ///     MUST carry the model the station actually airs on — the same voice reads at materially
    ///     different rates per model (135.6 wpm on eleven_v3 vs 159.4 on eleven_multilingual_v2 vs
    ///     170.0 on eleven_turbo_v2_5, measured), so measuring on the configured default instead bakes a
    ///     systematic error into the row. Bootstraps a cold row; on a row that already carries real
    ///     bulletin measurements it only refreshes the staleness clock.
    /// </summary>
    Task<Result<VoiceCalibration>> CalibrateVoiceAsync(
        string voiceId,
        Option<VoiceSettings> settings = default,
        CancellationToken cancellationToken = default,
        Option<string> modelOverride = default);

    Task<bool> HasValidCalibrationAsync(
        string voiceId,
        Option<VoiceSettings> settings = default,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Records a measured bulletin read against the same voice, settings, and model tuple used
    ///     for prediction. Omitting <paramref name="modelOverride" /> retains the legacy
    ///     <c>unknown</c>-model bucket for callers without model context.
    /// </summary>
    Task RecordActualDurationAsync(
        string voiceId,
        VoiceSettings settings,
        string text,
        double actualDuration,
        CancellationToken cancellationToken = default,
        Option<string> modelOverride = default);
}

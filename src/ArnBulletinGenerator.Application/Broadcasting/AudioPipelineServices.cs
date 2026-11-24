using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     The audio-side services a finalized script is rendered through: TTS synthesis, duration
///     estimation, filler-cart caching, per-market voice calibration, delivery to the operator
///     output, and (optionally) transcription for word-timing verification. Bundled because
///     <see cref="BroadcastOrchestrator" /> only ever threads them through as a group into
///     <c>ScriptGenerationEngine</c> and <c>AudioFinalizer</c>.
/// </summary>
public sealed record AudioPipelineServices(
    IAudioPipelineService AudioPipeline,
    IDurationEstimator DurationEstimator,
    IFillerAudioCacheService FillerAudioCacheService,
    IVoiceCalibrationService VoiceCalibrationService,
    IBroadcastOutput BroadcastOutput,
    IAudioTranscriber? AudioTranscriber = null);

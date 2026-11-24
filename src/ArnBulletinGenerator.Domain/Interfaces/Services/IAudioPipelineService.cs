using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Drives script-to-audio synthesis toward a target slot duration, then fine-tunes, trims, and stitches the result into the final broadcast clip.</summary>
public interface IAudioPipelineService
{
    Task<Result<AudioSynthesisResult>> SynthesizeWithDurationTargetAsync(
        string script,
        double targetDuration,
        Option<string> voiceIdOverride = default,
        Option<VoiceSettings> settings = default,
        Option<string> modelOverride = default,
        CancellationToken cancellationToken = default,
        double initialSpeed = 0,
        Option<string> market = default,
        PronunciationStack? pronunciationStack = null,
        double? maxSpeedUpFactorOverride = null,
        bool allowDynamicSlot = false,
        double? maxSlowDownFactorOverride = null,
        // The station's resolved outro, when it has one. An overrun is fitted by dropping trailing
        // sentences, so without this the closer — the last thing the listener hears, and the whole
        // point of a bookend — is the first thing deleted.
        string? lockedSuffix = null,
        // The read's weather/fuel lines as the workflow identified them, and whether the station
        // marks them ALWAYS INCLUDE. An optional close is preferred-but-droppable: when the slot
        // cannot hold both it and the traffic, the trim degrades to a plain prefix cut that deletes
        // it. Required fixtures are a contract, so the trim sheds traffic to the floor and keeps them.
        Services.FixtureLines? fixtures = null,
        // The station's resolved intro, when it has one. The keep-floor must never count the intro
        // as the one sentence it saves — that collapse aired a weather-only read from a full
        // evidence set (dev 9bb43d88/dce23ad1, 2026-08-17).
        string? lockedPrefix = null,
        // Per-broadcast generation owns the total attempt budget; callers that do not participate
        // retain the configured retry policy.
        int? maxSynthesisAttemptsOverride = null);

    byte[] FineTuneToTargetDuration(byte[] audio, double targetDuration,
        double? maxSpeedUpFactorOverride = null, double? maxSlowDownFactorOverride = null);

    /// <summary>True when audio is within EffectiveToleranceSeconds of targetDuration AND its trailing silence is within MaxSilencePadSeconds — the same gate SynthesizeWithDurationTargetAsync applies to its own output.</summary>
    bool IsSlotFilled(byte[] audio, double targetDuration);

    /// <summary>
    ///     Trims any silence padding appended past the real spoken content, returning audio
    ///     containing only the actual speech. <see cref="SynthesizeWithDurationTargetAsync" />'s
    ///     underrun path pads a take toward its target with raw silence even when it can't reach
    ///     it (correctly reporting SlotFilled=false on that padded clip alone) — stitching filler
    ///     directly onto that padded clip buries the filler behind dead air that a later uniform
    ///     speed-up only shrinks, never removes. Callers append filler to the TRIMMED result, not
    ///     the raw synthesis output, so filler lands right after real speech.
    /// </summary>
    byte[] TrimToSpokenDuration(byte[] audio);

    /// <summary>
    ///     Concatenates whole WAV clips (traffic synthesis + resolved filler audio) into one file
    ///     with a fixed silence pause between each. Exposed here (rather than a static Infrastructure
    ///     helper called directly) so the orchestrator never depends on a concrete Infrastructure type.
    /// </summary>
    byte[] StitchAudioClips(IReadOnlyList<byte[]> wavClips, double pauseSecondsBetweenClips);

    /// <summary>
    ///     Pitch-preserving retime of a whole clip to <paramref name="targetSeconds" />. Exposed
    ///     alongside <see cref="StitchAudioClips" /> for the same reason: the stitch step needs to
    ///     match a cached filler clip's speaking rate to the read it follows
    ///     (<see cref="Services.FillerPaceMatcher" />) without the orchestrator taking a dependency
    ///     on a concrete Infrastructure audio processor. Unlike
    ///     <see cref="FineTuneToTargetDuration" /> this applies no ceilings, tolerances or silence
    ///     padding — the caller has already decided the exact target and clamped it.
    /// </summary>
    byte[] RetimeClip(byte[] audio, double targetSeconds);
}

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Contextual information used by <c>IDurationPredictor</c> to produce a prediction.
///     Carries voice, model, speed, and optionally a calibrated history snapshot.
/// </summary>
public sealed record PredictionContext
{
    /// <summary>ElevenLabs voice ID (e.g. "21m00Tcm4TlvDq8ikWAM").</summary>
    public required string VoiceId { get; init; }

    /// <summary>TTS model ID (e.g. "eleven_v3", "eleven_turbo_v2_5").</summary>
    public string ModelId { get; init; } = "";

    /// <summary>Speech speed multiplier (default 1.0).</summary>
    public double Speed { get; init; } = 1.0;

    /// <summary>ElevenLabs seed used for deterministic synthesis.</summary>
    public int? Seed { get; init; }

    /// <summary>
    ///     How many syllables per second this voice model has historically achieved.
    ///     Null means use default-constant fallback.
    /// </summary>
    public double? CalibratedSyllablesPerSecond { get; init; }

    /// <summary>
    ///     How many syllables per word this voice model has historically averaged.
    ///     Null means use default-constant fallback.
    /// </summary>
    public double? CalibratedSyllablesPerWord { get; init; }
}

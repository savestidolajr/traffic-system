namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     A single historical synthesis outcome.
///     Stored per-broadcast so the <c>HistoryBasedDurationPredictor</c> can
///     interpolate the expected duration for future (voice, model, speed, syllableCount) tuples.
/// </summary>
public sealed record SynthesisRecord
{
    /// <summary>ElevenLabs voice ID used for this synthesis.</summary>
    public required string VoiceId { get; init; }

    /// <summary>TTS model ID (e.g. "eleven_v3", "eleven_turbo_v2_5").</summary>
    public string ModelId { get; init; } = "";

    /// <summary>ElevenLabs seed used for this generation (0–4294967295).</summary>
    public int? Seed { get; init; }

    /// <summary>Speed multiplier used during synthesis (0.25–4.0).</summary>
    public required double Speed { get; init; }

    /// <summary>Number of syllables in the script text.</summary>
    public required int SyllableCount { get; init; }

    /// <summary>Number of words in the script text.</summary>
    public int WordCount { get; init; }

    /// <summary>Actual audio duration achieved, in seconds.</summary>
    public required double AchievedDuration { get; init; }

    /// <summary>Target duration that was requested, in seconds.</summary>
    public required double TargetDuration { get; init; }

    /// <summary>First 120 characters of the script, for audit/debugging.</summary>
    public string ScriptPreview { get; init; } = "";

    /// <summary>When this synthesis was completed.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>True if the achieved duration was within effective tolerance of the target.</summary>
    public bool WasWithinTolerance { get; init; }
}

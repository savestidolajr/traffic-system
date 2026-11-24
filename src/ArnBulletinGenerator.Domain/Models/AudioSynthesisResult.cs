namespace ArnBulletinGenerator.Domain.Models;

// Note: byte[] Audio uses reference equality (default for arrays).
// If value-equality comparison is needed, use Audio.AsSpan().SequenceEqual().
public sealed record AudioSynthesisResult(
    byte[] Audio,
    string FinalScript,
    double DurationSeconds,
    double FinalSpeed = 1.0)
{
    /// <summary>
    ///     Approximate word-level timings on the final audio (empty when the TTS provider
    ///     returned no alignment). See <see cref="WordTiming" /> for accuracy semantics.
    /// </summary>
    public IReadOnlyList<WordTiming> WordTimings { get; init; } = [];

    /// <summary>
    ///     Duration (seconds) of the NATURAL spoken read — the end of the last voiced block measured
    ///     BEFORE any slow-to-fill stretch or trailing-silence pad applied to reach the slot. Voice
    ///     calibration and the retry word-budget MUST key off this (not <see cref="DurationSeconds" />,
    ///     the slot-length file), or a stretched/padded take reads back as a perfect fit and poisons
    ///     the learned read rate. 0 means "not measured" — callers fall back to <see cref="DurationSeconds" />.
    /// </summary>
    public double SpokenDurationSeconds { get; init; }

    /// <summary>
    ///     True when the pipeline delivered a file at the target slot length within the silence
    ///     budget (overrun compressed, or underrun grown by content + slow-to-fill, with ≤ the
    ///     configured trailing-silence pad). False means the slot could not be filled within the
    ///     slow-down cap; the orchestrator rejects such a take and falls back rather than ship a
    ///     short or dead-air bulletin.
    /// </summary>
    public bool SlotFilled { get; init; }

    /// <summary>
    ///     True when this result came from a real synthesis pass rather than a cached or synthetic
    ///     stand-in. Duration-revision tests and telemetry use the distinction without inferring it
    ///     from the audio byte shape.
    /// </summary>
    public bool IsNaturalSynthesis { get; init; }
}

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Word-level timing within the final bulletin audio, used by the review UI to jump
///     playback to a clicked script word (Workshop 2 / SDD §4.2).
///     Timings are derived from TTS character alignment and rescaled across duration
///     fine-tuning, so they are approximate (±~0.3s worst case after retiming/padding).
/// </summary>
public sealed record WordTiming(string Word, double StartSeconds, double EndSeconds);

/// <summary>
///     Character-level timing alignment as reported by the TTS provider for one synthesis
///     call. Character indices correspond to the exact text sent to synthesis.
/// </summary>
public sealed record AudioAlignment(
    IReadOnlyList<string> Characters,
    IReadOnlyList<double> StartSeconds,
    IReadOnlyList<double> EndSeconds);

/// <summary>
///     Raw output of one TTS synthesis call: finalized audio bytes, the provider's
///     character alignment when the timestamps endpoint is in use, and the effective model
///     actually sent to the provider.
/// </summary>
public sealed record SynthesizedAudio(
    byte[] Audio,
    Option<AudioAlignment> Alignment = default,
    string ModelId = "");

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Loudness normalization applied to the final bulletin audio so every broadcast goes to
///     air at a consistent perceived level regardless of voice or content. Gated RMS-to-target
///     gain with a soft peak limiter (see <see cref="Audio.WavLoudnessNormalizer" />).
/// </summary>
public sealed record AudioLoudnessOptions
{
    public const string SectionName = "AudioLoudness";

    /// <summary>Master switch. When false the audio is published at its raw synthesized level.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Target gated RMS level. ≈ −14 LUFS for these voices, matching the ATN references.</summary>
    public double TargetRmsDbFs { get; set; } = -14.5;

    /// <summary>Sample-peak ceiling the limiter holds under. −1.5 dBFS leaves ~1 dB true-peak headroom.</summary>
    public double PeakCeilingDbFs { get; set; } = -1.5;

    /// <summary>Samples quieter than this are excluded from the RMS measurement (pause/silence gating).</summary>
    public double SilenceFloorDbFs { get; set; } = -50.0;

    /// <summary>Cap on upward gain so near-silent input does not boost the noise floor.</summary>
    public double MaxMakeupGainDb { get; set; } = 20.0;
}

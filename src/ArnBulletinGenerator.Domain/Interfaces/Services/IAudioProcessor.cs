namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Provides low-level audio processing operations such as duration measurement,
///     silence padding, and playback-speed adjustment on raw WAV byte arrays.
/// </summary>
public interface IAudioProcessor
{
    /// <summary>Pads <paramref name="audioData" /> with silence so its duration reaches <paramref name="targetSeconds" />.</summary>
    byte[] EnsureDuration(byte[] audioData, double targetSeconds);

    /// <summary>Returns the duration of the WAV <paramref name="audioData" /> in seconds.</summary>
    double GetDuration(byte[] audioData);

    /// <summary>Re-samples <paramref name="audioData" /> so its playback duration matches <paramref name="targetSeconds" />.</summary>
    byte[] AdjustSpeed(byte[] audioData, double targetSeconds);
}

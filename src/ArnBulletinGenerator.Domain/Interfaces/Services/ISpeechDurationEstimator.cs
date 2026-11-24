namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Estimates spoken duration of text.
///     Used to predict TTS output length before synthesis.
/// </summary>
public interface ISpeechDurationEstimator
{
    /// <summary>
    ///     Estimates the total spoken duration (seconds).
    /// </summary>
    /// <param name="text">The script text to estimate.</param>
    /// <param name="secondsPerWord">Seconds per word (default 0.4 ≈ 150 WPM). Pass a calibrated value for better accuracy.</param>
    double EstimateDuration(string text, double secondsPerWord = 0.4);
}

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Estimates and measures audio duration.
///     Separates duration concerns from the synthesis pipeline.
/// </summary>
public interface IDurationEstimator
{
    /// <summary>
    ///     Estimates the spoken duration of text before synthesis.
    /// </summary>
    double EstimateDuration(string text, double secondsPerWord = 0.4);

    /// <summary>
    ///     Measures the actual duration of synthesized audio.
    /// </summary>
    double GetDuration(byte[] audio);
}

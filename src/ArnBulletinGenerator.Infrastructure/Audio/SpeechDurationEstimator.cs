using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Infrastructure.Audio;

/// <summary>
///     Estimates the spoken duration of a script from its word count using a configurable
///     seconds-per-word rate.
/// </summary>
public class SpeechDurationEstimator : ISpeechDurationEstimator
{
    /// <inheritdoc />
    public double EstimateDuration(string text, double secondsPerWord = 0.4)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return WordCounter.Count(text) * secondsPerWord;
    }
}

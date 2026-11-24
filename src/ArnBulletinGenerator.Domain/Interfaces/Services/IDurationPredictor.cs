using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Predicts spoken duration of text and computes syllable targets
///     needed to hit a desired duration. Implementations can use static rates,
///     historical data, or machine-learned models.
///     <para>
///         Selectable at runtime via <c>DurationPredictionOptions.Mode</c>.
///     </para>
/// </summary>
public interface IDurationPredictor
{
    /// <summary>
    ///     Predict how long <paramref name="text"/> will take to speak
    ///     given the voice configuration in <paramref name="context"/>.
    /// </summary>
    /// <param name="text">The full script text to estimate.</param>
    /// <param name="context">Voice, model, speed, and optionally calibration data.</param>
    PredictionResult PredictDuration(string text, PredictionContext context);

    /// <summary>
    ///     Compute the syllable target needed to hit <paramref name="targetDurationSeconds"/>
    ///     with the given voice configuration.
    ///     <para>
    ///         Used to compute the word/syllable target to pass to the LLM prompt.
    ///     </para>
    /// </summary>
    /// <param name="targetDurationSeconds">Desired audio length in seconds.</param>
    /// <param name="context">Voice, model, speed, and optionally calibration data.</param>
    /// <returns>
    ///     The recommended syllable count and a confidence interval.
    ///     Callers are responsible for converting syllables to words for the LLM prompt.
    /// </returns>
    PredictionResult GetTargetSyllables(double targetDurationSeconds, PredictionContext context);
}

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Pure exponential moving average (EMA) calculator.
///     New measurements are weighted at <paramref name="alpha" />, history at (1 − alpha).
/// </summary>
public static partial class ExponentialMovingAverage
{
    /// <summary>
    ///     Applies a single EMA step. On the first observation, the measurement is accepted as-is.
    /// </summary>
    public static double Apply(double current, double measurement, bool isFirstObservation, double alpha = 0.3)
    {
        return isFirstObservation ? measurement : alpha * measurement + (1 - alpha) * current;
    }
}

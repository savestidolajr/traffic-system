namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Result of a duration prediction.
/// </summary>
public sealed record PredictionResult
{
    /// <summary>Expected spoken duration in seconds.</summary>
    public required double ExpectedDuration { get; init; }

    /// <summary>Lower bound (p10) of the 80% prediction interval.</summary>
    public double ConfidenceLower { get; init; }

    /// <summary>Upper bound (p90) of the 80% prediction interval.</summary>
    public double ConfidenceUpper { get; init; }

    /// <summary>How many historical data points contributed to this prediction.</summary>
    public int DataPointsUsed { get; init; }

    /// <summary>Name of the strategy that produced this prediction.</summary>
    public required string StrategyName { get; init; }

    /// <summary>True if this prediction draws on historical data (not just defaults).</summary>
    public bool HasCalibratedData => DataPointsUsed > 0;
}

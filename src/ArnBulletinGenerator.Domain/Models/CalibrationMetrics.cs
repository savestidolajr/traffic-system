namespace ArnBulletinGenerator.Domain.Models;

/// <summary>A voice's measured or default speaking-rate metrics, used to estimate how long a given script will take to speak.</summary>
public sealed record CalibrationMetrics(
    double WordsPerMinute,
    double SyllablesPerSecond,
    double SyllablesPerWord);

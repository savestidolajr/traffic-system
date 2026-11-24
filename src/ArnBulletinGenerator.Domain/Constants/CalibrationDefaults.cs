namespace ArnBulletinGenerator.Domain.Constants;

/// <summary>Fallback syllables-per-word ratio used to estimate spoken duration when a voice has no calibration data yet.</summary>
public static class CalibrationDefaults
{
    public const double SyllablesPerWord = 1.45;
}

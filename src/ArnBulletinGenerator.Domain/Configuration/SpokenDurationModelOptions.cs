namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Controls the calibrated spoken-duration model. The switch exists so an environment can
///     return to the established 150-WPM ceiling path without a redeploy if live calibration
///     behaviour ever needs to be contained.
/// </summary>
public sealed class SpokenDurationModelOptions
{
    public const string SectionName = "SpokenDurationModel";

    /// <summary>When false, every prediction is absent and callers retain their existing fallback.</summary>
    public bool Enabled { get; set; } = true;
}

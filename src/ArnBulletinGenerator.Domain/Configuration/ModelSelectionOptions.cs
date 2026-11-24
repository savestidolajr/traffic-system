namespace ArnBulletinGenerator.Domain.Configuration;

/// <summary>
///     Configuration for ElevenLabs model selection per use case.
/// </summary>
public sealed record ModelSelectionOptions
{
    public const string SectionName = "ModelSelection";

    /// <summary>Model used for scheduled broadcast generation (default: eleven_v3).</summary>
    public string BroadcastModel { get; set; } = "eleven_v3";

    /// <summary>Model optimised for quality/turbo synthesis (default: eleven_turbo_v2_5).</summary>
    public string QualityModel { get; set; } = "eleven_turbo_v2_5";

    /// <summary>
    ///     Fallback model when the preferred model does not support the requested language (default:
    ///     eleven_multilingual_v2).
    /// </summary>
    public string FallbackModel { get; set; } = "eleven_multilingual_v2";
}

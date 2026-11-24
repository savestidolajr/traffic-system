using System.ComponentModel.DataAnnotations;

namespace ArnBulletinGenerator.Infrastructure.Configuration;

/// <summary>
///     Configuration options for ElevenLabs Text-to-Speech integration.
///     Optimized for Australian radio broadcast quality.
/// </summary>
public sealed class ElevenLabsOptions
{
    public const string SectionName = "AiSettings:ElevenLabs";

    [Required] public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    ///     Model ID. Recommended for radio:
    ///     - "eleven_v3" - Flagship model, emotionally rich, very high quality. 5k char limit.
    ///     - "eleven_turbo_v2_5" - Supports phoneme tags and pronunciation dictionaries, fast (~250ms). 40k char limit.
    /// </summary>
    [Required]
    public string ModelId { get; set; } = "eleven_v3";

    /// <summary>
    ///     ISO 639-1 language code to enforce pronunciation and text normalization.
    ///     For English (including Australian), use "en" with Turbo v2.5.
    /// </summary>
    public string? LanguageCode { get; set; } = "en";

    /// <summary>
    ///     Base URL for the ElevenLabs TTS API (e.g., "https://api.elevenlabs.io/v1/text-to-speech").
    /// </summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = "https://api.elevenlabs.io/v1/text-to-speech";

    /// <summary>
    ///     Derived API base URL for non-TTS endpoints (voices, etc.).
    /// </summary>
    public string ApiBaseUrl => BaseUrl.Contains("/text-to-speech", StringComparison.OrdinalIgnoreCase)
        ? BaseUrl[..BaseUrl.IndexOf("/text-to-speech", StringComparison.OrdinalIgnoreCase)]
        : BaseUrl;

    /// <summary>
    ///     Output format. For broadcast quality, use:
    ///     - "mp3_44100_192" - High quality MP3 (Creator tier+)
    ///     - "pcm_44100" - Uncompressed PCM (Pro tier+)
    ///     - "pcm_24000" - Good quality PCM
    ///     - "pcm_16000" - Standard quality PCM
    /// </summary>
    [Required]
    public string OutputFormat { get; set; } = "pcm_44100";

    [Range(1, 300)] public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    ///     Process-wide cap on concurrent ElevenLabs generation requests. Size to the plan's
    ///     concurrent-request limit.
    /// </summary>
    [Range(1, 64)]
    public int MaxConcurrentRequests { get; set; } = 2;

    /// <summary>
    ///     Optional seed for reproducible output (0-4294967295).
    ///     Use a fixed seed for consistent delivery across regenerations.
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>
    ///     Use the /with-timestamps endpoint for precise duration measurement.
    ///     Returns exact character timing from ElevenLabs rather than estimating from audio bytes.
    ///     Recommended: true for accurate duration targeting.
    /// </summary>
    public bool UseTimestampsEndpoint { get; set; } = true;

    /// <summary>
    ///     Relative endpoint path for fetching available voices.
    ///     Appended to <see cref="ApiBaseUrl" /> when listing voices.
    /// </summary>
    public string VoicesEndpoint { get; set; } = "voices";

    /// <summary>
    ///     Lower bound applied by <c>ElevenLabsClient.ClampSpeed</c> before posting the
    ///     speed multiplier to the ElevenLabs API. Defaults match the ElevenLabs documented
    ///     range (0.7); narrow this for production stations that need tighter delivery pace.
    /// </summary>
    [Range(0.5, 2.0)]
    public double MinSpeed { get; set; } = 0.7;

    /// <summary>
    ///     Upper bound applied by <c>ElevenLabsClient.ClampSpeed</c>. Defaults match the
    ///     ElevenLabs documented range (1.2); narrow this for production stations that need
    ///     tighter delivery pace.
    /// </summary>
    [Range(0.5, 2.0)]
    public double MaxSpeed { get; set; } = 1.2;

    /// <summary>
    ///     Text processing settings applied during synthesis.
    /// </summary>
    public ElevenLabsTextSettings TextSettings { get; set; } = new();

    /// <summary>
    ///     Voice settings optimized for Australian radio broadcast.
    /// </summary>
    public ElevenLabsVoiceSettings VoiceSettings { get; set; } = new();


}

/// <summary>
///     Text processing settings for ElevenLabs synthesis requests.
/// </summary>
public sealed class ElevenLabsTextSettings
{
    /// <summary>
    ///     Apply text normalization for numbers, dates, abbreviations.
    ///     Sent as "on" (true) or "auto" (false) to ElevenLabs API.
    /// </summary>
    public bool ApplyTextNormalization { get; set; } = true;
}

/// <summary>
///     Voice settings for ElevenLabs synthesis.
///     Tuned for Australian radio presenter delivery with natural expressiveness.
/// </summary>
public sealed class ElevenLabsVoiceSettings
{
    /// <summary>
    ///     Controls voice consistency/variability (0.0 to 1.0).
    ///     Lower = more expressive/variable, Higher = more stable/consistent.
    ///     For eleven_v3: Strictly [0.0, 0.5, 1.0] (0.5 = Natural).
    /// </summary>
    [Range(0.0, 1.0)]
    public double Stability { get; set; } = 0.50;

    /// <summary>
    ///     How closely the voice matches the original sample (0.0 to 1.0).
    ///     Higher = more similar to original voice.
    ///     For Option D2 (Presence): 0.80 maintains strong voice identity.
    /// </summary>
    [Range(0.0, 1.0)]
    public double SimilarityBoost { get; set; } = 0.80;

    /// <summary>
    ///     Style exaggeration (0.0 to 1.0). Only for multilingual v2 and turbo v2.5.
    ///     Higher = more dramatic/expressive delivery.
    ///     For Option D2 (Presence): 0.20 adds natural warmth without being over-the-top.
    /// </summary>
    [Range(0.0, 1.0)]
    public double Style { get; set; } = 0.20;

    /// <summary>
    ///     Enhances speaker clarity and presence.
    ///     Recommended: true for broadcast to improve audio clarity.
    /// </summary>
    public bool UseSpeakerBoost { get; set; } = true;

    /// <summary>
    ///     Speed multiplier (0.7 to 1.2 for ElevenLabs).
    ///     For Option D2 (Presence): 1.0 provides clear, natural radio pacing.
    /// </summary>
    [Range(0.7, 1.2)]
    public double Speed { get; set; } = 1.0;

    /// <summary>
    ///     Seed for deterministic TTS generation (0+).
    ///     Using a fixed seed produces reproducible audio output across identical requests.
    ///     Set to null/omit for non-deterministic generation.
    /// </summary>
    public int? Seed { get; set; }
}


using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Infrastructure.AiProviders.ElevenLabs;

// =============================================================================
// Public Models
// =============================================================================

/// <summary>
///     Represents a voice available in ElevenLabs.
/// </summary>
public sealed record ElevenLabsVoice(
    string VoiceId,
    string Name,
    string? Category,
    string? Description,
    IReadOnlyDictionary<string, string>? Labels,
    string[] ModelIds)
{
    /// <summary>
    ///     Whether ElevenLabs will accept this voice as a remix source. See
    ///     <see cref="ArnBulletinGenerator.Domain.Models.Voice.CanRemix" /> for the rule.
    /// </summary>
    public bool CanRemix { get; init; } = true;
}

/// <summary>
///     Result from audio generation including the audio data, duration, and (when the
///     timestamps endpoint is used) character-level alignment matching the input text.
/// </summary>
public sealed record ElevenLabsAudioResult(
    byte[] AudioData,
    double DurationSeconds,
    ArnBulletinGenerator.Domain.Models.AudioAlignment? Alignment = null);

/// <summary>
///     Locates a pronunciation dictionary in ElevenLabs.
/// </summary>
public sealed record PronunciationDictionaryLocator(
    [property: JsonPropertyName("pronunciation_dictionary_id")]
    string DictionaryId,
    [property: JsonPropertyName("version_id")]
    string VersionId);

// =============================================================================
// Internal API DTOs
// =============================================================================

internal sealed record VoicesResponse(
    [property: JsonPropertyName("voices")] IReadOnlyList<VoiceDto> Voices);

internal sealed record VoiceDto(
    [property: JsonPropertyName("voice_id")]
    string VoiceId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("category")]
    string? Category,
    [property: JsonPropertyName("description")]
    string? Description,
    [property: JsonPropertyName("labels")] Dictionary<string, string>? Labels)
{
    [JsonPropertyName("high_quality_base_model_ids")]
    public string[] ModelIds { get; init; } = [];

    /// <summary>
    ///     Present only for Voice Library voices (owned or copied); null for voices created
    ///     directly in this account (generated, IVC/PVC) with no sharing history.
    /// </summary>
    [JsonPropertyName("sharing")]
    public VoiceSharingDto? Sharing { get; init; }

    public ElevenLabsVoice ToModel()
    {
        // Remixable: owned outright (no sharing record) or self-shared ("enabled"/"disabled").
        // Not remixable: a Voice Library voice *copied* into this account ("copied"/
        // "copied_disabled") — ElevenLabs enforces this regardless of notice period length.
        var canRemix = Sharing is null
            || Sharing.Status is "enabled" or "disabled";
        return new ElevenLabsVoice(VoiceId, Name, Category, Description, Labels, ModelIds)
        {
            CanRemix = canRemix
        };
    }
}

/// <summary>
///     Voice Library sharing metadata — only the fields needed to decide remix eligibility.
/// </summary>
internal sealed record VoiceSharingDto(
    [property: JsonPropertyName("status")] string? Status);

/// <summary>
///     Response from the /with-timestamps TTS endpoint.
/// </summary>
internal sealed record TimestampsResponse(
    [property: JsonPropertyName("audio_base64")]
    string AudioBase64,
    [property: JsonPropertyName("alignment")]
    AlignmentData? Alignment,
    [property: JsonPropertyName("normalized_alignment")]
    AlignmentData? NormalizedAlignment);

/// <summary>
///     Character-level timing alignment data.
/// </summary>
internal sealed record AlignmentData(
    [property: JsonPropertyName("characters")]
    IReadOnlyList<string> Characters,
    [property: JsonPropertyName("character_start_times_seconds")]
    IReadOnlyList<double> CharacterStartTimesSeconds,
    [property: JsonPropertyName("character_end_times_seconds")]
    IReadOnlyList<double> CharacterEndTimesSeconds);

/// <summary>
///     Response from POST /v1/text-to-voice/{voice_id}/remix — a list of previews of the
///     remixed voice, each carrying a generated_voice_id usable to mint a permanent voice.
/// </summary>
internal sealed record VoiceRemixResponse(
    [property: JsonPropertyName("previews")]
    IReadOnlyList<VoiceRemixPreviewDto> Previews);

internal sealed record VoiceRemixPreviewDto(
    [property: JsonPropertyName("audio_base_64")]
    string AudioBase64,
    [property: JsonPropertyName("generated_voice_id")]
    string GeneratedVoiceId,
    [property: JsonPropertyName("duration_secs")]
    double DurationSecs);

/// <summary>
///     Response from POST /v1/text-to-voice — the newly created permanent voice.
/// </summary>
internal sealed record CreateVoiceResponse(
    [property: JsonPropertyName("voice_id")]
    string VoiceId,
    [property: JsonPropertyName("name")] string? Name);

/// <summary>
///     Response from the pronunciation-dictionary mutation endpoints (add-from-rules, add-rules,
///     remove-rules). Every mutation returns a fresh <c>version_id</c> that must be persisted so
///     the correct version is attached on air.
/// </summary>
internal sealed record PronunciationDictionaryMutationResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version_id")]
    string VersionId);

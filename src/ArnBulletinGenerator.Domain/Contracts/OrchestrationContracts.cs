namespace ArnBulletinGenerator.Domain.Contracts;

// =============================================================================
// Request DTOs - Nullable fields for wire format, validated before use
// =============================================================================

// CoverageRequestDto was a flat duplicate of CoverageDto; orchestration
// requests now share the shape-aware CoverageDto from ScheduleContracts.

/// <summary>
///     Request to trigger a broadcast orchestration.
///     All required fields must be validated before processing.
/// </summary>
public sealed record OrchestrationRequestDto(
    double? TargetDurationSeconds,
    string? Location,
    CoverageDto? Coverage,
    string? VoiceId = null,
    string? VoiceName = null,
    string? VoiceProvider = null,
    string? VoiceModel = null,
    string? ModelId = null,
    VoiceSettingsDto? VoiceSettings = null,
    bool ScriptOnly = false,
    ContentSelectionDto? Content = null,
    IReadOnlyList<string>? FillOrder = null,
    // Voice-profile id the caller triggered with (Trigger Studio sends the schedule's assigned
    // profile). When present the backend resolves the profile's full tuning — including
    // PaceBoostWpm, which has no per-request field — instead of trusting the client-side
    // resolution of voice id/settings alone. Appended last to keep positional construction stable.
    string? VoiceProfileId = null);

// =============================================================================
// Response DTOs - All fields required, no nullability
// =============================================================================

/// <summary>
///     Response when orchestration is queued for async processing.
///     <paramref name="Warning" /> is non-null only when the request was accepted with a caveat
///     the caller should see — today, a Location that matches no configured station, which
///     generates a real bulletin with none of that market's roads, filler, guidance or
///     pronunciation applied.
/// </summary>
public sealed record OrchestrationQueuedResponseDto(
    string Status,
    string JobId,
    string Message,
    string? Warning = null);

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts.Mapping;

// Return type is Result<VoiceSettings> so callers can propagate validation failures.

/// <summary>
///     Input mapping: DTO → Domain conversions used by Domain validation.
///     Response mapping (Domain → DTO) is consolidated in Application.Mapping.ResponseMapper.
/// </summary>
public static class InputMappingExtensions
{
    public static Result<VoiceSettings> ToDomain(this VoiceSettingsDto dto)
    {
        return VoiceSettings.Create(
            dto.Stability,
            dto.SimilarityBoost,
            dto.Style,
            dto.UseSpeakerBoost,
            dto.Speed);
    }
}

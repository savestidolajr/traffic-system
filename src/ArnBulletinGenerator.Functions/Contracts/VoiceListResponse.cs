using ArnBulletinGenerator.Domain.Contracts;

namespace ArnBulletinGenerator.Functions.Contracts;

/// <summary>
///     Wire format for the ListVoices response: the ElevenLabs voice catalog mapped to
///     VoiceDto entries with resolved model ids.
/// </summary>
internal sealed record VoiceListResponse(IReadOnlyList<VoiceDto> Voices);

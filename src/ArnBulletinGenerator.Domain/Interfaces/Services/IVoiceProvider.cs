using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Lists the voices available from the TTS provider for assignment to a station's voice profile.</summary>
public interface IVoiceProvider
{
    Task<Result<IReadOnlyList<Voice>>> GetVoicesAsync(CancellationToken cancellationToken = default);
}

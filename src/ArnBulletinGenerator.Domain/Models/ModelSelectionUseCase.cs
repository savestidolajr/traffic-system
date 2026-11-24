namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Distinguishes the low-latency broadcast synthesis path from the higher-fidelity quality path when IModelSelector picks a TTS model.</summary>
public enum ModelSelectionUseCase
{
    Broadcast,
    Quality
}

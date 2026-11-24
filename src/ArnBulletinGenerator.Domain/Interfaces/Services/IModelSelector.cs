using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Selects the ElevenLabs TTS model a voice should render on, honouring per-use-case (broadcast vs quality) and language overrides, and the voice's own verified base models (never selects a model the voice is not verified for).</summary>
public interface IModelSelector
{
    Task<string> SelectModelAsync(string voiceId, Option<string> voiceLanguage = default,
        ModelSelectionUseCase useCase = ModelSelectionUseCase.Broadcast, CancellationToken cancellationToken = default);
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Adds ElevenLabs v3 inline audio tags to an already-approved plain script to steer HOW it
///     is read (emphasis, pacing, emotion) per a <see cref="DeliveryStylePolicy" /> — without ever
///     changing the spoken words. Runs after generation + safety, before synthesis.
///
///     The implementation MUST be fail-safe: a neutral policy, a non-v3 TTS model, a model error,
///     unparseable output, or any change to the spoken words all return the ORIGINAL plain script
///     unchanged. The director can only ever improve delivery, never block a bulletin or alter content.
/// </summary>
public interface IScriptDirector
{
    /// <summary>
    ///     Returns the script with audio tags applied per <paramref name="policy" />, or the
    ///     unchanged <paramref name="plainScript" /> when tagging is skipped or fails the
    ///     word-immutability guard. <paramref name="ttsModel" /> gates v3 (tags are spoken by
    ///     non-v3 models). <paramref name="operatorIntent" /> is an optional plain-language
    ///     re-direction ("read it calmer") applied on top of the policy.
    /// </summary>
    Task<Result<string>> DirectAsync(
        string plainScript,
        DeliveryStylePolicy policy,
        string? operatorIntent,
        string? ttsModel,
        string? jobId = null,
        CancellationToken cancellationToken = default);
}

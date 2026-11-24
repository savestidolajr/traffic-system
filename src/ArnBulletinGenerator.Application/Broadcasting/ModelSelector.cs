using System.Collections.Frozen;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArnBulletinGenerator.Application.Broadcasting;

/// <summary>
///     Selects the ElevenLabs model for a voice, honouring per-use-case overrides, falling back
///     when the preferred model does not support the voice's language, and — via
///     <see cref="VoiceModelResolver" /> — never selecting a model the voice itself is not
///     verified for. This is the no-profile fallback path (ad-hoc jobs without a voice profile,
///     and the configured default voice); profile-driven jobs resolve their model directly from
///     the voice profile version and never reach this class.
/// </summary>
public sealed class ModelSelector(
    ILogger<ModelSelector> logger,
    IOptions<ModelSelectionOptions> modelOptions,
    IVoiceProvider voiceProvider,
    IMemoryCache cache) : IModelSelector
{
    private const string VoicesCacheKey = "ModelSelector:Voices";
    private static readonly TimeSpan VoicesCacheTtl = TimeSpan.FromMinutes(5);

    private static readonly FrozenDictionary<string, string[]> ModelLanguageSupport =
        new Dictionary<string, string[]>
        {
            ["eleven_flash_v2_5"] =
            [
                "en", "es", "fr", "de", "it", "pt", "pl", "tr", "ru", "nl", "cs", "ar", "zh", "ja", "hu", "ko", "hi",
                "sv", "fi", "da", "no", "uk", "el", "ms", "cmn"
            ],
            ["eleven_turbo_v2_5"] =
            [
                "en", "es", "fr", "de", "it", "pt", "pl", "tr", "ru", "nl", "cs", "ar", "zh", "ja", "hu", "ko", "hi",
                "sv", "fi", "da", "no", "uk", "el", "ms", "cmn"
            ],
            ["eleven_multilingual_v2"] =
            [
                "en", "es", "fr", "de", "it", "pt", "pl", "tr", "ru", "nl", "cs", "ar", "zh", "ja", "hu", "ko"
            ],
            ["eleven_v3"] =
            [
                "en", "es", "fr", "de", "it", "pt", "pl", "tr", "ru", "nl", "cs", "ar", "zh", "ja", "hu", "ko", "hi",
                "sv", "fi", "da", "no", "uk", "el", "ms", "cmn", "bg", "hr", "sk", "sl", "et", "lv", "lt", "ro", "is",
                "ga", "cy", "af", "xh", "zu", "tn", "st", "ts", "ss", "ve", "nr", "sq", "mk", "bs", "me", "sr", "sh"
            ]
        }.ToFrozenDictionary();

    private readonly ModelSelectionOptions _models = modelOptions.Value;

    public async Task<string> SelectModelAsync(string voiceId, Option<string> voiceLanguage = default,
        ModelSelectionUseCase useCase = ModelSelectionUseCase.Broadcast, CancellationToken cancellationToken = default)
    {
        string preferredModel = useCase switch
        {
            ModelSelectionUseCase.Broadcast => _models.BroadcastModel,
            ModelSelectionUseCase.Quality => _models.QualityModel,
            _ => _models.FallbackModel
        };

        string languageSelectedModel = voiceLanguage
            .Map(lang => ModelSupportsLanguage(preferredModel, lang) ? preferredModel : ResolveFallback(voiceId, lang))
            .GetValueOr(preferredModel);

        string selectedModel = await ApplyVoiceCapabilityAsync(voiceId, languageSelectedModel, cancellationToken);

        logger.LogInformation("Selected model {Model} for voice {VoiceId} (language: {Language}, use case: {UseCase})",
            selectedModel, voiceId, voiceLanguage.GetValueOr("unknown"), useCase);

        return selectedModel;
    }

    /// <summary>
    ///     Narrows <paramref name="languageSelectedModel" /> to a model the voice is actually
    ///     verified for (<see cref="VoiceModelResolver" />). A voice unknown to the fetched voice
    ///     list (synthetic/test id, or the list is temporarily unavailable) keeps the
    ///     language-selected model unchanged — the prior, capability-blind behaviour — rather than
    ///     risk blocking selection on a transient ElevenLabs API failure.
    /// </summary>
    private async Task<string> ApplyVoiceCapabilityAsync(string voiceId, string languageSelectedModel,
        CancellationToken cancellationToken)
    {
        var voices = await GetVoicesCachedAsync(cancellationToken);
        var voice = voices?.FirstOrDefault(v => string.Equals(v.VoiceId, voiceId, StringComparison.Ordinal));
        if (voice is null)
            return languageSelectedModel;

        string resolved = VoiceModelResolver.Resolve(voice.ModelIds, languageSelectedModel);
        if (!string.Equals(resolved, languageSelectedModel, StringComparison.Ordinal))
            logger.LogInformation(
                "Voice {VoiceId} is not verified for {Preferred}; rendering on its best verified model {Resolved} instead.",
                voiceId, languageSelectedModel, resolved);
        return resolved;
    }

    private async Task<IReadOnlyList<Voice>?> GetVoicesCachedAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(VoicesCacheKey, out IReadOnlyList<Voice>? cached))
            return cached;

        var result = await voiceProvider.GetVoicesAsync(cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning(
                "Could not fetch voices for capability-aware model selection ({Error}); falling back to language-only selection.",
                result.Error);
            return null;
        }

        cache.Set(VoicesCacheKey, result.Value, VoicesCacheTtl);
        return result.Value;
    }

    private string ResolveFallback(string voiceId, string language)
    {
        logger.LogInformation(
            "Preferred model does not support language {Language} for voice {VoiceId}, falling back to {FallbackModel}",
            language, voiceId, _models.FallbackModel);
        return _models.FallbackModel;
    }

    private static bool ModelSupportsLanguage(string modelId, string language)
    {
        return ModelLanguageSupport.TryGetValue(modelId, out string[]? supported)
               && (supported.Contains(language.ToLowerInvariant()) ||
                   supported.Contains(language.Split('-')[0].ToLowerInvariant()));
    }
}

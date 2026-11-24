namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Picks the TTS model a specific voice should render on, from the models the voice is
///     VERIFIED for (ElevenLabs <c>high_quality_base_model_ids</c>) and the configured preferred
///     model (e.g. the broadcast default).
///
///     A trained / cloned / professional voice reports its base models (the v2-family it was built
///     on); rendering such a voice on a newer model it was not built for (notably eleven_v3)
///     degrades it badly. So when a voice lists base models, it renders on its best verified one
///     unless that list explicitly includes the preferred model. A voice that lists NO base models
///     is treated as native to the preferred model (Voice Design / generated voices are v3-native,
///     and premade voices accept the default) and uses it directly.
///
///     This is the DEFAULT only — an operator's explicit per-profile model choice always wins, as
///     it is persisted on the voice-profile version and resolved live by the schedule poller.
/// </summary>
public static partial class VoiceModelResolver
{
    // Best-fidelity base model first; used when the voice does not list the preferred model.
    private static readonly string[] BaseModelPreference =
        ["eleven_multilingual_v2", "eleven_turbo_v2_5", "eleven_turbo_v2", "eleven_flash_v2_5"];

    /// <param name="verifiedBaseModelIds">The voice's ElevenLabs <c>high_quality_base_model_ids</c>.</param>
    /// <param name="preferredModel">The configured default (e.g. <c>eleven_v3</c>).</param>
    public static string Resolve(IReadOnlyList<string>? verifiedBaseModelIds, string preferredModel)
    {
        // No verified base models → native to the preferred model (v3-native / premade).
        if (verifiedBaseModelIds is null || verifiedBaseModelIds.Count == 0)
            return preferredModel;

        // The voice explicitly supports the preferred model → honour it.
        if (verifiedBaseModelIds.Contains(preferredModel))
            return preferredModel;

        // Otherwise render on the voice's best verified base model (never the unsupported preferred).
        foreach (var model in BaseModelPreference)
            if (verifiedBaseModelIds.Contains(model))
                return model;

        return verifiedBaseModelIds[0];
    }
}

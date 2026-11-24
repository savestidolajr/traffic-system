using System.Security.Cryptography;
using System.Text;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Builds the cache key identifying one (script × voice × settings × model) filler-cart
///     synthesis. The cart rides the filler statement cache machinery with a synthetic
///     statement id, so a voice-profile version change (which alters <see cref="VoiceSettings" />)
///     naturally produces a new cached clip exactly like filler statements already do.
/// </summary>
public static class FillerCartAudioKey
{
    /// <summary>Pseudo filler-statement id: the cart rides the filler statement cache
    /// machinery with a synthetic statement whose text is the cart script.</summary>
    public const string CartStatementId = "__cart__";

    /// <summary>Key for one (script × voice × settings × model) cart clip. The script hash
    /// suffix makes a wording change self-invalidate exactly like the FillerAudioCacheKey._t2
    /// processing bump — old rows miss naturally, orphans are harmless.</summary>
    public static string Build(string scriptText, string voiceId, VoiceSettings settings, string model) =>
        $"{FillerAudioCacheKey.Build(voiceId, settings, model)}_c{StableHash8(scriptText)}";

    // SHA256 (not string.GetHashCode(), which is randomized per process) so the key is stable
    // across app restarts and instances.
    private static string StableHash8(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
}

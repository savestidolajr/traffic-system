using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Fixes the temperature preposition: "temperatures ON 13 degrees" -> "temperatures AT 13 degrees".
/// </summary>
/// <remarks>
///     <para>
///         Observed on BOTH pilot stations in a single run on 2026-08-23 — "Clear skies across Sydney
///         right now, with temperatures on 13 degrees" and "Clear skies across Cairns, with
///         temperatures on 21 degrees". It is not a grounding or length defect, which is why every
///         existing gate passed it, and it is the kind of thing a listener notices before anything
///         else in a bulletin.
///     </para>
///     <para>
///         Deterministic rather than prompt-only for the reason
///         <see cref="ClosureVerbNormalizer" /> already documents: a soft instruction proved
///         unreliable under model sampling variance. The prompt states the preference so good phrasing
///         is the default; this guarantees it. A whole-word preposition swap in this exact frame
///         cannot change a fact or break the sentence.
///     </para>
///     <para>
///         Deliberately narrow. Only "temperature(s) on &lt;number&gt;" is rewritten — "on" is a
///         perfectly good preposition everywhere else in a traffic read ("roadworks on Church Street",
///         "on the Bruce Highway"), so a broader rule would do real damage.
///     </para>
/// </remarks>
public static partial class WeatherPhrasingNormalizer
{
    [GeneratedRegex(@"\b(temperatures?)\s+on\s+(?=\d)", RegexOptions.IgnoreCase)]
    private static partial Regex TemperatureOnRegex();

    /// <summary>
    ///     Rewrites the temperature preposition; all other text is returned unchanged. Idempotent.
    /// </summary>
    public static string Normalize(string script)
    {
        if (string.IsNullOrEmpty(script))
            return script;

        return TemperatureOnRegex().Replace(script, match => $"{match.Groups[1].Value} at ");
    }
}

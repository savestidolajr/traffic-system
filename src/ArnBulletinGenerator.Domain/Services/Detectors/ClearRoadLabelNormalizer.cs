using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Whole-phrase normaliser for the two clear-road data-labels the writer must never voice —
///     "flowing well" and "moving well". Real ATN says "no dramas", "looking good", "moving nicely"
///     or "a clear run"; "flowing well"/"moving well" are the flow-feed's own status wording and the
///     machine tell. A prompt ban proved unreliable across the pipeline (observed live through many
///     versions: the draft honoured it but the phrase resurfaced from the summariser's data block
///     and, once that was made opaque, still occasionally from the model's own default). This runs
///     in CODE on every draft/refine/polish output — the same always-on shape as
///     <see cref="ClosureVerbNormalizer" /> — so the leak is structurally impossible rather than
///     merely discouraged. Case and sentence position are preserved; only the two banned phrases move.
/// </summary>
public static partial class ClearRoadLabelNormalizer
{
    [GeneratedRegex(@"\bflowing well\b", RegexOptions.IgnoreCase)]
    private static partial Regex FlowingWell();
    [GeneratedRegex(@"\bmoving well\b", RegexOptions.IgnoreCase)]
    private static partial Regex MovingWell();

    public static string Normalize(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        script = FlowingWell().Replace(script, m => Preserve(m.Value, "moving nicely"));
        script = MovingWell().Replace(script, m => Preserve(m.Value, "looking good"));
        return script;
    }

    // Keep the original leading case so a sentence-initial phrase stays capitalised.
    private static string Preserve(string original, string replacement) =>
        original.Length > 0 && char.IsUpper(original[0])
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
}

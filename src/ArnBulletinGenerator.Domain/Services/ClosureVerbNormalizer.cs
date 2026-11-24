using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministically voices a road closure as "closed", not "shut" — ARN's marked-up
///     preference for the closure verb (BFN-017: "Reid Highway <s>is shut</s> closed at Altone
///     Road"). The soft prompt instruction alone proved unreliable under model sampling variance
///     (fresh live gen still led with "The Pacific Motorway citybound is shut", job 7f9b9723), the
///     same failure class the deterministic detector/normalizer suite exists to close. In an
///     Australian traffic read "shut" only ever means a road closure, so a whole-word rewrite is
///     safe; casing is preserved so a sentence-initial "Shut" becomes "Closed". Mirrors
///     <see cref="TimeOfDayNormalizer" />'s mechanical-rewrite approach and runs at the same points
///     (the final generate/refine output), so it never affects source data or citation checks.
/// </summary>
public static partial class ClosureVerbNormalizer
{
    [GeneratedRegex(@"\bshut\b", RegexOptions.IgnoreCase)]
    private static partial Regex ShutRegex();

    /// <summary>Rewrites the whole word "shut" to "closed" (preserving leading casing); other text
    /// is returned unchanged.</summary>
    public static string Normalize(string script)
    {
        if (string.IsNullOrEmpty(script))
            return script;

        return ShutRegex().Replace(script, match =>
            char.IsUpper(match.Value[0]) ? "Closed" : "closed");
    }
}

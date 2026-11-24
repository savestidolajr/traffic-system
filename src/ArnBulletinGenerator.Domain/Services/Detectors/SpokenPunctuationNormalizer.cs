using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministically repairs punctuation artifacts that must never reach the voice.
///     Prompt- or model-authored ellipses remain untouched, while accidental ASCII runs of four
///     or more dots (for example, the six dots in "roads......") are reduced to the standard
///     three-dot ellipsis. This is character-level hygiene only: no spoken word changes.
///     A run of sentence terminators separated only by whitespace collapses to the first
///     (". ." → "."). An em dash or en dash used as a clause break becomes a comma; a dash between
///     digits (a range) is untouched. No doubled comma and no space before punctuation survives.
///     The rewrite is idempotent and runs on the plain script (no tags), so it never conflicts with
///     the delivery director, which only ever adds tags, capitals and pauses.
/// </summary>
public static partial class SpokenPunctuationNormalizer
{
    // ". .", "!  .", ". . .": terminators separated by whitespace only. Matched whole and
    // replaced by the FIRST terminator, so "degrees. . That's" becomes "degrees. That's".
    [GeneratedRegex(@"[.!?](?:\s+[.!?])+")]
    private static partial Regex TerminatorRun();

    // Four or more ASCII periods are accidental punctuation damage. Three periods are the
    // authored ellipsis pause and remain unchanged.
    [GeneratedRegex(@"\.{4,}")]
    private static partial Regex LongPeriodRun();

    // Exactly two periods are also damage, but the genuine three-period ellipsis is deliberate.
    [GeneratedRegex(@"(?<!\.)\.\.(?!\.)")]
    private static partial Regex DoubledPeriod();

    // Em dash (U+2014) or en dash (U+2013) clause break -> comma. A dash flanked by digits on
    // both sides is a range ("5 to 10"), not a clause break, and is preserved: a comma there would
    // change the spoken meaning. Group 1 captures the dash run only in the range alternative.
    [GeneratedRegex(@"(?<=\d)(\s*[\u2014\u2013]+\s*)(?=\d)|\s*[\u2014\u2013]+\s*")]
    private static partial Regex ClauseDash();

    // The comma a dash becomes must never sit beside a comma that was already there. The
    // match ends at the LAST comma so the space after it survives ("slow, , keep" -> "slow, keep").
    [GeneratedRegex(@",(?:\s*,)+")]
    private static partial Regex DoubledComma();

    // Space before punctuation. A period that STARTS an ellipsis ("...") is excluded so a
    // spaced " ... " survives as the pause device it is.
    [GeneratedRegex(@"\s+[,!?]|\s+\.(?!\.)")]
    private static partial Regex SpaceBeforePunctuation();

    /// <summary>
    ///     Repairs sentence-terminator runs, accidental long dot runs, clause-break dashes, doubled
    ///     commas and space-before-punctuation on the plain script. Returns the input unchanged when
    ///     it carries none of these artifacts.
    /// </summary>
    public static string Normalize(string script)
    {
        if (string.IsNullOrEmpty(script))
            return script;

        script = TerminatorRun().Replace(script, static m => m.Value[..1]);
        script = LongPeriodRun().Replace(script, "...");
        script = DoubledPeriod().Replace(script, ".");
        script = ClauseDash().Replace(script, static m => m.Groups[1].Success ? m.Value : ", ");
        script = DoubledComma().Replace(script, ",");
        script = SpaceBeforePunctuation().Replace(script, static m => m.Value[^1].ToString());
        return script;
    }
}

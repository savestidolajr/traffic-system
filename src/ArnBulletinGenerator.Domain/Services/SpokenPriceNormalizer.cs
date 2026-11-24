using System.Text;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Deterministically voices a fuel price in spoken words, never as a currency numeral —
///     "$1.79" becomes "a dollar seventy-nine". The bulletin is READ ALOUD, so a numeral is a
///     rendering the TTS has to guess at; the ATN corpus and the overwhelming majority of our own
///     output already say it in words. The prompt asks for spoken numbers, but that instruction is
///     unreliable under sampling variance: the SAME market on the SAME config produced
///     "Unleaded 91 is cheapest at $1.78" on one run and "a dollar seventy-eight" on the next
///     (demo-coast, 2026-07-27), and job 5dfe174e shipped "$1.79 in Chermside West" while nine
///     sibling bulletins said it in words. An LLM review pass does not catch this (measured 0/6),
///     because it is a rendering defect rather than a fidelity one — exactly the class the
///     deterministic normalizer suite exists to close. Mirrors <see cref="ClosureVerbNormalizer" />
///     and runs at the same points (the final generate/refine/polish output), so it never affects
///     source data or citation checks.
///     <para>
///         Currency forms are always touched. The unsigned decimal and bare cents forms are touched
///         only inside a fuel sentence (or when an explicit "cents" unit is present), so ordinary
///         bulletin numerals such as temperatures, durations, road identifiers and station names
///         remain exactly as written. The fixture composer intentionally emits plain numeric fuel
///         lines and leaves this spoken rendering downstream (2026-08-23).
///     </para>
/// </summary>
public static partial class SpokenPriceNormalizer
{
    private static readonly string[] _ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen"
    ];

    private static readonly string[] _tens =
    [
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"
    ];

    // Sentence boundaries keep a fuel word from authorising an unrelated number in the next
    // sentence. Decimal and bare-cents candidates are then checked against FuelContextRegex.
    private static readonly char[] _sentenceTerminators = ['.', '!', '?', '\n', ';'];

    // "$1.79", "$2.19", "$1.8", "$2". The decimal run is captured GREEDILY and whole so a
    // 3-decimal wholesale figure ("$1.779") cannot be half-matched by backtracking into "$1"; the
    // replace below leaves any such figure alone, since it is not a price we voice and rounding it
    // would invent a number the data did not supply.
    [GeneratedRegex(@"\$(\d{1,3})(?:\.(\d+))?(?!\d)")]
    private static partial Regex PriceRegex();

    // Explicit cents are unambiguous even outside a fuel sentence. Replacing only the numeric/unit
    // phrase leaves a following "a litre" or "per litre" in place.
    [GeneratedRegex(@"(?<![\w.])(?<value>\d{1,3})\s+cents\b", RegexOptions.IgnoreCase)]
    private static partial Regex CentsUnitRegex();

    // Models sometimes omit "$" but retain the normal dollar-and-cents shape. A third decimal digit
    // blocks the match, preserving wholesale precision just as PriceRegex does.
    [GeneratedRegex(@"(?<![\w.$])(?<dollars>\d{1,3})\.(?<cents>\d{1,2})(?!\d)")]
    private static partial Regex UnsignedDecimalRegex();

    // A three-digit bare number is the observed cents-per-litre form ("235"), not a general numeric
    // rewrite. FuelContextRegex is required before this candidate can be converted.
    // Reject a DECIMAL continuation ("102.7", "1.96") but allow sentence-terminating punctuation:
    // an earlier `(?![\w.])` also rejected a trailing full stop, so a price at the END of a sentence
    // ("Unleaded 91 is 235.") was never normalised — the exact shape cairns-4ca aired on 2026-08-23.
    [GeneratedRegex(@"(?<![\w.$])(?<value>\d{3})(?!\.?\d)")]
    private static partial Regex BareCentsRegex();

    [GeneratedRegex(@"\b(?:unleaded|diesel|e10|lpg|fuel|petrol|gas|91|cents?\s+(?:a|per)\s+lit(?:re|er))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex FuelContextRegex();

    /// <summary>
    ///     Rewrites fuel price numerals to their spoken form. Returns the input unchanged when it
    ///     carries no price. Unsigned decimals and bare cents are scoped to a fuel sentence; an
    ///     explicit cents unit is sufficient on its own.
    /// </summary>
    public static string Normalize(string script)
    {
        if (string.IsNullOrEmpty(script))
            return script;

        script = PriceRegex().Replace(script, static match =>
            SpeakDollarPrice(match.Value, match.Groups[1].Value, match.Groups[2].Value));
        script = CentsUnitRegex().Replace(script, static match => SpeakCents(int.Parse(match.Groups["value"].Value)));
        script = UnsignedDecimalRegex().Replace(script, match =>
        {
            return IsFuelContext(script, match.Index, match.Length)
                ? SpeakDollarPrice(match.Value, match.Groups["dollars"].Value, match.Groups["cents"].Value)
                : match.Value;
        });
        return BareCentsRegex().Replace(script, match =>
        {
            return IsFuelContext(script, match.Index, match.Length)
                ? SpeakCents(int.Parse(match.Groups["value"].Value))
                : match.Value;
        });
    }

    private static bool IsFuelContext(string script, int matchIndex, int matchLength)
    {
        int start = matchIndex == 0
            ? 0
            : script.LastIndexOfAny(_sentenceTerminators, matchIndex - 1) + 1;
        int end = script.IndexOfAny(_sentenceTerminators, matchIndex + matchLength);
        if (end < 0)
            end = script.Length;

        return FuelContextRegex().IsMatch(script[start..end]);
    }

    private static string SpeakDollarPrice(string original, string dollarsText, string centsDigits)
    {
        if (!int.TryParse(dollarsText, out int dollars))
            return original;

        if (centsDigits.Length > 2)
            return original;
        int cents = centsDigits.Length switch
        {
            1 => (centsDigits[0] - '0') * 10,
            2 => int.Parse(centsDigits),
            _ => 0
        };

        return Speak(dollars, cents);
    }

    private static string SpeakCents(int centsPerLitre)
    {
        return Speak(centsPerLitre / 100, centsPerLitre % 100);
    }

    private static string Speak(int dollars, int cents)
    {
        var sb = new StringBuilder();
        sb.Append(dollars == 1 ? "a dollar" : $"{Spell(dollars)} dollars");
        if (cents > 0)
            sb.Append(' ').Append(Spell(cents));
        return sb.ToString();
    }

    /// <summary>Spells 0-999 in Australian spoken form ("seventy-nine", "one hundred and five").</summary>
    private static string Spell(int value)
    {
        if (value < 0)
            return value.ToString();
        if (value < 20)
            return _ones[value];
        if (value < 100)
        {
            int unit = value % 10;
            return unit == 0 ? _tens[value / 10] : $"{_tens[value / 10]}-{_ones[unit]}";
        }

        int remainder = value % 100;
        string hundreds = $"{_ones[value / 100]} hundred";
        return remainder == 0 ? hundreds : $"{hundreds} and {Spell(remainder)}";
    }
}

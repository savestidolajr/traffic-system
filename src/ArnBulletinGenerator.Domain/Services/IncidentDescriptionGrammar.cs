using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>The four shapes HERE's incident <c>description</c> takes. Measured across 793 Australian
///  records: <c>At X</c> 391, no prefix 340, <c>Between X and Y</c> 48, <c>Ramp to X</c> 14.</summary>
public enum DescriptionShape
{
    /// <summary>No location prefix at all — the whole string is the condition ("Closed").</summary>
    None,

    /// <summary>"At &lt;anchor&gt; - &lt;condition&gt;".</summary>
    At,

    /// <summary>"Between &lt;a&gt; and &lt;b&gt; - &lt;condition&gt;".</summary>
    Between,

    /// <summary>"Ramp to &lt;target&gt; - &lt;condition&gt;" — the RAMP is the affected asset, not the road.</summary>
    RampTo,

    /// <summary>A prefix in none of the known shapes. The anchor is withheld; the condition still stands.</summary>
    Unknown
}

/// <summary>
///     One HERE description, split into the location it anchors to and the condition it reports.
///     Anchors are withheld rather than guessed: an unrecognised prefix costs the anchor only, because
///     the condition itself comes from <c>codes[0]</c> (see <see cref="IncidentConditionCatalog" />).
/// </summary>
public readonly record struct ParsedDescription(
    DescriptionShape Shape,
    Option<string> PrimaryAnchor,
    Option<string> SecondaryAnchor,
    Option<string> RampTarget,
    Option<string> StatedDirection,
    string ConditionText);

/// <summary>
///     Total function over HERE's incident description grammar. Never throws, never returns null, and
///     never invents an anchor.
///     <para>
///         The grammar is closed and its separator is the LAST <c>" - "</c>, never the first: four
///         measured Australian records carry a road name containing <c>" - "</c>
///         ("At Beaudesert - Nerang Road - Road surface in poor condition", and three on "Railway
///         Alice Springs - Darwin"). Splitting on the first occurrence silently truncates the road.
///     </para>
/// </summary>
public static partial class IncidentDescriptionGrammar
{
    private const string Separator = " - ";

    /// <summary>Splits HERE's description into its location prefix and the condition it reports.</summary>
    public static ParsedDescription Parse(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return new ParsedDescription(DescriptionShape.None, default, default, default, default, string.Empty);

        string text = description.Trim();

        // The LAST separator, so a hyphenated road name in the prefix survives intact.
        int split = text.LastIndexOf(Separator, StringComparison.Ordinal);
        if (split < 0)
            return new ParsedDescription(DescriptionShape.None, default, default, default, default, text);

        string prefix = text[..split].Trim();
        string condition = text[(split + Separator.Length)..].Trim();

        (string cleanedPrefix, var direction) = ExtractDirection(prefix);

        if (cleanedPrefix.StartsWith("Ramp to ", StringComparison.OrdinalIgnoreCase))
            return new ParsedDescription(
                DescriptionShape.RampTo, default, default,
                Anchor(cleanedPrefix["Ramp to ".Length..]), direction, condition);

        var between = BetweenRegex().Match(cleanedPrefix);
        if (between.Success)
            return new ParsedDescription(
                DescriptionShape.Between,
                Anchor(between.Groups["a"].Value),
                Anchor(between.Groups["b"].Value),
                default, direction, condition);

        if (cleanedPrefix.StartsWith("At ", StringComparison.OrdinalIgnoreCase))
            return new ParsedDescription(
                DescriptionShape.At, Anchor(cleanedPrefix["At ".Length..]),
                default, default, direction, condition);

        return new ParsedDescription(DescriptionShape.Unknown, default, default, default, direction, condition);
    }

    private static Option<string> Anchor(string value)
    {
        string trimmed = value.Trim().Trim(',').Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? default : Option<string>.Some(trimmed);
    }

    /// <summary>
    ///     Lifts a direction word HERE stated anywhere in the prefix — including inside a parenthetical
    ///     ("Ramp to Gold Coast Highway (Eastbound)") — out of the anchor, so the anchor names a place
    ///     and the direction is carried as its own fact.
    /// </summary>
    private static (string Prefix, Option<string> Direction) ExtractDirection(string prefix)
    {
        var match = DirectionRegex().Match(prefix);
        if (!match.Success)
            return (prefix, default);

        string stripped = DirectionRegex().Replace(prefix, " ");
        // Removing the token can leave an empty parenthetical or doubled spaces behind.
        stripped = EmptyParenthesesRegex().Replace(stripped, " ");
        stripped = WhitespaceRegex().Replace(stripped, " ").Trim();

        return (stripped, Option<string>.Some(match.Value.ToLowerInvariant()));
    }

    /// <summary>Anchored at the start, unlike the summarizer's old mid-string anchor regex: a
    ///  "between" that is not the prefix's own shape is not a Between record.</summary>
    [GeneratedRegex(@"^Between\s+(?<a>.+?)\s+and\s+(?<b>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BetweenRegex();

    [GeneratedRegex(@"\b(northbound|southbound|eastbound|westbound|citybound|outbound)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex DirectionRegex();

    [GeneratedRegex(@"\(\s*\)")]
    private static partial Regex EmptyParenthesesRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();
}

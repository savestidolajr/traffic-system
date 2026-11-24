using System.Collections.Frozen;
using System.Text;
using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Substitutes <c>{token}</c> placeholders in a bookend template with per-station values, so
///     one shared library entry ("Looking over {city} today") serves every station on a brand
///     without each retyping it — and without a station accidentally inheriting another's wording.
///     <para>
///         Resolution is hybrid: a small set of BUILT-IN tokens is derived from the schedule the
///         bulletin is being generated for, and a station may override any of them (or add its
///         own) via <c>MarketSchedule.BookendTokens</c>. Overrides always win.
///     </para>
///     <para>
///         <c>{city}</c> and <c>{market}</c> are the station's human-readable place name from
///         <see cref="KnownMarketNames" /> — never the raw market-id slug, which carries call-sign
///         suffixes and storage-key munging ("cairns-4ca") that would air as noise. A market not in
///         that table resolves to an EMPTY value, so the assignment gate rejects the bookend instead
///         of airing a mangled word; a station override is the escape hatch for any market, exactly
///         as goldcoast does with <c>{"city": "the Gold Coast"}</c>. That is why the schedule studio
///         previews the fully resolved sentence before an operator can save: the default is a
///         convenience, the preview is the guard. Pure and deterministic: no I/O, no clock.
///     </para>
/// </summary>
public static partial class BookendTokenResolver
{
    /// <summary>The token names resolved automatically from schedule data.</summary>
    public static readonly IReadOnlyList<string> BuiltInTokens = ["city", "market", "station"];

    /// <summary>
    ///     Human-readable place name for every market, keyed by MarketId or short alias — the
    ///     SHARED <see cref="MarketNames" /> table, not a private copy, so this cannot drift from
    ///     the LLM prompt layer's idea of what a market is called. <c>{city}</c> and
    ///     <c>{market}</c> both resolve from it (a station override can differentiate them when one
    ///     needs different wording). A market id is a storage key, not a place name, so slug
    ///     munging cannot recover "Port Augusta" from "portaugusta" or "Cairns" from
    ///     "cairns-4ca". Keep the table in step when a market is added: the authoritative list is
    ///     <c>GET /api/schedules</c>.
    /// </summary>
    private static FrozenDictionary<string, string> KnownMarketNames => MarketNames.All;

    /// <summary>Outcome of resolving a template: the final text plus any token it could not fill.</summary>
    /// <param name="Text">
    ///     The substituted text. Unresolved tokens are REMOVED rather than left as literal braces —
    ///     a bookend must never air the string "{city}" — but callers should treat any
    ///     <paramref name="UnresolvedTokens" /> as a configuration error and drop the bookend.
    /// </param>
    /// <param name="UnresolvedTokens">Distinct token names with no built-in or override value.</param>
    public sealed record Resolution(string Text, IReadOnlyList<string> UnresolvedTokens)
    {
        public bool IsComplete => UnresolvedTokens.Count == 0;
    }

    /// <summary>
    ///     Resolves a template against a station's built-in values and overrides. Token names are
    ///     matched case-insensitively so <c>{City}</c> and <c>{city}</c> behave identically.
    /// </summary>
    public static Resolution Resolve(
        string template,
        string marketId,
        string station,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (string.IsNullOrWhiteSpace(template))
            return new Resolution("", []);

        var values = BuildValues(marketId, station, overrides);
        List<string>? unresolved = null;

        string text = TokenPattern().Replace(template, match =>
        {
            string name = match.Groups[1].Value;
            if (values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value))
                return value;

            unresolved ??= [];
            if (!unresolved.Contains(name, StringComparer.OrdinalIgnoreCase))
                unresolved.Add(name);
            return "";
        });

        return new Resolution(CollapseWhitespace(text), unresolved ?? []);
    }

    /// <summary>
    ///     The distinct token names a template references, in first-appearance order. Used by the
    ///     API boundary to reject a station assignment whose tokens cannot all be filled, and by
    ///     the studio to prompt for the values a chosen bookend needs.
    /// </summary>
    public static IReadOnlyList<string> ExtractTokens(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return [];

        var names = new List<string>();
        foreach (Match match in TokenPattern().Matches(template))
        {
            string name = match.Groups[1].Value;
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);
        }

        return names;
    }

    /// <summary>
    ///     The effective token values for a station: built-ins first, then overrides applied on
    ///     top. Exposed so the studio can preview exactly what the pipeline will substitute.
    ///     <para>
    ///         <c>{city}</c> and <c>{market}</c> both resolve to the market's human-readable place
    ///         name (<see cref="KnownMarketNames" />). An unknown market leaves them EMPTY — the
    ///         resolution then reports them as unfillable and the assignment gate rejects the
    ///         bookend, which is the mandated alternative to airing a slug-mangled word.
    ///     </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildValues(
        string marketId,
        string station,
        IReadOnlyDictionary<string, string>? overrides)
    {
        string placeName = marketId is not null
            && KnownMarketNames.TryGetValue(marketId.Trim(), out string? known)
                ? known
                : "";

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["city"] = placeName,
            ["market"] = placeName,
            ["station"] = (station ?? "").Trim()
        };

        if (overrides is not null)
            foreach (var (key, value) in overrides)
                if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                    values[key.Trim()] = value.Trim();

        return values;
    }

    /// <summary>
    ///     Removes the double spaces and orphaned leading/trailing space a dropped token leaves
    ///     behind, so a partially resolved line still reads as a sentence rather than a gap.
    /// </summary>
    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool lastWasSpace = false;
        foreach (char c in text)
        {
            bool isSpace = c is ' ' or '\t';
            if (isSpace && lastWasSpace)
                continue;
            builder.Append(isSpace ? ' ' : c);
            lastWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}

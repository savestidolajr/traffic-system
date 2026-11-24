using System.Text.RegularExpressions;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     A bulletin script separated into the STATION-AUTHORED lines that top and tail it and the
///     EDITORIAL BODY between them. See <see cref="StationLineSplitter" /> for why the distinction
///     exists.
/// </summary>
/// <param name="Leading">Station lines the script opens with, in reading order (normally the bookend intro).</param>
/// <param name="Body">The generated, evidence-grounded read between them.</param>
/// <param name="Trailing">Station lines the script closes with, in reading order (filler statements, then the bookend outro).</param>
public sealed record ScriptBodySplit(
    IReadOnlyList<string> Leading,
    string Body,
    IReadOnlyList<string> Trailing)
{
    /// <summary>How many station-authored lines were peeled off — what an operator is told is untouchable.</summary>
    public int PreservedLineCount => Leading.Count + Trailing.Count;

    /// <summary>
    ///     Puts <paramref name="body" /> back between the station lines this split peeled off, using
    ///     the same phrase-boundary punctuation rule the generation pipeline joins them with.
    /// </summary>
    public string Recompose(string body) =>
        BulletinBookendComposer.Join([.. Leading, body, .. Trailing]);
}

/// <summary>
///     Peels the station-authored top-and-tail off a bulletin script so an LLM pass only ever sees
///     the part it may change.
///
///     <para>
///         Bookends and filler statements are ARN's own editorial content. The generation pipeline
///         appends them VERBATIM after drafting, review, safety and delivery direction — they never
///         pass through an LLM stage and they are never checked against traffic evidence, because
///         they are not traffic claims ("Roadworks are updated weekly on the council website",
///         "Download the iHeartRadio app"). An operator-driven rewrite of an ALREADY-COMPOSED script
///         sees them inline and cannot tell them apart, which produced two real failures: the
///         grounding reviewer refused the whole rewrite as "unsourced content" for lines the writer
///         never wrote, and the rewriter felt free to reword a station's fixed promo.
///     </para>
///     <para>
///         Peeling them off head and tail — the only positions
///         <see cref="BulletinBookendComposer.Compose" /> ever places them — makes both impossible
///         by construction: the rewriter cannot touch what it is not shown, and the reviewer cannot
///         flag it. Matching is on the punctuation-stripped, case-folded, whitespace-collapsed text
///         (<see cref="FillerStatementSelector.Normalize" />, the pipeline's own statement identity),
///         so a re-punctuated or re-cased line still matches.
///     </para>
///     Pure and deterministic: no I/O, no clock.
/// </summary>
public static partial class StationLineSplitter
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\{[A-Za-z][A-Za-z0-9_]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>
    ///     Splits <paramref name="script" /> around the station lines it opens and closes with.
    ///     Returns the whole script as <see cref="ScriptBodySplit.Body" /> when
    ///     <paramref name="stationLines" /> is empty, nothing matches, or the script is nothing but
    ///     station lines — the caller then treats it exactly as an unsplit script.
    /// </summary>
    /// <param name="stationLines">
    ///     Station-authored texts for this bulletin: the market's filler statements and the bookend
    ///     intro/outro. A line may be several sentences (each is matched individually, so a
    ///     partially-spoken statement still peels) and may still carry unresolved
    ///     <c>{token}</c> placeholders — the aired text substituted them from schedule values this
    ///     caller may not have, so a tokenised line matches on its literal fragments in order.
    /// </param>
    public static ScriptBodySplit Split(string script, IEnumerable<string> stationLines)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new ScriptBodySplit([], script ?? string.Empty, []);

        var exact = new HashSet<string>(StringComparer.Ordinal);
        List<string[]>? tokenised = null;
        foreach (string line in stationLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            foreach (string sentence in ScriptSentenceSplitter.Split(line))
            {
                if (!TokenPattern().IsMatch(sentence))
                {
                    string key = Key(sentence);
                    if (key.Length > 0)
                        exact.Add(key);
                    continue;
                }

                var fragments = TokenPattern().Split(sentence)
                    .Select(Key)
                    .Where(f => f.Length > 0)
                    .ToArray();
                if (fragments.Length > 0)
                    (tokenised ??= []).Add(fragments);
            }
        }

        var sentences = ScriptSentenceSplitter.Split(script);
        if ((exact.Count == 0 && tokenised is null) || sentences.Count == 0)
            return new ScriptBodySplit([], script.Trim(), []);

        int start = 0;
        while (start < sentences.Count && IsStationLine(sentences[start], exact, tokenised))
            start++;

        int end = sentences.Count;
        while (end > start && IsStationLine(sentences[end - 1], exact, tokenised))
            end--;

        // Every sentence is a station line: there is no editorial body to rewrite. Hand the whole
        // script back as the body rather than an empty one, so the caller's own "nothing to rewrite"
        // and slot checks stay the single source of that refusal.
        if (start >= end)
            return new ScriptBodySplit([], script.Trim(), []);

        return new ScriptBodySplit(
            [.. sentences.Take(start)],
            BulletinBookendComposer.Join(sentences.Skip(start).Take(end - start)),
            [.. sentences.Skip(end)]);
    }

    private static bool IsStationLine(string sentence, HashSet<string> exact, List<string[]>? tokenised)
    {
        string key = Key(sentence);
        if (key.Length == 0)
            return false;
        if (exact.Contains(key))
            return true;
        if (tokenised is null)
            return false;

        // A tokenised template matches when every literal fragment around its {tokens} appears in
        // order — "looking over {city} today" catches the aired "Looking over Cairns today" without
        // this caller needing the station's token overrides.
        foreach (var fragments in tokenised)
        {
            int cursor = 0;
            bool matched = true;
            foreach (string fragment in fragments)
            {
                int at = key.IndexOf(fragment, cursor, StringComparison.Ordinal);
                if (at < 0)
                {
                    matched = false;
                    break;
                }

                cursor = at + fragment.Length;
            }

            if (matched)
                return true;
        }

        return false;
    }

    /// <summary>The pipeline's statement identity, plus whitespace collapse so a re-wrapped line still matches.</summary>
    private static string Key(string text) =>
        WhitespaceRegex().Replace(FillerStatementSelector.Normalize(text), " ").Trim();
}

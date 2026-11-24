using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Assembles the final read from the station's bookend pair, the generated body, and any filler
///     top-up. Pure and deterministic: no I/O, no clock.
///     <para>
///         Bookends are ARN's own editorial content and never pass through an LLM stage — this
///         composer runs AFTER drafting, review/refine, word-target compliance, safety, and
///         delivery direction, exactly like filler. Unlike filler, a bookend is unconditional, so
///         its spoken cost is subtracted from the writer's word target BEFORE drafting
///         (<see cref="EstimateSeconds" />) rather than topped up afterwards.
///     </para>
/// </summary>
public static class BulletinBookendComposer
{
    /// <summary>
    ///     Characters that already close a spoken phrase. A trailing comma is intentionally retained:
    ///     some station bookends are authored as connective fragments (for example, "powered by,").
    ///     The generated body is handled separately in <see cref="Compose" /> so its seam into a
    ///     bookend always receives a sentence boundary.
    /// </summary>
    private const string TerminalPunctuation = ".!?,:;…—-";

    /// <summary>The pair's opening line, or empty when unassigned or intro-less.</summary>
    public static string IntroOf(Option<ResolvedBookends> bookends) =>
        bookends.HasValue ? bookends.Value.IntroText : "";

    /// <summary>The pair's closing line, or empty when unassigned or outro-less.</summary>
    public static string OutroOf(Option<ResolvedBookends> bookends) =>
        bookends.HasValue ? bookends.Value.OutroText : "";

    /// <summary>The pair's opening line as a TRIM LOCK: null when the station has none, so "no
    ///     locked prefix" and "empty intro" are the same thing to every consumer and mock.</summary>
    public static string? LockedIntroOf(Option<ResolvedBookends> bookends) =>
        IntroOf(bookends) is { Length: > 0 } intro ? intro : null;

    /// <summary>True when this station closes its bulletins with a fixed line.</summary>
    public static bool HasOutro(Option<ResolvedBookends> bookends) =>
        bookends.HasValue && bookends.Value.HasOutro;

    /// <summary>True when either end is read, i.e. the composer will change the script.</summary>
    public static bool HasAny(Option<ResolvedBookends> bookends) =>
        bookends.HasValue && (bookends.Value.HasIntro || bookends.Value.HasOutro);

    /// <summary>Combined word count of both lines — the words the body must give up.</summary>
    public static int WordCount(Option<ResolvedBookends> bookends) =>
        WordCounter.Count(IntroOf(bookends)) + WordCounter.Count(OutroOf(bookends));

    /// <summary>
    ///     Estimated spoken seconds the pair consumes at the voice's calibrated pace. Subtract this
    ///     from the slot before sizing the body, or every bookended bulletin overshoots and burns
    ///     duration-fit retries.
    /// </summary>
    public static double EstimateSeconds(Option<ResolvedBookends> bookends, double secondsPerWord) =>
        WordCount(bookends) * secondsPerWord;

    /// <summary>
    ///     Builds the full read: intro, body, any inline filler, then outro — so the closer always
    ///     has the last word. Absent parts are skipped without leaving double spaces.
    /// </summary>
    /// <param name="inlineFiller">
    ///     Filler statement texts spoken INSIDE this take. Only populated when the station's pair has
    ///     an outro: filler is normally stitched as separate audio after the body, which would land
    ///     it after the closer. Passing it here instead keeps the order right and keeps the join
    ///     seamless, at the cost of the filler's exact-duration guarantee (it now rides the TTS
    ///     duration-fit loop like the rest of the read).
    /// </param>
    public static string Compose(
        Option<ResolvedBookends> bookends,
        string body,
        IReadOnlyList<string> inlineFiller)
    {
        // The body carries CONTENT only: the locked pair owns the opener and the closer. The writer
        // is told the pair (so it sizes around them) and sometimes writes them into the body anyway —
        // observed live 2026-08-17, dev job 6727ce48: "Now looking over Sydney. Now looking over
        // Sydney." aired the station opener twice. A body edge that duplicates its bookend is
        // stripped here, at the single place intro/body/outro are joined.
        string trimmedBody = StripDuplicateEdge(body, IntroOf(bookends), fromStart: true);
        trimmedBody = StripDuplicateEdge(trimmedBody, OutroOf(bookends), fromStart: false);

        var trailingParts = new List<string>(1 + inlineFiller.Count);
        trailingParts.AddRange(inlineFiller);
        trailingParts.Add(OutroOf(bookends));
        string bodyAndTrailing = JoinBodyAndFollowing(trimmedBody, trailingParts);
        return Join([IntroOf(bookends), bodyAndTrailing], normalizePunctuation: true);
    }

    /// <summary>
    ///     The text with its locked bookend edges removed — the BODY a reviewer should judge. The
    ///     expansion grounding check flagged the station outro as "unsupported station sign-off"
    ///     (observed live, dev job 9bb43d88: the dangling "…together, powered by" closer), costing a
    ///     refine cycle per bulletin on config it can never change. Bookends are locked config, not
    ///     model-grown content; nothing that reviews growth should ever see them.
    /// </summary>
    public static string StripBookendEdges(string text, Option<ResolvedBookends> bookends)
    {
        string body = StripDuplicateEdge(text, IntroOf(bookends), fromStart: true);
        return StripDuplicateEdge(body, OutroOf(bookends), fromStart: false);
    }

    /// <summary>
    ///     Splits a read into its body and a trailing station outro, when one is present. Returns
    ///     the text unchanged with a null outro otherwise.
    ///     <para>
    ///         Exists for the required-fixture splice, which appends a missing weather or fuel line
    ///         to the TAIL. When the tail is already the station closer — which happens whenever a
    ///         composed take re-enters the loop — appending buries the outro mid-read, and
    ///         <see cref="StripDuplicateEdge" /> then cannot match it because it only ever looks at
    ///         the trailing edge. Composition duly appends the closer a second time. Observed on
    ///         dev sydney <c>ffcf233d</c> (2026-08-25): "…local traffic can still get through.
    ///         That's KIIS traffic and weather together, powered by Light rain, 17 degrees. That's
    ///         KIIS traffic and weather together, powered by". Same aired defect as test
    ///         <c>b322bc0c</c>, a different route to it.
    ///     </para>
    /// </summary>
    public static (string Body, string? Outro) SplitTrailingOutro(
        string text, Option<ResolvedBookends> bookends)
    {
        string outro = OutroOf(bookends);
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(outro))
            return (text, null);

        string body = StripDuplicateEdge(text, outro, fromStart: false);
        return ReferenceEquals(body, text) || body.Length == text.Length
            ? (text, null)
            : (body, text[body.Length..].Trim());
    }

    /// <summary>
    ///     Removes <paramref name="bookendText" /> from the body's leading (or trailing) words when
    ///     the words match under punctuation/case-insensitive comparison. Exact duplicates only — a
    ///     paraphrased opener is the prompt's problem, not a mechanical join fault.
    ///     <para>
    ///         Inline delivery tags ("[brightly]") are skipped when matching. They are never spoken,
    ///         and the VOICED form of an already-composed read carries them — so a tag sitting on the
    ///         closer defeated this guard entirely and the outro was appended a second time. Observed
    ///         live on test <c>b322bc0c</c> (2026-08-24): a ceiling trim went content-limited, the
    ///         re-fit re-entered with the composed tagged take, the trailing-edge match failed on the
    ///         tag, and the read aired "…powered by. That's KIIS traffic and weather together,
    ///         powered by". Only tag tokens are skipped; every other token still has to match, so a
    ///         genuinely different tail is still left alone.
    ///     </para>
    /// </summary>
    private static string StripDuplicateEdge(string body, string bookendText, bool fromStart)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(bookendText))
            return body;

        static string Norm(string w) => new(w.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        static bool IsTagToken(string t) => t.Length > 1 && t[0] == '[' && t[^1] == ']';
        var bookendWords = bookendText.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Norm).Where(w => w.Length > 0).ToList();
        if (bookendWords.Count == 0)
            return body;

        var bodyTokens = System.Text.RegularExpressions.Regex.Matches(body, @"\S+")
            .Where(t => !IsTagToken(t.Value))
            .ToList();
        if (bodyTokens.Count < bookendWords.Count)
            return body;

        for (int i = 0; i < bookendWords.Count; i++)
        {
            var token = fromStart ? bodyTokens[i] : bodyTokens[bodyTokens.Count - bookendWords.Count + i];
            if (Norm(token.Value) != bookendWords[i])
                return body;
        }

        // Cutting at the matched RUN's outer boundary also removes any tag that decorated the
        // duplicated line — it belongs to the copy being removed, and the fresh bookend is appended
        // unadorned by the caller.
        return fromStart
            ? body[(bodyTokens[bookendWords.Count - 1].Index + bodyTokens[bookendWords.Count - 1].Length)..].TrimStart()
            : body[..bodyTokens[bodyTokens.Count - bookendWords.Count].Index].TrimEnd();
    }
    /// <summary>
    ///     Joins generated body content to one or more authored trailing parts. Unlike a generic
    ///     join, the generated body is never allowed to flow into a following part without a
    ///     sentence boundary; authored trailing commas remain untouched on the parts they belong
    ///     to.
    /// </summary>
    public static string JoinBodyAndFollowing(
        string body,
        IEnumerable<string> following,
        bool normalizePunctuation = true)
    {
        var trailing = following
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();
        if (trailing.Count == 0)
            return Join([body], normalizePunctuation);

        string trimmedBody = body.Trim();
        if (trimmedBody.Length > 0)
        {
            char last = trimmedBody[^1];
            if (last is ',' or ':' or ';')
                trimmedBody = trimmedBody[..^1] + ".";
            else if (!TerminalPunctuation.Contains(last))
                trimmedBody += ".";
        }

        return Join([trimmedBody, .. trailing], normalizePunctuation);
    }

    /// <summary>
    ///     Joins spoken parts into one read: blanks dropped, each part trimmed, and every part but
    ///     the last given a phrase boundary. Shared with <see cref="StationLineSplitter" /> so a
    ///     script taken apart and put back together rejoins exactly as the pipeline composed it.
    /// </summary>
    public static string Join(IEnumerable<string> parts, bool normalizePunctuation = false)
    {
        var kept = parts
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        if (kept.Count == 0)
            return "";

        // Every part but the last is punctuated so the synthesiser hears a phrase boundary; the
        // last is left exactly as authored.
        for (int i = 0; i < kept.Count - 1; i++)
            kept[i] = Punctuated(kept[i]);

        string result = string.Join(" ", kept);
        return normalizePunctuation ? SpokenPunctuationNormalizer.Normalize(result) : result;
    }

    private static string Punctuated(string text) =>
        TerminalPunctuation.Contains(text[^1]) ? text : text + ".";
}

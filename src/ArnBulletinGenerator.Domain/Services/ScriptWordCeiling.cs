namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The hard word ceiling a bulletin script may reach for a given slot, and the deterministic
///     whole-sentence trim back to it.
///     <para>
///         The ceiling is ~15% over the slot's word target, sized to sit inside the audio
///         pipeline's pitch-preserving speed-up headroom: a read at or under it still airs, and only
///         genuine over-production is cut. The DRAFT has been capped here since the structured-draft
///         work (<c>DraftWordBudget</c> calls <see cref="For" />), but for a long time nothing capped
///         what came AFTER it. Measured over 219 dev bulletins: 43% of generation attempts that ran a
///         refine ended above the word target and 8% above this ceiling, worst on short slots (20% of
///         22-second attempts against 4% of 30-second ones, and one 22-second read committed 100
///         words against a 69-word ceiling). The overrun then reached the audio stage, where the only
///         remaining lever is a TIME-based trim that cuts far more than the slot needs and leaves the
///         read short enough to pull in standing filler. Capping here instead keeps the decision where
///         the information is: whole sentences, in worst-first order, with the close protected.
///     </para>
///     <para>
///         The trim is deliberately blunt and never rewrites: it drops whole TRAFFIC sentences from
///         the end of the traffic block, keeps the weather/fuel lines the workflow identified
///         (<see cref="FixtureLines" />), and never goes below one traffic sentence. Dropping the
///         least important item is the correct sacrifice because the read is already ordered
///         worst-first.
///     </para>
/// </summary>
public static class ScriptWordCeiling
{
    /// <summary>
    ///     The hard word ceiling for a slot's word target: ~15% over, with a floor of 8 extra words
    ///     so a very short slot still has usable headroom.
    /// </summary>
    public static int For(int targetWordCount) =>
        targetWordCount + Math.Max(8, targetWordCount * 3 / 20);

    /// <summary>Words past <see cref="For" />, or 0 when the script is within the ceiling.</summary>
    public static int Excess(string script, int targetWordCount) =>
        Math.Max(0, WordCounter.Count(script) - For(targetWordCount));

    /// <summary>
    ///     Drops whole trailing traffic sentences until the script fits <see cref="For" />, keeping
    ///     the weather/fuel lines in <paramref name="fixtures" /> and never going below one traffic
    ///     sentence or <paramref name="minimumWords" />. A no-op (returns the input unchanged) when
    ///     the script already fits, when there is nothing droppable, or when dropping everything
    ///     droppable still would not fit, so a caller can compare by reference or by value to tell
    ///     whether anything happened.
    ///     <para>
    ///         Optional fixtures are protected only as the trailing close (they were selected to fit
    ///         if room allowed, and the read is worst-first, so the close is the natural last thing
    ///         standing). Required fixtures are protected wherever they sit: a station that marks
    ///         weather or fuel "always include" has promised it airs whenever the data was fetched
    ///         (test 2026-08-21, Sydney job 2895bb53 - the required weather line sat second-to-last
    ///         and the trim cut it with the good-run sweep).
    ///     </para>
    ///     <para>
    ///         The trim used to step through the slot floor because it only knew the ceiling (job
    ///         e55c3619). A read left over the ceiling is compressed or measure-trimmed by the audio
    ///         fitter; a read under the floor cannot be rescued, so a candidate drop that would
    ///         cross <paramref name="minimumWords" /> is refused.
    ///     </para>
    /// </summary>
    public static string TrimToCeiling(string script, int targetWordCount, FixtureLines? fixtures = null,
        int minimumWords = 0)
    {
        if (string.IsNullOrWhiteSpace(script) || Excess(script, targetWordCount) == 0)
            return script;

        var sentences = ScriptSentenceSplitter.Split(script);
        if (sentences.Count <= 1)
            return script;

        var isProtected = sentences
            .Select(s => fixtures?.Contains(s) ?? false)
            .ToArray();
        if (fixtures is not { Required: true })
        {
            int closeStart = sentences.Count;
            while (closeStart > 0 && isProtected[closeStart - 1])
                closeStart--;
            for (int i = 0; i < isProtected.Length; i++)
                isProtected[i] = i >= closeStart;
        }

        // Everything protected: there is no traffic to sacrifice, so leave it to the audio stage
        // rather than cutting into the only content there is.
        if (isProtected.All(p => p))
            return script;

        int ceiling = For(targetWordCount);
        int protectedWords = sentences.Where((_, i) => isProtected[i]).Sum(WordCounter.Count);
        var traffic = sentences.Where((_, i) => !isProtected[i]).ToList();

        int keep = traffic.Count;
        int total = traffic.Sum(WordCounter.Count) + protectedWords;
        int floor = Math.Max(0, minimumWords);
        while (keep > 1 && total > ceiling)
        {
            int candidateTotal = total - WordCounter.Count(traffic[keep - 1]);
            if (candidateTotal < floor)
                break;
            total = candidateTotal;
            keep--;
        }

        if (keep == traffic.Count)
            return script;

        var result = new List<string>(sentences.Count);
        int trafficSeen = 0;
        for (int i = 0; i < sentences.Count; i++)
        {
            if (isProtected[i])
            {
                result.Add(sentences[i]);
                continue;
            }
            trafficSeen++;
            if (trafficSeen <= keep)
                result.Add(sentences[i]);
        }
        return string.Join(" ", result);
    }
}

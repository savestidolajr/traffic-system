using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The complete set of deterministic rewrites that make a written read speakable, applied in one
///     call so every caller gets all of them.
/// </summary>
/// <remarks>
///     <para>
///         This exact five-normalizer expression existed character-for-character at four call sites
///         (the draft, polish and refine paths in <c>LlmScriptGenerator</c>, plus the structured-matrix
///         generator). Four copies of one rule is four chances to update three of them, and that is
///         precisely what happened: when normalisation had to be re-applied after the review loop's
///         mutations on 2026-08-23, only <see cref="SpokenPriceNormalizer" /> was added back. So a
///         post-generation mutation silently undid ARN's marked-up "shut" -> "closed" preference
///         (BFN-017), the clear-road labelling, the daypart correction and the punctuation cleanup —
///         while the price fix, being the one that got copied, survived.
///     </para>
///     <para>
///         The general rule this encodes: <b>a gate that runs before the last mutation does not
///         hold.</b> This pipeline mutates script text after generation in at least six places —
///         sentence refines, mechanical repairs, fixture splices, word-target expansion, filler
///         top-up, bookend composition — so the normalisation sweep has to be re-runnable and cheap.
///         Every rewrite here is idempotent, which is what makes running it again free.
///     </para>
/// </remarks>
public static class SpokenFormNormalizer
{
    /// <summary>
    ///     Applies every spoken-form rewrite. Safe to call repeatedly; each step is idempotent.
    /// </summary>
    /// <param name="script">The read to normalise.</param>
    /// <param name="actualDaypart">
    ///     Resolved daypart ("morning", "midday", "afternoon", "evening") used to correct a
    ///     mis-stated time of day, or <c>null</c> to leave daypart wording untouched.
    /// </param>
    public static string Normalize(string script, string? actualDaypart = null)
    {
        if (string.IsNullOrWhiteSpace(script))
            return script;

        return SpokenPunctuationNormalizer.Normalize(
            TimeOfDayNormalizer.Normalize(
                WeatherPhrasingNormalizer.Normalize(
                    SpokenPriceNormalizer.Normalize(
                        ClearRoadLabelNormalizer.Normalize(
                            ClosureVerbNormalizer.Normalize(script.Trim())))),
                actualDaypart));
    }

    /// <summary>
    ///     Resolves the daypart for <paramref name="timezoneId" /> at <paramref name="nowUtc" />.
    /// </summary>
    /// <remarks>
    ///     Lives here rather than in the generator because the Application layer now normalises too
    ///     (after the review loop and after word-target expansion) and needs the same answer. An
    ///     unknown or malformed timezone yields <c>null</c>, which leaves daypart wording alone —
    ///     never a guess, because correcting "this morning" to the wrong daypart is worse than
    ///     leaving the writer's own words.
    /// </remarks>
    public static string? ResolveDaypart(Option<string> timezoneId, DateTimeOffset nowUtc)
    {
        if (!timezoneId.HasValue || string.IsNullOrWhiteSpace(timezoneId.Value))
            return null;

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId.Value);
            return TimeZoneInfo.ConvertTime(nowUtc, timeZone).Hour switch
            {
                >= 5 and < 12 => "morning",
                >= 12 and < 14 => "midday",
                >= 14 and < 18 => "afternoon",
                _ => "evening"
            };
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }
}

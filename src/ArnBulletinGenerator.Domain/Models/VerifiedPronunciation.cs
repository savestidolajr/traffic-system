using System.Text;
using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     One script word an operator listened to and confirmed airs correctly, even though
///     <see cref="Services.AudioPreReview" /> flagged it as a suspect pronunciation.
///     <para>
///         The pre-review is a script-vs-transcript comparison, so it flags any word the
///         transcriber spells back differently from the written form. For Australian place
///         names whose orthography and phonetics diverge (Keilor → "Keeler", Prahran → "Pran",
///         Cheltenham → "Cheltnum") that happens precisely BECAUSE the name was said correctly,
///         and it recurs on every bulletin naming that road. This record is the operator's
///         standing "I checked it, it's fine" verdict, so the flag stops competing for
///         attention with the ones that matter.
///     </para>
///     <para>
///         Deliberately NOT consumed by the generation pipeline: <c>AudioPreReview.Compare</c>
///         keeps flagging everything and <c>Job.PreReview</c> keeps storing the raw, unfiltered
///         suspect list. This is a presentation lens over that stored truth, applied at read
///         time, so removing a verdict re-exposes the word on every bulletin ever generated
///         rather than only on future ones. See the 2026-07-28 decision.
///     </para>
/// </summary>
/// <param name="Word">The script word as flagged, in its original casing (e.g. "Keilor").</param>
/// <param name="Note">Optional operator note — why it is fine, or what it actually sounds like.</param>
/// <param name="Enabled">False suspends the verdict (the word flags again) without losing its provenance.</param>
/// <param name="VerifiedBy">Display name of the operator who confirmed it.</param>
/// <param name="VerifiedAtUtc">When the verdict was recorded.</param>
public sealed record VerifiedPronunciation(
    string Word,
    string? Note = null,
    bool Enabled = true,
    string? VerifiedBy = null,
    DateTime VerifiedAtUtc = default)
{
    /// <summary>
    ///     Case- and punctuation-insensitive match key. MUST stay byte-identical to the web
    ///     client's <c>stripPunctuation</c> (<c>AudioPreReviewBadge.tsx</c>), because the two
    ///     sides independently key the same verdict: the badge filters chips by this, the
    ///     trigger de-duplicates by it. Suspect words routinely carry possessives
    ///     ("Gate's", "Unleaded's"), so punctuation cannot be part of identity.
    /// </summary>
    [JsonIgnore]
    public string Key => NormalizeWord(Word);

    /// <summary>Letters and digits only, lower-invariant. Empty for a word with no alphanumerics.</summary>
    public static string NormalizeWord(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return string.Empty;

        var sb = new StringBuilder(word.Length);
        foreach (char c in word)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));

        return sb.ToString();
    }
}

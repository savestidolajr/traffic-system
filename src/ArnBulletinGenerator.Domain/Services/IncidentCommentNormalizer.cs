using System.Net;
using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Normalizes the operator-authored <c>comment</c> free-text that HERE attaches to some
///     incidents — road-authority entries like
///     "Beeliar Dr westbound before Kwinana Fwy, Banjup - Road Crash Left Lane(s) Blocked.
///     No Known Congestion. Exercise Extreme Caution." The raw text is messier than the
///     structured <c>description</c>: it carries HTML entities (<c>&amp;#13;</c>), embedded
///     newlines, website referrals ("Click 'Find out more'"), and — critically — ADVISORY
///     IMPERATIVES ("Seek Alternative Routes", "Exercise Extreme Caution") that would breach
///     the bulletin's no-advice boundary if a script voiced them.
///     <para>
///         We HTML-decode, collapse whitespace, drop advisory and referral sentences, and
///         accumulate whole sentences up to a length cap (never cutting mid-sentence). The
///         result is surfaced only when it adds detail the <c>description</c> does not already
///         carry verbatim — mirroring the summary dedup in <c>HereTrafficProvider.MapToDomain</c>.
///     </para>
/// </summary>
public static partial class IncidentCommentNormalizer
{
    private const int MaxLength = 140;

    /// <summary>Safety/advice imperatives that must never reach air (no-advice boundary).
    ///  "observe/follow signage": QLDTraffic's stock roadwork advice, observed live 2026-08-18
    ///  the moment authority descriptions started flowing to the writer.</summary>
    [GeneratedRegex(
        @"\b(exercise (extreme )?caution|seek alternative routes?|approach with care|use caution|allow extra time|plan ahead|take care|drive to (the )?conditions?|reduce speed|merge early|expect delays|(observe|follow) (the )?sign(age|s))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AdvisoryRegex();

    /// <summary>Website / UI referrals that carry no on-air value.</summary>
    [GeneratedRegex(@"\b(find out more|click here|click '|for (a )?detour map)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReferralRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    /// <summary>
    ///     Returns the cleaned, advisory-stripped comment, or <see cref="Option{T}.None" /> when
    ///     the comment is empty, reduces to nothing after filtering, or adds nothing beyond
    ///     <paramref name="description" />.
    /// </summary>
    public static Option<string> Normalize(string? comment, string description)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return Option<string>.None;

        // Decode entities (&#13;, &#39;, &amp;) then flatten all whitespace runs to single spaces.
        string flat = WhitespaceRegex().Replace(WebUtility.HtmlDecode(comment), " ").Trim();
        if (flat.Length == 0)
            return Option<string>.None;

        // Keep whole sentences only, dropping advisory/referral ones, until the length cap.
        var kept = new List<string>();
        int length = 0;
        foreach (string raw in flat.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (AdvisoryRegex().IsMatch(raw) || ReferralRegex().IsMatch(raw))
                continue;

            string sentence = kept.Count == 0 && raw.Length > MaxLength
                ? TruncateAtWord(raw, MaxLength)
                : raw;

            int added = sentence.Length + (kept.Count == 0 ? 0 : 2); // ". " separator
            if (kept.Count > 0 && length + added > MaxLength)
                break;

            kept.Add(sentence);
            length += added;
        }

        if (kept.Count == 0)
            return Option<string>.None;

        string normalized = string.Join(". ", kept);

        // Only worth carrying when the description doesn't already state it.
        if (!string.IsNullOrEmpty(description) &&
            description.Contains(normalized, StringComparison.OrdinalIgnoreCase))
            return Option<string>.None;

        return Option<string>.Some(normalized);
    }

    /// <summary>Truncates to the last word boundary at or before <paramref name="max" /> characters.</summary>
    private static string TruncateAtWord(string text, int max)
    {
        if (text.Length <= max)
            return text;
        int cut = text.LastIndexOf(' ', max - 1);
        return (cut > 0 ? text[..cut] : text[..max]).TrimEnd();
    }
}

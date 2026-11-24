using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Builds the operator-facing pre-review message from the raw comparison. Pronunciation
///     clearances are deliberately a presentation filter for suspects only: on prod job
///     c2894ffb109f4c218c73e274e43665b1 (2026-08-18, cairns-4ca), clearing Manoora as a known
///     transcription respelling also muted the evidence that the final read was cut off.
/// </summary>
public sealed record AudioPreReviewReport(
    IReadOnlyList<string> OutstandingSuspects,
    IReadOnlyList<string> UnheardTailWords,
    string Message,
    bool IsWarning)
{
    public static AudioPreReviewReport For(
        AudioPreReview.Result review, IReadOnlyCollection<string> clearedWords)
    {
        var clearedKeys = clearedWords
            .Select(VerifiedPronunciation.NormalizeWord)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The truncation ANCHOR is the script's LAST significant word — the final element of the
        // raw tail. A clearance may mute the words BEFORE it (those are pronunciation questions,
        // exactly what the 2026-07-28 verdicts exist to silence) but never the anchor: that is the
        // evidence the read stopped before the script did. Measured on the real aired bulletin, the
        // raw tail is ["Unleaded's", "Manoora"] — "Unleaded's" DID air, mispronounced as
        // "unlettered's", and cairns-4ca had already cleared it, so naming it as cut off would
        // resurrect a flag an operator deliberately silenced.
        var tail = new List<string>();
        for (int i = 0; i < review.UnheardTailWords.Count; i++)
        {
            string word = review.UnheardTailWords[i];
            bool isAnchor = i == review.UnheardTailWords.Count - 1;
            if (isAnchor || !clearedKeys.Contains(VerifiedPronunciation.NormalizeWord(word)))
                tail.Add(word);
        }

        var tailKeys = tail
            .Select(VerifiedPronunciation.NormalizeWord)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outstanding = review.SuspectWords
            .Where(word => !clearedKeys.Contains(VerifiedPronunciation.NormalizeWord(word)))
            .Where(word => !tailKeys.Contains(VerifiedPronunciation.NormalizeWord(word)))
            .ToList();

        var parts = new List<string>();
        if (tail.Count > 0)
            parts.Add($"the read is cut off — {string.Join(", ", tail)} not heard at the end of the audio");
        if (outstanding.Count > 0)
            parts.Add($"{outstanding.Count} suspect pronunciation(s) — {string.Join(", ", outstanding)}");
        if (review.NumberMismatches.Count > 0)
            parts.Add($"aired quantities not verified — {string.Join(", ", review.NumberMismatches)}");

        bool isWarning = parts.Count > 0;
        string message = isWarning
            ? $"Audio pre-review: {string.Join("; ", parts)}. Listen before approving."
            : $"Audio pre-review: transcript matches the script ({review.MatchRatio:P0} of significant words heard).";

        return new AudioPreReviewReport(outstanding, tail, message, isWarning);
    }
}

using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Contracts;


namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     Reviews a generated script against broadcast quality criteria.
///     When <paramref name="context" /> is supplied, the reviewer also
///     verifies the script against the traffic data (fabrication check).
/// </summary>
public interface IScriptReviewer
{
    Task<Result<ScriptReviewResult>> ReviewScriptAsync(
        string script,
        int targetDurationSeconds,
        int? targetWordCountOverride = null,
        string? rejectionFeedback = null,
        BroadcastWithTraffic? context = null,
        CancellationToken cancellationToken = default,
        IReadOnlyList<DraftedSentence>? draftedSentences = null,
        IReadOnlyList<string>? removedSentences = null);
}

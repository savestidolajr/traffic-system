namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     Result of an LLM-based script review. Contains approval status, structured issues addressed to
///     the smallest repairable scope (<see cref="ReviewIssue" />), and human-readable feedback.
/// </summary>
public sealed record ScriptReviewResult
{
    /// <summary>Whether the script passed semantic review.</summary>
    public bool IsApproved { get; init; }

    /// <summary>Structured review issues found during review.</summary>
    public IReadOnlyList<ReviewIssue> Issues { get; init; }

    /// <summary>
    ///     Human-readable feedback derived exclusively from <see cref="Issues" /> so the job log and
    ///     whole-script refine path cannot drift from the structured review contract.
    /// </summary>
    public string Feedback => IsApproved
        ? "Approved."
        : string.Join(" ", Issues
            .Select(issue => !string.IsNullOrWhiteSpace(issue.Reason) ? issue.Reason : issue.Defect)
            .Where(text => !string.IsNullOrWhiteSpace(text)));

    /// <summary>
    ///     Creates a review result from its single source of truth: the structured issue list.
    ///     A rejected result without issues is retained as an empty rejection for fail-safe callers.
    /// </summary>
    public ScriptReviewResult(bool isApproved, IReadOnlyList<ReviewIssue>? issues = null)
    {
        IsApproved = isApproved;
        Issues = issues ?? Array.Empty<ReviewIssue>();
    }

    /// <summary>
    ///     Backward-compatible constructor for callers that still provide legacy prose feedback.
    ///     The prose is immediately represented as an unresolved issue, so <see cref="Feedback" />
    ///     remains derived from <see cref="Issues" />.
    /// </summary>
    public ScriptReviewResult(bool isApproved, string feedback)
        : this(
            isApproved,
            isApproved || string.IsNullOrWhiteSpace(feedback)
                ? Array.Empty<ReviewIssue>()
                : [ReviewIssue.ForUnresolvedSentence("LEGACY_REVIEW", feedback)])
    {
    }

    /// <summary>Creates an approved review result.</summary>
    public static ScriptReviewResult Approved() => new(true);

    /// <summary>Creates a rejected review result with structured issues.</summary>
    public static ScriptReviewResult Rejected(IReadOnlyList<ReviewIssue> issues) => new(false, issues);

    /// <summary>Creates a rejected result from legacy, unstructured feedback.</summary>
    public static ScriptReviewResult Rejected(string feedback) => new(false, feedback);
}

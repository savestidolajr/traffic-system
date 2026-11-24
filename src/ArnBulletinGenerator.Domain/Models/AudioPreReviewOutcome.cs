namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The persisted outcome of the pre-approval audio review (see
///     <see cref="Services.AudioPreReview" />): how much of the script's significant
///     vocabulary survived transcription of the FINAL audio, which words did not, and
///     which aired quantities (prices/temperatures) could not be verified. Carried on the
///     job so the review cockpit can surface suspect words as playable chips.
/// </summary>
public sealed record AudioPreReviewOutcome(
    double MatchRatio,
    IReadOnlyList<string> SuspectWords,
    IReadOnlyList<string> NumberMismatches);

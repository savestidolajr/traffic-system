namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
///     The bulletin this job generated and then had replaced by a fallback. Written once, at
///     the moment the fallback is applied, so the operator can still read and hear what the
///     pipeline produced instead of only what aired — before this existed,
///     <see cref="Job.ApplyFillerFallback" /> cleared <see cref="Job.ScriptText" /> outright and
///     the generated words were unrecoverable from the job record (measured on dev + test
///     2026-08-24: 40 of 49 jobs in a 24-hour window carried an empty script).
///     <para>
///         Evidence is deliberately NOT copied here: corridors, incidents, weather and fuel
///         remain on the job and describe this same take. Exactly one take is retained — the one
///         that existed immediately before the fallback fired — so the serialized payload stays
///         far inside the Table Storage per-property cap.
///     </para>
/// </summary>
public sealed record SupersededTake(
    DateTime CapturedAt,
    string Reason,
    string ScriptText,
    Option<string> TaggedScriptText,
    Option<string> AudioUrl,
    string? AudioSha256,
    Option<double> MeasuredDurationSec,
    Option<double> EstimatedDurationSec,
    IReadOnlyList<JobScriptSentence> ScriptSentences,
    Option<IReadOnlyList<WordTiming>> WordTimings,
    Option<AudioPreReviewOutcome> PreReview,
    Option<string> DeliveryStyleId,
    Option<int> DeliveryStyleVersion,
    Option<string> BookendId,
    Option<int> BookendVersion);

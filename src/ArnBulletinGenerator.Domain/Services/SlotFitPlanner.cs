namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     Shared slot-fill timing logic so the production orchestrator
///     (<c>BroadcastOrchestrator.EnsureWordTargetComplianceAsync</c>) and the eval's full-pipeline
///     mirror agree on WHEN an under-length final script needs expanding and WITH WHAT feedback.
///     <para>
///         Only UNDERSHOOT is corrected here: a script below the slot's word target cannot be
///         filled at synthesis (the audio pipeline can slow-fill only within a small budget, then
///         falls back — dead air). Overshoot is handled downstream by the audio pipeline's
///         measure-then-trim (drop the lowest-priority trailing sentences to fit the slot), so a
///         slightly-long script is desirable headroom, not a defect.
///     </para>
/// </summary>
public static partial class SlotFitPlanner
{
    /// <summary>A script within ±this many words of the target needs no timing correction.</summary>
    public const int Tolerance = 5;

    /// <summary>
    ///     Fewest words that still fill the slot WITHOUT LLM padding. A grounded read within ~16% of
    ///     target fills the slot at a slightly slower — still natural — pace (verified live: a 76-word
    ///     read filled a 30s slot whose word target was ~90), and the standing fillers plus the audio
    ///     slow-fill close any small remaining gap. LLM-expanding a read that is only marginally short
    ///     just invents content that fails grounding and reverts (observed: an approved 76-word Sydney
    ///     read burned two expansion round-trips, then reverted to itself). Expansion is reserved for a
    ///     genuinely short read — a real dead-air risk — not a few words under a natural-pace target.
    /// </summary>
    public static int MinAcceptableWords(int targetWords) =>
        targetWords - Math.Max(Tolerance, (int)Math.Round(targetWords * 0.16));

    /// <summary>True when the script is short enough that synthesis could not fill the slot.</summary>
    public static bool NeedsExpansion(int currentWords, int targetWords) =>
        currentWords < MinAcceptableWords(targetWords);

    /// <summary>
    ///     Refine feedback to expand an under-length script toward the slot using only UNVOICED
    ///     data. The escape hatch ("if everything is voiced, return unchanged") is load-bearing:
    ///     without it the model reaches the target by restating items it already covered — echo
    ///     padding a listener hears as the same closure read twice (frozen-eval iter4, Gold Coast)
    ///     — or by inventing reassurance ("the rest of the main runs are clear"). A short honest
    ///     read is the accepted outcome for a content-limited market, not a failure.
    /// </summary>
    public static string ExpansionFeedback(int targetWords) =>
        $"Timing correction only: bring this script toward {targetWords} words " +
        $"(acceptable range {targetWords - Tolerance} to {targetWords + Tolerance}) by adding ONLY data " +
        "not yet voiced — a flowing road, the weather line, the fuel line, EVERY provided standing road fact " +
        "not already in the script, or a concrete detail (cross-street, lane, direction) the data states and " +
        "the script hasn't used. NEVER restate an item already in the script in different words, and never " +
        "invent. If every data source is already voiced, return the script unchanged — a shorter honest read " +
        "is the correct result.";
}

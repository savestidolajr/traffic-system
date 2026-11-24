namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Which triage lane a ledger event currently sits in.</summary>
public enum LedgerLane
{
    /// <summary>First seen in the market's most recent fetch.</summary>
    New,

    /// <summary>Present in the most recent fetch and observed before it, with at least one recorded change.</summary>
    Changed,

    /// <summary>Present in the most recent fetch, previously seen, unchanged.</summary>
    Active,

    /// <summary>Seen before but absent from the market's most recent fetch.</summary>
    Cleared,

    /// <summary>Active but first seen long ago with no recent change — likely a rolling re-issue.</summary>
    Stale
}

/// <summary>
///     An operator's standing judgment on a ledger event, persisted across bulletins. Omission is
///     the ONLY judgment: every event airs by default, and this is the single lever that keeps one
///     out. Confirmed/Flagged existed to feed a trust ladder that was never given a UI surface —
///     no control could write them and, once the ladder went, nothing read them.
/// </summary>
public enum VerdictKind
{
    /// <summary>Keep this event out of bulletins while the verdict stands.</summary>
    Omitted
}

/// <summary>One recorded change to an event between fetches ("window rolled", "NSW joined").</summary>
public sealed record LedgerObservation(
    DateTimeOffset AtUtc,
    string JobId,
    string Summary);

/// <summary>One published bulletin that voiced this event, with the sentence that aired.</summary>
public sealed record LedgerAiring(
    string JobId,
    DateTimeOffset AtUtc,
    string Sentence);

/// <summary>
///     A physical road event with identity across fetches — the fold of every observation the
///     stored jobs carry for one stable incident key. Derived, never authored: rebuildable at any
///     time from the job records themselves.
/// </summary>
public sealed record LedgerEvent(
    string Key,
    string RoadName,
    string Headline,
    string? Detail,
    IReadOnlyList<string> Sources,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    DateTimeOffset? StartTimeUtc,
    DateTimeOffset? EndTimeUtc,
    bool RoadClosed,
    LedgerLane Lane,
    int SightingCount,
    IReadOnlyList<LedgerObservation> Observations,
    IReadOnlyList<LedgerAiring> Airings,
    /// <summary>The newest sighting's full record, for the payload view.</summary>
    JobIncident Latest,
    /// <summary>The newest sighting's matched authority records from its corridor, when any.</summary>
    IReadOnlyList<OfficialEvidence>? OfficialEvidences);

/// <summary>An operator's persisted verdict on a ledger event.</summary>
public sealed record EventVerdict(
    string Market,
    string EventKey,
    VerdictKind Verdict,
    string? Notes,
    string By,
    DateTimeOffset AtUtc);

/// <summary>One feed's contribution health over the folded window.</summary>
public sealed record FeedHealth(
    string SourceId,
    DateTimeOffset? LastContributedUtc,
    int JobsWithSource,
    int JobsInWindow);

/// <summary>How one event moved between a bulletin and the market's previous settled bulletin.</summary>
public enum DeltaStatus
{
    New,
    Changed,
    Cleared,
    Unchanged
}

/// <summary>Per-event delta for a bulletin's diff-review strip, keyed by stable incident key.</summary>
public sealed record EventDelta(
    string Key,
    string RoadName,
    string Headline,
    DeltaStatus Status,
    string? ChangeSummary);

/// <summary>An after-the-fact review of a bulletin that aired without operator sign-off.</summary>
public sealed record SpotCheckReview(
    string JobId,
    string By,
    DateTimeOffset AtUtc);

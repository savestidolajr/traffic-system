namespace ArnBulletinGenerator.Domain.Contracts;

// =============================================================================================
// Event Ledger wire contracts — the Desk, the diff-review strip, feed health, spot checks.
// =============================================================================================

public sealed record LedgerObservationDto(DateTimeOffset AtUtc, string JobId, string Summary);

public sealed record LedgerAiringDto(string JobId, DateTimeOffset AtUtc, string Sentence);

public sealed record LedgerEventDto(
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
    /// <summary>Triage lane: New | Changed | Active | Cleared | Stale.</summary>
    string Lane,
    int SightingCount,
    IReadOnlyList<LedgerObservationDto> Observations,
    IReadOnlyList<LedgerAiringDto> Airings,
    /// <summary>Operator verdict: "Omitted", or null when the event is not omitted.</summary>
    string? Verdict,
    string? VerdictNotes,
    string? VerdictBy,
    DateTimeOffset? VerdictAtUtc,
    /// <summary>The newest sighting's full record — the payload view's HERE-side column.</summary>
    JobIncidentDto Record,
    /// <summary>The newest sighting's matched authority records verbatim-as-parsed (same shape as corridors carry).</summary>
    IReadOnlyList<Models.OfficialEvidence>? OfficialEvidences);

public sealed record FeedHealthDto(
    string SourceId,
    DateTimeOffset? LastContributedUtc,
    int JobsWithSource,
    int JobsInWindow);

public sealed record LedgerDto(
    string Market,
    IReadOnlyList<LedgerEventDto> Events,
    IReadOnlyList<FeedHealthDto> FeedHealth);

public sealed record SetVerdictRequest(string? Verdict, string? Notes);

public sealed record EventDeltaDto(
    string Key,
    string RoadName,
    string Headline,
    /// <summary>New | Changed | Cleared | Unchanged.</summary>
    string Status,
    string? ChangeSummary);

public sealed record JobDeltaDto(
    string JobId,
    string? PreviousJobId,
    DateTimeOffset? PreviousJobAtUtc,
    IReadOnlyList<EventDeltaDto> Deltas);

public sealed record SpotCheckReviewDto(string JobId, string By, DateTimeOffset AtUtc);

namespace ArnBulletinGenerator.Domain.Models;

/// <summary>A traffic incident captured on a Job for review evidence, carrying the authority's own direction, lane, and closure detail alongside the script headline.</summary>
public sealed record JobIncident(
    string Key,
    int Priority,
    string Headline,
    string Location,
    int FunctionalClass = 5,
    double LengthMeters = 0,
    Option<string> Detail = default,
    Option<double> Latitude = default,
    Option<double> Longitude = default,
    IReadOnlyList<CoveragePoint>? Shape = null,
    // Below: carried straight from TrafficIncident (already computed from the
    // HERE incident, previously dropped on the floor at ToJobIncident) so the
    // rundown can surface direction, lane state, the road authority's own
    // free-text reason, and a hard closure flag instead of free-text guessing.
    Option<string> Direction = default,
    Option<string> LaneDetail = default,
    Option<string> Comment = default,
    bool RoadClosed = false,
    DateTimeOffset? EntryTimeUtc = default,
    /// <summary>
    ///     Why <see cref="ClaimGate" /> withheld this record from the script, or null when it was
    ///     voiceable. Persisted so a stored job — and a replay of it — can answer why a record present in
    ///     the evidence was never reported.
    /// </summary>
    string? WithheldReason = null,
    // Evidence-modal fields (2026-08-18): the record's own claim-bearing attributes so the
    // fact-check dialog can show HERE's record as parsed, next to the authority's.
    string? Type = null,
    DateTimeOffset? StartTimeUtc = null,
    DateTimeOffset? EndTimeUtc = null,
    IReadOnlyList<string>? Sources = null,
    int? LanesClosed = null,
    int? LanesTotal = null,
    IReadOnlyList<string>? OtherAssets = null,
    string? CanonicalClass = null,
    /// <summary>Fields filled by the prose extractor rather than the source's structured payload.</summary>
    IReadOnlyList<string>? ProseFields = null,
    /// <summary>Source-native current/stable identifiers retained on the persisted job evidence.</summary>
    IReadOnlyList<SourceRecordIdentity>? SourceRecordIdentities = null);

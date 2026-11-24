using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts.Messages;

/// <summary>
///     Message queued to trigger asynchronous regeneration orchestration.
///     Contains all information needed (including traffic data) to regenerate
///     a broadcast without re-fetching external data.
/// </summary>
public sealed record RegenerationQueueMessage(
    string JobId,
    string Location,
    double TargetDurationSeconds,
    string Traffic,
    IReadOnlyList<RegenerationIncident> Incidents,
    string? VoiceId = null,
    string? Model = null,
    VoiceSettings? VoiceSettings = null,
    // Additive WPM boost from the resolved voice profile (see VoiceProfileVersion.PaceBoostWpm).
    // 0 = legacy messages / voices resolved without a profile. This was MISSING from the
    // regeneration path (only BroadcastQueueMessage carried it): every regenerate ran with
    // pace boost 0, so a boosted station's re-read lost its compression headroom and
    // AudioFinalizer's revoice boost math was dead code. Naming identical to
    // BroadcastQueueMessage.PaceBoostWpm for the same reason.
    double PaceBoostWpm = 0,
    string? RejectionFeedback = null,
    string? PreviousScript = null,
    IReadOnlyList<string>? SelectedCorridors = null,
    BulletinContentSelection? Content = null,
    string? DeliveryIntent = null,
    // The operator's per-line tags, already applied to the approved words. Verified
    // word-for-word against the plain script before use, then synthesised verbatim.
    string? TonedScript = null,
    IReadOnlyList<string>? SelectedGoodRuns = null,
    IReadOnlyList<string>? ExcludedIncidentKeys = null,
    bool RevoiceOnly = false,
    PronunciationLocator? PronunciationLocator = null,
    PronunciationLocator? GlobalPronunciationLocator = null,
    // Delivery provenance, carried for the same reason as the pronunciation locator: without
    // these the regenerated bulletin falls back to the neutral default policy and silently
    // loses the station's configured delivery (dev, 2026-07-28).
    DeliveryStylePolicy? DeliveryStyle = null,
    string? DeliveryStyleId = null,
    int? DeliveryStyleVersion = null,
    // Station bookend pair, carried for exactly the same reason: a regenerated bulletin that
    // drops the station's fixed opener/closer silently loses its branding, and the operator has no
    // way to tell from the console that a regenerate did it. Text is already token-substituted.
    ResolvedBookends? Bookends = null);

/// <summary>
///     Serializable traffic incident for the regeneration queue message.
/// </summary>
public sealed record RegenerationIncident(
    string Id,
    string Description,
    int Severity,
    string Location,
    string Type,
    double LengthMeters = 0,
    int FunctionalClass = 5,
    double? Latitude = null,
    double? Longitude = null,
    IReadOnlyList<SourceRecordIdentity>? SourceRecordIdentities = null);

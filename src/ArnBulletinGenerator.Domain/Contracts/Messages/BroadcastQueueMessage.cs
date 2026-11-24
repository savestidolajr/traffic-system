using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Contracts.Messages;

/// <summary>
///     Processing phase for a broadcast queue message. The default combined phase preserves
///     legacy/manual message behaviour; scheduled curation uses the two explicit phases so a
///     Functions restart cannot lose the operator's window.
/// </summary>
public enum Phase
{
    FetchAndGenerate = 0,
    Fetch = 1,
    Generate = 2
}

/// <summary>
///     Message queued to trigger asynchronous broadcast orchestration.
///     Contains all information needed to run the orchestration independently.
/// </summary>
public sealed record BroadcastQueueMessage(
    string JobId,
    string MarketId,
    string Location,
    int TargetDurationSeconds,
    double Latitude,
    double Longitude,
    double RadiusKm,
    string? VoiceId = null,
    string? Model = null,
    VoiceSettings? VoiceSettings = null,
    string? TimezoneId = null,
    bool ScriptOnly = false,
    // Full coverage geometry (bbox/corridor). Null for legacy messages and
    // plain circles — the flat lat/lng/radius above remain the fallback.
    CoverageArea? Coverage = null,
    // WS1 content sources. Null = legacy traffic-only.
    BulletinContentSelection? Content = null,
    // Additive WPM boost from the resolved voice profile (see VoiceProfileVersion.PaceBoostWpm).
    // 0 = legacy messages / manual broadcasts with no profile.
    double PaceBoostWpm = 0,
    // Per-station fill priority (BFN-003): which supplementary sources may fill a short read and
    // in what preference order. Null/empty = the BroadcastContext default (weather, fuel, filler).
    IReadOnlyList<FillSource>? FillOrder = null,
    // Station identity for continuity lookups and provenance. Null = ad-hoc/legacy messages.
    string? StationId = null,
    // Resolved delivery style, snapshotted at dispatch (same philosophy as the resolved TTS
    // model, 2026-07-20): what the poller resolved is what airs, even across queue redelivery.
    // Null = neutral/config-default delivery.
    DeliveryStylePolicy? DeliveryStyle = null,
    string? DeliveryStyleId = null,
    int? DeliveryStyleVersion = null,
    // Resolved station playbook overlay, snapshotted at dispatch so a later library edit cannot
    // change a bulletin already scheduled for this queue message. Null = no playbook assigned.
    PlaybookOverlay? Playbook = null,
    // keeps the original property name and meaning, so a message enqueued before the global layer
    // existed still deserialises to exactly the dictionary it was dispatched with. Null = none.
    PronunciationLocator? PronunciationLocator = null,
    PronunciationLocator? GlobalPronunciationLocator = null,
    // Resolved station bookend pair, token-substituted and snapshotted at dispatch (same
    // philosophy as DeliveryStyle above): the exact opener/closer the poller resolved is what airs,
    // even if the library entry is edited between dispatch and execution. Null = station has none.
    ResolvedBookends? Bookends = null,
    /// <summary>Optional curation phase; zero keeps legacy fetch-and-generate behaviour.</summary>
    Phase Phase = Phase.FetchAndGenerate);

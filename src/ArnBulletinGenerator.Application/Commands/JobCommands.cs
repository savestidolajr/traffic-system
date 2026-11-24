using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Application.Commands;

/// <summary>
///     Input DTO for creating an ad-hoc broadcast job: location, target duration, optional
///     voice overrides, and content/coverage selection.
/// </summary>
public sealed record CreateJobCommand(
    string Location,
    double TargetDurationSeconds,
    Option<double> Latitude = default,
    Option<double> Longitude = default,
    Option<double> RadiusKm = default,
    Option<string> TimezoneId = default,
    Option<string> VoiceId = default,
    Option<string> VoiceName = default,
    Option<string> VoiceProvider = default,
    Option<string> VoiceModel = default,
    Option<VoiceSettings> VoiceSettings = default,
    Option<BulletinContentSelection> ContentSelection = default,
    Option<CoverageArea> Coverage = default,
    Option<string> VoiceProfileId = default);

public sealed record RejectJobCommand(
    string JobId,
    RejectionReasonCode ReasonCode,
    string Notes,
    string By);

public sealed record RejectToFallbackCommand(
    string JobId,
    RejectionReasonCode ReasonCode,
    string Notes,
    string By,
    string? ExpectedEtag = null);

/// <summary>
///     The audio source an operator chooses for a slot's air-source override. The wire values
///     are the JSON strings <c>"repeat"</c> / <c>"cart"</c>; anything else is rejected at the
///     trigger boundary with a 400 so a renamed/unknown kind can never be silently treated as
///     one of these.
/// </summary>
public enum AirSourceKind
{
    /// <summary>Re-air an earlier approved same-day bulletin for the same market/station.</summary>
    Repeat,

    /// <summary>Air the station's filler cart (per-voice, else evergreen).</summary>
    Cart
}

/// <summary>
///     Operator-initiated override of what airs in a slot: choose a repeat of an earlier
///     same-day bulletin or the filler cart, apply it to the slot's job, and deliver it to
///     playout in the same call. This is the manual counterpart to the deadline sweep's
///     fallback — the operator acts instead of the clock, so the choice is attributed to
///     <see cref="RequestedBy"/> in the audit trail, never to <c>system</c>.
/// </summary>
public sealed record SetAirSourceCommand(
    string JobId,
    AirSourceKind Kind,
    string? SourceJobId,
    string RequestedBy,
    string? ExpectedEtag);

public sealed record RegenerateJobCommand(
    string JobId,
    Option<double> TargetDurationSec = default,
    Option<string> AdditionalContext = default,
    Option<string> RejectionReason = default,
    Option<string> RejectionDescription = default,
    Option<string> EditedScript = default,
    Option<IReadOnlyList<string>> SelectedCorridors = default,
    Option<string> DeliveryIntent = default,
    Option<string> TonedScript = default,
    Option<IReadOnlyList<string>> SelectedGoodRuns = default,
    Option<IReadOnlyList<string>> ExcludedIncidentKeys = default,
    bool RevoiceOnly = false,
    bool RefreshPronunciation = false,
    bool RefreshDelivery = false,
    Option<IReadOnlyList<RegenerateFeedbackDto>> Feedback = default,
    Option<string> VoiceProfileId = default);

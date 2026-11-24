using System.Text.Json.Serialization;
using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>Outcome of a playout-delivery attempt — distinguishes a real delivery from a
///     deliberate skip (delivery disabled), so callers log honestly instead of always
///     claiming "delivered to playout".</summary>
public enum FileDeliveryStatus
{
    /// <summary>The audio was written to the playout drop share.</summary>
    Delivered = 0,

    /// <summary>Delivery was skipped because it is disabled for the whole environment
    ///     (<c>Zetta:Enabled</c>); nothing was sent.</summary>
    SkippedDisabled = 1,

    /// <summary>
    ///     Delivery was skipped because THIS STATION has playout switched off
    ///     (<c>MarketSchedule.PlayoutEnabled</c>) while the environment's playout is on; nothing
    ///     was sent. Distinct from <see cref="SkippedDisabled" /> because the two say different
    ///     things to an operator: one is "this environment never delivers", the other is "this
    ///     station is deliberately held back here, and another environment may be airing it".
    /// </summary>
    SkippedStationDisabled = 2
}

/// <summary>
///     Whether a market's delivery file is still sitting in the playout drop folder. Playout
///     COLLECTS the file from the folder, so a file still present means it has not been picked
///     up yet — and a same-name overwrite swaps cleanly for the next slot. A file that is
///     absent has already aired (or was never written), and writing again would queue a new
///     file that could land in the wrong break. The property names are the frozen wire contract
///     the on-air board reads (<c>fileName</c>/<c>enabled</c>/<c>awaitingPickup</c>/<c>writtenAtUtc</c>).
/// </summary>
public sealed record PlayoutTrayStatus(
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("awaitingPickup")] bool AwaitingPickup,
    [property: JsonPropertyName("writtenAtUtc")] DateTimeOffset? WrittenAtUtc);

/// <summary>
///     Delivers broadcast audio to the playout drop share the station's playout system
///     autoloads from (e.g. Zetta Autoload).
/// </summary>
public interface IFileDeliveryService
{
    /// <param name="slotDeadlineUtc">
    ///     The moment the SLOT this audio belongs to stops being airable. Delivery refuses when it
    ///     has passed, independently of whatever upstream logic decided to publish — on 2026-08-11 a
    ///     restart sweep walked 15 already-expired slots and wrote their stale audio onto the live
    ///     playout share. The orchestrator and watchdog guards added for that (see decisions.md,
    ///     2026-08-11/12) are the first line; this is the last one, immediately before bytes reach
    ///     the share, and it is deliberately in a different layer so one bug cannot defeat both.
    ///     Null means "no deadline known" and delivery proceeds with a warning, matching how
    ///     <paramref name="expectedSha256" /> treats a missing integrity seal.
    /// </param>
    Task<Result<FileDeliveryStatus>> DeliverAsync(string audioFileName, string marketId, string? expectedSha256,
        DateTimeOffset? slotDeadlineUtc = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Reports whether this market's drop file is still in the playout folder awaiting pickup
    ///     (see <see cref="PlayoutTrayStatus" />). The on-air board needs this to decide whether
    ///     swapping the next slot's audio under the same file name is safe; the present/absent
    ///     semantics come from <c>IDeliveryFileWriter.GetPendingWriteTimeAsync</c>.
    /// </summary>
    Task<Result<PlayoutTrayStatus>> GetTrayStatusAsync(
        string marketId, CancellationToken cancellationToken = default);
}

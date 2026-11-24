namespace ArnBulletinGenerator.Domain.Models;

/// <summary>
/// A per-market, admin-authored filler statement used to fill remaining slot time when
/// traffic incidents alone can't reach the target bulletin duration. Eligibility and
/// selection order are derived automatically from the statement's own word count
/// (<see cref="Services.FillerStatementSelector"/>) — admins author the text only, never a
/// duration estimate. The generation pipeline excludes statements already used by a
/// station earlier the same broadcast day.
/// <para>
///     <see cref="Availability"/> optionally narrows eligibility to a station-local window
///     (date range, days of week, time of day). <c>null</c> = always eligible — the shape of
///     every record persisted before windows existed, so existing stored data is unaffected
///     (the statement list is persisted as JSON, and a missing property deserialises to null).
///     Window evaluation happens at the load boundaries (MarketContentFetcher,
///     FallbackAudioPadder), never inside <see cref="Services.FillerStatementSelector"/>,
///     which stays pure and clock-free.
/// </para>
/// </summary>
public sealed record FillerStatement(
    string Id,
    string Text,
    bool Enabled = true,
    FillerAvailability? Availability = null);

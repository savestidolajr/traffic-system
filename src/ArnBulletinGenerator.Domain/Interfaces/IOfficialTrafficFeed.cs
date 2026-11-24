using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Interfaces;

/// <summary>
///     An authoritative road-authority feed used to corroborate HERE's picture of a coverage area.
///     Implementations fetch their whole jurisdiction and return only events inside the area; a feed
///     whose jurisdiction does not overlap the area naturally returns an empty list, so callers ask
///     every registered feed without routing logic.
/// </summary>
public interface IOfficialTrafficFeed
{
    /// <summary>Stable feed identifier, used as the metric dimension ("livetraffic-nsw", "qldtraffic").</summary>
    string SourceId { get; }
    /// <summary>What this source can supply, and which of its fields are free prose.</summary>
    SourceCapabilities Capabilities { get; }

    /// <summary>Events inside <paramref name="area" />. Failure is the feed's outage, never a guess.</summary>
    Task<Result<IReadOnlyList<OfficialTrafficEvent>>> GetEventsAsync(
        CoverageArea area, CancellationToken cancellationToken = default);
}

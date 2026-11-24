using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;

namespace ArnBulletinGenerator.Application.Mapping;

/// <summary>
///     Extension methods for mapping traffic incidents between domain representations.
/// </summary>
public static class IncidentMappingExtensions
{
    public static JobIncident ToJobIncident(this TrafficIncident incident)
    {
        return new JobIncident(
            incident.Id,
            incident.Severity,
            $"{incident.Type} on {incident.Location}",
            incident.Location,
            incident.FunctionalClass,
            incident.LengthMeters,
            Option<string>.Some(incident.Description),
            incident.Latitude,
            incident.Longitude,
            incident.Shape,
            Direction: incident.Direction,
            LaneDetail: incident.Summary,
            Comment: incident.Comment,
            RoadClosed: incident.RoadClosed,
            EntryTimeUtc: incident.EntryTimeUtc,
            // Why the writer saw this record reduced or not at all, straight from the boundary
            // decision IncidentEligibility stamped when the fetch was vetted.
            WithheldReason: incident.WithheldReason.HasValue ? incident.WithheldReason.Value : null,
            Type: incident.Type,
            StartTimeUtc: incident.StartTimeUtc,
            EndTimeUtc: incident.EndTimeUtc,
            Sources: incident.Sources,
            LanesClosed: incident.Fact?.LanesClosed.Match(v => (int?)v, () => null),
            LanesTotal: incident.Fact?.LanesTotal.Match(v => (int?)v, () => null),
            OtherAssets: incident.Fact?.OtherAssets is { Count: > 0 } assets ? assets : null,
            CanonicalClass: incident.Fact?.CanonicalClass.Match(v => (string?)v.ToString(), () => null),
            ProseFields: incident.ProseFields,
            SourceRecordIdentities: incident.SourceRecordIdentities);
    }

    /// <summary>
    ///     Reverse of <see cref="ToJobIncident" /> — reconstructs the writer-facing
    ///     <see cref="TrafficIncident" /> from a persisted <see cref="JobIncident" />, for
    ///     regression-fixture replay of a job whose <c>GeneratorInputJson</c> snapshot was never
    ///     captured (frequently null — the best-effort capture call is silently swallowed on
    ///     failure). <see cref="JobIncident.Headline" /> is <c>"{Type} on {Location}"</c>
    ///     (see <see cref="ToJobIncident" />); this strips the known <c>" on {Location}"</c> suffix
    ///     to recover <c>Type</c>, falling back to the whole headline if the suffix isn't present
    ///     (defensive — should not happen for anything produced by <see cref="ToJobIncident" />).
    /// </summary>
    public static TrafficIncident ToTrafficIncident(this JobIncident incident)
    {
        string suffix = $" on {incident.Location}";
        string type = incident.Headline.EndsWith(suffix, StringComparison.Ordinal)
            ? incident.Headline[..^suffix.Length]
            : incident.Headline;

        return new TrafficIncident(
            Id: incident.Key,
            Description: incident.Detail.GetValueOr(string.Empty),
            Severity: incident.Priority,
            Location: incident.Location,
            Type: type,
            LengthMeters: incident.LengthMeters,
            FunctionalClass: incident.FunctionalClass,
            Latitude: incident.Latitude,
            Longitude: incident.Longitude,
            Direction: incident.Direction,
            Summary: incident.LaneDetail,
            Comment: incident.Comment,
            Shape: incident.Shape,
            RoadClosed: incident.RoadClosed,
            EntryTimeUtc: incident.EntryTimeUtc,
            OriginalId: incident.SourceRecordIdentities?
                .FirstOrDefault(identity => string.Equals(
                    identity.SourceId, EvidenceSources.Here, StringComparison.OrdinalIgnoreCase))
                ?.OriginalRecordId ?? string.Empty,
            StartTimeUtc: incident.StartTimeUtc,
            EndTimeUtc: incident.EndTimeUtc,
            Sources: incident.Sources,
            ProseFields: incident.ProseFields,
            SourceRecordIdentities: incident.SourceRecordIdentities);
    }

    /// <summary>
    ///     Reverse of <see cref="ToJobCorridor" /> — reconstructs the writer-facing
    ///     <see cref="TrafficCorridor" /> from a persisted <see cref="JobCorridor" /> plus the
    ///     job's full incident pool (<paramref name="incidentsByKey" />, keyed by
    ///     <see cref="JobIncident.Key" />), used to resolve <see cref="JobCorridor.IncidentKeys" />
    ///     back into full <see cref="TrafficIncident" /> objects. <c>TotalLengthMeters</c> and the
    ///     two drive-time fields round-trip through <see cref="JobCorridor" />'s already-rounded
    ///     km/int representation (a few metres/seconds of imprecision — invisible in the rendered
    ///     prompt, which itself rounds to "about N kays"/"about N minutes"). When the corridor's
    ///     combined <c>DelayMinutes</c> came from live-flow speed (no routed drive time on either
    ///     side), it is carried back as <c>FlowDelayMinutes</c> rather than a fabricated drive time.
    /// </summary>
    public static TrafficCorridor ToTrafficCorridor(
        this JobCorridor corridor, IReadOnlyDictionary<string, JobIncident> incidentsByKey)
    {
        var incidents = (corridor.IncidentKeys ?? [])
            .Select(key => incidentsByKey.TryGetValue(key, out var inc) ? inc.ToTrafficIncident() : null)
            .Where(inc => inc is not null)
            .Select(inc => inc!)
            .ToList();

        bool hasRoutedDriveTime = corridor.DriveTimeMinutes is not null || corridor.NormalDriveTimeMinutes is not null;

        return new TrafficCorridor(
            RoadName: corridor.RoadName,
            Severity: corridor.Severity,
            FunctionalClass: corridor.FunctionalClass,
            TotalLengthMeters: corridor.TotalLengthKm * 1000.0,
            ImpactScore: corridor.ImpactScore,
            PrimaryType: corridor.PrimaryType,
            Description: corridor.Description,
            Incidents: incidents,
            Suburb: Option.OfNullable(corridor.Suburb),
            Direction: Option.OfNullable(corridor.Direction),
            DriveTimeMinutes: corridor.DriveTimeMinutes is { } dt ? Option<int>.Some((int)Math.Round(dt)) : Option<int>.None,
            NormalDriveTimeMinutes: corridor.NormalDriveTimeMinutes is { } ndt ? Option<int>.Some((int)Math.Round(ndt)) : Option<int>.None,
            FlowDelayMinutes: !hasRoutedDriveTime && corridor.DelayMinutes is { } dm
                ? Option<int>.Some((int)Math.Round(dm))
                : Option<int>.None,
            Sources: corridor.Sources is { Count: > 0 } ? corridor.Sources : EvidenceSources.HereOnly,
            OfficialHeadline: Option.OfNullable(corridor.OfficialHeadline),
            OfficialCrossStreet: Option.OfNullable(corridor.OfficialCrossStreet),
            OfficialEvidences: corridor.OfficialEvidences)
        {
            CorroboratingSourceCount = corridor.CorroboratingSourceCount
        };
    }

    public static JobCorridor ToJobCorridor(this TrafficCorridor corridor, int rank, bool isSelected)
    {
        return new JobCorridor(
            corridor.RoadName,
            corridor.Severity,
            corridor.ImpactScore,
            corridor.PrimaryType,
            corridor.Description,
            corridor.Incidents.Count,
            Math.Round(corridor.TotalLengthMeters / 1000.0, 1),
            rank,
            isSelected,
            corridor.FunctionalClass,
            corridor.Direction.HasValue ? corridor.Direction.Value : null,
            corridor.Suburb.HasValue ? corridor.Suburb.Value : null,
            corridor.DriveTimeMinutes.HasValue ? corridor.DriveTimeMinutes.Value : null,
            corridor.NormalDriveTimeMinutes.HasValue ? corridor.NormalDriveTimeMinutes.Value : null,
            corridor.DelayMinutes.HasValue ? corridor.DelayMinutes.Value : null,
            // The incident↔corridor link: without it the UI cannot tell which
            // incidents actually back the bulletin's selected corridors.
            corridor.Incidents.Select(i => i.Id).ToList(),
            corridor.PriorityBoosted,
            BaseScore: corridor.BaseScore,
            BoostAmount: corridor.BoostAmount,
            Sources: EvidenceSources.Of(corridor.Sources).ToList(),
            OfficialHeadline: corridor.OfficialHeadline.HasValue ? corridor.OfficialHeadline.Value : null,
            OfficialCrossStreet: corridor.OfficialCrossStreet.HasValue ? corridor.OfficialCrossStreet.Value : null,
            OfficialEvidences: corridor.OfficialEvidences,
            CorroboratingSourceCount: corridor.CorroborationCount);
    }
    public static TrafficCorridorDto ToCorridorDto(this TrafficCorridor corridor, int rank = 0, bool isSelected = false)
    {
        return new TrafficCorridorDto(
            corridor.RoadName,
            corridor.Severity,
            corridor.ImpactScore,
            corridor.PrimaryType,
            corridor.Description,
            corridor.Incidents.Count,
            Math.Round(corridor.TotalLengthMeters / 1000.0, 1),
            rank,
            isSelected,
            corridor.FunctionalClass,
            corridor.Direction.HasValue ? corridor.Direction.Value : null,
            corridor.Suburb.HasValue ? corridor.Suburb.Value : null,
            corridor.DriveTimeMinutes.HasValue ? corridor.DriveTimeMinutes.Value : null,
            corridor.NormalDriveTimeMinutes.HasValue ? corridor.NormalDriveTimeMinutes.Value : null,
            corridor.DelayMinutes.HasValue ? corridor.DelayMinutes.Value : null,
            corridor.Latitude.HasValue ? corridor.Latitude.Value : null,
            corridor.Longitude.HasValue ? corridor.Longitude.Value : null,
            corridor.Incidents.Select(i => i.Id).ToList(),
            corridor.PriorityBoosted,
            BaseScore: corridor.BaseScore,
            BoostAmount: corridor.BoostAmount,
            Magnitude: CorridorMagnitude.Classify(corridor).ToString(),
            Sources: EvidenceSources.Of(corridor.Sources),
            CorroborationCount: corridor.CorroborationCount,
            OfficialHeadline: corridor.OfficialHeadline.HasValue ? corridor.OfficialHeadline.Value : null,
            OfficialCrossStreet: corridor.OfficialCrossStreet.HasValue ? corridor.OfficialCrossStreet.Value : null,
            OfficialEvidences: corridor.OfficialEvidences);
    }
    public static TrafficCorridorDto ToCorridorDto(this JobCorridor corridor)
    {
        return new TrafficCorridorDto(
            corridor.RoadName,
            corridor.Severity,
            corridor.ImpactScore,
            corridor.PrimaryType,
            corridor.Description,
            corridor.IncidentCount,
            corridor.TotalLengthKm,
            corridor.Rank,
            corridor.IsSelected,
            corridor.FunctionalClass,
            corridor.Direction,
            corridor.Suburb,
            (int?)corridor.DriveTimeMinutes,
            (int?)corridor.NormalDriveTimeMinutes,
            (int?)corridor.DelayMinutes,
            null,
            null,
            corridor.IncidentKeys,
            corridor.PriorityBoosted,
            BaseScore: corridor.BaseScore,
            BoostAmount: corridor.BoostAmount,
            Magnitude: CorridorMagnitude.Classify(
                corridor.PrimaryType, corridor.FunctionalClass,
                (int?)corridor.DelayMinutes, corridor.TotalLengthKm * 1000.0).ToString(),
            Sources: corridor.Sources,
            CorroborationCount: corridor.CorroboratingSourceCount ?? corridor.Sources?.Count ?? 1,
            OfficialHeadline: corridor.OfficialHeadline,
            OfficialCrossStreet: corridor.OfficialCrossStreet,
            OfficialEvidences: corridor.OfficialEvidences);
    }
    public static FlowRoadDto ToDto(this FlowRoad f)
    {
        return new FlowRoadDto(
            f.RoadName,
            f.JamFactor,
            Math.Round(f.LengthMeters / 1000.0, 1),
            f.IsWeighted,
            false,
            f.Score,
            f.PriorityBoosted,
            BaseScore: f.BaseScore,
            BoostAmount: f.BoostAmount,
            FunctionalClass: f.FunctionalClass);
    }
    public static FlowRoadDto ToDto(this JobFlowRoad f)
    {
        return new FlowRoadDto(
            f.RoadName,
            f.JamFactor,
            Math.Round(f.LengthMeters / 1000.0, 1),
            f.IsWeighted,
            f.IsSelected,
            f.Score,
            f.PriorityBoosted,
            BaseScore: f.BaseScore,
            BoostAmount: f.BoostAmount,
            FunctionalClass: f.FunctionalClass);
    }

    /// <summary>
    ///     Projects a reconciled incident delta onto the job's persisted change summary.
    ///     <para>
    ///         Restored 2026-08-20: a refactor of this file dropped the method while its two call
    ///         sites in <c>TrafficEvidenceBuilder</c> (the raw and narrative-facing summaries)
    ///         remained, breaking the Application build. Both summaries are deliberately separate
    ///         fields, so this projection is shared rather than inlined at either call site.
    ///     </para>
    /// </summary>
    public static Option<IncidentChangeSummary> ToChangeSummary(
        this IncidentDeltaContext delta,
        TimeProvider timeProvider)
    {
        string summaryText = IncidentChangeFormatter.BuildSummary(Option<IncidentDeltaContext>.Some(delta));

        return Option<IncidentChangeSummary>.Some(new IncidentChangeSummary(
            timeProvider.GetUtcNow().UtcDateTime,
            delta.NewIncidents.Count,
            delta.UpdatedIncidents.Count,
            delta.ClearedIncidents.Count,
            string.IsNullOrWhiteSpace(summaryText) ? Option<string>.None : Option<string>.Some(summaryText)));
    }
}

using ArnBulletinGenerator.Domain.Models;

namespace ArnBulletinGenerator.Domain.Services;

/// <summary>
///     The editorial role an item can safely play in a bulletin, based only on structured evidence.
///     This is deliberately separate from <see cref="CorridorTiering"/>: tiering decides which
///     corridors survive caps and major-headline suppression; this classifier describes the role of
///     each corridor that tiering has selected.
/// </summary>
public enum Voiceability
{
    Leadable,
    Standalone,
    GroupedOnly,
    DoNotVoice
}

/// <summary>
///     Classifies traffic items against the voiceability floor. No prose is parsed and no inferred
///     detail is promoted: a detail counts only when its corresponding structured field is present.
/// </summary>
public static class VoiceabilityClassifier
{
    /// <summary>
    ///     Classifies an aggregated corridor. Leadable requires road, condition, severity and one
    ///     usable structured detail. Standalone requires road, condition and one usable detail.
    ///     A road and condition without detail may only be grouped; anything below that floor must not
    ///     be voiced.
    /// </summary>
    public static Voiceability Classify(TrafficCorridor corridor)
    {
        bool hasRoad = HasText(corridor.RoadName);
        bool hasCondition = HasText(corridor.PrimaryType);
        if (!hasRoad || !hasCondition)
            return Voiceability.DoNotVoice;

        bool hasDetail = HasDetail(corridor);
        if (corridor.Severity > 0 && hasDetail)
            return Voiceability.Leadable;
        if (hasDetail)
            return Voiceability.Standalone;
        return Voiceability.GroupedOnly;
    }

    /// <summary>Classifies one incident using its own structured fact when one is available.</summary>
    public static Voiceability Classify(TrafficIncident incident)
    {
        bool hasRoad = HasText(incident.Fact?.RoadName) || HasText(incident.Location);
        bool hasCondition = HasText(incident.Type);
        if (!hasRoad || !hasCondition)
            return Voiceability.DoNotVoice;

        bool hasDetail = HasDetail(incident);
        if (incident.Severity > 0 && hasDetail)
            return Voiceability.Leadable;
        if (hasDetail)
            return Voiceability.Standalone;
        return Voiceability.GroupedOnly;
    }

    private static bool HasDetail(TrafficCorridor corridor) =>
        HasText(corridor.Direction) ||
        HasText(corridor.Suburb) ||
        HasText(corridor.Extent) ||
        corridor.VoicedExtentMeters > 0 ||
        (corridor.HotSpotMeters.HasValue && corridor.HotSpotMeters.Value > 0) ||
        corridor.Incidents.Any(HasDetail);

    private static bool HasDetail(TrafficIncident incident)
    {
        if (HasText(incident.Direction) ||
            HasText(incident.Suburb) ||
            HasText(incident.Summary) ||
            HasText(incident.Comment) ||
            HasText(incident.JunctionTraversability) ||
            incident.LengthMeters > 0 ||
            incident.RoadClosed)
            return true;

        IncidentFact? fact = incident.Fact;
        if (fact is null)
            return false;

        return HasText(fact.Anchor) ||
               HasText(fact.SecondaryAnchor) ||
               HasText(fact.RampTarget) ||
               HasText(fact.Direction) ||
               HasText(fact.Suburb) ||
               HasText(fact.LaneDetail) ||
               HasText(fact.SourceDetail) ||
               fact.RoadClosed ||
               (fact.LanesClosed.HasValue && fact.LanesClosed.Value > 0);
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool HasText(Option<string> value) =>
        value.Match(HasText, static () => false);
}

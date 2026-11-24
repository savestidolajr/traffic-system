using System.Text.Json.Serialization;

namespace ArnBulletinGenerator.Domain.Models;


/// <summary>A ranked congestion corridor (grouped road segment) captured on a Job, recording its rank, impact score, and whether it was selected for the script.</summary>
public sealed record JobCorridor(
    string RoadName,
    int Severity,
    double ImpactScore,
    string PrimaryType,
    string Description,
    int IncidentCount,
    double TotalLengthKm,
    int Rank,
    bool IsSelected,
    int FunctionalClass = 5,
    string? Direction = null,
    string? Suburb = null,
    double? DriveTimeMinutes = null,
    double? NormalDriveTimeMinutes = null,
    double? DelayMinutes = null,
    IReadOnlyList<string>? IncidentKeys = null,
    bool PriorityBoosted = false,
    double? BaseScore = null,
    double? BoostAmount = null,
    /// <summary>Provenance feeds ("here", "livetraffic-nsw", …). Every contributing feed is retained for operator attribution, including planned-work records.</summary>
    IReadOnlyList<string>? Sources = null,
    string? OfficialHeadline = null,
    string? OfficialCrossStreet = null,
    /// <summary>The matched authority records + match basis, persisted for the evidence modal.</summary>
    IReadOnlyList<OfficialEvidence>? OfficialEvidences = null,
    /// <summary>
    ///     Number of distinct sources that observed the current condition. Null on legacy rows;
    ///     readers fall back to the historical Sources-derived value.
    /// </summary>
    int? CorroboratingSourceCount = null)
{
    /// <summary>
    ///     Compatibility-only receipt populated when reading pre-array corridor JSON. It is omitted
    ///     when writing current jobs; the persistence mapper folds it into <see cref="OfficialEvidences" />
    ///     so old evidence remains visible after a reload.
    /// </summary>
    [JsonPropertyName("OfficialEvidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OfficialEvidence? LegacyOfficialEvidence { get; init; }
}

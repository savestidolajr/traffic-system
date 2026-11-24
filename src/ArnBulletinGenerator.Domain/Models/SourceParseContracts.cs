namespace ArnBulletinGenerator.Domain.Models;

/// <summary>Canonical field names used across every source's coverage report.</summary>
public static class ParsedField
{
    public const string RoadName = "roadName";
    public const string Category = "category";
    public const string SubCategory = "subCategory";
    public const string Direction = "direction";
    public const string LanesClosed = "lanesClosed";
    public const string LanesTotal = "lanesTotal";
    public const string SpeedLimitKph = "speedLimitKph";
    public const string StartTime = "startTime";
    public const string EndTime = "endTime";
    public const string AuthorityPriority = "authorityPriority";
    public const string OtherAssets = "otherAssets";

    public static IReadOnlyList<string> All { get; } =
    [
        RoadName, Category, SubCategory, Direction, LanesClosed, LanesTotal,
        SpeedLimitKph, StartTime, EndTime, AuthorityPriority, OtherAssets
    ];
}

/// <summary>
///     What a source CAN supply, declared by its adapter rather than discovered by the caller.
///     <paramref name="ProseFields" /> names fields eligible for free-text extraction. The live
///     builder currently records only structured values and absent values, so this declaration
///     does not by itself produce <see cref="FieldOrigin.Prose" /> observations.
/// </summary>
public sealed record SourceCapabilities(
    string SourceId,
    string SchemaVersion,
    IReadOnlySet<string> StructuredFields,
    IReadOnlySet<string> ProseFields);

/// <summary>
///     Where a field's value actually came from, per record. The persisted contract supports
///     <see cref="Prose" />, although the live builder currently emits only <see cref="Structured" />
///     and <see cref="Absent" />.
/// </summary>
public enum FieldOrigin { Absent = 0, Structured = 1, Prose = 2 }

public sealed record FieldCoverage(string Field, int Structured, int Prose, int Absent)
{
    public int Total => Structured + Prose + Absent;
    public double StructuredRate => Total == 0 ? 0 : (double)Structured / Total;
}

/// <summary>
///     One fetch's parse outcome for one source. The persisted contract can measure LLM
///     dependence as adapters learn permutations, but the live builder currently records only
///     structured-versus-absent values. Prose counts remain available for explicitly supplied
///     origin maps.
/// </summary>
public sealed record SourceParseReport(
    string SourceId,
    string SchemaVersion,
    int Records,
    IReadOnlyList<FieldCoverage> Fields,
    IReadOnlyList<string> UnknownVocabulary)
{
    /// <summary>
    ///     Folds one origin map per parsed record into deterministic coverage counts. Missing map
    ///     entries are absent values, which keeps the denominator equal to the fetched record count.
    /// </summary>
    public static SourceParseReport FromOrigins(
        SourceCapabilities capabilities,
        IReadOnlyList<IReadOnlyDictionary<string, FieldOrigin>> origins,
        IReadOnlyList<string>? unknownVocabulary = null)
    {
        var fields = capabilities.StructuredFields
            .Concat(capabilities.ProseFields)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .Select(field =>
            {
                int structured = 0;
                int prose = 0;
                foreach (var record in origins)
                {
                    if (!record.TryGetValue(field, out var origin))
                        continue;

                    switch (origin)
                    {
                        case FieldOrigin.Structured:
                            structured++;
                            break;
                        case FieldOrigin.Prose:
                            prose++;
                            break;
                    }
                }

                return new FieldCoverage(field, structured, prose, origins.Count - structured - prose);
            })
            .ToArray();

        return new SourceParseReport(
            capabilities.SourceId,
            capabilities.SchemaVersion,
            origins.Count,
            fields,
            unknownVocabulary ?? []);
    }
}

using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class SourceParseCoverageTests
{
    [Fact]
    public void Calculate_IdentifiesFallingProseShareBetweenWindows()
    {
        DateTimeOffset from = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset to = from.AddDays(10);
        var reports = new StoredParseReport[]
        {
            Stored("here", from.AddDays(1), new FieldCoverage(ParsedField.LanesClosed, 1, 3, 0)),
            Stored("here", from.AddDays(9), new FieldCoverage(ParsedField.LanesClosed, 3, 1, 0))
        };

        var item = SourceParseCoverageHttpTrigger.CoverageCalculator.Calculate(reports, from, to)
            .Single(x => x.Field == ParsedField.LanesClosed);

        item.FirstWindowProseShare.ShouldBe(0.75);
        item.LastWindowProseShare.ShouldBe(0.25);
        item.ProseShareDelta.ShouldBe(-0.5);
        item.ProseFalling.ShouldBeTrue();
    }

    [Fact]
    public void UnknownVocabulary_DeduplicatesAcrossFetchesAndKeepsFirstLastAndCount()
    {
        DateTimeOffset first = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var reports = new StoredParseReport[]
        {
            Stored("nsw", first, new FieldCoverage(ParsedField.Category, 1, 0, 0), ["Warringah Freeway Upgrade"]),
            Stored("nsw", first.AddDays(2), new FieldCoverage(ParsedField.Category, 1, 0, 0), ["Warringah Freeway Upgrade", "New value"]),
            Stored("nsw", first.AddDays(4), new FieldCoverage(ParsedField.Category, 1, 0, 0), ["warringah freeway upgrade"])
        };

        var item = SourceParseCoverageHttpTrigger.CoverageCalculator.UnknownVocabulary(reports)
            .Single(x => x.Value.Equals("Warringah Freeway Upgrade", StringComparison.OrdinalIgnoreCase));

        item.Count.ShouldBe(3);
        item.FirstSeenAt.ShouldBe(first);
        item.LastSeenAt.ShouldBe(first.AddDays(4));
        item.ToDo.ShouldContain("IncidentClassCrosswalk");
    }

    private static StoredParseReport Stored(
        string source,
        DateTimeOffset at,
        FieldCoverage field,
        IReadOnlyList<string>? unknown = null) =>
        new("sydney", at, new SourceParseReport(source, "v1", field.Total, [field], unknown ?? []));
}

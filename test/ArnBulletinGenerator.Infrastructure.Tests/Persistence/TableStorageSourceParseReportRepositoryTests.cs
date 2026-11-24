using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageSourceParseReportRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class TableStorageSourceParseReportRepositoryTests
{
    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly TableStorageSourceParseReportRepository _sut;

    public TableStorageSourceParseReportRepositoryTests()
    {
        _sut = new TableStorageSourceParseReportRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            Substitute.For<ILogger<TableStorageSourceParseReportRepository>>());
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    [Fact]
    public async Task SaveThenList_RoundTripsReportAndUsesMarketPartition()
    {
        SourceParseReport report = Report("here", structured: 3, prose: 1, unknown: ["new-value"]);
        SourceParseReportEntity? saved = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<SourceParseReportEntity>(entity => saved = entity),
                TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));
        EmptyQuery();

        DateTimeOffset at = new(2026, 8, 19, 1, 2, 3, TimeSpan.Zero);
        var write = await _sut.SaveAsync("Sydney", at, [report]);

        write.IsSuccess.ShouldBeTrue(write.Error);
        saved.ShouldNotBeNull();
        saved.PartitionKey.ShouldBe("sydney");
        saved.AtUtc.ShouldBe(at);

        SeedQuery(saved);
        var read = await _sut.ListAsync("SYDNEY", at.AddMinutes(-1));

        read.IsSuccess.ShouldBeTrue(read.Error);
        read.Value.Count.ShouldBe(1);
        read.Value[0].Report.SourceId.ShouldBe(report.SourceId);
        read.Value[0].Report.SchemaVersion.ShouldBe(report.SchemaVersion);
        read.Value[0].Report.Records.ShouldBe(report.Records);
        read.Value[0].Report.Fields.ShouldBe(report.Fields);
        read.Value[0].Report.UnknownVocabulary.ShouldBe(report.UnknownVocabulary);

    }

    [Fact]
    public async Task ListAsync_RangeQueryExcludesRowsBeforeWindow()
    {
        DateTimeOffset since = new(2026, 8, 19, 2, 0, 0, TimeSpan.Zero);
        var before = Entity("before", since.AddTicks(-1));
        var inside = Entity("inside", since.AddTicks(1));
        string? filter = null;
        _tableClient.QueryAsync<SourceParseReportEntity>(
                Arg.Do<string?>(value => filter = value), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable([before, inside]));

        var result = await _sut.ListAsync("sydney", since);

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.Select(x => x.Report.SourceId).ShouldBe(["inside"]);
        filter.ShouldNotBeNull();
        filter!.ShouldContain("PartitionKey eq 'sydney'");
        filter.ShouldContain("RowKey lt '");
    }

    private void EmptyQuery() => _tableClient.QueryAsync<SourceParseReportEntity>(
            Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
        .Returns(TableClientTestHelper.EmptyPageable<SourceParseReportEntity>());

    private void SeedQuery(SourceParseReportEntity entity) => _tableClient.QueryAsync<SourceParseReportEntity>(
            Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
        .Returns(TableClientTestHelper.ToAsyncPageable([entity]));

    private static SourceParseReportEntity Entity(string source, DateTimeOffset at) => new()
    {
        PartitionKey = "sydney",
        RowKey = $"{at.UtcDateTime.Ticks:D19}-abc",
        MarketId = "sydney",
        AtUtc = at,
        SourceId = source,
        SchemaVersion = "v1",
        Records = 1,
        FieldsJson = "[{\"Field\":\"roadName\",\"Structured\":1,\"Prose\":0,\"Absent\":0}]",
        UnknownVocabularyJson = "[]"
    };

    private static SourceParseReport Report(string source, int structured, int prose, IReadOnlyList<string> unknown) =>
        new(source, "v1", structured + prose,
            [new FieldCoverage(ParsedField.RoadName, structured, prose, 0)], unknown);
}

using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageIncidentPriorityRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class TableStorageIncidentPriorityRepositoryTests
{
    private readonly TableClient _prioritiesClient = Substitute.For<TableClient>();
    private readonly TableClient _landscapeClient = Substitute.For<TableClient>();
    private readonly TableClient _topClient = Substitute.For<TableClient>();

    private TableStorageIncidentPriorityRepository CreateSut(IncidentLandscapeOptions? settings = null)
    {
        var sut = new TableStorageIncidentPriorityRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            Substitute.For<ILogger<TableStorageIncidentPriorityRepository>>(),
            Options.Create(settings ?? new IncidentLandscapeOptions()),
            _landscapeClient,
            _topClient);
        TableClientTestHelper.InjectMockTableClient(sut, _prioritiesClient);
        return sut;
    }

    [Fact]
    public async Task CompletePages_ReconstructLegacyOrderAndTotalCount()
    {
        var entries = new[]
        {
            Entry("a", 7),
            Entry("b", 4),
            Entry("c", 2)
        };
        _landscapeClient.QueryAsync<TableEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable<TableEntity>(entries.Select(ToEntity)));


        var sut = CreateSut();
        var first = await sut.GetLandscapePageAsync("sydney", 2, complete: true);
        first.IsSuccess.ShouldBeTrue(first.Error);
        first.Value.Entries.Select(entry => entry.Provider).ShouldBe(["a", "b"]);
        first.Value.TotalCount.ShouldBe(13);
        first.Value.ContinuationToken.ShouldNotBeNull();

        var second = await sut.GetLandscapePageAsync(
            "sydney", 2, first.Value.ContinuationToken, complete: true);
        second.IsSuccess.ShouldBeTrue(second.Error);
        second.Value.Entries.Select(entry => entry.Provider).ShouldBe(["c"]);
        second.Value.ContinuationToken.ShouldBeNull();

        first.Value.Entries.Concat(second.Value.Entries)
            .Select(entry => entry.Provider)
            .ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public async Task RecordOccurrences_UsesBoundedPointReadsInsteadOfPartitionAggregation()
    {
        var existing = ToEntity(Entry("here", 3));
        _landscapeClient.GetEntityAsync<TableEntity>(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.MockResponse(existing)));
        _landscapeClient.SubmitTransactionAsync(
                Arg.Any<IReadOnlyList<TableTransactionAction>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue<IReadOnlyList<Response>>(
                [], TableClientTestHelper.StubResponse())));

        var sut = CreateSut(new IncidentLandscapeOptions { WriteReadModelEnabled = false });
        var result = await sut.RecordOccurrencesAsync("sydney", [Entry("here", 1)]);

        result.IsSuccess.ShouldBeTrue(result.Error);
        _landscapeClient.DidNotReceive().QueryAsync<TableEntity>(
            Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
        await _landscapeClient.Received(1).GetEntityAsync<TableEntity>(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    private static IncidentLandscapeEntry Entry(string provider, int count) => new(
        provider,
        "Closure",
        "roadClosure",
        $"{provider} detail",
        "closed",
        null,
        count,
        new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc));

    private static TableEntity ToEntity(IncidentLandscapeEntry entry) => new("sydney", entry.Provider)
    {
        ["Count"] = entry.Count,
        ["FirstSeenUtc"] = entry.FirstSeenUtc,
        ["LastSeenUtc"] = entry.LastSeenUtc,
        ["DataJson"] = JsonSerializer.Serialize(entry)
    };
}

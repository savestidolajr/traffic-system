using System.Text.Json;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public sealed class TableStorageBookendRepositoryTests
{
    private const string PartitionKey = "Bookend";

    private readonly ILogger<TableStorageBookendRepository> _logger =
        Substitute.For<ILogger<TableStorageBookendRepository>>();
    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly TableStorageBookendRepository _sut;

    public TableStorageBookendRepositoryTests()
    {
        _sut = new TableStorageBookendRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger,
            Options.Create(new VersionedLibraryOptions
            {
                // These tests exercise the pre-normalization aggregate row contract. The
                // normalized read/write model has its own rollout tests and is enabled by
                // production defaults.
                NormalizedStorageEnabled = false
            }));
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    private static Bookend MakeBookend(string id, string outro = "That's it for {city} traffic.") =>
        Bookend.Create("Drive-Time Pair", null,
            new BookendTemplate("Looking over {city} today", outro), "initial", "operator", DateTime.UtcNow, id).Value;

    private static TableStorageBookendRepository.BookendEntity Entity(string id, string outro = "That's it for {city} traffic.")
    {
        var version = new BookendVersion(1, new BookendTemplate("Looking over {city} today", outro), "initial",
            DateTime.UtcNow, "operator");
        return new TableStorageBookendRepository.BookendEntity
        {
            PartitionKey = PartitionKey,
            RowKey = id,
            Name = "Drive-Time Pair",
            Status = "active",
            AssignedStationsJson = "[]",
            VersionsJson = JsonSerializer.Serialize(new[] { version }),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task Save_WritesToTheSingleBookendPartition()
    {
        _tableClient.UpsertEntityAsync(
                Arg.Any<TableStorageBookendRepository.BookendEntity>(),
                TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.SaveAsync(MakeBookend("bk-1"));

        result.IsSuccess.ShouldBeTrue(result.Error);
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Is<TableStorageBookendRepository.BookendEntity>(e =>
                e.PartitionKey == "Bookend" && e.RowKey == "bk-1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_QueriesTheSingleBookendPartition()
    {
        _tableClient.QueryAsync<TableStorageBookendRepository.BookendEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable([Entity("bk-1")]));

        var result = await _sut.ListAsync();

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Value.Count.ShouldBe(1);
        result.Value[0].Id.ShouldBe("bk-1");
        _tableClient.Received(1).QueryAsync<TableStorageBookendRepository.BookendEntity>(
            Arg.Is<string?>(f => f == "PartitionKey eq 'Bookend'"),
            Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task List_EmptyPartition_ReturnsEmptyList()
    {
        _tableClient.QueryAsync<TableStorageBookendRepository.BookendEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.EmptyPageable<TableStorageBookendRepository.BookendEntity>());

        var result = await _sut.ListAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Save_ThenGet_RoundTripsVersionsAndBothLines()
    {
        TableStorageBookendRepository.BookendEntity? saved = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<TableStorageBookendRepository.BookendEntity>(e => saved = e),
                TableUpdateMode.Replace, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var bookend = MakeBookend("bk-1");
        var save = await _sut.SaveAsync(bookend);
        save.IsSuccess.ShouldBeTrue(save.Error);
        saved.ShouldNotBeNull();
        saved!.VersionsJson.ShouldNotBe("[]");
        // The pair shape serialises through JsonDefaults.Storage: both lines are in the history.
        saved.VersionsJson.ShouldContain("IntroText");
        saved.VersionsJson.ShouldContain("OutroText");

        _tableClient.GetEntityAsync<TableStorageBookendRepository.BookendEntity>(
                "Bookend", "bk-1", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.MockResponse(saved)));

        var fetched = await _sut.GetAsync("bk-1");

        fetched.IsSuccess.ShouldBeTrue(fetched.Error);
        fetched.Value.HasValue.ShouldBeTrue();
        fetched.Value.Value.CurrentVersion.ShouldBe(1);
        fetched.Value.Value.CurrentTemplate.IntroText.ShouldBe("Looking over {city} today");
        fetched.Value.Value.CurrentTemplate.OutroText.ShouldBe("That's it for {city} traffic.");
    }

    [Fact]
    public async Task Save_ThenGet_RoundTripsPairWithEmptyOutro()
    {
        var bookend = MakeBookend("bk-2", outro: "");
        var save = await _sut.SaveAsync(bookend);
        save.IsSuccess.ShouldBeTrue(save.Error);

        var entity = Entity("bk-2", outro: "");
        _tableClient.GetEntityAsync<TableStorageBookendRepository.BookendEntity>(
                "Bookend", "bk-2", null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.MockResponse(entity)));

        var fetched = await _sut.GetAsync("bk-2");

        fetched.IsSuccess.ShouldBeTrue(fetched.Error);
        fetched.Value.HasValue.ShouldBeTrue();
        fetched.Value.Value.CurrentTemplate.HasIntro.ShouldBeTrue();
        fetched.Value.Value.CurrentTemplate.HasOutro.ShouldBeFalse();
        fetched.Value.Value.CurrentTemplate.OutroText.ShouldBe("");
    }

    [Fact]
    public async Task Get_Missing_ReturnsNone()
    {
        _tableClient.GetEntityAsync<TableStorageBookendRepository.BookendEntity>(
                "Bookend", "bk-nope", null, Arg.Any<CancellationToken>())
            .Returns<Task<Response<TableStorageBookendRepository.BookendEntity>>>(
                _ => throw new RequestFailedException(404, "Not Found"));

        var result = await _sut.GetAsync("bk-nope");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_UsesTheSingleBookendPartition()
    {
        _tableClient.DeleteEntityAsync("Bookend", "bk-1", Arg.Any<ETag>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.DeleteAsync("bk-1");

        result.IsSuccess.ShouldBeTrue(result.Error);
        await _tableClient.Received(1).DeleteEntityAsync("Bookend", "bk-1", ETag.All, Arg.Any<CancellationToken>());
    }
}

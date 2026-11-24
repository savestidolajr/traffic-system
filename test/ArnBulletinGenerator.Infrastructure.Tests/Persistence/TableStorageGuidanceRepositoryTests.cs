using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageGuidanceRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

// Regression coverage for bug-bash-2026-08-05 F1: an unconditional Azure Table delete of a
// missing row no-ops silently, so DeleteAsync must report whether a row was actually removed.
public sealed class TableStorageGuidanceRepositoryTests
{
    private readonly ILogger<TableStorageGuidanceRepository> _logger =
        Substitute.For<ILogger<TableStorageGuidanceRepository>>();

    private readonly TableStorageGuidanceRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageGuidanceRepositoryTests()
    {
        _sut = new TableStorageGuidanceRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    private static GuidanceEntity ExistingEntity() => new()
    {
        PartitionKey = "Market:sydney",
        RowKey = "g1",
        Id = "g1",
        Scope = "Market",
        ScopeId = "sydney",
        GuidanceType = "Style",
        Text = "Keep it snappy",
        Enabled = true,
        CreatedBy = "alice",
        ETag = new ETag("v1")
    };

    [Fact]
    public async Task DeleteAsync_ExistingRow_DeletesConditionally_AndReportsTrue()
    {
        var entity = ExistingEntity();
        _tableClient.GetEntityAsync<GuidanceEntity>(
                "Market:sydney", "g1", Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
        _tableClient.DeleteEntityAsync("Market:sydney", "g1", new ETag("v1"), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.DeleteAsync(GuidanceScope.Market, "sydney", "g1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeTrue();
        // The delete must carry the fetched ETag — not ETag.All — so it is conditional on the row
        // we actually saw (reverting to an unconditional delete re-opens the silent no-op).
        await _tableClient.Received(1).DeleteEntityAsync(
            "Market:sydney", "g1", new ETag("v1"), Arg.Any<CancellationToken>());
        await _tableClient.DidNotReceive().DeleteEntityAsync(
            "Market:sydney", "g1", ETag.All, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_MissingRow_ReportsFalse_AndDoesNotDelete()
    {
        // Azure Table returns 404 for a missing (partition, row) — the repository must surface
        // that as "nothing was deleted" instead of an unconditional-delete success.
        _tableClient.GetEntityAsync<GuidanceEntity>(
                "Global", "g-nope", Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        var result = await _sut.DeleteAsync(GuidanceScope.Global, string.Empty, "g-nope");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        await _tableClient.DidNotReceive().DeleteEntityAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_WrongScopeRow_ReportsFalse()
    {
        // A row that lives in the Global partition must not be deletable via Market:portlincoln —
        // the wrong-scope lookup misses and reports false (the trigger answers 404).
        _tableClient.GetEntityAsync<GuidanceEntity>(
                "Market:portlincoln", "g1", Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not Found"));

        var result = await _sut.DeleteAsync(GuidanceScope.Market, "portlincoln", "g1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeFalse();
        await _tableClient.DidNotReceive().DeleteEntityAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_StorageError_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<GuidanceEntity>(
                "Global", "g1", Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var result = await _sut.DeleteAsync(GuidanceScope.Global, string.Empty, "g1");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("Boom");
    }
}

using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageOperatorAssignmentRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageOperatorAssignmentRepositoryTests
{
    private readonly ILogger<TableStorageOperatorAssignmentRepository> _logger =
        Substitute.For<ILogger<TableStorageOperatorAssignmentRepository>>();

    private readonly TableStorageOperatorAssignmentRepository _sut;
    private readonly TableClient _tableClient = Substitute.For<TableClient>();

    public TableStorageOperatorAssignmentRepositoryTests()
    {
        _sut = new TableStorageOperatorAssignmentRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static OperatorAssignmentEntity CreateEntity(string objectId = "oid-abc-123",
        string displayName = "Alice", string[]? markets = null)
    {
        return new OperatorAssignmentEntity
        {
            PartitionKey = "Operator",
            RowKey = objectId,
            DisplayName = displayName,
            MarketsJson = System.Text.Json.JsonSerializer.Serialize(markets ?? ["sydney", "melbourne"]),
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedBy = "admin@test.com"
        };
    }

    private void SeedQuery(params OperatorAssignmentEntity[] entities)
    {
        _tableClient.QueryAsync<OperatorAssignmentEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(entities));
    }

    private void SeedGet(OperatorAssignmentEntity entity)
    {
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }

    private void SeedGetNotFound(string rowKey)
    {
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Is(rowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
    }

    private static SwaAliasEntity CreateAliasEntity(string swaUserId = "swa-user-1", string objectId = "oid-abc-123")
    {
        return new SwaAliasEntity
        {
            PartitionKey = "SwaAlias",
            RowKey = swaUserId,
            ObjectId = objectId
        };
    }

    private void SeedAliasGet(SwaAliasEntity entity)
    {
        _tableClient.GetEntityAsync<SwaAliasEntity>(
                Arg.Any<string>(), Arg.Is(entity.RowKey),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
    }

    private void SeedAliasGetNotFound(string swaUserId)
    {
        _tableClient.GetEntityAsync<SwaAliasEntity>(
                Arg.Any<string>(), Arg.Is(swaUserId),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
    }

    // ── GetAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_Existing_ReturnsSome()
    {
        var entity = CreateEntity("oid-abc");
        SeedGet(entity);

        var result = await _sut.GetAsync("oid-abc");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSome.ShouldBeTrue();
        result.Value.Value.ObjectId.ShouldBe("oid-abc");
        result.Value.Value.DisplayName.ShouldBe("Alice");
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNone()
    {
        SeedGetNotFound("oid-xyz");

        var result = await _sut.GetAsync("oid-xyz");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetAsync_StorageError_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var result = await _sut.GetAsync("oid-err");

        result.IsFailure.ShouldBeTrue();
    }

    // ── ListAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_ReturnsAll()
    {
        var alice = CreateEntity("oid-a", "Alice", ["sydney"]);
        var bob = CreateEntity("oid-b", "Bob", ["melbourne"]);
        SeedQuery(alice, bob);

        var result = await _sut.ListAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
        result.Value.ShouldContain(a => a.DisplayName == "Alice");
        result.Value.ShouldContain(a => a.DisplayName == "Bob");
    }

    [Fact]
    public async Task ListAsync_Empty_ReturnsEmpty()
    {
        SeedQuery();

        var result = await _sut.ListAsync();

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    // ── UpsertAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_ExistingEntity_UpdatesWithETag()
    {
        var entity = CreateEntity("oid-existing", "Alice", ["sydney"]);
        entity.ETag = new ETag("v1");
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Is("oid-existing"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));

        _tableClient.UpdateEntityAsync(
                Arg.Any<OperatorAssignmentEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var assignment = new OperatorAssignment("oid-existing", "Alice Updated", ["brisbane"], DateTime.UtcNow, "admin@test.com");

        var result = await _sut.UpsertAsync(assignment);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<OperatorAssignmentEntity>(e => e.DisplayName == "Alice Updated"),
            Arg.Is<ETag>(e => e.ToString() == "v1"),
            TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_MarketsOnlyEdit_KeepsTheNameAndSignInIdentity()
    {
        // The station-scoping dialog sends markets only. Writing its blanks through renamed the
        // person to their raw UPN in the console and erased what their token last told us.
        var entity = CreateEntity("oid-scoped", "Reviewer", ["sydney"]);
        entity.UserPrincipalName = "reviewer@arn.com.au";
        entity.SignInRolesJson = """["Operator"]""";
        entity.LastSignInUtc = new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);
        entity.ETag = new ETag("v1");
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Is("oid-scoped"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(entity));
        _tableClient.UpdateEntityAsync(
                Arg.Any<OperatorAssignmentEntity>(), Arg.Any<ETag>(),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        var result = await _sut.UpsertAsync(new OperatorAssignment(
            "oid-scoped", string.Empty, ["cairns-4ca"], DateTime.UtcNow, "admin@test.com"));

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpdateEntityAsync(
            Arg.Is<OperatorAssignmentEntity>(e =>
                e.DisplayName == "Reviewer" &&
                e.UserPrincipalName == "reviewer@arn.com.au" &&
                e.SignInRolesJson == """["Operator"]""" &&
                e.LastSignInUtc == new DateTimeOffset(2026, 7, 29, 9, 0, 0, TimeSpan.Zero) &&
                e.MarketsJson.Contains("cairns-4ca", StringComparison.Ordinal)),
            Arg.Any<ETag>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_NewEntity_Creates()
    {
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Is("oid-new"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var assignment = new OperatorAssignment("oid-new", "Charlie", ["brisbane"], DateTime.UtcNow, "admin@test.com");

        var result = await _sut.UpsertAsync(assignment);

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).UpsertEntityAsync(
            Arg.Any<OperatorAssignmentEntity>(), TableUpdateMode.Replace, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertAsync_StorageError_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        _tableClient.UpsertEntityAsync(
                Arg.Any<OperatorAssignmentEntity>(), Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var assignment = new OperatorAssignment("oid-err", "Err", [], DateTime.UtcNow, "admin");

        var result = await _sut.UpsertAsync(assignment);

        result.IsFailure.ShouldBeTrue();
    }

    // ── DeleteAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_Success_Called()
    {
        var result = await _sut.DeleteAsync("oid-del");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).DeleteEntityAsync(
            "Operator", "oid-del", Arg.Any<ETag>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsSuccess()
    {
        _tableClient.DeleteEntityAsync(
                "Operator", "oid-missing", Arg.Any<ETag>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        var result = await _sut.DeleteAsync("oid-missing");

        result.IsSuccess.ShouldBeTrue();
    }

    // ── Mapping ──────────────────────────────────────────────────────

    [Fact]
    public async Task Mapping_RoundTrips_AllFields()
    {
        var assignment = new OperatorAssignment(
            "oid-rt", "Alice", ["sydney", "melbourne"], DateTime.UtcNow, "admin@test.com");

        // Seed GetEntityAsync to return 404 (creation path)
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Is("oid-rt"),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(404, "Not found"));

        OperatorAssignmentEntity? captured = null;
        _tableClient.UpsertEntityAsync(
                Arg.Do<OperatorAssignmentEntity>(e => captured = e),
                Arg.Any<TableUpdateMode>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(TableClientTestHelper.StubResponse()));

        await _sut.UpsertAsync(assignment);

        captured.ShouldNotBeNull();
        captured!.PartitionKey.ShouldBe("Operator");
        captured.RowKey.ShouldBe("oid-rt");
        captured.DisplayName.ShouldBe("Alice");
        captured.UpdatedBy.ShouldBe("admin@test.com");

        // Feed entity back through GetEntityAsync
        _tableClient.GetEntityAsync<OperatorAssignmentEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.MockResponse(captured));

        var loaded = (await _sut.GetAsync("oid-rt")).Value.Value;
        loaded.ObjectId.ShouldBe("oid-rt");
        loaded.DisplayName.ShouldBe("Alice");
        loaded.Markets.ShouldBe(["sydney", "melbourne"]);
        loaded.UpdatedBy.ShouldBe("admin@test.com");
    }

    // ── GetObjectIdByAliasAsync ──────────────────────────────────────

    [Fact]
    public async Task GetObjectIdByAliasAsync_Existing_ReturnsSome()
    {
        SeedAliasGet(CreateAliasEntity("swa-user-1", "oid-abc-123"));

        var result = await _sut.GetObjectIdByAliasAsync("swa-user-1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSome.ShouldBeTrue();
        result.Value.Value.ShouldBe("oid-abc-123");
    }

    [Fact]
    public async Task GetObjectIdByAliasAsync_NonExistent_ReturnsNone()
    {
        SeedAliasGetNotFound("swa-user-missing");

        var result = await _sut.GetObjectIdByAliasAsync("swa-user-missing");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetObjectIdByAliasAsync_BlankObjectId_ReturnsNone()
    {
        SeedAliasGet(CreateAliasEntity("swa-user-1", string.Empty));

        var result = await _sut.GetObjectIdByAliasAsync("swa-user-1");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task GetObjectIdByAliasAsync_BlankSwaUserId_ReturnsNoneWithoutQuery()
    {
        var result = await _sut.GetObjectIdByAliasAsync("  ");

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsNone.ShouldBeTrue();
        await _tableClient.DidNotReceive().GetEntityAsync<SwaAliasEntity>(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetObjectIdByAliasAsync_StorageError_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<SwaAliasEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var result = await _sut.GetObjectIdByAliasAsync("swa-user-err");

        result.IsFailure.ShouldBeTrue();
    }

    // ── UpsertDirectoryAliasAsync ────────────────────────────────────

    [Fact]
    public async Task UpsertDirectoryAliasAsync_NoExistingAlias_Inserts()
    {
        SeedAliasGetNotFound("swa-user-new");

        var result = await _sut.UpsertDirectoryAliasAsync("swa-user-new", "oid-new-1");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.Received(1).AddEntityAsync(
            Arg.Is<SwaAliasEntity>(e =>
                e.PartitionKey == "SwaAlias" &&
                e.RowKey == "swa-user-new" &&
                e.ObjectId == "oid-new-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertDirectoryAliasAsync_SameObjectId_NoWrite()
    {
        // Casing differs to prove the comparison is ordinal-ignore-case.
        SeedAliasGet(CreateAliasEntity("swa-user-1", "OID-ABC-123"));

        var result = await _sut.UpsertDirectoryAliasAsync("swa-user-1", "oid-abc-123");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.DidNotReceive().AddEntityAsync(
            Arg.Any<SwaAliasEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertDirectoryAliasAsync_DifferentObjectId_DoesNotOverwrite()
    {
        // First writer wins: an established alias is never repointed, even by a later sign-in.
        SeedAliasGet(CreateAliasEntity("swa-user-1", "oid-original"));

        var result = await _sut.UpsertDirectoryAliasAsync("swa-user-1", "oid-intruder");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.DidNotReceive().AddEntityAsync(
            Arg.Any<SwaAliasEntity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertDirectoryAliasAsync_InsertRaceConflict_ReturnsSuccess()
    {
        SeedAliasGetNotFound("swa-user-race");
        _tableClient.AddEntityAsync(
                Arg.Any<SwaAliasEntity>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(409, "Conflict"));

        var result = await _sut.UpsertDirectoryAliasAsync("swa-user-race", "oid-race-1");

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task UpsertDirectoryAliasAsync_BlankArguments_NoWrite()
    {
        var result = await _sut.UpsertDirectoryAliasAsync("  ", "oid-blank-1");

        result.IsSuccess.ShouldBeTrue();
        await _tableClient.DidNotReceive().GetEntityAsync<SwaAliasEntity>(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());

        var resultEmptyObjectId = await _sut.UpsertDirectoryAliasAsync("swa-user-2", string.Empty);

        resultEmptyObjectId.IsSuccess.ShouldBeTrue();
        await _tableClient.DidNotReceive().GetEntityAsync<SwaAliasEntity>(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpsertDirectoryAliasAsync_StorageError_ReturnsFailure()
    {
        _tableClient.GetEntityAsync<SwaAliasEntity>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new RequestFailedException(500, "Boom"));

        var result = await _sut.UpsertDirectoryAliasAsync("swa-user-err", "oid-err-1");

        result.IsFailure.ShouldBeTrue();
    }
}

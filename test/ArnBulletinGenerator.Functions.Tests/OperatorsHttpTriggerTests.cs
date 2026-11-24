using System.Net;
using System.Text;
using Microsoft.Azure.Functions.Worker.Http;
using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Functions.Triggers.Http;
using ArnBulletinGenerator.Functions.Auth;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class OperatorsHttpTriggerTests
{
    private readonly IOperatorAssignmentRepository _repository = Substitute.For<IOperatorAssignmentRepository>();
    private readonly IOperatorDirectory _directory = Substitute.For<IOperatorDirectory>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();
    private readonly IScheduleRepository _schedules = Substitute.For<IScheduleRepository>();

    private readonly OperatorsHttpTrigger _sut;

    public OperatorsHttpTriggerTests()
    {
        _repository.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _repository.UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _sut = new OperatorsHttpTrigger(
            _repository, _directory, _marketAccess, _schedules,
            new OperatorRegistrar(_repository, Substitute.For<ILogger<OperatorRegistrar>>()),
            TimeProvider.System,
            Substitute.For<ILogger<OperatorsHttpTrigger>>());
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static MarketSchedule Schedule(string marketId) =>
        MarketSchedule.Create(marketId, $"{marketId} Traffic", "3AW", "Australia/Sydney", "*/15 * * * *", true,
            CoverageArea.Create(-33.87, 151.21, 5.0).Value, "voice-123", $"{marketId}/traffic/", 30,
            5, 2, DateTime.UtcNow, DateTime.UtcNow).Value;

    // === ListOperatorAssignments ===

    [Fact]
    public async Task ListOperatorAssignments_MergesLiveDirectoryAndPersistedAssignments()
    {
        var updatedAt = new DateTime(2026, 7, 28, 12, 34, 56, DateTimeKind.Utc);
        var assigned = new OperatorAssignment(
            "assigned", "Old Bob Name", ["sydney"], updatedAt, "admin@example.com");
        var orphan = new OperatorAssignment(
            "orphan", "Persisted Only", ["melbourne"], updatedAt.AddMinutes(-1), "admin@example.com");
        _repository.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<OperatorAssignment>>.Success([assigned, orphan]));
        _directory.ListRoleHoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<DirectoryPrincipal>>.Success([
                new DirectoryPrincipal(
                    "assigned", "Bob Directory Name", "bob@example.com",
                    ["Operator"], "Group:APP-IBP-Test-Operator"),
                new DirectoryPrincipal(
                    "admin", "Admin User", "admin@example.com",
                    ["Admin"], "Direct"),
                new DirectoryPrincipal(
                    "unassigned", "No Station", "none@example.com",
                    ["Operator"], "Group:APP-IBP-Test-Operator")
            ]));

        var response = await _sut.ListOperatorAssignments(
            MockHttpRequestData.CreateGet("http://localhost/api/operators"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await MockHttpRequestData.ReadResponseJsonAsync<OperatorDirectoryDto>(response);
        payload.ShouldNotBeNull();
        payload!.DirectoryStatus.ShouldBe("Ok");
        payload.DirectoryError.ShouldBeNull();
        payload.Operators.Count.ShouldBe(4);

        var fromDirectory = payload.Operators.Single(o => o.ObjectId == "assigned");
        fromDirectory.DisplayName.ShouldBe("Bob Directory Name");
        fromDirectory.UserPrincipalName.ShouldBe("bob@example.com");
        fromDirectory.Roles.ShouldBe(["Operator"]);
        fromDirectory.Source.ShouldBe("Group:APP-IBP-Test-Operator");
        fromDirectory.Markets.ShouldBe(["sydney"]);
        fromDirectory.IsAdmin.ShouldBeFalse();
        fromDirectory.UpdatedAtUtc.ShouldBe("2026-07-28T12:34:56Z");
        fromDirectory.UpdatedBy.ShouldBe("admin@example.com");

        var admin = payload.Operators.Single(o => o.ObjectId == "admin");
        admin.Markets.ShouldBeEmpty();
        admin.IsAdmin.ShouldBeTrue();
        admin.UpdatedAtUtc.ShouldBeNull();
        admin.UpdatedBy.ShouldBeNull();

        var noAssignment = payload.Operators.Single(o => o.ObjectId == "unassigned");
        noAssignment.Markets.ShouldBeEmpty();
        noAssignment.IsAdmin.ShouldBeFalse();

        var persistedOnly = payload.Operators.Single(o => o.ObjectId == "orphan");
        persistedOnly.DisplayName.ShouldBe("Persisted Only");
        persistedOnly.Roles.ShouldBeEmpty();
        persistedOnly.Source.ShouldBe("Assignment");
        persistedOnly.Markets.ShouldBe(["melbourne"]);
        persistedOnly.IsAdmin.ShouldBeFalse();
    }

    [Fact]
    public async Task ListOperatorAssignments_DirectoryFailureReturnsPersistedRowsAsUnavailable()
    {
        var assignment = new OperatorAssignment(
            "orphan", "Persisted Only", ["melbourne"], DateTime.UtcNow, "admin@example.com");
        _repository.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<OperatorAssignment>>.Success([assignment]));
        _directory.ListRoleHoldersAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<DirectoryPrincipal>>.Failure("Entra unavailable"));

        var response = await _sut.ListOperatorAssignments(
            MockHttpRequestData.CreateGet("http://localhost/api/operators"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await MockHttpRequestData.ReadResponseJsonAsync<OperatorDirectoryDto>(response);
        payload.ShouldNotBeNull();
        payload!.DirectoryStatus.ShouldBe("Unavailable");
        payload.DirectoryError.ShouldBe("Entra unavailable");
        payload.Operators.Count.ShouldBe(1);
        payload.Operators[0].Source.ShouldBe("Assignment");
        payload.Operators[0].Roles.ShouldBeEmpty();
        payload.Operators[0].Markets.ShouldBe(["melbourne"]);
    }

    // === GetMyOperatorScope ===

    [Fact]
    public async Task GetMyOperatorScope_FirstSignIn_SelfRegistersOnceAndIsIdempotent()
    {
        const string objectId = "self";
        var registered = new OperatorAssignment(
            objectId, "Alice Operator", Array.Empty<string>(), DateTime.UtcNow, "Alice Operator");
        _repository.GetAsync(objectId, Arg.Any<CancellationToken>())
            .Returns(
                Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None),
                Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(registered)));

        var firstRequest = MockHttpRequestData.CreateGet("http://localhost/api/operators/me");
        AddPrincipal(firstRequest, objectId, "Alice Operator");
        var firstResponse = await _sut.GetMyOperatorScope(firstRequest, Ctx());

        var secondRequest = MockHttpRequestData.CreateGet("http://localhost/api/operators/me");
        AddPrincipal(secondRequest, objectId, "Alice Operator");
        var secondResponse = await _sut.GetMyOperatorScope(secondRequest, Ctx());

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MockHttpRequestData.ReadResponseJsonAsync<OperatorScopeDto>(firstResponse))
            .ShouldBe(new OperatorScopeDto(false, null));
        (await MockHttpRequestData.ReadResponseJsonAsync<OperatorScopeDto>(secondResponse))
            .ShouldBe(new OperatorScopeDto(false, null));

        await _repository.Received(1).UpsertAsync(
            Arg.Is<OperatorAssignment>(a =>
                a.ObjectId == objectId &&
                a.DisplayName == "Alice Operator" &&
                a.Markets.Count == 0),
            Arg.Any<CancellationToken>());
        await _directory.DidNotReceiveWithAnyArgs().ListRoleHoldersAsync(default);
    }

    [Fact]
    public async Task GetMyOperatorScope_ExistingAssignment_RefreshesDisplayNameWithoutChangingMarkets()
    {
        const string objectId = "existing";
        var existing = new OperatorAssignment(
            objectId, "Old Name", ["sydney", "melbourne"], DateTime.UtcNow, "admin");
        _repository.GetAsync(objectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(existing)));
        _marketAccess.GetAllowedMarketsAsync(objectId, false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["sydney", "melbourne"]));

        var request = MockHttpRequestData.CreateGet("http://localhost/api/operators/me");
        AddPrincipal(request, objectId, "New Name");
        var response = await _sut.GetMyOperatorScope(request, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var scope = await MockHttpRequestData.ReadResponseJsonAsync<OperatorScopeDto>(response);
        scope.ShouldNotBeNull();
        scope!.Scoped.ShouldBeTrue();
        scope.Markets.ShouldBe(["sydney", "melbourne"]);
        await _repository.Received(1).UpsertAsync(
            Arg.Is<OperatorAssignment>(a =>
                a.ObjectId == objectId &&
                a.DisplayName == "New Name" &&
                a.Markets.SequenceEqual(existing.Markets)),
            Arg.Any<CancellationToken>());
        await _marketAccess.DidNotReceiveWithAnyArgs()
            .GetAllowedMarketsAsync(default!, default, default);
    }

    [Fact]
    public async Task GetMyOperatorScope_RegistrationFailure_ReturnsScopeWithoutFailingRequest()
    {
        const string objectId = "registration-fails";
        _repository.GetAsync(objectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Failure("table unavailable"));
        _marketAccess.GetAllowedMarketsAsync(objectId, false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.None);

        var request = MockHttpRequestData.CreateGet("http://localhost/api/operators/me");
        AddPrincipal(request, objectId, "Alice Operator");
        var response = await _sut.GetMyOperatorScope(request, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var scope = await MockHttpRequestData.ReadResponseJsonAsync<OperatorScopeDto>(response);
        scope.ShouldNotBeNull();
        scope!.Scoped.ShouldBeFalse();
        await _repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task GetMyOperatorScope_PrincipalWithoutOidClaim_DoesNotSelfRegister()
    {
        // SWA back-fills ObjectId from `userId`, which is a provider-specific id, not an Entra
        // directory object id. Registering it would create a row that can never join to
        // appRoleAssignedTo — an operator the admin can see but Entra cannot explain.
        string json =
            """{"identityProvider":"aad","userId":"swa-user-123","userDetails":"ops@arn.com.au","userRoles":["authenticated","Operator"]}""";
        var request = MockHttpRequestData.CreateGet("http://localhost/api/operators/me");
        request.Headers.Add(
            "X-MS-CLIENT-PRINCIPAL",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));

        var response = await _sut.GetMyOperatorScope(request, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }


    private static void AddPrincipal(HttpRequestData request, string objectId, string displayName)
    {
        string json = $$"""{"auth_typ":"aad","claims":[{"typ":"oid","val":"{{objectId}}"},{"typ":"name","val":"{{displayName}}"}]}""";
        request.Headers.Add(
            "X-MS-CLIENT-PRINCIPAL",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
    }

    // === UpsertOperatorAssignment ===

    [Fact]
    public async Task UpsertOperatorAssignment_EmptyMarketsArrayMeansUnscopedAccess()
    {
        var persisted = new OperatorAssignment(
            "operator-empty", "No Station", Array.Empty<string>(), DateTime.UtcNow, "admin@example.com");
        _repository.GetAsync("operator-empty", Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(persisted)));

        var response = await _sut.UpsertOperatorAssignment(
            MockHttpRequestData.CreatePut(
                new { markets = Array.Empty<string>(), displayName = "No Station" },
                "http://localhost/api/operators/operator-empty"),
            "operator-empty", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await MockHttpRequestData.ReadResponseJsonAsync<OperatorAssignmentDto>(response);
        payload.ShouldNotBeNull();
        payload!.Markets.ShouldBeEmpty();
        payload!.DisplayName.ShouldBe("No Station");
    }

    [Fact]
    public async Task UpsertOperatorAssignment_BlankDisplayName_EchoesThePersistedNameNotTheRequest()
    {
        // Finding 35: the repository treats a blank DisplayName as "not supplied" (the scoping
        // dialog sends markets only), so the response must reflect what was actually persisted
        // — echoing the request would report a name that was never written.
        var persisted = new OperatorAssignment(
            "operator-echo", "ZZ BB Auth Test", ["sydney"], DateTime.UtcNow, "admin@example.com");
        _repository.GetAsync("operator-echo", Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(persisted)));
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("sydney")]));

        var response = await _sut.UpsertOperatorAssignment(
            MockHttpRequestData.CreatePutRaw(
                """{"markets":["sydney"]}""",
                "http://localhost/api/operators/operator-echo"),
            "operator-echo", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await MockHttpRequestData.ReadResponseJsonAsync<OperatorAssignmentDto>(response);
        payload.ShouldNotBeNull();
        payload!.DisplayName.ShouldBe("ZZ BB Auth Test");
        payload!.Markets.ShouldBe(["sydney"]);
    }

    [Fact]
    public async Task UpsertOperatorAssignment_UnknownMarket_ReturnsBadRequestNamingIt()
    {
        // Finding 23: a typo'd market id would save with a success toast and then 403 the
        // operator on every route, so unknown ids are rejected up front against the schedule
        // catalogue.
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("melbourne")]));

        var response = await _sut.UpsertOperatorAssignment(
            MockHttpRequestData.CreatePutRaw(
                """{"markets":["melbourn"]}""",
                "http://localhost/api/operators/operator-typo"),
            "operator-typo", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        body.ShouldContain("melbourn", Case.Insensitive);
        await _repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task UpsertOperatorAssignment_KnownMarkets_DedupedAndSaved()
    {
        var persisted = new OperatorAssignment(
            "operator-known", "Known Name", ["sydney"], DateTime.UtcNow, "admin@example.com");
        _repository.GetAsync("operator-known", Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(persisted)));
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Success([Schedule("sydney")]));

        var response = await _sut.UpsertOperatorAssignment(
            MockHttpRequestData.CreatePutRaw(
                """{"markets":["sydney","SYDNEY"," sydney "]}""",
                "http://localhost/api/operators/operator-known"),
            "operator-known", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await MockHttpRequestData.ReadResponseJsonAsync<OperatorAssignmentDto>(response);
        payload.ShouldNotBeNull();
        payload!.Markets.ShouldBe(["sydney"]);
    }

    [Fact]
    public async Task UpsertOperatorAssignment_CatalogueLookupFailure_ReturnsInternalError()
    {
        _schedules.ListSchedulesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<MarketSchedule>>.Failure("table unavailable"));

        var response = await _sut.UpsertOperatorAssignment(
            MockHttpRequestData.CreatePutRaw(
                """{"markets":["sydney"]}""",
                "http://localhost/api/operators/operator-cat-fail"),
            "operator-cat-fail", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        await _repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task UpsertOperatorAssignment_MissingMarketsArrayReturnsBadRequest()
    {
        var response = await _sut.UpsertOperatorAssignment(
            MockHttpRequestData.CreatePutRaw(
                """{"displayName":"No Markets"}""",
                "http://localhost/api/operators/operator-missing"),
            "operator-missing", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpsertOperatorAssignment_NullMarketsArrayReturnsBadRequest()
    {
        var response = await _sut.UpsertOperatorAssignment(
            MockHttpRequestData.CreatePutRaw(
                """{"markets":null,"displayName":"No Markets"}""",
                "http://localhost/api/operators/operator-null"),
            "operator-null", Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // === DeleteOperatorAssignment ===
    [Fact]
    public async Task DeleteOperatorAssignment_ValidObjectId_ReturnsOperatorActionResponseDtoWithObjectId()
    {
        const string objectId = "operator-abc-123";

        var resp = await _sut.DeleteOperatorAssignment(
            MockHttpRequestData.CreateDelete($"http://localhost/api/operators/{objectId}"), objectId, Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await MockHttpRequestData.ReadResponseJsonAsync<OperatorActionResponseDto>(resp);
        dto.ShouldNotBeNull();
        dto.Status.ShouldBe("deleted");
        dto.ObjectId.ShouldBe(objectId);

        // Regression guard for QT-013: the response must not be the job DTO shape (which
        // serializes the identifier under "jobId" rather than "objectId").
        string body = await MockHttpRequestData.ReadResponseBodyAsync(resp);
        body.ShouldNotContain("jobId", Case.Insensitive);
        body.ShouldContain("objectId", Case.Insensitive);
        dto.Message.ShouldContain("unrestricted");
    }

    [Fact]
    public async Task DeleteOperatorAssignment_MissingObjectId_ReturnsBadRequest()
    {
        var resp = await _sut.DeleteOperatorAssignment(
            MockHttpRequestData.CreateDelete("http://localhost/api/operators/"), "  ", Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteOperatorAssignment_RepositoryFailure_ReturnsInternalError()
    {
        const string objectId = "operator-fail";
        _repository.DeleteAsync(objectId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure("storage unavailable"));

        var resp = await _sut.DeleteOperatorAssignment(
            MockHttpRequestData.CreateDelete($"http://localhost/api/operators/{objectId}"), objectId, Ctx());

        resp.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }
}

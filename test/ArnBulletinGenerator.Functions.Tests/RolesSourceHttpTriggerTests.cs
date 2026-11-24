using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Middleware;
using ArnBulletinGenerator.Functions.Triggers.Http;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Azure.Functions.Worker;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class RolesSourceHttpTriggerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 11, 9, 30, 0, TimeSpan.Zero);

    private const string TokenOid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private const string TokenDisplayName = "Reviewer";
    private const string TokenUpn = "reviewer@arn.com.au";

    [Fact]
    public void ExtractAllowedRoles_RolesClaim_MapsKnownRolesOnlyWithCanonicalCasing()
    {
        string json = """
            {
              "claims": [
                { "typ": "roles", "val": "operator" },
                { "typ": "roles", "val": " Admin " },
                { "typ": "roles", "val": "ADMIN" },
                { "typ": "roles", "val": "anonymous" },
                { "typ": "roles", "val": "authenticated" },
                { "typ": "roles", "val": "Reader" },
                { "typ": "groups", "val": "Admin" }
              ]
            }
            """;

        var roles = Extract(json);

        roles.ShouldBe([AuthorizationMiddleware.RoleOperator, AuthorizationMiddleware.RoleAdmin]);
    }

    [Fact]
    public void ExtractAllowedRoles_LegacyRoleClaimUri_MapsAllowedAppRoles()
    {
        string json = """
            {
              "claims": [
                { "typ": "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "val": "Admin" },
                { "typ": "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "val": "Operator" }
              ]
            }
            """;

        var roles = Extract(json);

        roles.ShouldBe([AuthorizationMiddleware.RoleAdmin, AuthorizationMiddleware.RoleOperator]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"claims\":null}")]
    [InlineData("{\"claims\":{}}")]
    [InlineData("[]")]
    [InlineData("\"claims\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void ExtractAllowedRoles_MissingClaimsOrNonObjectPayload_ReturnsEmpty(string json)
    {
        Extract(json).ShouldBeEmpty();
    }

    [Fact]
    public void ExtractAllowedRoles_MalformedClaimEntries_ReturnsOnlyValidAllowedRoles()
    {
        string json = """
            {
              "claims": [
                {},
                { "typ": "roles" },
                { "val": "Admin" },
                { "typ": 7, "val": "Admin" },
                { "typ": "roles", "val": 7 },
                { "typ": "roles", "val": "   " },
                { "typ": "roles", "val": "Operator" }
              ]
            }
            """;

        var roles = Extract(json);

        roles.ShouldBe([AuthorizationMiddleware.RoleOperator]);
    }

    [Fact]
    public async Task GetRolesAsync_InvalidJson_ReturnsOkWithNoCustomRoles()
    {
        var request = MockHttpRequestData.CreatePostRaw("not-json", "http://localhost/api/GetRoles");
        var repository = Substitute.For<IOperatorAssignmentRepository>();

        var response = await Sut(repository, UnverifiableValidator()).GetRolesAsync(request, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("Roles").EnumerateArray().ShouldBeEmpty();
        await repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default!);
        await repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!);
        await repository.DidNotReceiveWithAnyArgs().UpsertDirectoryAliasAsync(default!, default!, default!);
    }

    [Fact]
    public async Task GetRolesAsync_UnverifiableSignInWithForgedOid_ReturnsRolesButRecordsNothing()
    {
        // Finding-10 regression, preserved: the endpoint is anonymous and reachable directly on
        // the Function App hostname, so a payload claiming an oid and Admin must not persist
        // anything. The roles response still maps (SWA's sign-in depends on it), but an
        // unverifiable sign-in records no operator and no alias.
        const string payload = """
            {
              "identityProvider": "aad",
              "userId": "swa-user-123",
              "userDetails": "reviewer@arn.com.au",
              "accessToken": "attacker-supplied-garbage",
              "claims": [
                { "typ": "oid", "val": "11111111-2222-3333-4444-555555555555" },
                { "typ": "name", "val": "FORGED NAME" },
                { "typ": "preferred_username", "val": "forged@example.test" },
                { "typ": "roles", "val": "Admin" }
              ]
            }
            """;
        var repository = Substitute.For<IOperatorAssignmentRepository>();

        var response = await Sut(repository, UnverifiableValidator()).GetRolesAsync(
            MockHttpRequestData.CreatePostRaw(payload, "http://localhost/api/GetRoles"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("Roles").EnumerateArray()
            .Select(role => role.GetString()).ShouldBe([AuthorizationMiddleware.RoleAdmin]);

        // Nothing was recorded: no registration lookup/upsert, no alias write.
        await repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default!);
        await repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!);
        await repository.DidNotReceiveWithAnyArgs().UpsertDirectoryAliasAsync(default!, default!, default!);
    }

    [Fact]
    public async Task GetRolesAsync_SignInWithoutAccessToken_MapsRolesButRecordsNobody()
    {
        // No accessToken means there is nothing to cryptographically validate, so nobody is
        // recorded. Roles must still map, or the person could not sign in at all.
        const string payload = """
            {
              "identityProvider": "aad",
              "userId": "swa-provider-id",
              "userDetails": "reviewer@arn.com.au",
              "claims": [
                { "typ": "name", "val": "Reviewer" },
                { "typ": "roles", "val": "Admin" }
              ]
            }
            """;
        var repository = Substitute.For<IOperatorAssignmentRepository>();

        var response = await Sut(repository, UnverifiableValidator()).GetRolesAsync(
            MockHttpRequestData.CreatePostRaw(payload, "http://localhost/api/GetRoles"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("Roles").EnumerateArray()
            .Select(role => role.GetString()).ShouldBe([AuthorizationMiddleware.RoleAdmin]);

        await repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default!);
        await repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!);
        await repository.DidNotReceiveWithAnyArgs().UpsertDirectoryAliasAsync(default!, default!, default!);
    }

    [Fact]
    public async Task GetRolesAsync_VerifiedSignIn_RegistersTokenIdentityAndAliasesPayloadUserId()
    {
        // The payload claims a forged identity; the validated token is the only source of truth
        // for what gets persisted.
        const string payload = """
            {
              "identityProvider": "aad",
              "userId": "swa-user-123",
              "userDetails": "forged@example.test",
              "accessToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6InRlc3QifQ.eyJzdWIiOiJ0b2tlbi1zdWIifQ.signature",
              "claims": [
                { "typ": "oid", "val": "FORGED-OID" },
                { "typ": "name", "val": "FORGED NAME" },
                { "typ": "preferred_username", "val": "forged@example.test" },
                { "typ": "roles", "val": "Admin" }
              ]
            }
            """;
        var repository = Substitute.For<IOperatorAssignmentRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        repository.UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var response = await Sut(repository, VerifiedValidator()).GetRolesAsync(
            MockHttpRequestData.CreatePostRaw(payload, "http://localhost/api/GetRoles"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("Roles").EnumerateArray()
            .Select(role => role.GetString()).ShouldBe([AuthorizationMiddleware.RoleAdmin]);

        // The registered row carries the TOKEN's identity, roles, and a non-null sign-in time
        // (this is the sign-in path, not the /operators/me read path).
        await repository.Received(1).UpsertAsync(
            Arg.Is<OperatorAssignment>(a =>
                a.ObjectId == TokenOid &&
                a.DisplayName == TokenDisplayName &&
                a.UserPrincipalName == TokenUpn &&
                a.RolesAtSignIn != null &&
                a.RolesAtSignIn.SequenceEqual(new[] { AuthorizationMiddleware.RoleAdmin }) &&
                a.LastSignInUtc == FixedNow.UtcDateTime),
            Arg.Any<CancellationToken>());
        // The payload's SWA userId is aliased to the token's oid so later /api/* calls resolve scope.
        await repository.Received(1).UpsertDirectoryAliasAsync("swa-user-123", TokenOid, Arg.Any<CancellationToken>());

        // The forged payload identity never reached storage.
        await repository.DidNotReceive().UpsertAsync(
            Arg.Is<OperatorAssignment>(a => a.ObjectId == "FORGED-OID" || a.DisplayName == "FORGED NAME"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRolesAsync_PayloadOidAndTokenOidDisagree_RegistersTheTokenOidNotThePayloadOid()
    {
        // Core security assertion: storage keys come from the cryptographically validated token.
        // A forged payload oid (and its Admin badge) must never reach the repository.
        const string payload = """
            {
              "identityProvider": "aad",
              "userId": "swa-user-123",
              "accessToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6InRlc3QifQ.signature",
              "claims": [
                { "typ": "oid", "val": "payload-oid" },
                { "typ": "name", "val": "Payload Name" },
                { "typ": "roles", "val": "Admin" }
              ]
            }
            """;
        var repository = Substitute.For<IOperatorAssignmentRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        repository.UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // The token validates to a DIFFERENT identity carrying only Operator.
        var response = await Sut(repository, VerifiedValidator(roles: [AuthorizationMiddleware.RoleOperator]))
            .GetRolesAsync(MockHttpRequestData.CreatePostRaw(payload, "http://localhost/api/GetRoles"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        await repository.Received(1).UpsertAsync(
            Arg.Is<OperatorAssignment>(a =>
                a.ObjectId == TokenOid &&
                a.RolesAtSignIn != null &&
                a.RolesAtSignIn.SequenceEqual(new[] { AuthorizationMiddleware.RoleOperator })),
            Arg.Any<CancellationToken>());
        await repository.Received(1).UpsertDirectoryAliasAsync("swa-user-123", TokenOid, Arg.Any<CancellationToken>());

        // The payload's forged oid and Admin badge were never persisted.
        await repository.DidNotReceive().UpsertAsync(
            Arg.Is<OperatorAssignment>(a => a.ObjectId == "payload-oid" ||
                (a.RolesAtSignIn != null && a.RolesAtSignIn.Contains(AuthorizationMiddleware.RoleAdmin))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRolesAsync_VerifiedSignInWithBlankUserId_RegistersOperatorButSkipsAliasWrite()
    {
        const string payload = """
            {
              "identityProvider": "aad",
              "userId": "",
              "accessToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6InRlc3QifQ.signature",
              "claims": [
                { "typ": "roles", "val": "Operator" }
              ]
            }
            """;
        var repository = Substitute.For<IOperatorAssignmentRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        repository.UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var response = await Sut(repository, VerifiedValidator()).GetRolesAsync(
            MockHttpRequestData.CreatePostRaw(payload, "http://localhost/api/GetRoles"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await repository.Received(1).UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceiveWithAnyArgs().UpsertDirectoryAliasAsync(default!, default!, default!);
    }

    [Fact]
    public async Task GetRolesAsync_ValidatorThrows_StillReturnsMappedRoles()
    {
        // The whole registration step is fail-soft: even a validator fault must not break the
        // roles response SWA's sign-in depends on.
        const string payload = """
            {
              "identityProvider": "aad",
              "accessToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6InRlc3QifQ.signature",
              "claims": [
                { "typ": "roles", "val": "Admin" }
              ]
            }
            """;
        var repository = Substitute.For<IOperatorAssignmentRepository>();

        var response = await Sut(
            repository,
            new StubSwaRolesSourceTokenValidator(() => throw new InvalidOperationException("validation exploded")))
            .GetRolesAsync(MockHttpRequestData.CreatePostRaw(payload, "http://localhost/api/GetRoles"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("Roles").EnumerateArray()
            .Select(role => role.GetString()).ShouldBe([AuthorizationMiddleware.RoleAdmin]);
        await repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default!);
        await repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!);
        await repository.DidNotReceiveWithAnyArgs().UpsertDirectoryAliasAsync(default!, default!, default!);
    }

    [Fact]
    public async Task GetRolesAsync_RegistrationLookupFails_StillReturnsMappedRoles()
    {
        // Storage failure inside the registrar's lookup: the operator is simply not recorded
        // this time, the alias write still proceeds, and the roles response is untouched.
        const string payload = """
            {
              "identityProvider": "aad",
              "userId": "swa-user-123",
              "accessToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6InRlc3QifQ.signature",
              "claims": [
                { "typ": "roles", "val": "Operator" }
              ]
            }
            """;
        var repository = Substitute.For<IOperatorAssignmentRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Failure("table down"));

        var response = await Sut(repository, VerifiedValidator()).GetRolesAsync(
            MockHttpRequestData.CreatePostRaw(payload, "http://localhost/api/GetRoles"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("Roles").EnumerateArray()
            .Select(role => role.GetString()).ShouldBe([AuthorizationMiddleware.RoleOperator]);
        await repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default!);
        // The alias write is independent of registration and still went through.
        await repository.Received(1).UpsertDirectoryAliasAsync("swa-user-123", TokenOid, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRolesAsync_AliasWriteThrows_StillReturnsMappedRoles()
    {
        // Alias storage fault after a successful registration: the roles response must still
        // come back, and the operator row written before the fault stays.
        const string payload = """
            {
              "identityProvider": "aad",
              "userId": "swa-user-123",
              "accessToken": "eyJhbGciOiJSUzI1NiIsImtpZCI6InRlc3QifQ.signature",
              "claims": [
                { "typ": "roles", "val": "Operator" }
              ]
            }
            """;
        var repository = Substitute.For<IOperatorAssignmentRepository>();
        repository.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        repository.UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        repository.UpsertDirectoryAliasAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Result>(new InvalidOperationException("alias table down")));

        var response = await Sut(repository, VerifiedValidator()).GetRolesAsync(
            MockHttpRequestData.CreatePostRaw(payload, "http://localhost/api/GetRoles"), Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("Roles").EnumerateArray()
            .Select(role => role.GetString()).ShouldBe([AuthorizationMiddleware.RoleOperator]);
        await repository.Received(1).UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>());
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static RolesSourceHttpTrigger Sut(
        IOperatorAssignmentRepository repository,
        SwaRolesSourceTokenValidator validator)
    {
        return new RolesSourceHttpTrigger(
            validator,
            new OperatorRegistrar(repository, Substitute.For<ILogger<OperatorRegistrar>>()),
            repository,
            new FakeTimeProvider(FixedNow),
            Substitute.For<ILogger<RolesSourceHttpTrigger>>());
    }

    private static SwaRolesSourceTokenValidator UnverifiableValidator() =>
        new StubSwaRolesSourceTokenValidator(Option<ValidatedSignIn>.None);

    private static SwaRolesSourceTokenValidator VerifiedValidator(
        IReadOnlyList<string>? roles = null) =>
        new StubSwaRolesSourceTokenValidator(
            new ValidatedSignIn(TokenOid, TokenDisplayName, TokenUpn,
                roles ?? [AuthorizationMiddleware.RoleAdmin]));

    /// <summary>
    ///     Test double for <see cref="SwaRolesSourceTokenValidator" />. The real class performs
    ///     cryptographic AAD validation with no offline analogue; <see cref="ValidateAsync" /> is
    ///     virtual precisely so tests can substitute the verdict. The base constructor is fed a
    ///     substitute <see cref="IConfiguration" /> (blank settings make the real validator
    ///     inert) and a substitute logger; the verdict is the only thing that matters here.
    /// </summary>
    private sealed class StubSwaRolesSourceTokenValidator : SwaRolesSourceTokenValidator
    {
        private readonly Func<Option<ValidatedSignIn>> _verdict;

        public StubSwaRolesSourceTokenValidator(Option<ValidatedSignIn> verdict) : this(() => verdict) { }

        public StubSwaRolesSourceTokenValidator(Func<Option<ValidatedSignIn>> verdict)
            : base(Substitute.For<IConfiguration>(), Substitute.For<ILogger<SwaRolesSourceTokenValidator>>())
        {
            _verdict = verdict;
        }

        public override Task<Option<ValidatedSignIn>> ValidateAsync(
            string? accessToken, CancellationToken cancellationToken = default)
            => Task.FromResult(_verdict());
    }

    private static IReadOnlyList<string> Extract(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return RolesSourceHttpTrigger.ExtractAllowedRoles(doc.RootElement);
    }
}

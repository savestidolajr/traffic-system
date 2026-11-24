using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class OperatorRegistrarTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);
    private const string Oid = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private readonly IOperatorAssignmentRepository _repository =
        Substitute.For<IOperatorAssignmentRepository>();

    private readonly OperatorRegistrar _sut;

    public OperatorRegistrarTests()
    {
        _repository.UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _sut = new OperatorRegistrar(_repository, Substitute.For<ILogger<OperatorRegistrar>>());
    }

    [Fact]
    public async Task RegisterAsync_NoObjectId_WritesNothing()
    {
        // A Static Web Apps userId is not a directory object id, so the caller passes an empty
        // string rather than a row key nothing in Entra can explain.
        await _sut.RegisterAsync(Identity(objectId: ""), Now, CancellationToken.None);

        await _repository.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_FirstSighting_CreatesAnUnscopedRowCarryingTheTokenIdentity()
    {
        GivenNoExistingRow();

        await _sut.RegisterAsync(Identity(signIn: Now), Now, CancellationToken.None);

        var saved = await CapturedUpsert();
        saved.ObjectId.ShouldBe(Oid);
        saved.DisplayName.ShouldBe("Mock Editor");
        saved.UserPrincipalName.ShouldBe("mock.editor@arn.com.au");
        saved.RolesAtSignIn.ShouldBe(["Operator"]);
        saved.LastSignInUtc.ShouldBe(Now);
        saved.Markets.ShouldBeEmpty();
    }

    [Fact]
    public async Task RegisterAsync_ExistingRow_KeepsMarketsAndRefreshesIdentity()
    {
        GivenExistingRow(new OperatorAssignment(
            Oid, "Old Name", ["cairns-4ca"], Now.AddDays(-3), "admin",
            "old@arn.com.au", ["Operator"], Now.AddDays(-3)));

        await _sut.RegisterAsync(
            Identity(displayName: "Mock Editor", upn: "mock.editor@arn.com.au", roles: ["Admin"], signIn: Now),
            Now, CancellationToken.None);

        var saved = await CapturedUpsert();
        saved.Markets.ShouldBe(["cairns-4ca"]);
        saved.DisplayName.ShouldBe("Mock Editor");
        saved.UserPrincipalName.ShouldBe("mock.editor@arn.com.au");
        saved.RolesAtSignIn.ShouldBe(["Admin"]);
        saved.LastSignInUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task RegisterAsync_ReadPathWithNothingChanged_WritesNothing()
    {
        // /operators/me runs on every page load. Rewriting an unchanged row on each one would
        // turn a read into a write storm.
        GivenExistingRow(new OperatorAssignment(
            Oid, "Mock Editor", ["cairns-4ca"], Now.AddDays(-1), "admin",
            "mock.editor@arn.com.au", ["Operator"], Now.AddDays(-1)));

        await _sut.RegisterAsync(Identity(), Now, CancellationToken.None);

        await _repository.DidNotReceive().UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_ReadPathSeesNoRoles_KeepsTheRolesCapturedAtSignIn()
    {
        // Only a directory read can prove a role was removed. A path that simply did not see
        // roles must never erase them.
        GivenExistingRow(new OperatorAssignment(
            Oid, "Mock Editor", [], Now.AddDays(-1), "admin",
            "mock.editor@arn.com.au", ["Admin"], Now.AddDays(-1)));

        await _sut.RegisterAsync(
            Identity(displayName: "Ben Renamed", roles: []), Now, CancellationToken.None);

        var saved = await CapturedUpsert();
        saved.RolesAtSignIn.ShouldBe(["Admin"]);
        saved.DisplayName.ShouldBe("Ben Renamed");
    }

    [Fact]
    public async Task RegisterAsync_LookupFails_DoesNotWriteAndDoesNotThrow()
    {
        _repository.GetAsync(Oid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Failure("table unavailable"));

        await _sut.RegisterAsync(Identity(signIn: Now), Now, CancellationToken.None);

        await _repository.DidNotReceive().UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_UpsertThrows_IsSwallowedSoSignInStillSucceeds()
    {
        GivenNoExistingRow();
        _repository.UpsertAsync(Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result>>(_ => throw new InvalidOperationException("storage exploded"));

        await _sut.RegisterAsync(Identity(signIn: Now), Now, CancellationToken.None);
    }

    [Fact]
    public async Task RegisterAsync_SignInTimestampWithinDebounceWindow_WritesNothing()
    {
        GivenExistingRow(new OperatorAssignment(
            Oid, "Mock Editor", ["cairns-4ca"], Now.AddDays(-1), "admin",
            "mock.editor@arn.com.au", ["Operator"], Now.AddSeconds(-10)));

        var result = await _sut.RegisterAsync(Identity(signIn: Now), Now, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsSome.ShouldBeTrue();
        result.Value.Value.LastSignInUtc.ShouldBe(Now.AddSeconds(-10));
        await _repository.DidNotReceive().UpsertAsync(
            Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_DueSignInTimestamp_UpdatesTimestamp()
    {
        GivenExistingRow(new OperatorAssignment(
            Oid, "Mock Editor", ["cairns-4ca"], Now.AddDays(-1), "admin",
            "mock.editor@arn.com.au", ["Operator"], Now.AddSeconds(-61)));

        var result = await _sut.RegisterAsync(Identity(signIn: Now), Now, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var saved = await CapturedUpsert();
        saved.Markets.ShouldBe(["cairns-4ca"]);
        saved.LastSignInUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task RegisterAsync_ClockSkewDoesNotRegressLastSignInTimestamp()
    {
        GivenExistingRow(new OperatorAssignment(
            Oid, "Mock Editor", ["cairns-4ca"], Now.AddDays(-1), "admin",
            "mock.editor@arn.com.au", ["Operator"], Now));

        var result = await _sut.RegisterAsync(
            Identity(displayName: "Updated Name", signIn: Now.AddHours(-1)), Now, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var saved = await CapturedUpsert();
        saved.DisplayName.ShouldBe("Updated Name");
        saved.LastSignInUtc.ShouldBe(Now);
    }

    [Fact]
    public async Task RegisterAsync_ConcurrentSameObject_MergesIdentityAndWritesOnce()
    {
        const string objectId = "coalesced-object";
        // Hold the assignment lookup open so the second caller provably arrives while the first
        // registration is still in flight. Without the gate a descheduled test thread lets the
        // first flight finish first, which is a sequential pair rather than a coalesced one.
        var lookupGate = new TaskCompletionSource<Result<Option<OperatorAssignment>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _repository.GetAsync(objectId, Arg.Any<CancellationToken>())
            .Returns(_ => lookupGate.Task);
        OperatorAssignment? saved = null;
        _repository.UpsertAsync(
                Arg.Do<OperatorAssignment>(assignment => saved = assignment),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        var first = _sut.RegisterAsync(
            Identity(objectId, "First Name", "first@arn.com.au", ["Operator"], Now),
            Now,
            CancellationToken.None);
        var second = _sut.RegisterAsync(
            Identity(objectId, "Second Name", "second@arn.com.au", ["Admin"], Now.AddSeconds(1)),
            Now.AddSeconds(1),
            CancellationToken.None);

        lookupGate.SetResult(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        var results = await Task.WhenAll(first, second);

        results.ShouldAllBe(result => result.IsSuccess && result.Value.IsSome);
        await _repository.Received(1).UpsertAsync(
            Arg.Any<OperatorAssignment>(), Arg.Any<CancellationToken>());
        saved.ShouldNotBeNull();
        saved!.DisplayName.ShouldBe("Second Name");
        saved.UserPrincipalName.ShouldBe("second@arn.com.au");
        saved.RolesAtSignIn.ShouldBe(["Admin"]);
        saved.LastSignInUtc.ShouldBe(Now.AddSeconds(1));
    }

    private static OperatorIdentity Identity(
        string objectId = Oid,
        string displayName = "Mock Editor",
        string? upn = "mock.editor@arn.com.au",
        IReadOnlyList<string>? roles = null,
        DateTime? signIn = null)
        => new(objectId, displayName, upn, roles ?? ["Operator"], signIn);

    private void GivenNoExistingRow()
        => _repository.GetAsync(Oid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));

    private void GivenExistingRow(OperatorAssignment existing)
        => _repository.GetAsync(Oid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(existing)));

    private async Task<OperatorAssignment> CapturedUpsert()
    {
        var calls = _repository.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IOperatorAssignmentRepository.UpsertAsync))
            .Select(call => (OperatorAssignment)call.GetArguments()[0]!)
            .ToList();
        calls.Count.ShouldBe(1);
        await Task.CompletedTask;
        return calls[0];
    }
}

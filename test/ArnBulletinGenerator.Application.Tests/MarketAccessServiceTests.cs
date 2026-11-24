using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Access;

namespace ArnBulletinGenerator.Application.Tests;

public class MarketAccessServiceTests
{
    private readonly IOperatorAssignmentRepository _assignments = Substitute.For<IOperatorAssignmentRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly MarketAccessService _sut;

    private const string Oid = "00000000-0000-0000-0000-000000000001";
    private const string SwaUserId = "swa-user-42";
    private const string AliasedOid = "00000000-0000-0000-0000-000000000002";

    public MarketAccessServiceTests()
    {
        _sut = new MarketAccessService(_assignments, _jobs, Substitute.For<ILogger<MarketAccessService>>());
    }

    private void SetupAssignment(params string[] markets)
    {
        _assignments.GetAsync(Oid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(Oid, "Test Operator", markets, DateTime.UtcNow, "admin"))));
    }

    private void SetupJob(string jobId, string market)
    {
        var job = Job.Create(jobId, market, market, 30, DateTime.UtcNow).Value;
        _jobs.GetJobAsync(jobId, Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Success(job));
    }

    [Fact]
    public async Task Admin_IsAlwaysUnrestricted_WithoutRepositoryLookup()
    {
        var allowed = await _sut.GetAllowedMarketsAsync(Oid, isAdmin: true);

        allowed.HasValue.ShouldBeFalse();
        await _assignments.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
    }

    [Fact]
    public async Task OperatorWithoutAssignmentRecord_IsUnrestricted()
    {
        _assignments.GetAsync(Oid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        // The row-absent path consults the SWA→Entra alias; no alias recorded keeps this caller
        // unrestricted (the opt-in policy), exactly as before the alias hop existed.
        _assignments.GetObjectIdByAliasAsync(Oid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<string>>.Success(Option<string>.None));
        SetupJob("job-no-assignment", "sydney");

        var allowed = await _sut.GetAllowedMarketsAsync(Oid, isAdmin: false);
        var access = await _sut.EnsureCanActOnJobAsync(Oid, false, "job-no-assignment");

        allowed.HasValue.ShouldBeFalse();
        access.IsSuccess.ShouldBeTrue();
        await _jobs.DidNotReceiveWithAnyArgs().GetJobAsync(Arg.Any<string>(), default);
    }

    [Fact]
    public async Task RepositoryFailure_DeniesEveryMarket_RatherThanWidening()
    {
        // Until 2026-07-29 this asserted "fails open as unrestricted", so a transient Azure Table
        // fault silently granted every operator access to every market, logged only as a warning.
        // Not knowing someone's scope is not the same as them having none. The opt-in policy is
        // untouched — an operator with no assignment row is still unrestricted (see
        // OperatorWithoutAssignmentRecord_IsUnrestricted and EmptyAssignment_IsUnrestricted) —
        // so no working caller loses access; only the unknown case changed.
        _assignments.GetAsync(Oid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Failure("table unavailable"));

        var allowed = await _sut.GetAllowedMarketsAsync(Oid, isAdmin: false);
        var access = await _sut.EnsureCanActOnJobAsync(Oid, false, "job-1");

        allowed.HasValue.ShouldBeTrue();
        allowed.Value.ShouldBeEmpty();
        access.IsFailure.ShouldBeTrue();
        access.Error.ShouldBe(MarketAccessService.UnverifiableAccessMessage);
        await _jobs.DidNotReceiveWithAnyArgs().GetJobAsync(Arg.Any<string>(), default);
    }

    [Fact]
    public async Task RepositoryThrows_DeniesEveryMarket_RatherThanWidening()
    {
        _assignments.GetAsync(Oid, Arg.Any<CancellationToken>())
            .Returns<Result<Option<OperatorAssignment>>>(_ => throw new InvalidOperationException("boom"));

        var access = await _sut.EnsureCanActOnJobAsync(Oid, false, "job-1");

        access.IsFailure.ShouldBeTrue();
        access.Error.ShouldBe(MarketAccessService.UnverifiableAccessMessage);
    }

    [Fact]
    public async Task ScopedOperator_CanActOnAssignedMarket_CaseInsensitive()
    {
        SetupAssignment("Gold Coast", "scottsdale");
        SetupJob("job-1", "gold coast");

        var access = await _sut.EnsureCanActOnJobAsync(Oid, false, "job-1");

        access.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ScopedOperator_BlockedFromUnassignedMarket_WithForbiddenMessage()
    {
        SetupAssignment("gold-coast");
        SetupJob("job-2", "sydney");

        var access = await _sut.EnsureCanActOnJobAsync(Oid, false, "job-2");

        access.IsFailure.ShouldBeTrue();
        access.Error.ShouldStartWith("Forbidden");
        access.Error.ShouldContain("sydney");
    }

    [Fact]
    public async Task EmptyAssignment_IsUnrestricted()
    {
        SetupAssignment(); // record exists with zero markets
        SetupJob("job-3", "sydney");

        var allowed = await _sut.GetAllowedMarketsAsync(Oid, isAdmin: false);
        var access = await _sut.EnsureCanActOnJobAsync(Oid, false, "job-3");

        allowed.HasValue.ShouldBeFalse();
        access.IsSuccess.ShouldBeTrue();
        await _jobs.DidNotReceiveWithAnyArgs().GetJobAsync(Arg.Any<string>(), default);
    }

    [Fact]
    public async Task JobNotFound_DefersToActionFor404()
    {
        SetupAssignment("sydney");
        _jobs.GetJobAsync("missing", Arg.Any<CancellationToken>())
            .Returns(Result<Job>.Failure("Job not found"));

        var access = await _sut.EnsureCanActOnJobAsync(Oid, false, "missing");

        access.IsSuccess.ShouldBeTrue(); // the action itself surfaces the proper 404
    }

    [Fact]
    public async Task MissingObjectId_DeniesEveryMarket_WithoutRepositoryLookup()
    {
        // Until 2026-07-29 this asserted "unrestricted": a non-admin caller we cannot identify
        // was granted every market. An unidentifiable caller has no scope to check, which is a
        // reason to deny, not to wave through.
        var allowed = await _sut.GetAllowedMarketsAsync("", isAdmin: false);

        allowed.HasValue.ShouldBeTrue();
        allowed.Value.ShouldBeEmpty();
        await _assignments.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
    }

    [Fact]
    public async Task SwaConsoleUserId_WithAlias_ResolvesToAssignedMarkets_NotUnrestricted()
    {
        // Regression: SWA never forwards the `claims` array to a linked backend, so
        // ClientPrincipal falls back to the SWA per-app userId; assignment rows are keyed by the
        // Entra oid, so every console operator missed their row and None meant unrestricted —
        // market scoping never bound on test or prod. The alias hop must restore the scope.
        _assignments.GetAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<string>>.Success(Option<string>.Some(AliasedOid)));
        _assignments.GetAsync(AliasedOid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(AliasedOid, "Console Operator", new[] { "sydney" }, DateTime.UtcNow, "admin"))));
        SetupJob("job-console-melb", "melbourne");

        var allowed = await _sut.GetAllowedMarketsAsync(SwaUserId, isAdmin: false);
        var blocked = await _sut.EnsureCanActOnJobAsync(SwaUserId, false, "job-console-melb");

        allowed.HasValue.ShouldBeTrue();
        allowed.Value.ShouldBe(new[] { "sydney" });
        // Scoping binds: an out-of-scope market is denied, not waved through.
        blocked.IsFailure.ShouldBeTrue();
        blocked.Error.ShouldStartWith("Forbidden");
        blocked.Error.ShouldContain("melbourne");
    }

    [Fact]
    public async Task EasyAuthOid_FoundOnFirstLookup_NeverConsultsAlias()
    {
        SetupAssignment("sydney");

        var allowed = await _sut.GetAllowedMarketsAsync(Oid, isAdmin: false);

        allowed.HasValue.ShouldBeTrue();
        allowed.Value.ShouldBe(new[] { "sydney" });
        await _assignments.DidNotReceiveWithAnyArgs().GetObjectIdByAliasAsync(default!, default);
    }

    [Fact]
    public async Task SwaUserId_NoAliasRecorded_IsUnrestricted()
    {
        _assignments.GetAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<string>>.Success(Option<string>.None));
        SetupJob("job-no-alias", "sydney");

        var allowed = await _sut.GetAllowedMarketsAsync(SwaUserId, isAdmin: false);
        var access = await _sut.EnsureCanActOnJobAsync(SwaUserId, false, "job-no-alias");

        allowed.HasValue.ShouldBeFalse();
        access.IsSuccess.ShouldBeTrue();
        await _jobs.DidNotReceiveWithAnyArgs().GetJobAsync(Arg.Any<string>(), default);
    }

    [Fact]
    public async Task AliasLookupFailure_DeniesEveryMarket_WithUnverifiableAccess()
    {
        _assignments.GetAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<string>>.Failure("alias table unavailable"));

        var allowed = await _sut.GetAllowedMarketsAsync(SwaUserId, isAdmin: false);
        var access = await _sut.EnsureCanActOnJobAsync(SwaUserId, false, "job-1");

        allowed.HasValue.ShouldBeTrue();
        allowed.Value.ShouldBeEmpty();
        access.IsFailure.ShouldBeTrue();
        access.Error.ShouldBe(MarketAccessService.UnverifiableAccessMessage);
        await _jobs.DidNotReceiveWithAnyArgs().GetJobAsync(Arg.Any<string>(), default);
    }

    [Fact]
    public async Task AliasLookupThrows_DeniesEveryMarket_WithUnverifiableAccess()
    {
        _assignments.GetAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns<Result<Option<string>>>(_ => throw new InvalidOperationException("alias boom"));

        var allowed = await _sut.GetAllowedMarketsAsync(SwaUserId, isAdmin: false);
        var access = await _sut.EnsureCanActOnJobAsync(SwaUserId, false, "job-1");

        allowed.HasValue.ShouldBeTrue();
        allowed.Value.ShouldBeEmpty();
        access.IsFailure.ShouldBeTrue();
        access.Error.ShouldBe(MarketAccessService.UnverifiableAccessMessage);
    }

    [Fact]
    public async Task AliasToOid_WithEmptyAssignment_IsUnrestricted()
    {
        _assignments.GetAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<string>>.Success(Option<string>.Some(AliasedOid)));
        _assignments.GetAsync(AliasedOid, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(AliasedOid, "Console Operator", Array.Empty<string>(), DateTime.UtcNow, "admin"))));
        SetupJob("job-empty-alias", "sydney");

        var allowed = await _sut.GetAllowedMarketsAsync(SwaUserId, isAdmin: false);
        var access = await _sut.EnsureCanActOnJobAsync(SwaUserId, false, "job-empty-alias");

        allowed.HasValue.ShouldBeFalse();
        access.IsSuccess.ShouldBeTrue();
        await _jobs.DidNotReceiveWithAnyArgs().GetJobAsync(Arg.Any<string>(), default);
    }

    [Fact]
    public async Task AliasPointingBackAtSameId_DoesNotLoop()
    {
        _assignments.GetAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(SwaUserId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<string>>.Success(Option<string>.Some(SwaUserId)));

        var allowed = await _sut.GetAllowedMarketsAsync(SwaUserId, isAdmin: false);

        allowed.HasValue.ShouldBeFalse();
        await _assignments.Received(1).GetAsync(SwaUserId, Arg.Any<CancellationToken>());
        await _assignments.Received(1).GetObjectIdByAliasAsync(SwaUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Admin_IsUnrestricted_WithoutTouchingRepositoryOrAlias()
    {
        var allowed = await _sut.GetAllowedMarketsAsync(SwaUserId, isAdmin: true);

        allowed.HasValue.ShouldBeFalse();
        await _assignments.DidNotReceiveWithAnyArgs().GetAsync(default!, default);
        await _assignments.DidNotReceiveWithAnyArgs().GetObjectIdByAliasAsync(default!, default);
    }
}

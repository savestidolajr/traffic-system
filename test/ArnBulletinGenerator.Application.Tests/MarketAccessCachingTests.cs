using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Application.Tests;

public sealed class MarketAccessCachingTests : IDisposable
{
    private const string ObjectId = "00000000-0000-0000-0000-000000000001";
    private const string AliasId = "swa-user-42";
    private const string AliasObjectId = "00000000-0000-0000-0000-000000000002";
    private readonly IOperatorAssignmentRepository _assignments = Substitute.For<IOperatorAssignmentRepository>();
    private readonly IJobRepository _jobs = Substitute.For<IJobRepository>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task PositiveScope_IsReusedUntilExpiry()
    {
        _assignments.GetAsync(ObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(ObjectId, "Operator", [" Sydney ", "sydney"], DateTime.UtcNow, "admin"))));
        var service = CreateService();

        var first = await service.GetAllowedMarketsAsync(ObjectId, false);
        var second = await service.GetAllowedMarketsAsync(ObjectId, false);

        first.Value.ShouldBe(new[] { "Sydney" });
        second.Value.ShouldBe(new[] { "Sydney" });
        await _assignments.Received(1).GetAsync(ObjectId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AliasScope_CachesBothAliasAndStableObjectId()
    {
        _assignments.GetAsync(AliasId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(AliasId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<string>>.Success(Option<string>.Some(AliasObjectId)));
        _assignments.GetAsync(AliasObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(AliasObjectId, "Operator", ["sydney"], DateTime.UtcNow, "admin"))));
        var service = CreateService();

        (await service.GetAllowedMarketsAsync(AliasId, false)).Value.ShouldBe(new[] { "sydney" });
        (await service.GetAllowedMarketsAsync(AliasId, false)).Value.ShouldBe(new[] { "sydney" });
        (await service.GetAllowedMarketsAsync(AliasObjectId, false)).Value.ShouldBe(new[] { "sydney" });

        await _assignments.Received(1).GetAsync(AliasId, Arg.Any<CancellationToken>());
        await _assignments.Received(1).GetObjectIdByAliasAsync(AliasId, Arg.Any<CancellationToken>());
        await _assignments.Received(1).GetAsync(AliasObjectId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AbsentScope_IsCachedAsUnrestrictedButFailureIsNeverCached()
    {
        _assignments.GetAsync(ObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(ObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<string>>.Success(Option<string>.None));
        var service = CreateService();

        (await service.GetAllowedMarketsAsync(ObjectId, false)).HasValue.ShouldBeFalse();
        (await service.GetAllowedMarketsAsync(ObjectId, false)).HasValue.ShouldBeFalse();
        await _assignments.Received(1).GetAsync(ObjectId, Arg.Any<CancellationToken>());
        await _assignments.Received(1).GetObjectIdByAliasAsync(ObjectId, Arg.Any<CancellationToken>());
        _cache.Remove(MarketAccessOptions.ScopeCacheKey(ObjectId));
        _assignments.GetAsync(ObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Failure("unavailable"));
        (await service.GetAllowedMarketsAsync(ObjectId, false)).Value.ShouldBeEmpty();
        (await service.GetAllowedMarketsAsync(ObjectId, false)).Value.ShouldBeEmpty();
        await _assignments.Received(3).GetAsync(ObjectId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PresentUnrestrictedAssignment_IsNeverCached()
    {
        _assignments.GetAsync(ObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(ObjectId, "Operator", [], DateTime.UtcNow, "admin"))));
        var service = CreateService();

        (await service.GetAllowedMarketsAsync(ObjectId, false)).HasValue.ShouldBeFalse();
        (await service.GetAllowedMarketsAsync(ObjectId, false)).HasValue.ShouldBeFalse();

        await _assignments.Received(2).GetAsync(ObjectId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignmentWriteInvalidation_RemovesTheCachedScopeImmediately()
    {
        _assignments.GetAsync(ObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(ObjectId, "Operator", ["sydney"], DateTime.UtcNow, "admin"))));
        var service = CreateService();

        (await service.GetAllowedMarketsAsync(ObjectId, false)).Value.ShouldBe(new[] { "sydney" });
        _assignments.GetAsync(ObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(ObjectId, "Operator", ["melbourne"], DateTime.UtcNow, "admin"))));

        service.InvalidateScope(ObjectId);

        (await service.GetAllowedMarketsAsync(ObjectId, false)).Value.ShouldBe(new[] { "melbourne" });
        await _assignments.Received(2).GetAsync(ObjectId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AliasWriteInvalidation_RemovesTheEarlierAbsentScopeImmediately()
    {
        _assignments.GetAsync(AliasId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.None));
        _assignments.GetObjectIdByAliasAsync(AliasId, Arg.Any<CancellationToken>())
            .Returns(
                Result<Option<string>>.Success(Option<string>.None),
                Result<Option<string>>.Success(Option<string>.Some(AliasObjectId)));
        _assignments.GetAsync(AliasObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(AliasObjectId, "Operator", ["sydney"], DateTime.UtcNow, "admin"))));
        var service = CreateService();

        (await service.GetAllowedMarketsAsync(AliasId, false)).HasValue.ShouldBeFalse();

        service.InvalidateAlias(AliasId);

        (await service.GetAllowedMarketsAsync(AliasId, false)).Value.ShouldBe(new[] { "sydney" });
        await _assignments.Received(2).GetAsync(AliasId, Arg.Any<CancellationToken>());
        await _assignments.Received(2).GetObjectIdByAliasAsync(AliasId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CacheDisabled_RestoresRepositoryLookupForEachCall()
    {
        _assignments.GetAsync(ObjectId, Arg.Any<CancellationToken>())
            .Returns(Result<Option<OperatorAssignment>>.Success(Option<OperatorAssignment>.Some(
                new OperatorAssignment(ObjectId, "Operator", ["sydney"], DateTime.UtcNow, "admin"))));
        var service = CreateService(cacheDisabled: true);

        await service.GetAllowedMarketsAsync(ObjectId, false);
        await service.GetAllowedMarketsAsync(ObjectId, false);

        await _assignments.Received(2).GetAsync(ObjectId, Arg.Any<CancellationToken>());
    }

    private MarketAccessService CreateService(bool cacheDisabled = false)
        => new(_assignments, _jobs, Substitute.For<ILogger<MarketAccessService>>(), _cache,
            Options.Create(new MarketAccessOptions
            {
                CacheDisabled = cacheDisabled,
                CacheTtlSeconds = 45,
                EmptyScopeCacheTtlSeconds = 5
            }));
}

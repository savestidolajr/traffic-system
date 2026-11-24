using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Persistence;
using ArnBulletinGenerator.Infrastructure.Tests.Persistence.Helpers;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ArnBulletinGenerator.Infrastructure.Persistence.TableStorageMapAttributesCacheRepository;

namespace ArnBulletinGenerator.Infrastructure.Tests.Persistence;

public class TableStorageMapAttributesCacheRepositoryTests
{
    private const string SampleRef = "here:cm:segment:221129749#+0.35801..0.98732";
    private readonly ILogger<TableStorageMapAttributesCacheRepository> _logger =
        Substitute.For<ILogger<TableStorageMapAttributesCacheRepository>>();
    private readonly TableClient _tableClient = Substitute.For<TableClient>();
    private readonly TableStorageMapAttributesCacheRepository _sut;

    public TableStorageMapAttributesCacheRepositoryTests()
    {
        _sut = new TableStorageMapAttributesCacheRepository(
            TableClientTestHelper.StubOptions(),
            TableClientTestHelper.StubCredential(),
            _logger);
        TableClientTestHelper.InjectMockTableClient(_sut, _tableClient);
    }

    [Fact]
    public async Task GetBatchAsync_EmptyInput_ReturnsEmptyDictionary()
    {
        var result = await _sut.GetBatchAsync([]);
        result.ShouldBeEmpty();
    }

    /// <summary>Seeds the one paged scan the repository now performs instead of a read per ref.</summary>
    private void SeedScan(params MapAttributesCacheEntity[] entities) =>
        _tableClient.QueryAsync<MapAttributesCacheEntity>(
                Arg.Any<string?>(), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable(entities));

    private static MapAttributesCacheEntity Entity(string segmentRef, string roadName)
    {
        string identity = HereSegmentReference.CacheIdentity(segmentRef);
        return new MapAttributesCacheEntity
        {
            PartitionKey = GetPartitionKey(identity),
            RowKey = Uri.EscapeDataString(identity),
            SegmentRef = identity,
            RoadName = roadName,
            FunctionalClass = 1,
            IsRamp = false,
            TravelDirection = "F",
            PhysicalLaneCount = 3,
            CachedAtUtc = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task GetBatchAsync_FoundEntities_ReturnsMappedSegmentRoads()
    {
        SeedScan(Entity(SampleRef, "M4 Western Motorway"));

        var result = await _sut.GetBatchAsync([SampleRef]);

        result.Count.ShouldBe(1);
        result.ContainsKey(SampleRef).ShouldBeTrue();
        result[SampleRef].Name.ShouldBe("M4 Western Motorway");
        result[SampleRef].FunctionalClass.ShouldBe(1);
        result[SampleRef].IsRamp.ShouldBeFalse();
        result[SampleRef].TravelDirection.ShouldBe("F");
        result[SampleRef].PhysicalLaneCount.ShouldBe(3);
    }

    [Fact]
    public async Task GetBatchAsync_ExpiredEntity_IsReturnedAsCacheMiss()
    {
        var expired = Entity(SampleRef, "Old Road Name");
        expired.CachedAtUtc = DateTimeOffset.UtcNow.AddHours(-25);
        SeedScan(expired);

        var result = await _sut.GetBatchAsync([SampleRef]);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetBatchAsync_UsesCanonicalSegmentIdAcrossOffsetVariants()
    {
        const string offsetVariant =
            "hrn:here:data::olp-here:rib-2:9320::367219725:here:cm:segment:221129749#-0.1..0.4";
        string? filter = null;
        _tableClient.QueryAsync<MapAttributesCacheEntity>(
                Arg.Do<string?>(value => filter ??= value), Arg.Any<int?>(),
                Arg.Any<IEnumerable<string>?>(), Arg.Any<CancellationToken>())
            .Returns(TableClientTestHelper.ToAsyncPageable<MapAttributesCacheEntity>(
                [Entity(SampleRef, "Canonical Road")]));

        var result = await _sut.GetBatchAsync([offsetVariant]);

        result[offsetVariant].Name.ShouldBe("Canonical Road");
        filter.ShouldNotBeNull();
        filter.ShouldContain("PartitionKey eq 'map-");
        filter.ShouldContain("RowKey eq '221129749'");
        _tableClient.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(TableClient.QueryAsync))
            .ShouldBe(1);
    }



    [Fact]
    public async Task GetBatchAsync_NotFound_ReturnsEmptyWithoutThrowing()
    {
        // Present in the cache, but not the ref being asked for: a miss is the caller's cue to
        // resolve from the map attributes API, never an error.
        SeedScan(Entity("here:cm:segment:999999999#+0.1..0.2", "Some Other Road"));

        var result = await _sut.GetBatchAsync([SampleRef]);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetBatchAsync_QueriesOneShardOnceForManyRefs()
    {
        // All three ids deliberately share one stable shard. Resolving a market's flow segments
        // must issue one filtered shard query, not one point read per reference.
        string second = "here:cm:segment:221129781#+0.1..0.9";
        string absent = "here:cm:segment:221129787#+0.0..1.0";
        SeedScan(Entity(SampleRef, "M4 Western Motorway"), Entity(second, "Pacific Motorway"));

        var first = await _sut.GetBatchAsync([SampleRef, second, absent]);

        first.Count.ShouldBe(2);
        _tableClient.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(TableClient.QueryAsync))
            .ShouldBe(1);
        _tableClient.ReceivedCalls()
            .Any(call => call.GetMethodInfo().Name == nameof(TableClient.GetEntityAsync))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task SaveBatchAsync_SubmitsTransaction()
    {
        const string secondRef = "here:cm:segment:221129750#+0.1..0.9";
        var roads = new Dictionary<string, SegmentRoad>
        {
            [SampleRef] = new("M4 Western Motorway", 1, false, "F", 3),
            [secondRef] = new("Pacific Motorway", 1, true, "T", 4)
        };
        await _sut.SaveBatchAsync(roads);

        var calls = _tableClient.ReceivedCalls().Select(c => c.GetMethodInfo().Name).ToList();
        calls.ShouldContain(nameof(TableClient.SubmitTransactionAsync));

    }
}

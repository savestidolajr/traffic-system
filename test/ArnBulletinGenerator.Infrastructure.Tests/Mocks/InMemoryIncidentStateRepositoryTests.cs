using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Tests.Mocks;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Mocks;

public class InMemoryIncidentStateRepositoryTests
{
    private readonly InMemoryIncidentStateRepository _sut = new();

    private static IncidentState CreateState(
        string marketId = "Sydney",
        string incidentId = "INC-1",
        DateTime? lastSeenAt = null,
        IncidentStateStatus status = IncidentStateStatus.Active)
    {
        return new IncidentState(
            incidentId,
            marketId,
            DateTime.UtcNow.AddHours(-1),
            lastSeenAt ?? DateTime.UtcNow,
            status,
            "CONSTRUCTION",
            3,
            500,
            "abc123",
            "HERE",
            DateTime.UtcNow);
    }

    // ── GetCurrentByMarketAsync ──

    [Fact]
    public async Task GetCurrentByMarketAsync_ReturnsMatchingStates()
    {
        await _sut.UpsertAsync(CreateState());
        await _sut.UpsertAsync(CreateState("Sydney", "INC-2"));
        await _sut.UpsertAsync(CreateState("Melbourne", "INC-3"));

        var result = await _sut.GetCurrentByMarketAsync("Sydney");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count().ShouldBe(2);
        result.Value.ShouldAllBe(s => s.MarketId == "Sydney");
    }

    [Fact]
    public async Task GetCurrentByMarketAsync_NoMatch_ReturnsEmptyCollection()
    {
        await _sut.UpsertAsync(CreateState());

        var result = await _sut.GetCurrentByMarketAsync("Brisbane");

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    // ── UpsertAsync ──

    [Fact]
    public async Task UpsertAsync_InsertsNewState()
    {
        var state = CreateState();

        var result = await _sut.UpsertAsync(state);

        result.IsSuccess.ShouldBeTrue();
        var fetched = await _sut.GetCurrentByMarketAsync("Sydney");
        fetched.Value.ShouldContain(s => s.IncidentId == "INC-1");
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExistingState()
    {
        var original = CreateState();
        await _sut.UpsertAsync(original);

        var updated = original with { Criticality = 5, DescriptionHash = "xyz789" };
        await _sut.UpsertAsync(updated);

        var fetched = await _sut.GetCurrentByMarketAsync("Sydney");
        fetched.Value.ShouldHaveSingleItem();
        fetched.Value.First().Criticality.ShouldBe(5);
        fetched.Value.First().DescriptionHash.ShouldBe("xyz789");
    }

    // ── MarkClearedAsync ──

    [Fact]
    public async Task MarkClearedAsync_SetsStatusToCleared()
    {
        await _sut.UpsertAsync(CreateState());
        var clearedAt = DateTime.UtcNow;

        var result = await _sut.MarkClearedAsync("Sydney", "INC-1", clearedAt);

        result.IsSuccess.ShouldBeTrue();
        var fetched = await _sut.GetCurrentByMarketAsync("Sydney");
        fetched.Value.First().Status.ShouldBe(IncidentStateStatus.Cleared);
        fetched.Value.First().UpdatedAt.ShouldBe(clearedAt);
    }

    [Fact]
    public async Task MarkClearedAsync_MissingKey_SucceedsAsNoOp()
    {
        var result = await _sut.MarkClearedAsync("Sydney", "MISSING", DateTime.UtcNow);

        result.IsSuccess.ShouldBeTrue();
    }

    // ── PurgeExpiredAsync ──

    [Fact]
    public async Task PurgeExpiredAsync_RemovesStatesOlderThanThreshold()
    {
        var cutoff = DateTime.UtcNow;
        await _sut.UpsertAsync(CreateState("Sydney", "OLD", cutoff.AddHours(-2)));
        await _sut.UpsertAsync(CreateState("Sydney", "NEW", cutoff.AddHours(1)));

        var result = await _sut.PurgeExpiredAsync("Sydney", cutoff);

        result.IsSuccess.ShouldBeTrue();
        var remaining = await _sut.GetCurrentByMarketAsync("Sydney");
        remaining.Value.ShouldContain(s => s.IncidentId == "NEW");
    }

    [Fact]
    public async Task PurgeExpiredAsync_OnlyAffectsSpecifiedMarket()
    {
        var cutoff = DateTime.UtcNow;
        await _sut.UpsertAsync(CreateState("Sydney", "OLD", cutoff.AddHours(-2)));
        await _sut.UpsertAsync(CreateState("Melbourne", "OLD-MEL", cutoff.AddHours(-2)));

        await _sut.PurgeExpiredAsync("Sydney", cutoff);

        var sydney = await _sut.GetCurrentByMarketAsync("Sydney");
        sydney.Value.ShouldBeEmpty();
        var melbourne = await _sut.GetCurrentByMarketAsync("Melbourne");
        melbourne.Value.ShouldHaveSingleItem();
    }
}

using System.Security.Cryptography;
using System.Text;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Jobs;

namespace ArnBulletinGenerator.Application.Tests;

public class IncidentStateServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OneHourAgo = FixedNow.AddHours(-1);
    private readonly ILogger<IncidentStateService> _logger = Substitute.For<ILogger<IncidentStateService>>();

    private readonly IIncidentStateRepository _repository = Substitute.For<IIncidentStateRepository>();
    private readonly IncidentStateService _sut;

    public IncidentStateServiceTests()
    {
        var timeProvider = Substitute.For<TimeProvider>();
        timeProvider.GetUtcNow().Returns(new DateTimeOffset(FixedNow));
        _sut = new IncidentStateService(_repository, _logger, Options.Create(new IncidentStateOptions()),
            timeProvider);
    }

    private void SetupExistingStates(string marketId, params IncidentState[] states)
    {
        _repository.GetCurrentByMarketAsync(marketId, Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Success(states));
    }

    private void SetupEmptyMarket(string marketId)
    {
        SetupExistingStates(marketId);
    }

    private static TrafficIncident Incident(string id, string desc, int severity, string location, string type,
        int lengthM = 1000)
    {
        return new TrafficIncident(id, desc, severity, location, type, lengthM);
    }

    private static IncidentState ExistingState(string id, string marketId, int criticality = 3,
        string type = "congestion",
        int lengthM = 1000, string description = "Heavy traffic on I-5")
    {
        return new IncidentState(id, marketId, OneHourAgo, OneHourAgo,
            IncidentStateStatus.Active, type, criticality, lengthM, TestHash(description), "HERE", OneHourAgo)
        {
            LastReportedAt = Option<DateTime>.Some(OneHourAgo),
            RoadName = Option<string>.Some("I-5 North")
        };
    }

    private static string TestHash(string input)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private async Task<IncidentDeltaContext> ComputeAndExpectSome(string marketId, List<TrafficIncident> incidents,
        Option<string> tz = default)
    {
        var timezone = tz.HasValue ? tz : Option<string>.Some("UTC");
        var result = await _sut.ComputeAndPersistDeltaAsync(marketId, timezone, incidents);
        result.IsSome.ShouldBeTrue();
        return result.Value;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceMarketId_ReturnsNone(string marketId)
    {
        (await _sut.ComputeAndPersistDeltaAsync(marketId, Option<string>.None, [])).IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task RepositoryFailure_ReturnsNone()
    {
        _repository.GetCurrentByMarketAsync("test-market", Arg.Any<CancellationToken>())
            .Returns(Result<IEnumerable<IncidentState>>.Failure("Repository error"));

        (await _sut.ComputeAndPersistDeltaAsync("test-market", Option<string>.Some("UTC"), [])).IsNone.ShouldBeTrue();
    }

    [Fact]
    public async Task NewIncidents_ReturnsDeltaWithNewItems()
    {
        SetupEmptyMarket("test-market");
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("incident-1", "Heavy traffic on I-5", 3, "I-5 North", "congestion")]);

        var item = delta.NewIncidents.ShouldHaveSingleItem();
        item.IncidentId.ShouldBe("incident-1");
        item.Type.ShouldBe("congestion");
        item.Criticality.ShouldBe(3);
        item.RoadName.ShouldBe(Option<string>.Some("I-5 North"));
        item.Direction.ShouldBe(Option<string>.None);
        delta.UpdatedIncidents.ShouldBeEmpty();
        delta.ClearedIncidents.ShouldBeEmpty();
        delta.UnchangedIncidents.ShouldBeEmpty();

        await _repository.Received().UpsertBatchAsync(Arg.Is<IEnumerable<IncidentState>>(list =>
                list.Any(s => s.IncidentId == "incident-1" && s.MarketId == "test-market" && s.Status == IncidentStateStatus.Active)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DirectionInDescription_ExtractsDirection()
    {
        SetupEmptyMarket("test-market");
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("incident-1", "Heavy traffic northbound on I-5", 3, "I-5 North", "congestion")]);

        delta.NewIncidents[0].Direction.ShouldBe(Option<string>.Some("northbound"));
    }

    [Fact]
    public async Task UnchangedIncidents_ReturnsDeltaWithUnchanged()
    {
        SetupExistingStates("test-market", ExistingState("incident-1", "test-market"));
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("incident-1", "Heavy traffic on I-5", 3, "I-5 North", "congestion")]);

        delta.NewIncidents.ShouldBeEmpty();
        delta.UpdatedIncidents.ShouldBeEmpty();
        delta.ClearedIncidents.ShouldBeEmpty();
        delta.UnchangedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("incident-1");
    }

    [Fact]
    public async Task StableSourceIdentity_PreservesStateAcrossProviderRecordRevision()
    {
        const string stableStateId = "here:original:here-event-42";
        SetupExistingStates("test-market", ExistingState(stableStateId, "test-market"));

        var revised = Incident(
            "here-record-v2", "Heavy traffic on I-5", 3, "I-5 North", "congestion") with
        {
            SourceRecordIdentities =
                [new SourceRecordIdentity("here", "here-record-v2", "here-event-42")]
        };

        var delta = await ComputeAndExpectSome("test-market", [revised]);

        delta.UnchangedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("here-record-v2");

        await _repository.Received().UpsertBatchAsync(Arg.Is<IEnumerable<IncidentState>>(list =>
                list.Any(state => state.IncidentId == stableStateId && state.Status == IncidentStateStatus.Active)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdatedIncidents_ReturnsDeltaWithUpdated()
    {
        SetupExistingStates("test-market", ExistingState("incident-1", "test-market", 3));
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("incident-1", "Heavy traffic on I-5", 4, "I-5 North", "congestion")]);

        delta.NewIncidents.ShouldBeEmpty();
        var updated = delta.UpdatedIncidents.ShouldHaveSingleItem();
        updated.IncidentId.ShouldBe("incident-1");
        updated.Criticality.ShouldBe(4);
        delta.ClearedIncidents.ShouldBeEmpty();
        delta.UnchangedIncidents.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClearedIncidents_ReturnsDeltaWithCleared()
    {
        SetupExistingStates("test-market", ExistingState("incident-1", "test-market"));
        var delta = await ComputeAndExpectSome("test-market", []);

        delta.NewIncidents.ShouldBeEmpty();
        delta.UpdatedIncidents.ShouldBeEmpty();
        delta.ClearedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("incident-1");
        delta.UnchangedIncidents.ShouldBeEmpty();

        await _repository.Received().UpsertBatchAsync(Arg.Is<IEnumerable<IncidentState>>(list =>
                list.Any(s => s.IncidentId == "incident-1" && s.Status == IncidentStateStatus.Cleared)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenericLocation_NormalizesToNone()
    {
        SetupEmptyMarket("test-market");
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("incident-1", "Heavy traffic", 3, "Unidentified Road", "congestion")]);

        delta.NewIncidents[0].RoadName.ShouldBe(Option<string>.None);
    }

    [Fact]
    public async Task ValidTimezone_DoesNotThrow()
    {
        SetupEmptyMarket("test-market");
        var result = await _sut.ComputeAndPersistDeltaAsync("test-market", Option<string>.Some("Australia/Sydney"), []);
        result.IsSome.ShouldBeTrue();
    }

    [Fact]
    public async Task InvalidTimezone_ReturnsNone()
    {
        SetupEmptyMarket("test-market");
        var result = await _sut.ComputeAndPersistDeltaAsync("test-market", Option<string>.Some("Invalid/Timezone"), []);

        result.IsNone.ShouldBeTrue();
    }

    // ── HasMeaningfulChange individual triggers ──

    [Fact]
    public async Task HasMeaningfulChange_TypeChange_DetectedAsUpdated()
    {
        SetupExistingStates("test-market", ExistingState("i-1", "test-market", type: "congestion"));
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("i-1", "Heavy traffic on I-5", 3, "I-5 North", "accident")]);

        delta.UpdatedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("i-1");
    }

    [Fact]
    public async Task HasMeaningfulChange_LengthDelta50_DetectedAsUpdated()
    {
        SetupExistingStates("test-market", ExistingState("i-1", "test-market", lengthM: 1000));
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("i-1", "Heavy traffic on I-5", 3, "I-5 North", "congestion", 1050)]);

        delta.UpdatedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("i-1");
    }

    [Fact]
    public async Task HasMeaningfulChange_LengthDeltaBelow50_NotUpdated()
    {
        SetupExistingStates("test-market", ExistingState("i-1", "test-market", lengthM: 1000));
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("i-1", "Heavy traffic on I-5", 3, "I-5 North", "congestion", 1049)]);

        delta.UnchangedIncidents.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task HasMeaningfulChange_RoadNameChange_DetectedAsUpdated()
    {
        SetupExistingStates("test-market", ExistingState("i-1", "test-market"));
        // Existing state has RoadName = "I-5 North", new one will have "M1 South"
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("i-1", "Heavy traffic on I-5", 3, "M1 South", "congestion")]);

        delta.UpdatedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("i-1");
    }

    [Fact]
    public async Task HasMeaningfulChange_DirectionChange_DetectedAsUpdated()
    {
        // Existing state has Direction = None (ExistingState creates with description "Heavy traffic on I-5" → no direction)
        SetupExistingStates("test-market", ExistingState("i-1", "test-market"));
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("i-1", "Heavy traffic on I-5 southbound", 3, "I-5 North", "congestion")]);

        delta.UpdatedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("i-1");
    }

    [Fact]
    public async Task HasMeaningfulChange_DescriptionHashChange_DetectedAsUpdated()
    {
        SetupExistingStates("test-market", ExistingState("i-1", "test-market", description: "Heavy traffic on I-5"));
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("i-1", "Major accident blocking all lanes on I-5", 3, "I-5 North", "congestion")]);

        delta.UpdatedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("i-1");
    }

    // ── ExtractDirection all variants ──

    [Theory]
    [InlineData("Traffic congestion southbound on M1", "southbound")]
    [InlineData("Accident eastbound on A3", "eastbound")]
    [InlineData("Roadwork westbound lane closure", "westbound")]
    [InlineData("Traffic city-bound on bridge", "city-bound")]
    [InlineData("Delays citybound approach", "city-bound")]
    [InlineData("Slow inbound traffic from suburbs", "inbound")]
    [InlineData("Heavy outbound traffic this evening", "outbound")]
    public async Task ExtractDirection_AllVariants(string description, string expectedDirection)
    {
        SetupEmptyMarket("test-market");
        var delta = await ComputeAndExpectSome("test-market",
            [Incident("i-1", description, 3, "Main Road", "congestion")]);

        delta.NewIncidents[0].Direction.ShouldBe(Option<string>.Some(expectedDirection));
    }

    // ── Mixed scenario: new + updated + cleared + unchanged ──

    [Fact]
    public async Task MixedDelta_AllCategoriesInSameBatch()
    {
        var existingUnchanged = ExistingState("unchanged-1", "test-market");
        var existingUpdated = ExistingState("updated-1", "test-market", 2);
        var existingCleared = ExistingState("cleared-1", "test-market");

        SetupExistingStates("test-market", existingUnchanged, existingUpdated, existingCleared);

        var delta = await ComputeAndExpectSome("test-market",
        [
            Incident("unchanged-1", "Heavy traffic on I-5", 3, "I-5 North", "congestion"),
            Incident("updated-1", "Heavy traffic on I-5", 5, "I-5 North", "congestion"), // criticality changed 2→5
            Incident("new-1", "New accident on M2", 4, "M2 East", "accident")
        ]);

        delta.NewIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("new-1");
        delta.UpdatedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("updated-1");
        delta.ClearedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("cleared-1");
        delta.UnchangedIncidents.ShouldHaveSingleItem().IncidentId.ShouldBe("unchanged-1");
    }
}

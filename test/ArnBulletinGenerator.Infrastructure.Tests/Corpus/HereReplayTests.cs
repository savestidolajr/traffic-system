using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Corpus;

/// <summary>
///     Deterministic, offline, quota-free replay of the HERE providers against the committed corpus
///     under <c>test/corpus/here</c>. These tests prove the recorded fixtures drive the real pipeline
///     end-to-end with zero network and zero credentials — the foundation for the bulletin eval harness.
/// </summary>
public sealed class HereReplayTests
{
    private static readonly (string Name, double Lat, double Lng, double RadiusKm)[] Markets =
    [
        ("sydney", -33.8688, 151.2093, 25),
        ("melbourne", -37.8136, 144.9631, 25),
        ("brisbane", -27.4698, 153.0251, 25),
        ("perth", -31.9505, 115.8605, 25),
        ("adelaide", -34.9285, 138.6007, 25),
        ("goldcoast", -28.0167, 153.4000, 20)
    ];

    [Fact]
    public async Task Replay_Sydney_ProducesCorridorsOfflineAndDeterministically()
    {
        var replay = HereCorpusReplay.Load("sydney");
        replay.Count.ShouldBeGreaterThan(0,
            "Sydney corpus is empty — capture and commit test/corpus/here first.");

        using var provider = HereReplay.BuildProvider(replay);
        using var scope = provider.CreateScope();
        var traffic = scope.ServiceProvider.GetRequiredService<ITrafficProvider>();

        var area = CoverageArea.Create(-33.8688, 151.2093, 25);
        area.IsSuccess.ShouldBeTrue();

        var first = await traffic.GetCorridorsAsync(area.Value, 5, CancellationToken.None);
        first.IsSuccess.ShouldBeTrue($"corridor aggregation failed: {first.Error}");

        var firstRoads = first.Value.Select(c => c.RoadName).ToList();
        firstRoads.ShouldNotBeEmpty();
        replay.Hits.ShouldBeGreaterThan(0, "no recorded exchanges were served");

        // Determinism: replaying the same corpus yields identical corridors.
        var second = await traffic.GetCorridorsAsync(area.Value, 5, CancellationToken.None);
        second.IsSuccess.ShouldBeTrue();
        second.Value.Select(c => c.RoadName).ToList().ShouldBe(firstRoads);
    }

    [Fact]
    public async Task Replay_Incidents_ServedFromCorpus_ForEveryMarket()
    {
        foreach (var market in Markets)
        {
            var replay = HereCorpusReplay.Load(market.Name);
            replay.Count.ShouldBeGreaterThan(0, $"{market.Name} corpus is empty.");

            using var provider = HereReplay.BuildProvider(replay);
            using var scope = provider.CreateScope();
            var traffic = scope.ServiceProvider.GetRequiredService<ITrafficProvider>();

            var area = CoverageArea.Create(market.Lat, market.Lng, market.RadiusKm);
            area.IsSuccess.ShouldBeTrue();

            var incidents = await traffic.GetIncidentsAsync(area.Value, CancellationToken.None);
            incidents.IsSuccess.ShouldBeTrue($"{market.Name}: incidents fetch failed — {incidents.Error}");
            incidents.Value.ShouldNotBeEmpty($"{market.Name}: no incidents replayed from corpus");
        }
    }

    [Fact]
    public void Signature_DropsApiKeyAndDepartureTime_SoCapturedAndReplayedUrisMatch()
    {
        var captured = new Uri(
            "https://router.hereapi.com/v8/routes?transportMode=car&origin=-33.8,151.2&destination=-33.9,151.3&departureTime=2026-06-05T03%3A17%3A19Z&return=summary,typicalDuration&apiKey=***");
        var replayed = new Uri(
            "https://router.hereapi.com/v8/routes?transportMode=car&origin=-33.8,151.2&destination=-33.9,151.3&departureTime=2026-06-05T09%3A42%3A00Z&return=summary,typicalDuration&apiKey=test-key");

        HereCorpusReplay.Signature("GET", replayed)
            .ShouldBe(HereCorpusReplay.Signature("GET", captured));
    }
}

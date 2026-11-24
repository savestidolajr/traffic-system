using System.Text.RegularExpressions;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Tests.Corpus;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.Eval;

/// <summary>
///     Deterministic, offline guard on the traffic→corridor stage: replays the committed HERE corpus
///     through the real corridor aggregation and asserts no corridor carries a placeholder/ID road name
///     (e.g. <c>Unknown-4386185267451476265</c>). Such names leak into the LLM prompt and get read on air,
///     so they must never survive aggregation. Fast and free — the inner-loop signal for traffic code.
/// </summary>
[Trait("Category", "Slow")]
public sealed partial class CorridorQualityTests
{
    private static readonly (string Market, double Lat, double Lng, double RadiusKm)[] Markets =
    [
        ("sydney", -33.8688, 151.2093, 25),
        ("melbourne", -37.8136, 144.9631, 25),
        ("brisbane", -27.4698, 153.0251, 25),
        ("perth", -31.9505, 115.8605, 25),
        ("adelaide", -34.9285, 138.6007, 25),
        ("goldcoast", -28.0167, 153.4000, 20)
    ];

    // A run of 5+ digits or the "Unknown-" prefix marks an unspeakable placeholder/ID road name.
    [GeneratedRegex(@"(?i:^unknown-)|\d{5,}")]
    private static partial Regex PlaceholderRoadName();

    [Fact]
    public async Task Corridors_FromReplayedCorpus_HaveNoPlaceholderRoadNames()
    {
        var offenders = new List<string>();

        foreach (var market in Markets)
        {
            var replay = HereCorpusReplay.Load(market.Market);
            replay.Count.ShouldBeGreaterThan(0, $"{market.Market} corpus is empty");

            using var provider = HereReplay.BuildProvider(replay);
            using var scope = provider.CreateScope();
            var traffic = scope.ServiceProvider.GetRequiredService<ITrafficProvider>();

            var area = CoverageArea.Create(market.Lat, market.Lng, market.RadiusKm);
            var corridors = await traffic.GetCorridorsAsync(area.Value, 10, CancellationToken.None);
            corridors.IsSuccess.ShouldBeTrue($"{market.Market}: {corridors.Error}");

            offenders.AddRange(corridors.Value
                .Select(c => c.RoadName)
                .Where(name => PlaceholderRoadName().IsMatch(name))
                .Select(name => $"{market.Market}: {name}"));
        }

        offenders.ShouldBeEmpty(
            $"placeholder/ID road names would be read on air:\n  {string.Join("\n  ", offenders)}");
    }
}

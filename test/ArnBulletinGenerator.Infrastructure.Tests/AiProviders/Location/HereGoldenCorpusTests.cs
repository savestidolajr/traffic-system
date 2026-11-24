using System.Net;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

/// <summary>
///     Golden corpus: two REAL HERE payloads captured live on 2026-08-17 (Sydney: 71 records of which
///     59 are months-long project sub-closures with bare "Closed" text — the pilot's phantom-closure
///     class; Cairns: 27 construction-dominated records) run through the full fetch path. These pin
///     the parser's measured behaviour: a change that shifts any invariant below is a visible,
///     reviewable diff — not a silent regression discovered on air.
/// </summary>
public class HereGoldenCorpusTests
{
    private static string AssetPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "HereGolden", name);

    private static HereTrafficProvider CreateProvider(string payload)
    {
        var handler = new StaticHandler(payload);
        var geocoding = Substitute.For<IGeocodingProvider>();
        geocoding.BatchReverseGeocodePlacesAsync(
                Arg.Any<IEnumerable<(double, double)>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IDictionary<int, GeocodedPlace>>.Success(new Dictionary<int, GeocodedPlace>()));
        var mapAttributes = Substitute.For<IMapAttributesProvider>();
        // The map is unavailable in this fixture: every identity resolves Unnamed, which the
        // eligibility boundary must answer by withholding claims — never by guessing a name.
        mapAttributes.GetSegmentRoadsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyDictionary<string, SegmentRoad>>.Success(
                new Dictionary<string, SegmentRoad>()));
        return new HereTrafficProvider(
            new HttpClient(handler),
            Options.Create(new HereOptions { ApiKey = "test_key" }),
            geocoding,
            Substitute.For<IRoutingProvider>(),
            Options.Create(new BroadcastOptions { DefaultTimezone = "Australia/Sydney" }),
            Substitute.For<ILogger<HereTrafficProvider>>(),
            mapAttributes);
    }

    [Theory]
    [InlineData("sydney-2026-08-17.json", -33.8688, 151.2093)]
    [InlineData("cairns-2026-08-17.json", -16.9186, 145.7781)]
    public async Task GoldenPayload_EveryIncidentCarriesAFactAndNoUnsupportedClosureSurvives(
        string asset, double lat, double lng)
    {
        var provider = CreateProvider(await File.ReadAllTextAsync(AssetPath(asset)));

        var result = await provider.GetIncidentsAsync(CoverageArea.Create(lat, lng, 25.0).Value);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error : "");
        var incidents = result.Value.ToList();
        incidents.ShouldNotBeEmpty();

        // Every live-pipeline record reduces to a fact — the boundary's precondition.
        // (Assert on ids, not records: Shouldly renders failures via ToString, and an empty
        // Option throws from a record's generated PrintMembers.)
        incidents.Where(i => i.Fact == null).Select(i => i.Id).ShouldBeEmpty();

        foreach (var incident in incidents.Where(i => !IncidentEligibility.IsWithheld(i)))
        {
            // The pilot's phantom class: no voiceable record may claim a closure the gate denies.
            if (incident.RoadClosed)
                ClaimGate.CanClaimClosure(incident.Fact!).Allowed.ShouldBeTrue(
                    $"{incident.Id} kept an unsupported closure claim");

            // No invented directions: a stated direction must have a source.
            if (incident.Direction.HasValue)
                incident.Fact!.DirectionSource.ShouldNotBe(DirectionSource.None,
                    $"{incident.Id} states a direction no record supports");
        }

        // Every reduction is ledgered: a reduced or flagged record must say why.
        incidents.Where(i => i.WithheldReason.HasValue)
            .Select(i => $"{i.Id}={i.WithheldReason.Value}")
            .Where(s => !s.Contains("=withheld:") && !s.Contains("=demoted:") && !s.Contains("=flagged:"))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task SydneyGolden_TheScheduledProjectClosureClassNeverAirsAsClosed()
    {
        // Measured on the raw payload: 59/71 records are roadClosure with months-long windows
        // (median entry age 326 h, windows to 693 days) — HERE's model of the Warringah Freeway
        // Upgrade class that operators rejected on pilot day one. With no map identity available,
        // NONE may survive as a voiceable closure claim.
        var provider = CreateProvider(await File.ReadAllTextAsync(AssetPath("sydney-2026-08-17.json")));

        var result = await provider.GetIncidentsAsync(CoverageArea.Create(-33.8688, 151.2093, 25.0).Value);

        result.IsSuccess.ShouldBeTrue();
        var voiceableClosures = result.Value
            .Where(i => !IncidentEligibility.IsWithheld(i) && i.RoadClosed)
            .ToList();
        voiceableClosures.ShouldBeEmpty(
            $"scheduled project sub-closures aired as live closures: " +
            string.Join(", ", voiceableClosures.Select(i => i.Id)));
    }

    private sealed class StaticHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload)
            });
    }
}

using System.Net;
using ArnBulletinGenerator.Domain.Services;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public sealed class OfficialFeedCancellationTests
{
    private static readonly CoverageArea Sydney = CoverageArea.Create(-33.8688, 151.2093, 25.0).Value;

    [Fact]
    public async Task LiveTrafficNswFeed_SharedBudgetCancelsSecondLayer()
    {
        using var budget = OfficialFeedCallBudget.Begin(TimeSpan.FromMilliseconds(30));
        var handler = new FirstSuccessThenWaitHandler("{\"type\":\"FeatureCollection\",\"features\":[]}");
        var feed = new LiveTrafficNswFeed(
            new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions()),
            NullLogger<LiveTrafficNswFeed>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(() => feed.GetEventsAsync(Sydney));

        handler.RequestCount.ShouldBe(2);
    }

    [Fact]
    public async Task MainRoadsWaFeed_SharedBudgetCancelsSecondLayer()
    {
        using var budget = OfficialFeedCallBudget.Begin(TimeSpan.FromMilliseconds(30));
        var handler = new FirstSuccessThenWaitHandler("{\"features\":[]}");
        var feed = new MainRoadsWaFeed(
            new HttpClient(handler),
            Options.Create(new OfficialFeedsOptions
            {
                WaTravelMapUrl = "https://wa.test/TravelInformation/MapServer"
            }),
            NullLogger<MainRoadsWaFeed>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(() => feed.GetEventsAsync(Sydney));

        handler.RequestCount.ShouldBe(2);
    }

    private sealed class FirstSuccessThenWaitHandler : HttpMessageHandler
    {
        private readonly string _firstResponse;
        private int _requestCount;

        internal FirstSuccessThenWaitHandler(string firstResponse)
        {
            _firstResponse = firstResponse;
        }

        internal int RequestCount => Volatile.Read(ref _requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref _requestCount);
            if (call == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_firstResponse)
                };
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

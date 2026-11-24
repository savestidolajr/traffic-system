using System.Net;
using ArnBulletinGenerator.Infrastructure.AiProviders.Location;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Infrastructure.Tests.AiProviders.Location;

public class HereMapAttributesProviderTests
{
    private const string FullRef =
        "hrn:here:data::olp-here:rib-2:9203::367219725:here:cm:segment:221129749#+0.35801..0.98732";

    private readonly MockHandler _handler = new();
    private readonly HereMapAttributesProvider _provider;

    public HereMapAttributesProviderTests()
    {
        _provider = new HereMapAttributesProvider(
            new HttpClient(_handler),
            Options.Create(new HereOptions { ApiKey = "test_key" }),
            new MemoryCache(new MemoryCacheOptions()),
            Substitute.For<ILogger<HereMapAttributesProvider>>());
    }

    /// <summary>
    ///     A literal '#' in a segment reference starts a URI FRAGMENT, so everything after it —
    ///     including the apiKey — never reaches HERE. Verified against the live API on 2026-08-13:
    ///     literal '#' returned 401 "No credentials found" while '%23' returned the segment's name.
    ///     This silently disabled segment-based road identity on the first dev deployment.
    /// </summary>
    [Fact]
    public async Task GetSegmentRoadsAsync_PercentEncodesTheFragmentCharacter()
    {
        _handler.SetResponse("""{"segments":[]}""", HttpStatusCode.OK);

        await _provider.GetSegmentRoadsAsync([FullRef]);

        _handler.LastUrl.ShouldNotBeNull();
        _handler.LastUrl.ShouldContain("%23");
        _handler.LastUrl.ShouldNotContain("#");
        _handler.LastUrl.ShouldContain("apiKey=test_key");
    }

    /// <summary>
    ///     HERE echoes segments back in its own compact form with its own offsets, so results are keyed
    ///     back to the caller's reference by segment id. The route-code prefix HERE puts on many names
    ///     ("1 / Kennedy Hwy" — 31 of 173 live segments carried one) is stripped: voiced verbatim it
    ///     would read as "one slash Kennedy Highway".
    /// </summary>
    [Fact]
    public async Task GetSegmentRoadsAsync_KeysByCallerReference_AndStripsRouteCodePrefix()
    {
        _handler.SetResponse(
            """
            {"segments":[{"segmentRef":"$0:22951232:$1:221129749#-0.51276..0.82629",
              "attributes":{
                "LINK_ATTRIBUTE_FCN":[{"FUNCTIONAL_CLASS":"2","RAMP":"N"}],
                "ROAD_GEOM_FCN":[{"NAME":"1 / Kennedy Hwy"}]}}]}
            """,
            HttpStatusCode.OK);

        var result = await _provider.GetSegmentRoadsAsync([FullRef]);

        result.IsSuccess.ShouldBeTrue();
        var road = result.Value[FullRef];
        road.Name.ShouldBe("Kennedy Hwy");
        road.FunctionalClass.ShouldBe(2);
        road.IsRamp.ShouldBeFalse();
    }

    [Fact]
    public async Task GetSegmentRoadsAsync_DeduplicatesOffsetVariantsBySegmentId()
    {
        const string offsetVariant =
            "hrn:here:data::olp-here:rib-2:9320::367219725:here:cm:segment:221129749#-0.1..0.4";
        _handler.SetResponse(
            """
            {"segments":[{"segmentRef":"$0:22951232:$1:221129749#-0.51276..0.82629",
              "attributes":{"LINK_ATTRIBUTE_FCN":[{"FUNCTIONAL_CLASS":"2","RAMP":"N"}],
              "ROAD_GEOM_FCN":[{"NAME":"Kennedy Hwy"}]}}]}
            """,
            HttpStatusCode.OK);

        var first = await _provider.GetSegmentRoadsAsync([FullRef, offsetVariant]);
        var second = await _provider.GetSegmentRoadsAsync([offsetVariant]);

        first.Value.Keys.ShouldBe([FullRef, offsetVariant], ignoreOrder: true);
        second.Value[offsetVariant].Name.ShouldBe("Kennedy Hwy");
        _handler.RequestCount.ShouldBe(1);
        _handler.LastUrl!.Split("here:cm:segment:221129749", StringSplitOptions.None).Length.ShouldBe(2);
    }

    /// <summary>Fail-soft: a failed lookup must be reported, never thrown, so names simply stay unverified.</summary>
    [Fact]
    public async Task GetSegmentRoadsAsync_ApiFailure_ReturnsFailureNotThrow()
    {
        _handler.SetResponse("nope", HttpStatusCode.Unauthorized);

        var result = await _provider.GetSegmentRoadsAsync([FullRef]);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task GetSegmentRoadsAsync_ConcurrentMissesShareOneHereBatch()
    {
        _handler.SetResponse(
            """
            {"segments":[{"segmentRef":"$0:22951232:$1:221129749#-0.51276..0.82629",
              "attributes":{"LINK_ATTRIBUTE_FCN":[{"FUNCTIONAL_CLASS":"2","RAMP":"N"}],
              "ROAD_GEOM_FCN":[{"NAME":"Kennedy Hwy"}]}}]}
            """,
            HttpStatusCode.OK);
        _handler.Blocker = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var first = _provider.GetSegmentRoadsAsync([FullRef]);
        await _handler.FirstRequest.Task;
        var second = _provider.GetSegmentRoadsAsync([FullRef]);
        await Task.Delay(20);

        _handler.RequestCount.ShouldBe(1);
        _handler.Blocker.SetResult(true);
        var results = await Task.WhenAll(first, second);
        results.ShouldAllBe(result => result.IsSuccess);
    }

    [Fact]
    public async Task GetSegmentRoadsAsync_NoResolvableReferences_SkipsTheCall()
    {
        var result = await _provider.GetSegmentRoadsAsync(["", "not-a-segment"]);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
        _handler.LastUrl.ShouldBeNull();
    }

    private sealed class MockHandler : HttpMessageHandler
    {
        private string _content = "";
        private HttpStatusCode _status = HttpStatusCode.OK;

        public string? LastUrl { get; private set; }
        public int RequestCount { get; private set; }
        public TaskCompletionSource<bool> FirstRequest { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool>? Blocker { get; set; }

        public void SetResponse(string content, HttpStatusCode status)
        {
            _content = content;
            _status = status;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            FirstRequest.TrySetResult(true);
            if (Blocker is not null)
                await Blocker.Task.WaitAsync(cancellationToken);

            LastUrl = request.RequestUri?.ToString();
            return new HttpResponseMessage
            {
                StatusCode = _status,
                Content = new StringContent(_content)
            };
        }
    }
}

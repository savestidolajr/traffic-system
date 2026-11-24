using System.Text;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class MarketAccessGuardTests
{
    private const string ObjectId = "00000000-0000-0000-0000-000000000001";
    private readonly IMarketAccessService _service = Substitute.For<IMarketAccessService>();
    private readonly ILogger _logger = Substitute.For<ILogger>();

    [Fact]
    public async Task LoadedJob_AssignedMarket_IsAllowedWithoutJobLookup()
    {
        _service.GetAllowedMarketsAsync(ObjectId, false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["sydney"]));
        var request = RequestFor(ObjectId, "Operator");
        var job = Job.Create("job-1", "sydney", "sydney", 30, DateTime.UtcNow).Value;

        var forbidden = await _service.EnsureMarketAccessAsync(
            request, job.Market, _logger, CancellationToken.None);

        forbidden.ShouldBeNull();
    }

    [Fact]
    public async Task LoadedJob_UnassignedMarket_IsForbidden()
    {
        _service.GetAllowedMarketsAsync(ObjectId, false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["melbourne"]));
        var request = RequestFor(ObjectId, "Operator");
        var job = Job.Create("job-2", "sydney", "sydney", 30, DateTime.UtcNow).Value;

        var forbidden = await _service.EnsureMarketAccessAsync(
            request, job.Market, _logger, CancellationToken.None);

        forbidden.ShouldNotBeNull();
        forbidden!.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        (await MockHttpRequestData.ReadResponseBodyAsync(forbidden)).ShouldContain("sydney");
    }

    [Fact]
    public async Task LoadedJob_Admin_IsAllowedRegardlessOfMarket()
    {
        _service.GetAllowedMarketsAsync(ObjectId, true, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.None);
        var request = RequestFor(ObjectId, "Admin");
        var job = Job.Create("job-3", "brisbane", "brisbane", 30, DateTime.UtcNow).Value;

        var forbidden = await _service.EnsureMarketAccessAsync(
            request, job.Market, _logger, CancellationToken.None);

        forbidden.ShouldBeNull();
    }

    private static HttpRequestData RequestFor(string objectId, string role)
    {
        var request = MockHttpRequestData.CreateGet("http://localhost/api/jobs/job-1");
        request.Headers.Add(ClientPrincipal.HeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes($$"""
            {
              "auth_typ": "aad",
              "claims": [
                { "typ": "oid", "val": "{{objectId}}" },
                { "typ": "roles", "val": "{{role}}" }
              ]
            }
            """)));
        return request;
    }
}

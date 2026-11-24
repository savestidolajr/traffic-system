using System.Net;
using ArnBulletinGenerator.Application.Access;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Auth;
using ArnBulletinGenerator.Functions.Triggers.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class PlayoutTrayHttpTriggerTests
{
    private readonly IFileDeliveryService _delivery = Substitute.For<IFileDeliveryService>();
    private readonly IMarketAccessService _marketAccess = Substitute.For<IMarketAccessService>();

    private readonly PlayoutTrayHttpTrigger _sut;

    public PlayoutTrayHttpTriggerTests()
    {
        _sut = new PlayoutTrayHttpTrigger(
            _delivery, _marketAccess, Substitute.For<ILogger<PlayoutTrayHttpTrigger>>());
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    [Fact]
    public async Task GetPlayoutTrayStatus_ReturnsFrozenCamelCaseContract()
    {
        _delivery.GetTrayStatusAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<PlayoutTrayStatus>.Success(new PlayoutTrayStatus(
                "SYD_TRAIN_TRAFF.wav", Enabled: true, AwaitingPickup: true,
                WrittenAtUtc: new DateTimeOffset(2026, 8, 11, 6, 14, 3, TimeSpan.Zero))));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetPlayoutTrayStatus(
                MockHttpRequestData.CreateGet("http://localhost/api/markets/sydney/playout-tray"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        // The on-air board reads these exact keys — a casing drift here silently breaks it.
        body.ShouldContain("\"fileName\":\"SYD_TRAIN_TRAFF.wav\"");
        body.ShouldContain("\"enabled\":true");
        body.ShouldContain("\"awaitingPickup\":true");
        body.ShouldContain("\"writtenAtUtc\":\"2026-08-11T06:14:03+00:00\"");
    }

    [Fact]
    public async Task GetPlayoutTrayStatus_DisabledDelivery_ReturnsEnabledFalse()
    {
        _delivery.GetTrayStatusAsync("sydney", Arg.Any<CancellationToken>())
            .Returns(Result<PlayoutTrayStatus>.Success(new PlayoutTrayStatus(
                "SYD_TRAIN_TRAFF.wav", Enabled: false, AwaitingPickup: false, WrittenAtUtc: null)));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetPlayoutTrayStatus(
                MockHttpRequestData.CreateGet("http://localhost/api/markets/sydney/playout-tray"),
                "sydney", Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"enabled\":false");
        body.ShouldContain("\"awaitingPickup\":false");
        body.ShouldContain("\"writtenAtUtc\":null");
    }

    [Fact]
    public async Task GetPlayoutTrayStatus_EmptyMarket_ReturnsBadRequest()
    {
        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetPlayoutTrayStatus(
                MockHttpRequestData.CreateGet("http://localhost/api/markets//playout-tray"),
                "", Ctx()));
        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Market is required.");
    }

    [Fact]
    public async Task GetPlayoutTrayStatus_MarketWithNoConfiguredCode_ReturnsNotFound()
    {
        _delivery.GetTrayStatusAsync("darwin", Arg.Any<CancellationToken>())
            .Returns(Result<PlayoutTrayStatus>.Failure(
                "No Zetta market code is configured for market 'darwin'. Playout file names must use the "
                + "code ARN's Zetta import expects; the market id is not a substitute."));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetPlayoutTrayStatus(
                MockHttpRequestData.CreateGet("http://localhost/api/markets/darwin/playout-tray"),
                "darwin", Ctx()));
        status.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("darwin");
    }

    [Fact]
    public async Task GetPlayoutTrayStatus_OperatorNotAssignedToMarket_ReturnsForbidden()
    {
        var principal = CreatePrincipal("operator1", isAdmin: false);
        var req = MockHttpRequestData.CreateGet("http://localhost/api/markets/sydney/playout-tray");
        req.Headers.Add(ClientPrincipal.HeaderName, principal);

        _marketAccess.GetAllowedMarketsAsync("operator1", false, Arg.Any<CancellationToken>())
            .Returns(Option<IReadOnlyList<string>>.Some(["melbourne"]));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.GetPlayoutTrayStatus(req, "sydney", Ctx()));
        status.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain("Forbidden");
    }

    /// <summary>
    ///     Creates a base64-encoded X-MS-CLIENT-PRINCIPAL header value for a test principal.
    /// </summary>
    private static string CreatePrincipal(string name, bool isAdmin = false)
    {
        var roles = isAdmin ? new[] { "admin" } : new[] { "operator" };
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            auth_typ = "aad",
            name_typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
            claims = new[]
            {
                new { typ = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name", val = name },
                new { typ = "http://schemas.microsoft.com/identity/claims/objectidentifier", val = name },
                new { typ = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role", val = roles[0] }
            }
        });
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload));
    }
}

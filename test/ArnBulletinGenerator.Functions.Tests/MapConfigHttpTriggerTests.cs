using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Shouldly;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class MapConfigHttpTriggerTests
{
    [Fact]
    public async Task GetAsync_ReturnsConfiguredBrowserMapKey()
    {
        var sut = new MapConfigHttpTrigger(Options.Create(new HereOptions
        {
            ApiKey = "server-rest-key",
            MapApiKey = "browser-map-key",
        }), Options.Create(new ZettaOptions()));

        var response = await sut.GetAsync(MockHttpRequestData.CreateGet("http://localhost/api/map-config"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("HereApiKey").GetString().ShouldBe("browser-map-key");
    }

    [Fact]
    public async Task GetAsync_NoBrowserKeyConfigured_ReturnsEmptyString()
    {
        // Empty key ⇒ the console falls back to the keyless Leaflet basemap.
        var sut = new MapConfigHttpTrigger(
            Options.Create(new HereOptions { ApiKey = "server-rest-key" }),
            Options.Create(new ZettaOptions()));

        var response = await sut.GetAsync(MockHttpRequestData.CreateGet("http://localhost/api/map-config"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("HereApiKey").GetString().ShouldBe(string.Empty);
    }

    [Fact]
    public async Task GetAsync_NeverLeaksTheServerRestKey()
    {
        // The paid server-side REST key must never reach the browser via this endpoint.
        var sut = new MapConfigHttpTrigger(Options.Create(new HereOptions
        {
            ApiKey = "SECRET-server-rest-key",
            MapApiKey = "browser-map-key",
        }), Options.Create(new ZettaOptions()));

        var response = await sut.GetAsync(MockHttpRequestData.CreateGet("http://localhost/api/map-config"));

        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        body.ShouldNotContain("SECRET-server-rest-key");
    }

    /// <summary>
    ///     The console cannot describe what a station's Send to playout switch will do without
    ///     the environment-wide gate: delivery needs BOTH. Development runs with playout off and
    ///     would otherwise have had every station card claim a published bulletin reaches playout.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAsync_ReportsWhetherThisEnvironmentDeliversToPlayout(bool enabled)
    {
        var sut = new MapConfigHttpTrigger(
            Options.Create(new HereOptions { ApiKey = "server-rest-key" }),
            Options.Create(new ZettaOptions { Enabled = enabled }));

        var response = await sut.GetAsync(MockHttpRequestData.CreateGet("http://localhost/api/map-config"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await MockHttpRequestData.ReadResponseBodyAsync(response));
        doc.RootElement.GetProperty("PlayoutEnabled").GetBoolean().ShouldBe(enabled);
    }
}

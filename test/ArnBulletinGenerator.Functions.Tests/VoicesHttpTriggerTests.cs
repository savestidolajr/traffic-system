using System.Net;
using System.Text.Json;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Functions.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

public sealed class VoicesHttpTriggerTests
{
    private readonly VoicesHttpTrigger _sut;
    private readonly IVoiceProvider _voiceProvider = Substitute.For<IVoiceProvider>();

    public VoicesHttpTriggerTests()
    {
        var modelOptions = Options.Create(new ModelSelectionOptions { BroadcastModel = "test_model" });
        _sut = new VoicesHttpTrigger(_voiceProvider, modelOptions, Substitute.For<ILogger<VoicesHttpTrigger>>());
    }

    private static FunctionContext Ctx()
    {
        return MockHttpRequestData.CreateFunctionContext();
    }

    [Fact]
    public async Task ListVoices_ReturnsOkWithMappedVoiceDtoList()
    {
        var voices = new List<Voice>
        {
            new("v1", "Alice", Option<string>.Some("premium"), Option<string>.Some("Warm voice"),
                new Dictionary<string, string> { ["accent"] = "us" }) { ModelIds = ["m1", "m2"] },
            new("v2", "Bob", Option<string>.None, Option<string>.None,
                new Dictionary<string, string>())
        };
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success(voices));

        var req = MockHttpRequestData.CreateGet("http://localhost/api/voices");
        var response = await _sut.ListVoices(req, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        var result = JsonSerializer.Deserialize<VoiceListResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result!.Voices.Count().ShouldBe(2);
        result.Voices[0].VoiceId.ShouldBe("v1");
        result.Voices[0].Name.ShouldBe("Alice");
        result.Voices[0].Category.ShouldBe("premium");
        result.Voices[0].Description.ShouldBe("Warm voice");
        result.Voices[0].Labels.ShouldContainKey("accent");
        result.Voices[0].ModelIds.ShouldBe(["m1", "m2"]);
        // Resolver is invoked, not hardcoded to the preferred: base models ["m1","m2"] exclude
        // the preferred "test_model" and match no known base-model preference → first verified base.
        result.Voices[0].ResolvedModelId.ShouldBe("m1");
        result.Voices[1].VoiceId.ShouldBe("v2");
        result.Voices[1].Category.ShouldBeEmpty();
        result.Voices[1].Description.ShouldBeEmpty();
        // No base models → native to the preferred broadcast model.
        result.Voices[1].ResolvedModelId.ShouldBe("test_model");
    }

    [Fact]
    public async Task ListVoices_ProviderFails_Returns500()
    {
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Failure("API unavailable"));

        var req = MockHttpRequestData.CreateGet("http://localhost/api/voices");
        var response = await _sut.ListVoices(req, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        body.ShouldContain("API unavailable");
    }

    [Fact]
    public async Task ListVoices_EmptyList_Returns200WithEmptyArray()
    {
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success(Array.Empty<Voice>()));

        var req = MockHttpRequestData.CreateGet("http://localhost/api/voices");
        var response = await _sut.ListVoices(req, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        var result = JsonSerializer.Deserialize<VoiceListResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result!.Voices.ShouldBeEmpty();
    }
    [Fact]
    public async Task ListVoices_CanRemix_FlowsToResponseDto()
    {
        var voices = new List<Voice>
        {
            new("v1", "Alice", Option<string>.Some("premium"), Option<string>.Some("Warm voice"),
                new Dictionary<string, string> { ["accent"] = "us" })
            {
                CanRemix = true
            },
            new("v2", "Hudson", Option<string>.Some("professional"), Option<string>.Some("Copied voice"),
                new Dictionary<string, string>())
            {
                CanRemix = false
            }
        };
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success(voices));

        var req = MockHttpRequestData.CreateGet("http://localhost/api/voices");
        var response = await _sut.ListVoices(req, Ctx());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        string body = await MockHttpRequestData.ReadResponseBodyAsync(response);
        var result = JsonSerializer.Deserialize<VoiceListResponse>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result!.Voices.Count().ShouldBe(2);
        result.Voices[0].VoiceId.ShouldBe("v1");
        result.Voices[0].CanRemix.ShouldBeTrue();
        result.Voices[1].VoiceId.ShouldBe("v2");
        result.Voices[1].CanRemix.ShouldBeFalse();
    }
}

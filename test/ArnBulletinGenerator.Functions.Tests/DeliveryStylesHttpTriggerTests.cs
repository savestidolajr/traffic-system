using System.Net;
using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Errors;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using ArnBulletinGenerator.Infrastructure.Configuration;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.ConfigTransfer;
using ArnBulletinGenerator.Application.Libraries;
using ArnBulletinGenerator.Functions.Triggers.Http;

namespace ArnBulletinGenerator.Functions.Tests;

/// <summary>
///     Covers the request-body guards added to <see cref="DeliveryStylesHttpTrigger" />'s
///     Create/Update: Name/Description length caps (M2), Intensity range validation (M3), and a
///     case-insensitive duplicate-name guard on Create (L6).
/// </summary>
public sealed class DeliveryStylesHttpTriggerTests
{
    private readonly IDeliveryStyleService _deliveryStyles = Substitute.For<IDeliveryStyleService>();
    private readonly IScriptDirector _scriptDirector = Substitute.For<IScriptDirector>();
    private readonly IAudioSynthesizer _audioSynthesizer = Substitute.For<IAudioSynthesizer>();
    private readonly IBroadcastOutput _broadcastOutput = Substitute.For<IBroadcastOutput>();
    private readonly IAudioUrlProvider _audioUrlProvider = Substitute.For<IAudioUrlProvider>();
    private readonly IAudioProcessor _audioProcessor = Substitute.For<IAudioProcessor>();
    private readonly DeliveryStylesHttpTrigger _sut;

    public DeliveryStylesHttpTriggerTests()
    {
        _sut = new DeliveryStylesHttpTrigger(
            _deliveryStyles,
            _scriptDirector,
            _audioSynthesizer,
            _broadcastOutput,
            _audioUrlProvider,
            _audioProcessor,
            Options.Create(new ElevenLabsOptions()),
            Options.Create(new BroadcastOptions()),
            TimeProvider.System,
            Substitute.For<ILogger<DeliveryStylesHttpTrigger>>());

        // Default: no existing styles, so duplicate-name tests that don't care about it never
        // hit an unconfigured (null) Task<Result<...>> from NSubstitute.
        _deliveryStyles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<DeliveryStyle>>.Success([]));

        // Preview happy path: the director leaves the script alone, synthesis succeeds, the
        // audio saves, and the URL/duration resolve — so preview tests only configure what they
        // assert on.
        _scriptDirector.DirectAsync(
                Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("Good morning — here is your traffic update."));
        _audioSynthesizer.SynthesizeAsync(
                Arg.Any<string>(), Arg.Any<Option<VoiceSettings>>(), Arg.Any<Option<string>>(),
                Arg.Any<Option<string>>(), Arg.Any<CancellationToken>(), Arg.Any<Option<string>>())
            .Returns(Result<SynthesizedAudio>.Success(new SynthesizedAudio([1, 2, 3])));
        _broadcastOutput.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _audioUrlProvider.GetAudioUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result<string>.Success("http://audio/deliverypreview.wav"));
        _audioProcessor.GetDuration(Arg.Any<byte[]>()).Returns(12.0);
    }

    private static FunctionContext Ctx() => MockHttpRequestData.CreateFunctionContext();

    private static DeliveryStylePolicyDto ValidPolicy(int intensity = 2) =>
        new("Warm and calm.", ["[calm]"], intensity);

    private static CreateDeliveryStyleRequestDto ValidCreateRequest(string name = "Drive-Time", int intensity = 2) =>
        new(name, "A description.", ValidPolicy(intensity));

    private static DeliveryStyle ExistingStyle(string name) =>
        DeliveryStyle.Create(name, null, ValidPolicy().ToDomain(), null, null, DateTime.UtcNow).Value;

    [Fact]
    public async Task Create_NameTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest(new string('a', ConfigValidationLimits.DeliveryStyleNameMax + 1));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/delivery-styles"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Name must be at most");
    }

    [Fact]
    public async Task Create_DescriptionTooLong_ReturnsBadRequest()
    {
        var request = ValidCreateRequest() with
        {
            Description = new string('d', ConfigValidationLimits.DeliveryStyleDescriptionMax + 1)
        };

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/delivery-styles"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Description must be at most");
    }

    [Theory]
    [InlineData(99999)]
    [InlineData(-50)]
    public async Task Create_IntensityOutOfRange_ReturnsBadRequest(int intensity)
    {
        var request = ValidCreateRequest(intensity: intensity);

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/delivery-styles"), Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Intensity must be between");
        await _deliveryStyles.DidNotReceive().CreateAsync(
            Arg.Any<CreateDeliveryStyleInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        _deliveryStyles.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<DeliveryStyle>>.Success([ExistingStyle("Drive-Time")]));

        // Case-insensitive match against the existing style's name.
        var request = ValidCreateRequest("drive-time");

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/delivery-styles"), Ctx()));

        status.ShouldBe(HttpStatusCode.Conflict);
        body.ShouldContain("already exists");
        await _deliveryStyles.DidNotReceive().CreateAsync(
            Arg.Any<CreateDeliveryStyleInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_ValidRequest_Succeeds()
    {
        var created = ExistingStyle("Drive-Time");
        _deliveryStyles.CreateAsync(Arg.Any<CreateDeliveryStyleInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeliveryStyle>.Success(created));

        var request = ValidCreateRequest();

        (var status, _) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Create(MockHttpRequestData.CreatePost(request, "http://localhost/api/delivery-styles"), Ctx()));

        status.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_NameTooLong_ReturnsBadRequest()
    {
        var request = new UpdateDeliveryStyleRequestDto(
            new string('a', ConfigValidationLimits.DeliveryStyleNameMax + 1), "d", ValidPolicy());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(MockHttpRequestData.CreatePut(request, "http://localhost/api/delivery-styles/ds-1"),
                "ds-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Name must be at most");
    }

    [Fact]
    public async Task Update_DescriptionTooLong_ReturnsBadRequest()
    {
        var request = new UpdateDeliveryStyleRequestDto(
            "Drive-Time", new string('d', ConfigValidationLimits.DeliveryStyleDescriptionMax + 1), ValidPolicy());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(MockHttpRequestData.CreatePut(request, "http://localhost/api/delivery-styles/ds-1"),
                "ds-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Description must be at most");
    }

    [Theory]
    [InlineData(99999)]
    [InlineData(-50)]
    public async Task Update_IntensityOutOfRange_ReturnsBadRequest(int intensity)
    {
        var request = new UpdateDeliveryStyleRequestDto("Drive-Time", "d", ValidPolicy(intensity));

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(MockHttpRequestData.CreatePut(request, "http://localhost/api/delivery-styles/ds-1"),
                "ds-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("Intensity must be between");
        await _deliveryStyles.DidNotReceive().UpdateAsync(
            Arg.Any<string>(), Arg.Any<UpdateDeliveryStyleInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_TypedInvalidInput_ReturnsBadRequest_NotServerError()
    {
        // A permanent client-input failure (over-ceiling single version, e.g. VersionHistoryTrimmer)
        // must map to 400 via its TYPED disposition — the message alone contains no matching
        // keyword, so message matching alone would send it to a 500.
        _deliveryStyles.UpdateAsync(
                Arg.Any<string>(), Arg.Any<UpdateDeliveryStyleInput>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<DeliveryStyle>.Failure(OperationalFailure.InvalidInput(
                "versionhistory.entry_too_large",
                "This delivery style is too large to save — remove or shorten some entries and save again.")));

        var request = new UpdateDeliveryStyleRequestDto("Drive-Time", "d", ValidPolicy());

        (var status, string body) = await MockHttpRequestData.ReadResponseAsync(
            await _sut.Update(MockHttpRequestData.CreatePut(request, "http://localhost/api/delivery-styles/ds-1"),
                "ds-1", Ctx()));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("too large to save");
    }

    // ── Preview model handling (finding 15) ────────────────────────────────

    private static PreviewDeliveryStyleRequestDto PreviewRequest(string? model) =>
        new(ValidPolicy(), "voice-1", null, model);

    private async Task<(HttpStatusCode Status, string Body)> Preview(string? model)
    {
        return await MockHttpRequestData.ReadResponseAsync(
            await _sut.Preview(
                MockHttpRequestData.CreatePost(PreviewRequest(model), "http://localhost/api/delivery-styles/preview"),
                Ctx()));
    }

    [Fact]
    public async Task Preview_ExplicitV2Model_ReportsTagsUnsupported_AndAuditionsOnThatModel()
    {
        // A v2 station's cues cannot air (only eleven_v3 interprets tags), so the preview must
        // audition on the v2 model AND say plainly that tags are unsupported — otherwise an
        // operator hears working cues in the studio and expects them on air.
        (var status, string body) = await Preview("eleven_multilingual_v2");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"TagsSupported\":false");
        await _scriptDirector.Received(1).DirectAsync(
            Arg.Any<string>(), Arg.Any<DeliveryStylePolicy>(), Arg.Any<string?>(), "eleven_multilingual_v2",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preview_DefaultModel_ReportsTagsSupported()
    {
        // No model supplied → the configured default (eleven_v3) is used, preserving the
        // historical audition behaviour.
        (var status, string body) = await Preview(null);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("\"TagsSupported\":true");
    }
}

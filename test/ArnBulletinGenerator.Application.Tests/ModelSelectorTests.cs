using ArnBulletinGenerator.Domain.Configuration;
using ArnBulletinGenerator.Domain.Interfaces;
using ArnBulletinGenerator.Domain.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using ArnBulletinGenerator.Application.Broadcasting;

namespace ArnBulletinGenerator.Application.Tests;

public class ModelSelectorTests
{
    private readonly ILogger<ModelSelector> _logger = Substitute.For<ILogger<ModelSelector>>();
    private readonly IVoiceProvider _voiceProvider = Substitute.For<IVoiceProvider>();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ModelSelector _sut;

    public ModelSelectorTests()
    {
        // No voices known by default: every test id ("test-voice", "v") is capability-blind,
        // exercising the pure language-selection logic unchanged from before capability-awareness
        // was added.
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Success([]));
        _sut = new ModelSelector(_logger, Options.Create(new ModelSelectionOptions()), _voiceProvider, _cache);
    }

    [Fact]
    public async Task SelectModelAsync_BroadcastUseCase_ReturnsElevenV3()
    {
        // Act
        string result = await _sut.SelectModelAsync("test-voice", useCase: ModelSelectionUseCase.Broadcast);

        // Assert
        result.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SelectModelAsync_QualityUseCase_ReturnsElevenTurboV25()
    {
        // Act
        string result = await _sut.SelectModelAsync("test-voice", useCase: ModelSelectionUseCase.Quality);

        // Assert
        result.ShouldBe("eleven_turbo_v2_5");
    }

    [Fact]
    public async Task SelectModelAsync_GeneralUseCase_ReturnsElevenMultilingualV2()
    {
        // Act
        string result = await _sut.SelectModelAsync("test-voice", useCase: (ModelSelectionUseCase)99);

        // Assert
        result.ShouldBe("eleven_multilingual_v2");
    }

    [Fact]
    public async Task SelectModelAsync_WithSupportedLanguage_ReturnsPreferredModel()
    {
        // Act
        string result = await _sut.SelectModelAsync("test-voice", Option<string>.Some("en"));

        // Assert
        result.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SelectModelAsync_WithUnsupportedLanguage_FallsBackToMultilingual()
    {
        // Act
        string result = await _sut.SelectModelAsync("test-voice", Option<string>.Some("unsupported"));

        // Assert
        result.ShouldBe("eleven_multilingual_v2");
    }

    [Fact]
    public async Task SelectModelAsync_WithCaseInsensitiveLanguage_ReturnsPreferredModel()
    {
        // Act
        string result = await _sut.SelectModelAsync("test-voice", Option<string>.Some("EN"));

        // Assert
        result.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SelectModelAsync_WithLanguageCode_ReturnsPreferredModel()
    {
        // Act
        string result = await _sut.SelectModelAsync("test-voice", Option<string>.Some("en-US"));

        // Assert
        result.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SelectModelAsync_CustomOptions_UsesConfiguredModels()
    {
        var customOptions = new ModelSelectionOptions
        {
            BroadcastModel = "eleven_flash_v2_5",
            QualityModel = "eleven_multilingual_v2",
            FallbackModel = "eleven_turbo_v2_5"
        };
        var sut = new ModelSelector(_logger, Options.Create(customOptions), _voiceProvider, _cache);

        (await sut.SelectModelAsync("v", useCase: ModelSelectionUseCase.Broadcast)).ShouldBe("eleven_flash_v2_5");
        (await sut.SelectModelAsync("v", useCase: ModelSelectionUseCase.Quality)).ShouldBe("eleven_multilingual_v2");
        (await sut.SelectModelAsync("v", useCase: (ModelSelectionUseCase)99)).ShouldBe("eleven_turbo_v2_5");
    }

    [Fact]
    public async Task SelectModelAsync_CustomModel_UnsupportedLanguage_FallsBackToConfiguredFallback()
    {
        var customOptions = new ModelSelectionOptions
        {
            BroadcastModel = "eleven_flash_v2_5",
            FallbackModel = "eleven_turbo_v2_5"
        };
        var sut = new ModelSelector(_logger, Options.Create(customOptions), _voiceProvider, _cache);

        string result = await sut.SelectModelAsync("v", Option<string>.Some("zzzz"));

        result.ShouldBe("eleven_turbo_v2_5");
    }

    [Fact]
    public async Task ModelSupportsLanguage_ModelNotInLookup_FallsBack()
    {
        var customOptions = new ModelSelectionOptions
        {
            BroadcastModel = "unknown_model_xyz",
            FallbackModel = "eleven_multilingual_v2"
        };
        var sut = new ModelSelector(_logger, Options.Create(customOptions), _voiceProvider, _cache);

        // Model "unknown_model_xyz" is not in the static dictionary, so even a valid language causes fallback
        string result = await sut.SelectModelAsync("v", Option<string>.Some("en"));

        result.ShouldBe("eleven_multilingual_v2");
    }

    // ── Capability-aware selection (VoiceModelResolver wiring) ──────────────

    [Fact]
    public async Task SelectModelAsync_VoiceVerifiedOnlyForV2Family_NeverSelectsV3()
    {
        // A PVC/cloned voice ElevenLabs reports as verified for the v2 family only (no v3 in its
        // high_quality_base_model_ids) — the real Dolly Traffic / Jeffrey David G shape. Selecting
        // v3 for it degrades the clone to few-shot IVC fallback (accent drift) rather than the
        // fine-tuned voice.
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<Voice>>.Success(
        [
            new Voice("v2-only-voice", "Cloned Voice", Option<string>.None, Option<string>.None,
                new Dictionary<string, string>())
            { ModelIds = ["eleven_multilingual_v2", "eleven_turbo_v2_5"] }
        ]));

        string result = await _sut.SelectModelAsync("v2-only-voice", useCase: ModelSelectionUseCase.Broadcast);

        result.ShouldBe("eleven_multilingual_v2");
    }

    [Fact]
    public async Task SelectModelAsync_VoiceVerifiedForPreferredModel_KeepsPreferred()
    {
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<Voice>>.Success(
        [
            new Voice("v3-capable-voice", "Designed Voice", Option<string>.None, Option<string>.None,
                new Dictionary<string, string>())
            { ModelIds = ["eleven_v3", "eleven_multilingual_v2"] }
        ]));

        string result = await _sut.SelectModelAsync("v3-capable-voice", useCase: ModelSelectionUseCase.Broadcast);

        result.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SelectModelAsync_VoiceWithNoVerifiedBaseModels_TreatedAsNativeToPreferred()
    {
        // Empty ModelIds (Voice Design / generated voices) — native to whatever the preferred
        // model resolves to, exactly like an unknown voice.
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<Voice>>.Success(
        [
            new Voice("v3-native-voice", "Generated Voice", Option<string>.None, Option<string>.None,
                new Dictionary<string, string>())
            { ModelIds = [] }
        ]));

        string result = await _sut.SelectModelAsync("v3-native-voice", useCase: ModelSelectionUseCase.Broadcast);

        result.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SelectModelAsync_VoiceProviderFails_FallsBackToLanguageSelection()
    {
        // A transient ElevenLabs API failure must never block model selection — degrade to the
        // prior capability-blind behaviour rather than fail the broadcast.
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<Voice>>.Failure("ElevenLabs unavailable"));

        string result = await _sut.SelectModelAsync("any-voice", useCase: ModelSelectionUseCase.Broadcast);

        result.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SelectModelAsync_UnknownVoiceId_UsesLanguageSelectedModelUnchanged()
    {
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<Voice>>.Success(
        [
            new Voice("some-other-voice", "Other Voice", Option<string>.None, Option<string>.None,
                new Dictionary<string, string>())
            { ModelIds = ["eleven_multilingual_v2"] }
        ]));

        string result = await _sut.SelectModelAsync("unknown-voice-id", useCase: ModelSelectionUseCase.Broadcast);

        result.ShouldBe("eleven_v3");
    }

    [Fact]
    public async Task SelectModelAsync_CachesVoiceList_OnlyFetchesOnce()
    {
        _voiceProvider.GetVoicesAsync(Arg.Any<CancellationToken>()).Returns(Result<IReadOnlyList<Voice>>.Success(
        [
            new Voice("v2-only-voice", "Cloned Voice", Option<string>.None, Option<string>.None,
                new Dictionary<string, string>())
            { ModelIds = ["eleven_multilingual_v2"] }
        ]));

        await _sut.SelectModelAsync("v2-only-voice", useCase: ModelSelectionUseCase.Broadcast);
        await _sut.SelectModelAsync("v2-only-voice", useCase: ModelSelectionUseCase.Broadcast);

        await _voiceProvider.Received(1).GetVoicesAsync(Arg.Any<CancellationToken>());
    }
}

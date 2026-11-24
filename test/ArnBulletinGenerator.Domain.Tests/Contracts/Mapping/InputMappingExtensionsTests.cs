using ArnBulletinGenerator.Domain.Contracts;
using ArnBulletinGenerator.Domain.Contracts.Mapping;
using Shouldly;

namespace ArnBulletinGenerator.Domain.Tests.Contracts.Mapping;

public class InputMappingExtensionsTests
{
    [Fact]
    public void ToDomain_ValidDto_ReturnsSuccess()
    {
        var dto = new VoiceSettingsDto(1.0, 0.5, 0.8, 0.2, true);

        var result = dto.ToDomain();

        result.IsSuccess.ShouldBeTrue();
        result.Value.Speed.ShouldBe(1.0);
        result.Value.Stability.ShouldBe(0.5);
        result.Value.SimilarityBoost.ShouldBe(0.8);
        result.Value.Style.ShouldBe(0.2);
        result.Value.UseSpeakerBoost.ShouldBeTrue();
    }

    [Fact]
    public void ToDomain_DefaultDto_ReturnsSuccess()
    {
        var dto = new VoiceSettingsDto();

        var result = dto.ToDomain();

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void ToDomain_StabilityOutOfRange_ReturnsFailure()
    {
        var dto = new VoiceSettingsDto(Stability: 1.5);

        var result = dto.ToDomain();

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ToDomain_SpeedOutOfRange_ReturnsFailure()
    {
        var dto = new VoiceSettingsDto(2.0);

        var result = dto.ToDomain();

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ToDomain_NegativeStyle_ReturnsFailure()
    {
        var dto = new VoiceSettingsDto(Style: -0.1);

        var result = dto.ToDomain();

        result.IsFailure.ShouldBeTrue();
    }
}
